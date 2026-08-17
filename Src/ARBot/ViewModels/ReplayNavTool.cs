using System;
using System.Collections.Generic;
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

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlayPauseText))]
        private bool isPlaying;

        [ObservableProperty] private string info = "-";

        /// <summary>Popisek jednoho prepinaciho tlacitka Play/Pauza.</summary>
        public string PlayPauseText => IsPlaying ? "⏸ Pauza" : "▶ Play";

        /// <summary>Radky indexu pro grid (Seq/typ/jmeno/cas). Vyber v gridu = <see cref="Position"/>.</summary>
        public IReadOnlyList<IndexEntry> Rows => src?.Index;

        /// <summary>Zdroj, ke kteremu je nastroj navazany. Slouzi k rozpoznani, zda uz otevreny
        /// panel patri k prave prehravanemu zaznamu, nebo je z predchoziho (a ma se nahradit).</summary>
        public FileMessageSource Source => src;

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

            // FileMessageSource nema udalost o postupu -> pollujeme kurzor casovacem. Casovac bezi
            // PORAD, ne jen behem Play: kurzorem muze pohnout i nekdo jiny (telemetricka tabulka
            // umi skocit na radek), a to se musi projevit i tady.
            playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            playTimer.Tick += (_, _) => SyncFromPlayback();
            if (src != null)
            {
                src.Completed += OnCompleted;
                playTimer.Start();
            }

            SetPositionSilently(CursorPosition);
            UpdateInfo();
        }

        /// <summary>Jedno tlacitko pro obe akce - prehrava se, nebo stoji, treti stav neni.</summary>
        [RelayCommand]
        private void TogglePlay()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        /// <summary>Spusti/obnovi prehravani od aktualniho kurzoru.</summary>
        private void Play()
        {
            src?.Play();
            IsPlaying = src != null && src.State == FileMessageSource.ReplayState.Playing;
        }

        /// <summary>Pozastavi prehravani a synchronizuje pozici na kurzor.</summary>
        private void Pause()
        {
            if (src == null) return;
            src.Pause();
            IsPlaying = false;
            SetPositionSilently(CursorPosition);
            UpdateInfo();
        }

        /// <summary>
        /// Pozice POSLEDNI prehrane zpravy. <see cref="FileMessageSource.Cursor"/> je <c>Seq</c>
        /// <b>nasledujici</b> zpravy (a <c>SeekTo(pos)</c> nastavi kurzor na <c>pos+1</c>), takze
        /// pozice v timeline je o jednu min - jinak by slider po kazdem seeku ujel o radek.
        /// </summary>
        private int CursorPosition
        {
            get
            {
                if (src == null) return 0;
                long pos = src.Cursor - 1;
                if (pos < 0) pos = 0;
                if (pos > Maximum) pos = Maximum;
                return (int)pos;
            }
        }

        /// <summary>Tik casovace: srovna slider/text s aktualnim kurzorem zdroje (i kdyz stoji -
        /// kurzorem mohl pohnout skok z telemetricke tabulky).</summary>
        private void SyncFromPlayback()
        {
            if (src == null)
                return;
            if (src.State != FileMessageSource.ReplayState.Playing)
                IsPlaying = false;   // prehravani skoncilo/pozastaveno jinou cestou

            SetPositionSilently(CursorPosition);
            UpdateInfo();
        }

        /// <summary>Konec zaznamu (z vlakna prehravani) -> na UI vlakne srovnej stav.</summary>
        private void OnCompleted(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
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

        /// <summary>Nasledujici zprava tehoz proudu (viz <see cref="SeekToSame"/>).</summary>
        [RelayCommand]
        private void NextSame() => SeekToSame(+1);

        /// <summary>Predchozi zprava tehoz proudu (viz <see cref="SeekToSame"/>).</summary>
        [RelayCommand]
        private void PrevSame() => SeekToSame(-1);

        /// <summary>
        /// Skoci na nejblizsi zpravu TEHOZ proudu v danem smeru, tedy se stejnou dvojici
        /// <c>(MsgName, Name)</c>.
        /// <para>Tutez dvojici pouziva jako identitu proudu i <see cref="FileMessageSource.SeekTo"/>
        /// pri rekonstrukci stavu, takze "stejna zprava" znamena dalsi snimek TEZE kamery, ne
        /// libovolne kamery - jinak by se krokovani prepinalo mezi levou a pravou.</para>
        /// <para>Kdyz uz v tom smeru zadna takova neni, nedela nic (zustane na miste).</para>
        /// </summary>
        private void SeekToSame(int direction)
        {
            var idx = src?.Index;
            if (idx == null) return;

            int from = Position;
            if (from < 0 || from >= idx.Count) return;

            var cur = idx[from];
            for (int i = from + direction; i >= 0 && i < idx.Count; i += direction)
            {
                if (string.Equals(idx[i].MsgName, cur.MsgName, StringComparison.Ordinal)
                    && string.Equals(idx[i].Name, cur.Name, StringComparison.Ordinal))
                {
                    SeekToPosition(i);
                    return;
                }
            }
        }

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

        /// <summary>
        /// Pozice jako <c>poradi/celkem</c>. Zamerne NIC dalsiho - typ zpravy i cas jsou videt
        /// na vybranem radku gridu, takze by se tu jen duplikovaly a braly misto tomu gridu.
        /// </summary>
        private void UpdateInfo()
        {
            if (src?.Index == null) { Info = "(bez indexu)"; return; }
            Info = $"{Position}/{Maximum}";
        }
    }
}
