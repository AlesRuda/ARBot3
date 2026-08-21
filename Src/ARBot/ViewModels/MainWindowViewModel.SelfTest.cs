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
                    $"images={cfg.OpenImages} robot={cfg.OpenRobotCentric} world={cfg.OpenWorld}");

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

                    // World az nakonec, aby zustal aktivnim tabem - jinak by ho screenshot
                    // (st_shot) nezachytil. Otevira se PRED Run: mapove vrstvy tak dostanou
                    // i zpravy z uvodu behu.
                    if (cfg.OpenWorld) OpenWorldView();

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

        /// <summary>
        /// Zachytí sérii snímků hlavního okna a zakóduje video. Když je k dispozici ffmpeg (auto-detekce),
        /// vytvoří komprimovaný GIF (nebo mp4) - malý a kvalitní; jinak fallback na vestavěný nekomprimovaný GIF.
        /// </summary>
        private async Task RecordVideoAsync(SelfTestConfig cfg)
        {
            int fps = (int)Math.Max(1, cfg.VideoFps);
            int frameCount = Math.Max(1, (int)(cfg.VideoSeconds * fps));
            int delayMs = 1000 / fps;
            int scale = Math.Max(1, cfg.VideoScale);

            string ffmpeg = Ffmpeg.Find(cfg.FfmpegPath);
            bool wantMp4 = string.Equals(cfg.VideoFormat, "mp4", StringComparison.OrdinalIgnoreCase);
            bool wantBuiltinGif = string.Equals(cfg.VideoFormat, "gif", StringComparison.OrdinalIgnoreCase);
            bool useFfmpeg = ffmpeg != null && !wantBuiltinGif;

            if (useFfmpeg)
            {
                // Šířku okna přečteme na UI vlákně (pro cílovou šířku videa; sudou kvůli yuv420p).
                int winW = 1280;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (App.MainTopLevel is Avalonia.Visual v && v.Bounds.Width > 0) winW = (int)v.Bounds.Width;
                });
                int targetW = Math.Max(2, (winW / scale) & ~1);

                // Snímky uložíme jako PNG do dočasné složky a necháme je zakódovat ffmpegem.
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arbot-sf-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(tmp);
                try
                {
                    int captured = 0;
                    for (int i = 0; i < frameCount; i++)
                    {
                        int idx = i;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (App.MainTopLevel is Avalonia.Visual v &&
                                ScreenCapture.SavePng(v, System.IO.Path.Combine(tmp, $"f_{idx:D5}.png")))
                                captured++;
                        });
                        await Task.Delay(delayMs);
                    }
                    string ext = wantMp4 ? "mp4" : "gif";
                    string outPath = System.IO.Path.Combine(SelfTest.MediaDir(), $"selftest-{cfg.Name}.{ext}");
                    bool ok = await Task.Run(() => wantMp4
                        ? Ffmpeg.EncodeMp4(ffmpeg, tmp, "f_%05d.png", fps, targetW, outPath)
                        : Ffmpeg.EncodeGif(ffmpeg, tmp, "f_%05d.png", fps, targetW, outPath));
                    System.Diagnostics.Debug.WriteLine($"SelfTest video (ffmpeg {ext}): {(ok ? outPath : "SELHALO")} ({captured} snímků)");
                    if (ok) return;
                }
                finally { try { System.IO.Directory.Delete(tmp, true); } catch { } }
                // Když ffmpeg selhal, spadneme do vestavěného GIF níže.
            }

            // Fallback: vestavěný nekomprimovaný GIF (bez ffmpeg).
            var frames = new List<byte[]>(frameCount);
            int w = 0, h = 0;
            for (int i = 0; i < frameCount; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (App.MainTopLevel is Avalonia.Visual v)
                    {
                        var rgb = ScreenCapture.CaptureRgb(v, downscale: scale, out int fw, out int fh);
                        if (rgb != null) { frames.Add(rgb); w = fw; h = fh; }
                    }
                });
                await Task.Delay(delayMs);
            }
            string gifPath = System.IO.Path.Combine(SelfTest.MediaDir(), $"selftest-{cfg.Name}.gif");
            bool okGif = await Task.Run(() => GifWriter.Save(frames, w, h, delayMs, gifPath));
            System.Diagnostics.Debug.WriteLine($"SelfTest video (builtin gif): {(okGif ? gifPath : "GIF selhal")} ({frames.Count} snímků {w}x{h})");
        }
    }
}
