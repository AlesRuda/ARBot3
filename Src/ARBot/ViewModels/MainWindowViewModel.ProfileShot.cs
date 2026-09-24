using ARBot.Common.Configuration;
using System;
using System.Linq;
using System.Threading.Tasks;
using ARBot.Robot;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Bezobsluzny screenshot <b>profilu sceny</b> do <c>doc/media/scene-profile*.png</c>. Zapne se
    /// parametrem <c>profileshot=true</c>; zaznam jako u <see cref="StartTelemetryShotIfRequested"/>
    /// (<c>ts_rec</c>, jinak nejnovejsi indexovany v <c>records/</c>).
    ///
    /// <para>Otevre zaznam ve View, skoci doprostred (pozastavene prehravani), zmrazi profil
    /// a poridi dva snimky - stredni azimut (primo pred kamerou) a pak azimut s nejvice prekazkami,
    /// pokud nejaky je, protoze na rovne ceste neni co vysvetlovat. Pak aplikaci ukonci.</para>
    /// </summary>
    public partial class MainWindowViewModel
    {
        private void StartProfileShotIfRequested()
        {
            if (!ParamRegistry.ProfileShot.Value) return;
            _ = RunProfileShotAsync();   // fire-and-forget; sam se ukonci
        }

        private async Task RunProfileShotAsync()
        {
            try
            {
                await Task.Delay(1500);   // nech UI ustalit

                string record = ParamRegistry.TsRec.Value ?? FindNewestIndexedRecord();
                if (record == null)
                {
                    System.Diagnostics.Debug.WriteLine("ProfileShot: zadny zaznam se sidecar indexem");
                    return;
                }

                SceneProfileDocument doc = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ARBotRuntime.Current.Start(ARBot.Robot.Mode.View, record);
                    RefreshRuntimeCommands();
                    OpenReplayNav();
                    OpenSceneProfile();
                    doc = _factory.DocumentDock?.VisibleDockables?
                        .FirstOrDefault(d => d.Id == "SceneProfile") as SceneProfileDocument;
                    var src = ARBotRuntime.Current?.FileSource;
                    if (src == null) return;
                    // SeekTo jen v Paused; sam vysle posledni snimek kazde kamery, prehravat netreba.
                    try
                    {
                        src.Pause();
                        src.SeekTo(src.Count / 2);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                });
                if (doc == null) return;

                // Pockat na prvni profil (prehravani musi dojet k snimku kamery).
                for (int i = 0; i < 120; i++)
                {
                    bool ready = await Dispatcher.UIThread.InvokeAsync(() => doc.Profile != null);
                    if (ready) break;
                    await Task.Delay(500);
                }

                await Dispatcher.UIThread.InvokeAsync(() => doc.IsFrozen = true);
                await Task.Delay(800);
                await Capture("scene-profile.png");

                // Azimut s nejvice prekazkami v gridu (je-li jaky) - tam je co vysvetlovat.
                bool moved = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    int best = -1, bestN = 0;
                    for (int a = 0; a <= doc.AzimuthMax; a++)
                    {
                        doc.Azimuth = a;
                        int n = doc.Profile?.Cells?.Count(c => c.Class == ARBot.Common.Vision.TraversabilityClass.Obstacle) ?? 0;
                        if (n > bestN) { bestN = n; best = a; }
                    }
                    if (best < 0) return false;
                    doc.Azimuth = best;
                    return true;
                });
                if (moved)
                {
                    await Task.Delay(800);
                    await Capture("scene-profile-obstacle.png");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ProfileShot chyba: " + ex);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(0));
            }
        }
    }
}
