using System;
using ARBot.Common.Communication;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Navigacni nastroj pro rezim View: ovlada <see cref="FileMessageSource"/> (Play/Pause,
    /// krokovani, seek na pozici). Timeline = poradove cislo zpravy (<c>Seq</c>) z indexu.
    /// Seek rekonstruuje stav (posledni &le; pozice pro kazdy stream) a emituje na Stream,
    /// takze pripojene dokumenty (napr. <see cref="ImageDocument"/>) se aktualizuji.
    ///
    /// <para>Nastroj o indexu vi jen kvuli rozsahu/pozici; samotnou rekonstrukci resi
    /// <see cref="FileMessageSource.SeekTo"/>.</para>
    /// </summary>
    public partial class ReplayNavTool : ToolBase
    {
        public override Type ViewType => typeof(ARBot.Views.ReplayNavToolView);

        private readonly FileMessageSource src;
        private readonly DispatcherTimer playTimer;   // behem Play polluje pozici (slider + text)
        private bool suppressSeek;

        [ObservableProperty] private int position;
        [ObservableProperty] private int maximum;
        [ObservableProperty] private bool isPlaying;
        [ObservableProperty] private string info = "-";

        /// <summary>Konstruktor pro design-time / navrhar (bez zdroje).</summary>
        public ReplayNavTool()
        {
            Id = "ReplayNav";
            Title = "Replay";
        }

        /// <param name="src">Zdroj replay (musi byt vytvoren s indexem pro seek).</param>
        public ReplayNavTool(FileMessageSource src) : this()
        {
            this.src = src;
            Maximum = Math.Max(0, (src?.Count ?? 0) - 1);
            IsPlaying = src != null && src.State == FileMessageSource.ReplayState.Playing;

            // Behem Play nema FileMessageSource udalost o postupu -> pollujeme kurzor casovacem.
            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            playTimer.Tick += (_, _) => SyncFromPlayback();
            if (src != null)
                src.Completed += OnCompleted;
            if (IsPlaying)
                playTimer.Start();

            UpdateInfo();
        }

        /// <summary>Spusti/obnovi prehravani od aktualniho kurzoru.</summary>
        [RelayCommand]
        private void Play()
        {
            src?.Play();
            IsPlaying = src != null && src.State == FileMessageSource.ReplayState.Playing;
            if (IsPlaying)
                playTimer?.Start();
        }

        /// <summary>Pozastavi prehravani a synchronizuje pozici na kurzor.</summary>
        [RelayCommand]
        private void Pause()
        {
            if (src == null) return;
            playTimer?.Stop();
            src.Pause();
            IsPlaying = false;
            SetPositionSilently((int)Math.Min(src.Cursor, Maximum));
            UpdateInfo();
        }

        /// <summary>Tik casovace behem Play: srovna slider/text s aktualnim kurzorem zdroje.</summary>
        private void SyncFromPlayback()
        {
            if (src == null)
                return;
            if (src.State != FileMessageSource.ReplayState.Playing)
            {
                // Prehravani skoncilo/pozastaveno jinou cestou -> zastavit polling a srovnat stav.
                playTimer?.Stop();
                IsPlaying = false;
            }
            SetPositionSilently((int)Math.Min(src.Cursor, Maximum));
            UpdateInfo();
        }

        /// <summary>Konec zaznamu (z vlakna prehravani) -> na UI vlakne srovnej stav.</summary>
        private void OnCompleted(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                playTimer?.Stop();
                IsPlaying = false;
                SetPositionSilently(Maximum);
                UpdateInfo();
            });
        }

        /// <summary>Krok vpred (o jednu pozici).</summary>
        [RelayCommand]
        private void StepForward() => SeekToPosition(Position + 1);

        /// <summary>Krok zpet (o jednu pozici).</summary>
        [RelayCommand]
        private void StepBack() => SeekToPosition(Position - 1);

        // Uzivatelska zmena slideru -> seek.
        partial void OnPositionChanged(int value)
        {
            if (suppressSeek) return;
            SeekToPosition(value);
        }

        private void SeekToPosition(int pos)
        {
            if (src?.Index == null) return;   // seek vyzaduje index

            if (pos < 0) pos = 0;
            if (pos > Maximum) pos = Maximum;

            // Seek je povolen jen v Paused.
            playTimer?.Stop();
            src.Pause();
            IsPlaying = false;
            try { src.SeekTo(pos); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            SetPositionSilently(pos);
            UpdateInfo();
        }

        private void SetPositionSilently(int value)
        {
            suppressSeek = true;
            Position = value;
            suppressSeek = false;
        }

        private void UpdateInfo()
        {
            if (src?.Index == null) { Info = "(bez indexu)"; return; }
            int p = Position;
            if (p >= 0 && p < src.Index.Count)
            {
                var e = src.Index[p];
                Info = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}/{1}  {2}  {3:HH:mm:ss.fff}",
                    p, src.Index.Count - 1, e.MsgName, e.CaptureTime);
            }
            else
            {
                Info = $"{p}/{Maximum}";
            }
        }
    }
}
