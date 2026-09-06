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
            //
            //    Clear() PRED pridanim naseho listeneru: vychozi DefaultTraceListener pise na
            //    Linuxu do SYSLOGU, takze pod systemd (kde journal sbira stdout i syslog) byl
            //    KAZDY RADEK V JOURNALU DVAKRAT - jednou _TRANSPORT=stdout, jednou =syslog
            //    (nalezeno na Orange Pi 5. 9. 2026). Na Windows to videt neni: tam tentyz listener
            //    pise do debuggeru, kam se v konzolove aplikaci nikdo nediva.
            Trace.Listeners.Clear();
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

            // 3b) Zamek jedne instance - HNED po konfiguraci (potrebuje dataroot=) a PRED
            //     jakymkoli sahnutim na hardware. Druha instance vedle bezici jednotky systemd by
            //     sahla na tytez UARTy a kamery; port nahledu se ostetri sam, takze by to zvenci
            //     vypadalo, ze vse bezi - jen by stranka ukazovala tu prvni. Viz SingleInstanceLock.
            using var zamek = SingleInstanceLock.TryAcquire(
                ARBot.Common.Configuration.RepoPaths.DataRootOrBase(), out string? zamekChyba);
            if (zamek == null)
            {
                Console.Error.WriteLine(zamekChyba);
                Trace.WriteLine(zamekChyba);
                return SingleInstanceLock.ExitCodeAlreadyRunning;
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
                Trace.WriteLine("POZOR: mise je zapnuta - jakmile bude HW pripravene, mise zacne bez dalsiho "
                    + "pokynu. FreeRun se rovnou rozjede; Robotour se sam nastartuje, ale prvni pohyb ceka "
                    + "na stisk a uvolneni nouzoveho zastaveni. Zastavi ho stop nebo ukonceni procesu.");
            else if (webPort > 0)
                Trace.WriteLine("Mise nezadana - po startu se ceka, az ji nekdo vybere na strance nahledu "
                    + "(jen pri drzenem nouzovem zastaveni). Do te doby robot stoji a NENAHRAVA se.");
            else
                Trace.WriteLine("POZOR: mise nezadana a nahled vypnuty (web=0) - neni cim misi vybrat, "
                    + "robot bude jen stat. Zadej mission= nebo web=<port>.");
            if (ParamRegistry.AutoRun.Value)
                Trace.WriteLine("autorun=true se v headless ignoruje: Run startuje vzdy, je to jediny duvod existence procesu.");

            // 7) Ukonceni: Ctrl+C v ssh, SIGTERM (kill <pid>), SIGHUP (zavrena ssh session). Prvni
            //    signal spusti radne ukonceni; Cancel = true, aby .NET proces nezabil driv, nez
            //    dobehne Stop() a dopise se zaznam. Registrace PRED Startem, aby signal behem
            //    startu nepropadl vychozimu chovani (okamzity konec bez Stop).
            using var konec = new ManualResetEventSlim(false);
            string? duvod = null;
            string? zvolenaMise = null;
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
            ManualResetEventSlim? cekaniNaMisi = null;
            if (webPort > 0)
            {
                webStatus = new WebStatus();
                var vybranaMise = new ManualResetEventSlim(false);
                web = new WebPreviewServer(webStatus,
                    onStop: () =>
                    {
                        duvod ??= "web /stop";
                        konec.Set();
                    },
                    // Vyber mise ze stranky: jen kdyz zadna zadana nebyla (jinak by web prebijel
                    // to, s cim clovek proces spustil). Hodnota jde do ParamStore, ne bokem - aby
                    // ji Start precetl z registru a ucinna konfigurace v zaznamu nelhala.
                    onMission: miseZapnuta ? null : (string m) =>
                    {
                        ParamStore.Current.SetRuntimeOverride("mission", m);
                        zvolenaMise = m;
                        vybranaMise.Set();
                    },
                    // Vypnuti CELE desky. Poradi je podstatne: nejdriv Stop() (dojede fronty,
                    // uzavre zaznam, zastavi motory) a teprve pak pokyn systemu - jinak by se
                    // vypinalo pres rozjety zaznam. Proces se sam neukoncuje: az prijde SIGTERM
                    // z vypinani, projde stejnou cestou jako Ctrl+C.
                    onPowerOff: string.IsNullOrWhiteSpace(ParamRegistry.PowerOffCmd.Value) ? null : () =>
                    {
                        Trace.WriteLine("Vypnuti zarizeni ze stranky: zastavuji runtime...");
                        try { ARBotRuntime.Current.Stop(); }
                        catch (Exception ex) { Trace.WriteLine("Stop pred vypnutim selhal: " + ex.Message); }
                        return SystemPower.TryPowerOff(ParamRegistry.PowerOffCmd.Value);
                    });
                if (web.Start(webPort))
                {
                    // Pripojeni na Stream prezije Stop() runtime (neni v jeho connections), takze
                    // stranka drzi posledni stav i po zastaveni - a odpoji se az na konci procesu.
                    webConnection = ARBotRuntime.Current.Stream.Connect(webStatus);
                    // Na misi se ceka JEN kdyz stranka opravdu bezi - jinak by neexistoval nikdo,
                    // kdo misi vybere, a proces by cekal navzdy.
                    if (!miseZapnuta) cekaniNaMisi = vybranaMise;
                    webStatus.PowerOffAvailable = web.PowerOffAvailable;
                    if (ParamRegistry.WebOpen.Value) OtevriNahled(web.Port);
                }
                else
                {
                    web.Dispose();
                    web = null;
                    webStatus = null;
                    vybranaMise.Dispose();
                    Trace.WriteLine("Nahled nebezi, takze neni cim vybrat misi -> robot bude jen stat.");
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
                //    logika, zadne dublovani.
                //
                //    BEZ ZADANE MISE se jede NADVAKRAT (doc/plan-headless-provoz.md, navrh A):
                //    nejdriv Run bez mise a BEZ ZAZNAMU, aby stranka ukazala senzory, kameru
                //    a hlavne stav nouzoveho zastaveni - bez bezicich zdroju totiz o stopu nevime
                //    vubec nic, a prave na nem stoji pojistka vyberu mise. Az kdyz clovek misi
                //    vybere, runtime se prestavi s ni (Start si sam zavola Stop) a od TE CHVILE
                //    se nahrava: jeden .rec = jedna mise, a cekani nezaplni disk (~19 MB/s).
                if (cekaniNaMisi != null)
                {
                    ARBotRuntime.Current.Start(Mode.Run, ARBotRuntime.NoRecord);
                    webStatus!.AwaitingMission = true;
                    Trace.WriteLine("Ceka se na vyber mise na strance. Robot stoji, zaznam nebezi.");

                    // Cekat na volbu NEBO na ukonceni - obojim se ceka jen tady, aby Ctrl+C
                    // fungovalo i pred vyberem mise.
                    int co = WaitHandle.WaitAny(new[] { cekaniNaMisi.WaitHandle, konec.WaitHandle });
                    if (co == 1)
                    {
                        Trace.WriteLine($"Ukonceni ({duvod}) jeste pred vyberem mise.");
                    }
                    else
                    {
                        webStatus.AwaitingMission = false;
                        Trace.WriteLine($"Mise zvolena z webu: {zvolenaMise}. Prestavuji runtime a zacinam zaznam.");
                        ARBotRuntime.Current.Start(Mode.Run);
                        Trace.WriteLine("Run s misi bezi. Ukonceni: Ctrl+C, tlacitko na strance, nebo kill <pid>.");
                        konec.Wait();
                    }
                }
                else
                {
                    ARBotRuntime.Current.Start(Mode.Run);
                    Trace.WriteLine("Run bezi. Ukonceni: Ctrl+C, nebo kill <pid> z druhe session.");
                    konec.Wait();
                }
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
            try { cekaniNaMisi?.Dispose(); } catch (Exception ex) { Trace.WriteLine("web: uklid cekani selhal: " + ex.Message); }
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
