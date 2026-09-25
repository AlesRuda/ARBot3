using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Export;
using ARBot.Robot;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// File → Export GPX: ulozi otevreny zaznam (rezim View) do GPX - stopa surovych GPS fixu
    /// a stopa fuze (<see cref="GpxExport"/>). Podnabidka (24. 9. 2026) voli, jak se obe stopy
    /// oddeli: v jednom souboru, do dvou souboru (<c>-gps</c> / <c>-fuze</c>), jen GPS, jen fuze -
    /// rada prohlizecu totiz ukaze jen prvni stopu souboru. Cte se CELY zaznam vlastnim read-only streamem
    /// (jako sken telemetrie), takze prehravani se to nedotkne a na pozici prehravani nezalezi.
    /// Vysledek jde do Trace (panel Debug output), ktery se po exportu otevre.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private bool exportingGpx;

        private bool CanExportGpx => !exportingGpx
                                     && ARBotRuntime.Current?.RecordPath != null
                                     && ARBotRuntime.Current?.FileSource?.Index?.Count > 0;

        /// <param name="mode"><c>both</c> (obe stopy v jednom souboru), <c>split</c> (dva soubory),
        /// <c>gps</c> (jen GPS), <c>pose</c> (jen fuze); jine/null = <c>both</c>.</param>
        [RelayCommand(CanExecute = nameof(CanExportGpx))]
        private async Task ExportGpx(string mode)
        {
            var runtime = ARBotRuntime.Current;
            string record = runtime?.RecordPath;
            var index = runtime?.FileSource?.Index;
            if (record == null || index == null) return;

            string suffix = mode switch { "gps" => "-gps", "pose" => "-fuze", _ => "" };
            string target = await PickGpxPathAsync(record, suffix);
            if (target == null) return;

            // Dva soubory: zvoleny nazev je ZAKLAD, pripony -gps / -fuze se pridaji.
            var ukoly = new List<(string Path, GpxTracks Tracks)>();
            if (mode == "split")
            {
                string dir = Path.GetDirectoryName(target) ?? "";
                string stem = Path.GetFileNameWithoutExtension(target);
                ukoly.Add((Path.Combine(dir, stem + "-gps.gpx"), GpxTracks.GpsOnly));
                ukoly.Add((Path.Combine(dir, stem + "-fuze.gpx"), GpxTracks.PoseOnly));
            }
            else
            {
                ukoly.Add((target, mode switch
                {
                    "gps" => GpxTracks.GpsOnly,
                    "pose" => GpxTracks.PoseOnly,
                    _ => GpxTracks.Both,
                }));
            }

            exportingGpx = true;
            ExportGpxCommand.NotifyCanExecuteChanged();
            try
            {
                string name = Path.GetFileNameWithoutExtension(record);
                var catalog = ARBotRuntime.BuildCatalog();
                var hlaseni = await Task.Run(() =>
                {
                    // Zaznam se projde JEDNOU i pro dva soubory.
                    List<ARBot.Common.Logs.Message> zpravy;
                    using (var fs = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        zpravy = GpxExport.ReadMessages(fs, index, catalog);
                    var vysledky = new List<string>();
                    foreach (var (path, tracks) in ukoly)
                    {
                        var r = GpxExport.Build(zpravy, name, new GpxExportOptions { Tracks = tracks });
                        File.WriteAllText(path, r.Gpx, new UTF8Encoding(false));
                        vysledky.Add($"{path} - {r.Summary()}");
                    }
                    return vysledky;
                });
                foreach (var h in hlaseni) Trace.WriteLine($"Export GPX: {h}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Export GPX do {target} selhal: {ex.Message}");
            }
            finally
            {
                exportingGpx = false;
                ExportGpxCommand.NotifyCanExecuteChanged();
                OpenDebugOutput();
            }
        }

        /// <summary>Dialog ulozeni; nabidne <c>&lt;zaznam&gt;&lt;pripona&gt;.gpx</c> vedle zaznamu. null = zruseno.</summary>
        private static async Task<string> PickGpxPathAsync(string record, string suffix = "")
        {
            try
            {
                var sp = App.MainTopLevel?.StorageProvider;
                if (sp == null) return null;

                IStorageFolder start = null;
                try { start = await sp.TryGetFolderFromPathAsync(Path.GetDirectoryName(record)); }
                catch (Exception ex) { Debug.WriteLine(ex); }

                var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export GPX",
                    SuggestedFileName = Path.GetFileNameWithoutExtension(record) + suffix + ".gpx",
                    SuggestedStartLocation = start,
                    DefaultExtension = "gpx",
                    FileTypeChoices = new[] { new FilePickerFileType("GPX") { Patterns = new[] { "*.gpx" } } },
                });
                return file?.TryGetLocalPath();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return null;
            }
        }
    }
}
