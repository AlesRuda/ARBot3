using Avalonia;
using Avalonia.Logging;
using System;
using System.Diagnostics;
using System.Linq;

namespace ARBot
{
    internal sealed class Program
    {
        /// <summary>
        /// Vraci ucinnou hodnotu parametru. Pokud parametr neni zadany, vraci defaultni hodnotu.
        ///
        /// <para><b>Uz to necte prikazovou radku primo</b> - hodnotu drzi
        /// <see cref="ARBot.Common.Configuration.ParamStore"/>, ktery ji sklada v poradi
        /// <c>default z registru</c> -&gt; <c>profil (config=)</c> -&gt; <c>prikazova radka</c>.
        /// Signatura zustala schvalne stejna, aby se zadne z ~50 mist cteni nemuselo menit.
        /// Viz doc/configuration.md.</para>
        ///
        /// <para><c>Debug.WriteLine</c> tu zustava: je to jedina stopa konfigurace v zaznamu
        /// (pres <c>Info</c> zpravu), a tu si vzit nechceme.</para>
        /// </summary>
        public static string GetParam(string param, string def = null)
        {
            string val = ARBot.Common.Configuration.ParamStore.Current.GetString(param, def);
            Debug.WriteLine(string.Format("{0}={1}", param, val));
            return val;
        }
        /// <summary>
        /// Vraci ucinnou hodnotu parametru jako cislo (vzdy <c>InvariantCulture</c>). Pokud
        /// parametr neni zadany, vraci defaultni hodnotu. Viz <see cref="GetParam"/>.
        /// </summary>
        public static double GetParamDouble(string param, double def)
        {
            double val = ARBot.Common.Configuration.ParamStore.Current.GetDouble(param, def);
            Debug.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                          "{0}={1}", param, val));
            return val;
        }
        /// <summary>
        /// Vraci ucinnou hodnotu parametru jako <c>bool</c>. Pokud parametr neni zadany, vraci
        /// defaultni hodnotu. Viz <see cref="GetParam"/>.
        /// </summary>
        public static bool GetParamBool(string param, bool def)
        {
            bool val = ARBot.Common.Configuration.ParamStore.Current.GetBool(param, def);
            Debug.WriteLine(string.Format("{0}={1}", param, val));
            return val;
        }

        /// <summary>
        /// Vraci hodnotu parametru jako **cestu k souboru/slozce**: relativni cesta se resi proti
        /// <b>korenu repa</b> (slozka s <c>.git</c>), ne proti pracovnimu adresari procesu.
        /// Absolutni cesta se necha, jak je.
        ///
        /// <para>Logiku (i duvod, proc ne proti pracovnimu adresari) drzi
        /// <see cref="ARBot.Common.Configuration.RepoPaths"/>.</para>
        /// </summary>
        public static string GetParamPath(string param, string def = null)
        {
            return ARBot.Common.Configuration.RepoPaths.Resolve(GetParam(param, def));
        }

        /// <summary>
        /// Koren git repa (slozka obsahujici <c>.git</c>); fallback na
        /// <see cref="AppContext.BaseDirectory"/> (nasazeni bez repa, napr. na Pi).
        ///
        /// <para>Zachovano kvuli volajicim v projektu ARBot; implementace je
        /// v <see cref="ARBot.Common.Configuration.RepoPaths.RootOrBase"/>, kam se presunula,
        /// aby na ni videl i <c>ARBot.Common</c> a testy. Viz doc/configuration.md.</para>
        /// </summary>
        public static string RepoRootOrBase()
        {
            return ARBot.Common.Configuration.RepoPaths.RootOrBase();
        }



        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Stopa po padu (logs/crash-<datum>.log + dopsani zaznamu) - driv nez cokoliv, co muze
            // spadnout. Viz CrashLog.
            CrashLog.Install();

            // Konfigurace se sklada JAKO PRVNI - driv, nez cokoliv sahne na GetParam. Vadna
            // konfigurace ma skoncit hlaskou, ne vyjimkou v pulce startu: aplikace by pak bezela
            // s necim jinym, nez co je v profilu napsano, a nikdo by se to nedozvedel.
            // Viz doc/configuration.md.
            try
            {
                var store = ARBot.Common.Configuration.ParamStore.Build(
                    Environment.GetCommandLineArgs());
                if (store.ConfigPath != null)
                    Debug.WriteLine("Konfigurace: profil " + store.ConfigPath);
                foreach (var w in store.Warnings)
                    Debug.WriteLine("Konfigurace: " + w);
            }
            catch (ARBot.Common.Configuration.ParamFileException ex)
            {
                Console.Error.WriteLine("Chyba konfigurace: " + ex.Message);
                Debug.WriteLine("Chyba konfigurace: " + ex.Message);
                Environment.Exit(2);
                return;
            }

            // Strop rychlosti z parametru maxspeed=. MUSI to byt tady, pred startem UI.
            ApplyMaxSpeedFromParams();
            // Tvrdy odstup od prekazek z parametru safedist= - tentyz duvod a mechanismus.
            ApplySafeDistFromParams();

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                // Vyjimka z UI vlakna vypadne z hlavni smycky sem (AppDomain.UnhandledException
                // ji zachyti az pri rethrow). Zapsat a nechat proces spadnout - tvarit se, ze nic,
                // by bylo horsi nez pad.
                CrashLog.Write("hlavni smycka Avalonie (UI vlakno)", ex, terminating: true);
                throw;
            }
        }

        /// <summary>
        /// Prenese <c>maxspeed=</c> do <see cref="ARBot.Common.Configuration.Profile.MaxAllowedSpeed"/>.
        /// Bez zadaneho parametru nedela nic (plati hodnota z kodu).
        ///
        /// <para><b>Proc uz pri startu a ne az u ctenare:</b> tu hodnotu ctou TRI ruzna mista, a to
        /// pri VZNIKU objektu - driver motoru <c>SDC2160Ex</c> a <c>TrapezoidMotionProfile</c>
        /// v konstruktoru, <c>LocalPlannerConfig.MaxSpeed</c> inicializatorem pole. Kdo by vznikl
        /// driv, nez se hodnota nastavi, drzel by tu starou a strop by platil jen zcasti - coz je
        /// u bezpecnostniho omezeni to nejhorsi, co muze byt.</para>
        ///
        /// <para><b>Past s odvozenymi statickymi poli</b> (kvuli ni <c>Profile</c> v registru
        /// zamerne neni - viz doc/configuration.md) se tady NEOTVIRA: z <c>MaxAllowedSpeed</c>
        /// nic nederivuje. <c>MaxTheoreticalSpeed</c> se pocita z obvodu kola a otacek motoru,
        /// ne z nej.</para>
        /// </summary>
        private static void ApplyMaxSpeedFromParams()
        {
            string raw = ARBot.Common.Configuration.ParamStore.Current.Get("maxspeed");
            if (string.IsNullOrWhiteSpace(raw))
                return;

            double v = GetParamDouble("maxspeed", ARBot.Common.Configuration.Profile.MaxAllowedSpeed);

            // Nad technicky dosazitelnou rychlost to nema smysl: motor tam nedojede a rychlostni
            // profil by planoval s cislem, ktere se nikdy nenaplni. Orezat je lepsi nez odmitnout
            // start - zamer ("jed naplno") je jednoznacny a robot v terenu ma jet. Nekladnou
            // hodnotu odmitne uz registr pri nacteni profilu, sem se nedostane.
            double strop = ARBot.Common.Configuration.Profile.MaxTheoreticalSpeed;
            if (v > strop)
            {
                Trace.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "maxspeed={0:F2} je nad technicky dosazitelnou rychlosti {1:F2} m/s -> orezano.",
                    v, strop));
                v = strop;
            }

            Trace.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "maxspeed: strop rychlosti {0:F2} -> {1:F2} m/s (plati pro motor, rychlostni profil "
                + "i obalku planovace).",
                ARBot.Common.Configuration.Profile.MaxAllowedSpeed, v));
            ARBot.Common.Configuration.Profile.MaxAllowedSpeed = v;
        }

        /// <summary>
        /// Prenese <c>safedist=</c> do <see cref="ARBot.Common.Configuration.Profile.SafeDist"/> -
        /// tvrdeho minimalniho odstupu od prekazek lokalniho planovace. Bez zadaneho parametru
        /// nedela nic (plati hodnota z kodu).
        ///
        /// <para>Stejny mechanismus a stejne duvody jako <see cref="ApplyMaxSpeedFromParams"/>:
        /// <c>LocalPlannerConfig.SafeDist</c> se inicializuje z <c>Profile</c> pri VZNIKU instance
        /// (hlida <c>SafeDistParamTests</c>), takze hodnota musi byt nastavena pred slozenim runtime.
        /// Z <c>SafeDist</c> nic staticky nederivuje, past s odvozenymi poli se neotvira.</para>
        ///
        /// <para><b>Vazba na <c>PrefDist</c>:</b> <c>LocalPlannerConfig.Validate()</c> vyzaduje
        /// <c>PrefDist &gt; SafeDist</c>. Kdyby odstup skoncil na <c>PrefDist</c> nebo nad nim,
        /// planovac by pri vzniku vyhodil vyjimku a runtime by se nesložil - kvuli parametru, ktery
        /// mel robota udelat opatrnejsim. Proto se v tom pripade <c>PrefDist</c> posune nad novy
        /// odstup se zachovanym puvodnim rozestupem (dnes 0,1 m) a zaloguje se to. Vzniklo 3. 9. 2026
        /// pri ladeni odstupu z rozboru „mrkev v nesjizdne oblasti" (doc/devlog.md).</para>
        /// </summary>
        private static void ApplySafeDistFromParams()
        {
            string raw = ARBot.Common.Configuration.ParamStore.Current.Get("safedist");
            if (string.IsNullOrWhiteSpace(raw))
                return;

            double puvodni = ARBot.Common.Configuration.Profile.SafeDist;
            double pref = ARBot.Common.Configuration.Profile.PrefDist;
            // Nekladnou hodnotu odmitne uz registr pri nacteni profilu, sem se nedostane.
            double v = GetParamDouble("safedist", puvodni);

            if (v >= pref)
            {
                double rozestup = pref - puvodni;
                if (!(rozestup > 0)) rozestup = 0.1;   // kdyby nekdo v kodu nastavil PrefDist <= SafeDist
                double novyPref = v + rozestup;
                Trace.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "safedist={0:F2} je na urovni PrefDist {1:F2} m nebo nad nim -> PrefDist posunut "
                    + "na {2:F2} m (zachovan rozestup {3:F2} m), jinak by planovac odmitl vzniknout.",
                    v, pref, novyPref, rozestup));
                ARBot.Common.Configuration.Profile.PrefDist = novyPref;
            }

            Trace.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "safedist: tvrdy odstup od prekazek {0:F2} -> {1:F2} m (lokalni planovac: prujezdnost "
                + "i rychlostni obalka).",
                puvodni, v));
            ARBot.Common.Configuration.Profile.SafeDist = v;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            // Logy Avalonie presmerujeme do Trace (misto vestaveneho .LogToTrace()), ale
            // bez oblasti "Binding" - ta by jinak zaplavila panel Debug output warningy
            // z Dock.Avalonia themy (bindi na volitelne, bezne null vlastnosti). Viz
            // FilteredTraceLogSink. Nastaveno pred Configure, aby platilo i pri inicializaci.
            Logger.Sink = new FilteredTraceLogSink(LogEventLevel.Warning, LogArea.Binding);

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont();
        }
    }
}
