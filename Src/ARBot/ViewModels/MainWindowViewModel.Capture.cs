using System;
using System.IO;
using System.Threading.Tasks;
using ARBot.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Snímek obrazovky a videozáznam okna z toolbaru (viz doc/screen-capture.md). Tenká vrstva nad
    /// <see cref="ScreenCapture"/> / <see cref="ScreenRecorder"/>: pojmenuje soubor, přepíná stav
    /// tlačítek a hlásí výsledek. Záznam běží vždy jen jeden - druhý formát je mezitím zakázaný.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private readonly ScreenRecorder _recorder = new();
        private DispatcherTimer _captureStatusTimer;
        private bool _autoStopHooked;
        private bool _savingRecording;

        /// <summary>Hláška vedle tlačítek (souhrn posledního výstupu / průběh záznamu / chyba).</summary>
        [ObservableProperty]
        private string captureStatus = "";

        /// <summary>
        /// Cesta k poslednímu uloženému souboru; prázdná, když žádný není (nebo právě běží záznam).
        /// V toolbaru se zobrazuje jako odkaz, který soubor otevře v přidružené aplikaci.
        /// </summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(LastFileName))]
        [NotifyPropertyChangedFor(nameof(HasLastFile))]
        [NotifyCanExecuteChangedFor(nameof(OpenLastFileCommand))]
        private string lastFilePath = "";

        /// <summary>Jméno posledního souboru (text odkazu; celá cesta zůstává v tooltipu).</summary>
        public string LastFileName
            => string.IsNullOrEmpty(LastFilePath) ? "" : Path.GetFileName(LastFilePath);

        /// <summary>Je co nabídnout k otevření?</summary>
        public bool HasLastFile => !string.IsNullOrEmpty(LastFilePath);

        /// <summary>Běží záznam do mp4?</summary>
        public bool IsRecordingMp4 => _recorder.IsRecording && _recorder.Format == "mp4";
        /// <summary>Běží záznam do GIF?</summary>
        public bool IsRecordingGif => _recorder.IsRecording && _recorder.Format == "gif";

        /// <summary>Popisek tlačítka mp4 (start/stop podle stavu).</summary>
        public string Mp4ButtonText => IsRecordingMp4 ? "■ Stop MP4" : "● MP4";
        /// <summary>Popisek tlačítka GIF (start/stop podle stavu).</summary>
        public string GifButtonText => IsRecordingGif ? "■ Stop GIF" : "● GIF";

        // Přepínat lze jen formát, který právě běží (zastavení), nebo cokoli když nic neběží;
        // během ukládání (kódování) jsou obě tlačítka zamčená.
        private bool CanToggleMp4 => !_savingRecording && (!_recorder.IsRecording || _recorder.Format == "mp4");
        private bool CanToggleGif => !_savingRecording && (!_recorder.IsRecording || _recorder.Format == "gif");

        /// <summary>Uloží PNG snímek hlavního okna do <c>doc/media/</c>.</summary>
        [RelayCommand]
        private void CaptureShot()
        {
            try
            {
                string path = Path.Combine(CaptureDir(), "shot-" + Stamp() + ".png");
                bool ok = App.MainTopLevel is Visual v && ScreenCapture.SavePng(v, path);
                CaptureStatus = ok ? "Snímek uložen" : "Snímek se nepodařilo uložit.";
                LastFilePath = ok ? path : "";
                System.Diagnostics.Debug.WriteLine("Snímek obrazovky: " + (ok ? path : "SELHALO"));
            }
            catch (Exception ex)
            {
                CaptureStatus = "Snímek selhal: " + ex.Message;
                LastFilePath = "";
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>Otevře poslední uložený soubor v přidružené aplikaci (prohlížeč / přehrávač).</summary>
        [RelayCommand(CanExecute = nameof(HasLastFile))]
        private void OpenLastFile()
        {
            if (!ShellOpen.File(LastFilePath))
                CaptureStatus = "Soubor se nepodařilo otevřít.";
        }

        /// <summary>
        /// Otevře složku s výstupy (<c>doc/media/</c>); je-li něco uloženo, rovnou v ní soubor označí.
        /// </summary>
        [RelayCommand]
        private void OpenCaptureFolder()
        {
            bool ok = HasLastFile ? ShellOpen.Reveal(LastFilePath) : ShellOpen.Folder(CaptureDir());
            if (!ok)
                CaptureStatus = "Složku se nepodařilo otevřít: " + CaptureDir();
        }

        /// <summary>Spustí/zastaví videozáznam okna do mp4 (H.264, vyžaduje ffmpeg).</summary>
        [RelayCommand(CanExecute = nameof(CanToggleMp4))]
        private Task ToggleMp4() => ToggleRecordingAsync("mp4");

        /// <summary>Spustí/zastaví videozáznam okna do animovaného GIF.</summary>
        [RelayCommand(CanExecute = nameof(CanToggleGif))]
        private Task ToggleGif() => ToggleRecordingAsync("gif");

        private async Task ToggleRecordingAsync(string format)
        {
            if (_recorder.IsRecording)
            {
                if (_recorder.Format == format)
                    await StopRecordingAsync();
                return;
            }

            if (App.MainTopLevel is not Visual visual)
            {
                CaptureStatus = "Okno není k dispozici.";
                return;
            }

            string path = Path.Combine(CaptureDir(), "rec-" + Stamp() + "." + format);
            if (!_recorder.Start(visual, format, path, out string error))
            {
                CaptureStatus = "Záznam nelze spustit: " + error;
                System.Diagnostics.Debug.WriteLine("Záznam nelze spustit: " + error);
                return;
            }

            // Odkaz na předchozí soubor by během nahrávání mátl (ukazoval by na starý výstup).
            LastFilePath = "";

            if (!_autoStopHooked)
            {
                // Recorder si sám říká o zastavení při dosažení limitu (nebo když spadne kodér).
                _recorder.AutoStopRequested += () => _ = StopRecordingAsync();
                _autoStopHooked = true;
            }

            System.Diagnostics.Debug.WriteLine($"Záznam {format} spuštěn: {path}");
            StartStatusTimer();
            RefreshCaptureCommands();
        }

        private async Task StopRecordingAsync()
        {
            if (_savingRecording || !_recorder.IsRecording) return;

            _savingRecording = true;
            StopStatusTimer();
            CaptureStatus = "Ukládám záznam…";
            RefreshCaptureCommands();
            try
            {
                var res = await _recorder.StopAsync();
                CaptureStatus = res.Message;
                LastFilePath = res.Ok ? res.Path : "";
                System.Diagnostics.Debug.WriteLine($"Záznam: {res.Message} → {res.Path}");
            }
            catch (Exception ex)
            {
                CaptureStatus = "Chyba při ukládání záznamu: " + ex.Message;
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                _savingRecording = false;
                RefreshCaptureCommands();
            }
        }

        /// <summary>Průběžná hláška o běžícím záznamu (délka, počet snímků, zbývající čas).</summary>
        private void StartStatusTimer()
        {
            _captureStatusTimer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, __) => UpdateRecordingStatus());
            UpdateRecordingStatus();
            _captureStatusTimer.Start();
        }

        private void StopStatusTimer() => _captureStatusTimer?.Stop();

        private void UpdateRecordingStatus()
        {
            if (!_recorder.IsRecording) return;
            string drop = _recorder.DroppedFrames > 0 ? $", {_recorder.DroppedFrames} zahozeno" : "";
            CaptureStatus = $"● REC {_recorder.Format} · {_recorder.Elapsed.TotalSeconds:0.0} s · " +
                            $"{_recorder.FrameCount} snímků{drop} · zbývá {_recorder.Remaining.TotalSeconds:0} s";
        }

        private void RefreshCaptureCommands()
        {
            ToggleMp4Command.NotifyCanExecuteChanged();
            ToggleGifCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsRecordingMp4));
            OnPropertyChanged(nameof(IsRecordingGif));
            OnPropertyChanged(nameof(Mp4ButtonText));
            OnPropertyChanged(nameof(GifButtonText));
        }

        /// <summary>Složka pro snímky a videa - <c>doc/media/</c> v kořenu repa (shodně se self-testem).</summary>
        private static string CaptureDir()
        {
            string dir = SelfTest.MediaDir();
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }
}
