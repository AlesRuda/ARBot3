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

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
