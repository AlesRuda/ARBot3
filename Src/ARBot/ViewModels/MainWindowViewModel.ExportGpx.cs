using System;
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
    /// a stopa fuze (<see cref="GpxExport"/>). Cte se CELY zaznam vlastnim read-only streamem
    /// (jako sken telemetrie), takze prehravani se to nedotkne a na pozici prehravani nezalezi.
    /// Vysledek jde do Trace (panel Debug output), ktery se po exportu otevre.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private bool exportingGpx;

        private bool CanExportGpx => !exportingGpx
                                     && ARBotRuntime.Current?.RecordPath != null
                                     && ARBotRuntime.Current?.FileSource?.Index?.Count > 0;

        [RelayCommand(CanExecute = nameof(CanExportGpx))]
        private async Task ExportGpx()
        {
            var runtime = ARBotRuntime.Current;
            string record = runtime?.RecordPath;
            var index = runtime?.FileSource?.Index;
            if (record == null || index == null) return;

            string target = await PickGpxPathAsync(record);
            if (target == null) return;

            exportingGpx = true;
            ExportGpxCommand.NotifyCanExecuteChanged();
            try
            {
                string name = Path.GetFileNameWithoutExtension(record);
                var catalog = ARBotRuntime.BuildCatalog();
                var result = await Task.Run(() =>
                {
                    using var fs = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var r = GpxExport.FromRecord(fs, index, catalog, name);
                    File.WriteAllText(target, r.Gpx, new UTF8Encoding(false));
                    return r;
                });
                Trace.WriteLine($"Export GPX: {target} - {result.Summary()}");
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

        /// <summary>Dialog ulozeni; nabidne <c>&lt;zaznam&gt;.gpx</c> vedle zaznamu. null = zruseno.</summary>
        private static async Task<string> PickGpxPathAsync(string record)
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
                    SuggestedFileName = Path.GetFileNameWithoutExtension(record) + ".gpx",
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
