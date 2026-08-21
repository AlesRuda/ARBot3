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
        /// Vraci hodnotu parametru z prikazove radky. Pokud parametr neni zadany, vraci defaultni hodnotu.
        /// </summary>
        /// <param name="param"></param>
        /// <param name="def"></param>
        /// <returns></returns>
        public static string GetParam(string param, string def = null)
        {
            var args = Environment.GetCommandLineArgs();
            string pa = param.ToLower() + "=";
            string val = args.FirstOrDefault((s) => s.ToLower().StartsWith(pa));
            if (val != null)
                val = val.Substring(pa.Length);
            else
                val = def;
            Debug.WriteLine(string.Format("{0}={1}", param, val));
            return val;
        }
        /// <summary>
        /// Vraci hodnotu parametru z prikazove radky. Pokud parametr neni zadany, vraci defaultni hodnotu.
        /// </summary>
        /// <param name="param"></param>
        /// <param name="def"></param>
        /// <returns></returns>
        public static double GetParamDouble(string param, double def)
        {
            var str = GetParam(param);
            double val = 0;
            if (str == null || !double.TryParse(str, out val))
            {
                Debug.WriteLine(string.Format("{0} using default {1}", param, def));
                return def;
            }
            return val;
        }
        /// <summary>
        /// Vraci hodnotu parametru z prikazove radky. Pokud parametr neni zadany, vraci defaultni hodnotu.
        /// </summary>
        /// <param name="param"></param>
        /// <param name="def"></param>
        /// <returns></returns>
        public static bool GetParamBool(string param, bool def)
        {
            var str = GetParam(param);
            bool val = false;
            if (str == null || !bool.TryParse(str, out val))
            {
                Debug.WriteLine(string.Format("{0} using default {1}", param, def));
                return def;
            }
            return val;
        }

        /// <summary>
        /// Vraci hodnotu parametru z prikazove radky jako **cestu k souboru/slozce**: relativni
        /// cesta se resi proti <b>korenu repa</b> (slozka s <c>.git</c>), ne proti pracovnimu
        /// adresari procesu. Absolutni cesta se necha, jak je.
        ///
        /// <para><i>Proc:</i> pracovni adresar se lisi podle toho, jak se app spusti (z VS je to
        /// build output <c>bin\...</c>, z <c>dotnet run</c> slozka projektu), takze
        /// <c>map=OSM/Neco.osm</c> by jednou nasel a jindy ne. Proti korenu repa to plati vzdy -
        /// diky tomu mohou byt cesty v <c>launchSettings.json</c> relativni, a tedy prenositelne
        /// mezi pracovnimi kopiemi. Viz doc/virtual-hw.md.</para>
        ///
        /// <para>Bez repa (nasazeni na zarizeni) je zakladem <see cref="AppContext.BaseDirectory"/>;
        /// tam se stejne pouzivaji absolutni cesty.</para>
        /// </summary>
        public static string GetParamPath(string param, string def = null)
        {
            var val = GetParam(param, def);
            if (string.IsNullOrWhiteSpace(val))
                return val;
            try
            {
                if (System.IO.Path.IsPathRooted(val))
                    return val;
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(RepoRootOrBase(), val));
            }
            catch
            {
                return val;             // vadna cesta -> at ji resi volajici (File.Exists + hlaska)
            }
        }

        /// <summary>
        /// Koren git repa (slozka obsahujici <c>.git</c>) hledany smerem nahoru od build outputu;
        /// fallback na <see cref="AppContext.BaseDirectory"/> (nasazeni bez repa, napr. na Pi).
        /// </summary>
        public static string RepoRootOrBase()
        {
            try
            {
                var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string git = System.IO.Path.Combine(dir.FullName, ".git");
                    if (System.IO.Directory.Exists(git) || System.IO.File.Exists(git))
                        return dir.FullName;
                    dir = dir.Parent;
                }
            }
            catch { }
            return AppContext.BaseDirectory;
        }



        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

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
