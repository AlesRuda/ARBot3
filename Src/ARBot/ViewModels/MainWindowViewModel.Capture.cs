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

            // ⚠️ Tlacitko v toolbaru nahrava podle HODIN, zamerne: ma byt videt, co dela
            // aplikace - jak rychle stiha kreslit, kde se zadrhne. Export zaznamu do videa je
            // jina uloha (video ma odpovidat ZAZNAMU) a ma vlastni prikaz v menu, viz
            // ExportRecordToMp4.
            _recorder.Timeline = null;
            _recorder.FpsOverride = null;

            if (!_recorder.Start(visual, format, path, out string error))
            {
                CaptureStatus = "Záznam nelze spustit: " + error;
                System.Diagnostics.Debug.WriteLine("Záznam nelze spustit: " + error);
                return;
            }

            // Odkaz na předchozí soubor by během nahrávání mátl (ukazoval by na starý výstup).
            LastFilePath = "";

            HookAutoStop();

            System.Diagnostics.Debug.WriteLine($"Záznam {format} spuštěn: {path}");
            StartStatusTimer();
            RefreshCaptureCommands();
        }

        /// <summary>
        /// <b>Export otevřeného záznamu do MP4.</b> Přehraje ho celý od začátku rychlostí, kterou
        /// nese záznam, a na jeho konci nahrávání sám ukončí.
        ///
        /// <para><b>Proč to není totéž co tlačítko v toolbaru.</b> Tlačítko nahrává podle
        /// <b>hodin</b>, a to schválně — je na něm vidět, co dělá aplikace, jak rychle stíhá
        /// kreslit a kde se zadrhne. Tady je úloha opačná: video má odpovídat <b>záznamu</b>.
        /// A to jsou dvě různé délky, protože <c>ReplayPacing.RealTime</c> zpoždění
        /// <b>nedohání</b> — přehrávání je reálný čas <i>nebo pomalejší</i>. Proto se tu nastaví
        /// <see cref="ScreenRecorder.Timeline"/> na čas záznamu a
        /// <see cref="ScreenRecorder.FpsOverride"/> na jeho skutečnou snímkovou frekvenci.</para>
        ///
        /// <para>⚠️ Export proto může trvat <b>déle</b>, než je záznam dlouhý — výsledné video má
        /// přesto délku záznamu. Okno se po tu dobu nesmí zavřít ani zmenšit (rozměr se fixuje
        /// při startu).</para>
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanExportRecord))]
        private void ExportRecordToMp4()
        {
            var fs = ARBot.Robot.ARBotRuntime.Current?.FileSource;
            if (fs == null)
            {
                CaptureStatus = "Export do MP4 jde jen v režimu View (otevřený záznam).";
                return;
            }
            if (App.MainTopLevel is not Visual visual)
            {
                CaptureStatus = "Okno není k dispozici.";
                return;
            }

            // Od zacatku: Pause je podminka SeekTo, a SeekTo(0) navic posklada stav tak, jak
            // vypadal na zacatku zaznamu (jinak by ve videu prvni sekundy visely zpravy
            // z predchoziho prehravani).
            try
            {
                fs.Pause();
                if (fs.Index != null) fs.SeekTo(0);
                else CaptureStatus = "Záznam nemá index - exportuje se od aktuální pozice.";
            }
            catch (Exception ex)
            {
                CaptureStatus = "Nelze převinout na začátek: " + ex.Message;
                System.Diagnostics.Debug.WriteLine(ex);
                return;
            }

            string path = Path.Combine(CaptureDir(), "rec-" + Stamp() + ".mp4");
            _recorder.Timeline = () => fs.ReplayTime;
            _recorder.FpsOverride = fs.FrameRate;

            if (!_recorder.Start(visual, "mp4", path, out string error))
            {
                CaptureStatus = "Export nelze spustit: " + error;
                System.Diagnostics.Debug.WriteLine("Export nelze spustit: " + error);
                return;
            }

            LastFilePath = "";
            HookAutoStop();

            // Konec zaznamu ukonci nahravani. ⚠️ Completed prijde z PREHRAVACIHO vlakna, kdezto
            // StopAsync se musi volat z UI vlakna (dokonceni kodovani sahá na recorder i na stav
            // tlacitek). A odhlasit se musi hned - jinak by se po dalsim prehrani zaznamu
            // zastavovalo nahravani, ktere uz davno nebezi.
            EventHandler hotovo = null;
            hotovo = (s, e) =>
            {
                fs.Completed -= hotovo;
                Dispatcher.UIThread.Post(() => _ = StopRecordingAsync());
            };
            fs.Completed += hotovo;

            System.Diagnostics.Debug.WriteLine($"Export zaznamu do MP4 spusten: {path}");
            StartStatusTimer();
            RefreshCaptureCommands();
            fs.Play();
        }

        /// <summary>Export jde jen ve View a jen když se zrovna nenahrává.</summary>
        private bool CanExportRecord
            => !_savingRecording && !_recorder.IsRecording
               && ARBot.Robot.ARBotRuntime.Current?.FileSource != null;

        private void HookAutoStop()
        {
            if (_autoStopHooked) return;
            // Recorder si sám říká o zastavení při dosažení limitu (nebo když spadne kodér).
            _recorder.AutoStopRequested += () => _ = StopRecordingAsync();
            _autoStopHooked = true;
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
            // Bez stropu (mp4) se misto "zbyva" nepise nic - nula by vypadala jako "hned konec".
            var zbyva = _recorder.Remaining;
            string limit = zbyva.HasValue ? $" · zbývá {zbyva.Value.TotalSeconds:0} s" : "";
            CaptureStatus = $"● REC {_recorder.Format} · {Delka(_recorder.Elapsed)} · " +
                            $"{_recorder.FrameCount} snímků{drop}{limit}";
        }

        /// <summary>
        /// Delka zaznamu pro cloveka. ⚠️ Nad minutu se prepne na <c>m:ss</c> - u hodinoveho
        /// zaznamu (maraton) je "3612,4 s" necitelne, a prave takhle dlouhe zaznamy jsou duvod,
        /// proc u mp4 strop zmizel.
        /// </summary>
        private static string Delka(TimeSpan t)
            => t.TotalMinutes < 1
                ? $"{t.TotalSeconds:0.0} s"
                : (t.TotalHours < 1 ? $"{(int)t.TotalMinutes}:{t.Seconds:00}"
                                    : $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}");

        private void RefreshCaptureCommands()
        {
            ToggleMp4Command.NotifyCanExecuteChanged();
            ToggleGifCommand.NotifyCanExecuteChanged();
            ExportRecordToMp4Command.NotifyCanExecuteChanged();
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
