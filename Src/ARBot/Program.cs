using Avalonia;
using Avalonia.Logging;
using System;
using System.Diagnostics;
using ARBot.Robot;

namespace ARBot
{
    internal sealed class Program
    {
        // POZN.: bývaly tu Program.GetParam / GetParamDouble / GetParamBool / GetParamPath - čtení
        // parametru řetězcovým klíčem s defaultem u volání. Zrušeno 4. 9. 2026: parametry se čtou
        // typovanými odkazy z registru (ParamRegistry.NoUart.Value), takže špatný klíč neprojde
        // překladačem a default je definovaný přesně jednou, v ParamDef. Viz doc/configuration.md
        // a doc/decisions.md.
        //
        // Bootstrap konfigurace (složení ParamStore, výpis účinné konfigurace, maxspeed= / safedist=
        // do Profile) se týž den přesunul do ARBot.Runtime (RuntimeBootstrap.TryConfigure), aby ho
        // sdílel konzolový ARBot.Headless. Zdůvodnění pořadí („proč před UI") je tam.

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

            // Konfigurace se sklada JAKO PRVNI - driv, nez cokoliv sahne na ParamRegistry. Vypis
            // jde pres Debug.WriteLine: do panelu Debug output a pres Info zpravu do zaznamu (v UI
            // to staci, Debug output je videt). Debug.WriteLine ma [Conditional], proto lambda.
            string? chyba = RuntimeBootstrap.TryConfigure(
                Environment.GetCommandLineArgs(), s => Debug.WriteLine(s));
            if (chyba != null)
            {
                Console.Error.WriteLine(chyba);
                Debug.WriteLine(chyba);
                Environment.Exit(RuntimeBootstrap.ExitCodeBadConfig);
                return;
            }

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
