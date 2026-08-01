using System;
using System.Collections.Generic;
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

                // Volitelně nahraj krátké video (animovaný GIF) z živých dat.
                if (cfg.Video)
                    await RecordVideoAsync(cfg);

                // Screenshot hlavního okna (s živými daty, ještě před zastavením).
                if (cfg.Shot)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        string path = System.IO.Path.Combine(SelfTest.MediaDir(), $"selftest-{cfg.Name}.png");
                        if (App.MainTopLevel is Avalonia.Visual v && ScreenCapture.SavePng(v, path))
                            System.Diagnostics.Debug.WriteLine("SelfTest screenshot: " + path);
                    });
                }

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

        /// <summary>Zachytí sérii snímků hlavního okna (na UI vlákně) a zakóduje je do animovaného GIF.</summary>
        private async Task RecordVideoAsync(SelfTestConfig cfg)
        {
            int fps = (int)Math.Max(1, cfg.VideoFps);
            int frameCount = Math.Max(1, (int)(cfg.VideoSeconds * fps));
            int delayMs = 1000 / fps;
            var frames = new List<byte[]>(frameCount);
            int w = 0, h = 0;

            for (int i = 0; i < frameCount; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (App.MainTopLevel is Avalonia.Visual v)
                    {
                        var rgb = ScreenCapture.CaptureRgb(v, downscale: Math.Max(1, cfg.VideoScale), out int fw, out int fh);
                        if (rgb != null) { frames.Add(rgb); w = fw; h = fh; }
                    }
                });
                await Task.Delay(delayMs);
            }

            // Kódování GIF je CPU náročné - mimo UI vlákno.
            string gifPath = System.IO.Path.Combine(SelfTest.MediaDir(), $"selftest-{cfg.Name}.gif");
            bool ok = await Task.Run(() => GifWriter.Save(frames, w, h, delayMs, gifPath));
            System.Diagnostics.Debug.WriteLine($"SelfTest video: {(ok ? gifPath : "GIF selhal")} ({frames.Count} snímků {w}x{h})");
        }
    }
}
