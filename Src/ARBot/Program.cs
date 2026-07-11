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
