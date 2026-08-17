using System;
using System.IO;
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
    /// Bezobsluzne poriz screenshoty <b>telemetrickeho pohledu a grafu</b> do <c>doc/media/</c>
    /// (pro denicek). Zapne se parametrem <c>telemetryshot=true</c>.
    ///
    /// <para>Otevre zaznam v rezimu View (posledni <c>*.rec</c> se sidecar indexem ve slozce
    /// <c>records/</c>, nebo ten zadany v <c>ts_rec</c>), pocka na sken, posune prehravani doprostred
    /// zaznamu, poridi snimek tabulky, pak zapne par udaju do grafu, poridi snimek grafu a aplikaci
    /// ukonci. Obdoba <see cref="StartWorldShotIfRequested"/> pro telemetrii - reprodukovatelne
    /// porizeni obrazku featury bez rucni obsluhy.</para>
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>Udaje, ktere se pro snimek grafu zapnou (zahlavi z registru sloupcu).</summary>
        private static readonly string[] ChartShotColumns = { "v [m/s]", "cmd v [m/s]", "omega [°/s]" };

        /// <summary>Spusti porizeni screenshotu telemetrie, je-li vyzadano parametrem <c>telemetryshot=true</c>.</summary>
        private void StartTelemetryShotIfRequested()
        {
            if (!Program.GetParamBool("telemetryshot", false)) return;
            _ = RunTelemetryShotAsync();   // fire-and-forget; sam se ukonci
        }

        private async Task RunTelemetryShotAsync()
        {
            try
            {
                await Task.Delay(1500);   // nech UI ustalit

                string record = Program.GetParam("ts_rec") ?? FindNewestIndexedRecord();
                if (record == null)
                {
                    System.Diagnostics.Debug.WriteLine("TelemetryShot: zadny zaznam se sidecar indexem");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("TelemetryShot: " + record);

                // View + replay panel (na snimku ma byt videt i to, ze tabulka na prehravani navazuje).
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ARBotRuntime.Current.Start(ARBot.Robot.Mode.View, record);
                    RefreshRuntimeCommands();
                    OpenReplayNav();
                    OpenTelemetry();
                });

                var doc = await Dispatcher.UIThread.InvokeAsync(() =>
                    _factory.DocumentDock?.VisibleDockables?
                        .FirstOrDefault(d => d.Id == "Telemetry") as TelemetryDocument);
                if (doc == null) return;

                if (!await WaitForRows(doc)) return;

                // Prehravani doprostred zaznamu: tabulka na kurzor navaze sama (a snimek tak ukaze
                // zvyrazneny radek i vyplneny detail, ne prazdny panel).
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var src = ARBotRuntime.Current?.FileSource;
                    if (src == null) return;
                    try
                    {
                        src.Pause();
                        src.SeekTo(src.Count / 2);
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                });

                await Task.Delay(1200);   // nech dobehnout synchronizaci (100ms tik) a prekresleni
                await Capture("telemetry-view.png");

                // --- Graf: zapnout par udaju (to zaroven otevre dokument grafu). ---
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var toggle in doc.ColumnToggles)
                        if (ChartShotColumns.Contains(toggle.Label, StringComparer.Ordinal))
                            toggle.InChart = true;
                });

                await Task.Delay(2000);   // vytazeni rad + prekresleni grafu
                await Capture("telemetry-chart.png");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TelemetryShot chyba: " + ex);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(0));
            }
        }

        /// <summary>Ceka na dokonceni skenu (sken bezi mimo UI vlakno). False = nedockal se.</summary>
        private static async Task<bool> WaitForRows(TelemetryDocument doc)
        {
            for (int i = 0; i < 120; i++)      // az 60 s; velky zaznam se skenuje déle
            {
                int rows = await Dispatcher.UIThread.InvokeAsync(() => doc.Rows.Count);
                if (rows > 0) return true;
                await Task.Delay(500);
            }

            System.Diagnostics.Debug.WriteLine("TelemetryShot: tabulka se nenaplnila");
            return false;
        }

        /// <summary>Zachyti hlavni okno do <c>doc/media</c> pod danym nazvem.</summary>
        private static async Task Capture(string name)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                string path = Path.Combine(SelfTest.MediaDir(), name);
                if (App.MainTopLevel is Visual v && ScreenCapture.SavePng(v, path))
                    System.Diagnostics.Debug.WriteLine("TelemetryShot: " + path);
                else
                    System.Diagnostics.Debug.WriteLine("TelemetryShot: screenshot se nezdaril (" + name + ")");
            });
        }

        /// <summary>
        /// Nejnovejsi zaznam ve slozce <c>records/</c>, ke kteremu existuje sidecar index -
        /// bez indexu tabulku postavit nelze (viz doc/telemetry-view.md).
        /// </summary>
        private static string FindNewestIndexedRecord()
        {
            try
            {
                string dir = Path.Combine(RepoRootOrBase(), "records");
                if (!Directory.Exists(dir)) return null;

                return Directory.EnumerateFiles(dir, "*.rec")
                                .Where(f => File.Exists(f + ".idx"))
                                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                .FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return null;
            }
        }
    }
}
