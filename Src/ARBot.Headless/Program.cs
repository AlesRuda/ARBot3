using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ARBot.Common.Configuration;
using ARBot.Robot;
using ARBot.Robot.Web;

namespace ARBot.Headless
{
    /// <summary>
    /// <b>Řídicí runtime bez UI.</b> Jeden příkaz přes ssh na OrangePi: bootstrap konfigurace →
    /// čekání na HW → <see cref="ARBotRuntime.Start"/>(Run) → čekání na Ctrl+C / SIGTERM →
    /// <see cref="ARBotRuntime.Stop"/>. Nic víc: žádné prohlížení záznamů (to je UI na Windows
    /// a <c>ARBot.Analyze</c>), žádná služba, žádný restart po pádu - robot, který se sám znovu
    /// rozjede, je horší než robot, který stojí. Viz doc/headless.md a doc/decisions.md (4. 9. 2026).
    ///
    /// <para><b>Návratové kódy:</b> 0 = řádně ukončeno signálem, 2 = vadná konfigurace (stejný kód
    /// jako UI, <see cref="RuntimeBootstrap.ExitCodeBadConfig"/>), jinak pád s neošetřenou výjimkou
    /// (<see cref="CrashLog"/> zapíše <c>logs/crash-*.log</c> vedle aplikace a dopíše záznam).</para>
    ///
    /// <para>Bez <c>[STAThread]</c> - ten patří jen k Avalonii na Windows.</para>
    /// </summary>
    internal static class Program
    {
        public static int Main(string[] args)
        {
            // 1) Konzole je jediny displej. Trace je zamerne (Debug.WriteLine v Release mlci, viz
            //    CLAUDE.md); vypis jde zaroven pres TraceInfoBridge do zaznamu, kdyz bezi.
            Trace.Listeners.Add(new ConsoleTraceListener());
            Trace.AutoFlush = true;

            // 2) Stopa po padu - driv nez cokoliv, co muze spadnout.
            CrashLog.Install();

            // 3) Konfigurace jako prvni, driv nez cokoliv sahne na ParamRegistry. Vadna konfigurace
            //    = hlaska na stderr a kod 2, stejne jako UI.
            //    (Trace.WriteLine ma [Conditional], proto lambda, ne skupina metod.)
            string? chyba = RuntimeBootstrap.TryConfigure(Environment.GetCommandLineArgs(), s => Trace.WriteLine(s));
            if (chyba != null)
            {
                Console.Error.WriteLine(chyba);
                return RuntimeBootstrap.ExitCodeBadConfig;
            }

            // 4) Co se chysta - at je v konzoli (a v zaznamu) videt, s cim se startovalo.
            //    UI parametry (selftest=, open=, worldshot=, telemetryshot=) se ignoruji tise -
            //    registr je zna, takze start neshodi; autorun= s hlaskou, protoze tady je Run vzdy.
            string mise = ParamRegistry.Mission.Value ?? "none";
            bool miseZapnuta = !ParamRegistry.Mission.Is("none");
            int webPort = (int)ParamRegistry.Web.Value;
            Trace.WriteLine("ARBot.Headless: rezim Run bez UI."
                + $" HW: {(ParamRegistry.VirtualHw.Value ? "virtualni (virtualhw=true)" : "skutecny")};"
                + $" mise: {mise};"
                + $" zaznam: {PopisZaznamu()};"
                + $" nahled: {(webPort > 0 ? "http://<ip>:" + webPort + "/" : "vypnuty (web=0)")}.");
            if (miseZapnuta)
                Trace.WriteLine("POZOR: mise je zapnuta - robot se rozjede bez dalsiho pokynu, "
                    + "jakmile bude HW pripravene. Zastavi ho jen nouzove zastaveni nebo ukonceni procesu.");
            if (ParamRegistry.AutoRun.Value)
                Trace.WriteLine("autorun=true se v headless ignoruje: Run startuje vzdy, je to jediny duvod existence procesu.");

            // 7) Ukonceni: Ctrl+C v ssh, SIGTERM (kill <pid>), SIGHUP (zavrena ssh session). Prvni
            //    signal spusti radne ukonceni; Cancel = true, aby .NET proces nezabil driv, nez
            //    dobehne Stop() a dopise se zaznam. Registrace PRED Startem, aby signal behem
            //    startu nepropadl vychozimu chovani (okamzity konec bez Stop).
            using var konec = new ManualResetEventSlim(false);
            string? duvod = null;
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                duvod ??= "Ctrl+C";
                konec.Set();
            };
            using var sigterm = RegistrujSignal(PosixSignal.SIGTERM, "SIGTERM", () => { duvod ??= "SIGTERM"; konec.Set(); });
            using var sighup = RegistrujSignal(PosixSignal.SIGHUP, "SIGHUP", () => { duvod ??= "SIGHUP"; konec.Set(); });

            // 4b) Webovy nahled (web=<port>, 0 = vypnuto). Startuje PRED Run schvalne: kdyby se HW
            //     nerozjelo, je stranka jedine, co o tom rekne. Selhani bindu nahled vypne, ale beh
            //     NEZASTAVI - stejna zasada jako u zaznamu. Viz doc/headless.md.
            WebStatus? webStatus = null;
            WebPreviewServer? web = null;
            IDisposable? webConnection = null;
            if (webPort > 0)
            {
                webStatus = new WebStatus();
                web = new WebPreviewServer(webStatus, () =>
                {
                    duvod ??= "web /stop";
                    konec.Set();
                });
                if (web.Start(webPort))
                {
                    // Pripojeni na Stream prezije Stop() runtime (neni v jeho connections), takze
                    // stranka drzi posledni stav i po zastaveni - a odpoji se az na konci procesu.
                    webConnection = ARBotRuntime.Current.Stream.Connect(webStatus);
                    if (ParamRegistry.WebOpen.Value) OtevriNahled(web.Port);
                }
                else
                {
                    web.Dispose();
                    web = null;
                    webStatus = null;
                }
            }

            try
            {
                // 5) Kamery a porty se otviraji lene - bez cekani by Run startoval nad polovicnim HW.
                //    Stejny postup a stejna prodleva jako autorun v UI (ARBotRuntime.HwSettleMs).
                Trace.WriteLine("Cekam na inicializaci HW...");
                ARBotHW.Current.WaitReady();
                if (konec.Wait(ARBotRuntime.HwSettleMs))
                {
                    Trace.WriteLine($"Ukonceno ({duvod}) jeste pred startem Run - nic nebezelo.");
                    return 0;
                }

                // 6) Run. Zaznam resi parametr record= uvnitr Start (stejne jako autorun) - jedna
                //    logika, zadne dublovani. Start se vola presne jednou: druhy Start by nejdriv
                //    zavolal Stop toho prvniho.
                ARBotRuntime.Current.Start(Mode.Run);
                Trace.WriteLine("Run bezi. Ukonceni: Ctrl+C, nebo kill <pid> z druhe session.");

                konec.Wait();
                Trace.WriteLine($"Ukonceni ({duvod}): zastavuji runtime...");
            }
            catch (Exception ex)
            {
                // Stejne jako UI: zapsat (CrashLog dopise zaznam a zastavi zdroje vcetne motoru)
                // a nechat proces spadnout s nenulovym kodem. Tvarit se, ze nic, by bylo horsi.
                CrashLog.Write("ARBot.Headless: start / beh Run", ex, terminating: true);
                throw;
            }

            // 8) Stop dojede fronty a uzavre zaznam. Jak dlouho to trva, je udaj do doc/headless.md.
            var sw = Stopwatch.StartNew();
            ARBotRuntime.Current.Stop();
            sw.Stop();
            Trace.WriteLine($"Runtime zastaven za {sw.ElapsedMilliseconds} ms. Konec.");

            // 9) Nahled drzel posledni stav i po Stop(), ale proces uz konci - odpojit a zavrit.
            try { webConnection?.Dispose(); } catch (Exception ex) { Trace.WriteLine("web: odpojeni selhalo: " + ex.Message); }
            try { web?.Dispose(); } catch (Exception ex) { Trace.WriteLine("web: zavreni selhalo: " + ex.Message); }
            return 0;
        }

        /// <summary>
        /// Otevre stranku nahledu ve vychozim prohlizeci (<c>webopen=true</c>). Slouzi vyvoji na
        /// Windows - launch profil tim usetri kopirovani adresy. <b>Na zarizeni bez displeje to
        /// nema kde vyskocit</b>, proto je parametr vychozi vypnuty a selhani se jen ohlasi.
        ///
        /// <para><c>localhost</c> zamerne: prohlizec bezi na temze stroji jako proces. Adresu pro
        /// pristup ze site vypisuje uvodni radek.</para>
        /// </summary>
        private static void OtevriNahled(int port)
        {
            string url = "http://localhost:" + port + "/";
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // UseShellExecute = predat URL registrovanemu prohlizeci.
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
                }
                Trace.WriteLine("webopen=true: otevrena stranka " + url);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"webopen=true: prohlizec se nepodarilo otevrit ({ex.Message}) "
                                + $"-> otevri {url} rucne. Beh to nijak neovlivnuje.");
            }
        }

        /// <summary>Lidsky popis parametru <c>record=</c> pro uvodni radek.</summary>
        private static string PopisZaznamu()
        {
            string raw = (ParamRegistry.Record.Value ?? string.Empty).Trim();
            if (raw.Length == 0 || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
                return "zadny (record= nezadan)";
            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                return "records/<datum>.rec";
            return raw;
        }

        /// <summary>
        /// Registrace POSIX signalu; na Windows .NET podporuje jen SIGINT/SIGQUIT/SIGTERM/SIGHUP
        /// (mapovane na konzolove udalosti), cokoliv jineho hazi <see cref="PlatformNotSupportedException"/>.
        /// Nepodporovany signal se jen ohlasi - Ctrl+C funguje vzdy.
        /// </summary>
        private static IDisposable? RegistrujSignal(PosixSignal signal, string jmeno, Action akce)
        {
            try
            {
                return PosixSignalRegistration.Create(signal, ctx =>
                {
                    ctx.Cancel = true;   // proces neukoncuj sam - dobehne Stop() a return 0
                    akce();
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{jmeno} se na tehle platforme nepodarilo zaregistrovat ({ex.GetType().Name}); Ctrl+C funguje.");
                return null;
            }
        }
    }
}
