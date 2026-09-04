using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ARBot.Robot;

namespace ARBot
{
    /// <summary>
    /// <b>Stopa po pádu aplikace.</b> Neodchycenou výjimku zapíše do <c>logs/crash-&lt;datum&gt;.log</c>
    /// vedle aplikace a před koncem procesu se pokusí dopsat rozjetý záznam na disk.
    ///
    /// <para><b>Proč to existuje (3. 9. 2026):</b> na Orange Pi aplikace několikrát „chvilku běžela a
    /// zmizela". Dohledat se nedalo nic: journal byl volatile, syslog posledního bootu se po
    /// vybití baterie nedostal na disk, aplikace byla spuštěná z terminálu (neodchycená výjimka
    /// .NET jde na stderr toho terminálu a nikam jinam) a core dumpy nevznikaly. Tohle je ta část
    /// řešení, která je v aplikaci; zbytek (spouštěcí skript s logem, trvalý journal, minidumpy)
    /// je na zařízení — viz OrangePi5Ultra/POSTUP.md, krok 12.</para>
    ///
    /// <para><b>Co zachytí:</b> <see cref="AppDomain.UnhandledException"/> (výjimka z libovolného
    /// vlákna, proces končí), <see cref="TaskScheduler.UnobservedTaskException"/> (proces pokračuje,
    /// ale je to stopa) a výjimku, která vypadne z hlavní smyčky aplikace (<c>Program.Main</c> UI
    /// i headless). Třída je veřejná, protože ji volají obě aplikace nad <c>ARBot.Runtime</c>.
    /// <b>Nezachytí</b> nativní pád (SIGSEGV v librealsense apod.) — na ten je minidump .NET
    /// zapnutý ve spouštěcím skriptu a <c>kernel.print-fatal-signals</c>.</para>
    ///
    /// <para><b>Záznam při pádu:</b> <see cref="ARBotRuntime.Stop"/> dojede fronty a flushne
    /// <c>RecordingTarget</c>, takže <c>.rec</c>/<c>.idx</c> nezůstanou useknuté v půlce zprávy.
    /// Volá se s časovým limitem — kdyby se runtime po výjimce zasekl, nesmí to zdržet konec
    /// procesu donekonečna. Zároveň Stop zastaví zdroje včetně motorů, což je při pádu řídicí
    /// aplikace to jediné správné.</para>
    /// </summary>
    public static class CrashLog
    {
        /// <summary>Kolik sekund nejvýš čekat na dopsání záznamu při terminálním pádu.</summary>
        private const int FlushTimeoutSeconds = 5;

        private static bool installed;

        /// <summary>Zaregistruje handlery. Volat jako první věc v <c>Main</c>; idempotentní.</summary>
        public static void Install()
        {
            if (installed) return;
            installed = true;

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception
                         ?? new Exception("neznámý objekt výjimky: " + (e.ExceptionObject?.ToString() ?? "null"));
                Write("AppDomain.UnhandledException" + (e.IsTerminating ? " — proces končí" : ""), ex, e.IsTerminating);
            };

            // Výchozí chování .NET Core: neobsloužená výjimka Tasku proces NESHODÍ. Nemění se to
            // (SetObserved se nevolá, není třeba), jen se zapíše — je to stopa po chybě, která by
            // jinak zmizela s garbage collectorem.
            TaskScheduler.UnobservedTaskException += (s, e) =>
                Write("TaskScheduler.UnobservedTaskException — proces pokračuje", e.Exception, terminating: false);
        }

        /// <summary>
        /// Zapíše výjimku do <c>logs/crash-&lt;datum&gt;.log</c> (a do Trace a na stderr). Při
        /// <paramref name="terminating"/> se navíc pokusí dopsat záznam. Nikdy nevyhazuje.
        /// </summary>
        /// <returns>Cesta k souboru, nebo null, když se zapsat nepodařilo.</returns>
        public static string Write(string source, Exception ex, bool terminating)
        {
            string path = null;
            try
            {
                string dir = Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

                var sb = new StringBuilder();
                sb.AppendLine($"ARBot pád — {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (místní čas)");
                sb.AppendLine($"zdroj:     {source}");
                sb.AppendLine($"verze:     {Version()}");
                sb.AppendLine($"argumenty: {string.Join(" ", Environment.GetCommandLineArgs())}");
                sb.AppendLine($"systém:    {Environment.OSVersion} / .NET {Environment.Version} / {(Environment.Is64BitProcess ? "x64" : "x86")}");
                sb.AppendLine($"běh:       {Process.GetCurrentProcess().StartTime:HH:mm:ss} start, pracovní paměť {Environment.WorkingSet / (1024 * 1024)} MB");
                sb.AppendLine();
                sb.AppendLine(ex?.ToString() ?? "(bez výjimky)");
                sb.AppendLine();

                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);

                string line = $"CrashLog: {source}: {ex?.GetType().Name}: {ex?.Message} -> {path}";
                Trace.WriteLine(line);
                Console.Error.WriteLine(sb.ToString());
            }
            catch
            {
                // Handler pádu nesmí sám padat. Když nejde zapsat soubor, aspoň stderr.
                try { Console.Error.WriteLine($"CrashLog ({source}): {ex}"); } catch { }
            }

            if (terminating) FlushRecordingBestEffort();
            return path;
        }

        /// <summary>
        /// Dopíše rozjetý záznam a zastaví zdroje (motory) — s limitem, aby zaseknutý runtime
        /// nedržel umírající proces. Runtime se kvůli tomu NEVYTVÁŘÍ: když nikdy nevznikl, není co flushovat.
        /// </summary>
        private static void FlushRecordingBestEffort()
        {
            try
            {
                if (!ARBotRuntime.HasCurrent) return;
                var stop = Task.Run(() => ARBotRuntime.Current.Stop());
                if (!stop.Wait(TimeSpan.FromSeconds(FlushTimeoutSeconds)))
                    Console.Error.WriteLine($"CrashLog: ARBotRuntime.Stop() se nestihl za {FlushTimeoutSeconds} s — záznam může být useknutý.");
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("CrashLog: flush záznamu selhal: " + ex.Message); } catch { }
            }
        }

        private static string Version()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? asm.GetName().Version?.ToString() ?? "?";
            }
            catch { return "?"; }
        }
    }
}
