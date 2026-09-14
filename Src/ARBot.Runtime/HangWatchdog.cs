using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

// Kvuli HangWatchdog.Jmeno: cistí jména souboru je implementacni detail, ne verejne API, ale je to
// presne ta cast, kde se chyba (zavorka v ceste) pozna az na zarizeni. Stejny vzor jako VN100IMUBinary.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ARBot.Runtime.Tests")]

namespace ARBot
{
    /// <summary>
    /// <b>Stopa po ZATUHNUTÍ</b> — sourozenec <see cref="CrashLog"/>. Ten zachytí pád (výjimku,
    /// tedy operaci, která skončila špatně); tohle zachytí opak: operaci, která <b>neskončila
    /// vůbec</b>. Ohraničenou práci obalíš <see cref="Guard"/> a když se do zadaného času nevrátí,
    /// hlídač to napíše do <see cref="Trace"/> a <b>sám na sebe</b> pustí <c>createdump</c>, takže
    /// v <c>logs/hang-*.dmp</c> zůstanou zásobníky všech vláken.
    ///
    /// <para><b>Proč to vzniklo (14. 9. 2026):</b> při jízdě na Hviezdoslavově se runtime při volbě
    /// mise ze stránky zasekl uvnitř <see cref="ARBot.Robot.ARBotRuntime.Start"/> — v journalu je
    /// vidět hláška <c>corridor=false</c> a pak už nikdy <c>mission=track</c>, ačkoli mezi nimi je
    /// běžně 5 ms. Proces žil dál (vlákno kamery logovalo ještě dvě minuty), ale stránka přestala
    /// odpovídat, takže obsluha neměla jak zjistit cokoli víc a robota vypnula — a tím zmizel
    /// i stav, ze kterého by to šlo přečíst. <b>Dohledávat to přes stránku nejde</b>: ta je právě
    /// to, co při zatuhnutí není k dispozici. V terénu je u robota často jen mobil, takže jediné,
    /// co pomůže, je aby se robot vyšetřil sám, bez zásahu člověka.</para>
    ///
    /// <para><b>Co to NEumí:</b> nic nezachrání ani neodblokuje — jen zapíše důkaz. Zatuhlá
    /// operace zůstane zatuhlá; léčba je na volajícím (nebo na restartu služby).</para>
    ///
    /// <para><b>Kde to funguje:</b> minidump umí jen Linux — <c>createdump</c> je součást .NET
    /// runtime a leží vedle něj. Na Windows (vývoj, simulace) zbyde hlášení v <c>Trace</c>, což na
    /// ladění u stolu stačí, protože tam je debugger po ruce. ⚠️ Na systémech s <c>yama</c>
    /// (<c>/proc/sys/kernel/yama/ptrace_scope ≥ 1</c>) si potomek nesmí vzít <c>ptrace</c> na svého
    /// rodiče a dump nevznikne; Orange Pi yama nemá (ověřeno 14. 9. 2026), na jiném stroji to ale
    /// chce <c>prctl(PR_SET_PTRACER)</c>, který odsud nezavoláme. Selhání se hlásí, netiší.</para>
    /// </summary>
    public static class HangWatchdog
    {
        /// <summary>Kolik sekund nejvýš čekat na <c>createdump</c>, než se to vzdá.</summary>
        private const int DumpTimeoutSeconds = 120;

        /// <summary>
        /// Ohraničí operaci hlídačem. Vrácený token <b>zahoď (Dispose) po dokončení</b> práce —
        /// tím se hlídač odzbrojí. Když se to nestihne do <paramref name="sekundy"/>, hlídač
        /// vystřelí.
        ///
        /// <para><paramref name="sekundy"/> ≤ 0 hlídač vypne (vrátí token, který nedělá nic), aby
        /// se volající nemusel ptát — parametr <c>hangwatch=0</c> tím vrací přesně dosavadní
        /// chování.</para>
        ///
        /// <para><b>Arm se musí vzít PŘED vstupem do zámku</b>, který hlídaná operace bere: zatuhnout
        /// se dá i na čekání na ten zámek, a to je stav, který hlídač musí umět popsat zrovna tak
        /// jako zatuhnutí uvnitř.</para>
        /// </summary>
        /// <param name="co">Jméno operace do hlášení a do jména souboru (např. <c>Start(Run)</c>).</param>
        /// <param name="sekundy">Limit; ≤ 0 = hlídač vypnutý.</param>
        public static IDisposable Guard(string co, double sekundy)
            => sekundy > 0 ? new Token(co ?? "?", sekundy) : (IDisposable)VypnutyToken.Instance;

        /// <summary>Token vypnutého hlídače — jedna instance, žádná práce, žádná alokace navíc.</summary>
        private sealed class VypnutyToken : IDisposable
        {
            public static readonly VypnutyToken Instance = new VypnutyToken();
            public void Dispose() { }
        }

        private sealed class Token : IDisposable
        {
            private readonly string co;
            private readonly double limit;
            private readonly Stopwatch bezi = Stopwatch.StartNew();
            private Timer casovac;
            private int vystrelil;

            public Token(string co, double sekundy)
            {
                this.co = co;
                limit = sekundy;
                // Jednorázový časovač (bez periody): hlásit se má jednou. Opakované hlášení by
                // u zatuhnutí, které trvá minuty, zaplnilo journal a další dumpy by už nic
                // nového neřekly - stav se nemění, právě proto je to zatuhnutí.
                casovac = new Timer(_ => Vystrel(), null,
                                    TimeSpan.FromSeconds(sekundy), Timeout.InfiniteTimeSpan);
            }

            private void Vystrel()
            {
                if (Interlocked.Exchange(ref vystrelil, 1) != 0) return;
                try
                {
                    // Trace (ne Debug): na zařízení běží Release a tohle je přesně ten druh
                    // diagnostiky, po které se pátrá až po poruše. Viz CLAUDE.md.
                    Trace.WriteLine($"HangWatchdog: '{co}' nedobehlo do {limit:F0} s -> "
                                    + "ZATUHNUTI. Sbiram minidump procesu.");
                    string dump = Dump(co);
                    Trace.WriteLine(dump != null
                        ? $"HangWatchdog: minidump v '{dump}'. Zasobniky vlaken z nej precte "
                          + "'dotnet-dump analyze' (nebo gdb nad zivym procesem)."
                        : "HangWatchdog: minidump se nepodaril - zbyva jen tenhle radek a journal.");
                }
                catch (Exception ex)
                {
                    // Hlídač nesmí shodit proces, který hlídá: je to diagnostika, ne funkce.
                    Trace.WriteLine("HangWatchdog: hlaseni zatuhnuti selhalo: " + ex.Message);
                }
            }

            public void Dispose()
            {
                var t = Interlocked.Exchange(ref casovac, null);
                if (t == null) return;   // idempotentní: druhý Dispose (using + ruční) nic nedělá
                t.Dispose();

                // Když už se vystřelilo, řekni i to, že to nakonec dojelo - jinak by v journalu
                // zůstalo viset obvinění z zatuhnutí u operace, která byla jen pomalá, a příště
                // by se podle toho hledalo špatně.
                if (Volatile.Read(ref vystrelil) != 0)
                    Trace.WriteLine($"HangWatchdog: '{co}' nakonec dobehlo po "
                                    + $"{bezi.Elapsed.TotalSeconds:F1} s.");
            }
        }

        /// <summary>
        /// Pustí <c>createdump</c> na vlastní proces. Vrací cestu k dumpu, nebo <c>null</c>
        /// s důvodem v <see cref="Trace"/>.
        ///
        /// <para>Dump je <b>minidump</b> (<c>-n</c>), ne plný core: obsahuje zásobníky všech vláken
        /// a seznam modulů, což je přesně to, kvůli čemu se sbírá, a je o řád menší — plný core
        /// procesu, který má běžně přes 5 GB (page cache záznamu), by se na zařízení sbíral
        /// minuty a vozil špatně.</para>
        /// </summary>
        private static string Dump(string co)
        {
            if (!OperatingSystem.IsLinux())
            {
                Trace.WriteLine("HangWatchdog: createdump je jen na Linuxu -> bez dumpu "
                                + "(na Windows si vezmi debugger).");
                return null;
            }

            // createdump leží vedle runtime, tedy tam, odkud běžíme - verzi ani cestu k .NET tím
            // nemusíme znát a nasazení se nemusí o nic starat.
            string exe = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "createdump");
            if (!File.Exists(exe))
            {
                Trace.WriteLine($"HangWatchdog: '{exe}' neexistuje -> bez dumpu.");
                return null;
            }

            // Tatáž složka jako crash logy (a stejný zdroj pravdy o dataroot=): kdo hledá stopu po
            // poruše, má ji hledat na jednom místě.
            string dir = Path.Combine(CrashLog.LogDirectory ?? AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                $"hang-{Jmeno(co)}-{DateTime.Now:yyyyMMdd-HHmmss}.dmp");

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("-n");          // minidump se zásobníky, ne plný core
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            using var p = Process.Start(psi);
            if (p == null) { Trace.WriteLine("HangWatchdog: createdump nesel spustit."); return null; }

            // Limit je tu proto, že createdump si na dobu sběru proces SIGSTOPne. U zatuhnutí to
            // nevadí (stejně stojí), ale viset na něm bez konce by znamenalo, že hlídač sám
            // potřebuje hlídač.
            if (!p.WaitForExit(DumpTimeoutSeconds * 1000))
            {
                Trace.WriteLine($"HangWatchdog: createdump nedobehl do {DumpTimeoutSeconds} s.");
                return null;
            }

            if (p.ExitCode != 0 || !File.Exists(path))
            {
                string chyba = (p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd()).Trim();
                Trace.WriteLine($"HangWatchdog: createdump skoncil s kodem {p.ExitCode}"
                                + (chyba.Length > 0 ? ": " + chyba : "."));
                return null;
            }
            return path;
        }

        /// <summary>
        /// Jméno operace na kus jména souboru. Pouští jen písmena, číslice, tečku, pomlčku
        /// a podtržítko — <c>Start(Run)</c> obsahuje závorky a ty by v shellu na zařízení musel
        /// člověk escapovat, což je u souboru, který se vozí a posílá, zbytečná otrava.
        /// </summary>
        internal static string Jmeno(string co)
        {
            if (string.IsNullOrWhiteSpace(co)) return "?";
            var sb = new System.Text.StringBuilder(co.Length);
            foreach (char c in co)
            {
                if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
                else if (c == '.' || c == '-' || c == '_') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-') is { Length: > 0 } s ? s : "?";
        }
    }
}
