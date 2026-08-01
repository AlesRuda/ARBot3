using System;
using System.Linq;
using System.Threading.Tasks;
using ARBot.Diagnostics;
using ARBot.Robot;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Bezobslužný self-test (viz <see cref="SelfTestConfig"/>): je-li zadán parametr <c>selftest=true</c>,
    /// aplikace sama otevře požadovaná okna, spustí Run, po zadaný čas nechá běžet, zastaví, zapíše
    /// souhrn z diagnostického CSV a ukončí se. Umožňuje reprodukovatelné A/B měření variant výkonu
    /// (záznam on/off, otevřená okna, UART senzory on/off, ...) bez ruční obsluhy.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Spustí self-test, je-li vyžádán parametrem <c>selftest=true</c> (jinak nedělá nic).</summary>
        private void StartSelfTestIfRequested()
        {
            var cfg = SelfTestConfig.FromArgs();
            if (!cfg.Enabled) return;
            _ = RunSelfTestAsync(cfg);   // fire-and-forget; sama se ukončí
        }

        private async Task RunSelfTestAsync(SelfTestConfig cfg)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SelfTest '{cfg.Name}' start: {cfg.Seconds}s record={cfg.Record} " +
                    $"images={cfg.OpenImages} robot={cfg.OpenRobotCentric}");

                // Počkej na inicializaci HW (kamery/porty) a chvíli na ustálení UI (kamera se připojuje líně).
                await Task.Run(() => ARBotHW.Current.WaitReady());
                await Task.Delay(1500);

                // Otevři okna + spusť Run (vše na UI vlákně).
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (cfg.OpenImages) OpenImages();
                    if (cfg.OpenRobotCentric) OpenRobotCentric();

                    // Volitelně zviditelni tab Images (jinak zůstane aktivní naposledy otevřený =
                    // robot-centric, a Images je na pozadí -> ověří gate viditelnosti obojím směrem).
                    if (cfg.OpenImages && cfg.ImagesActive)
                    {
                        var img = _factory.DocumentDock?.VisibleDockables?.FirstOrDefault(d => d.Id == "Images");
                        if (img != null) _factory.SetActiveDockable(img);
                    }

                    if (cfg.Record) RunAndLog(); else RunMode();
                });

                // Nech běžet zadaný čas.
                await Task.Delay(TimeSpan.FromSeconds(cfg.Seconds));

                // Zastav (drain + flush diagnostiky).
                await Dispatcher.UIThread.InvokeAsync(() => StopRuntime());
                await Task.Delay(300);

                // Souhrn z diagnostického CSV -> logs/selftest-result.txt.
                SelfTest.WriteSummary(cfg);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SelfTest chyba: " + ex);
            }
            finally
            {
                // Ukonči aplikaci (i při chybě), aby běh nezůstal viset.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(0);
                });
            }
        }
    }
}
