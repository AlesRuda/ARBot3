using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ARBot.Common.Telemetry;
using ARBot.Robot;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Graf telemetrickych rad v case (faze 2 telemetrickeho pohledu). Rady sem posila
    /// <see cref="TelemetryDocument"/> - tabulka je misto, kde se vybira co kreslit.
    ///
    /// <para>Kresli <see cref="ARBot.Views.Controls.TelemetryChartControl"/>; tenhle dokument drzi
    /// seznam rad, jejich nastaveni (viditelnost, schod/rampa), hodnoty pod kurzorem prehravani
    /// a synchronizaci s prehravanim. Viz doc/telemetry-view.md.</para>
    /// </summary>
    public partial class TelemetryChartDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.TelemetryChartDocumentView);

        /// <summary>
        /// Barvy rad. Zamerne vyrazne a navzajem odlisitelne i na tmavem pozadi; devata rada
        /// zacne od zacatku (v jednom grafu jich tolik nikdo necte).
        /// </summary>
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x4F, 0xC3, 0xF7),   // svetle modra
            Color.FromRgb(0xFF, 0xB7, 0x4D),   // oranzova
            Color.FromRgb(0x81, 0xC7, 0x84),   // zelena
            Color.FromRgb(0xE5, 0x73, 0x73),   // cervena
            Color.FromRgb(0xBA, 0x68, 0xC8),   // fialova
            Color.FromRgb(0xFF, 0xF1, 0x76),   // zluta
            Color.FromRgb(0x4D, 0xB6, 0xAC),   // tyrkysova
            Color.FromRgb(0xA1, 0x88, 0x7F),   // hneda
        };

        /// <summary>Kreslene rady.</summary>
        public ObservableCollection<TelemetryChartSeries> Series { get; }
            = new ObservableCollection<TelemetryChartSeries>();

        /// <summary>Cas kurzoru prehravani v tickach (0 = neni co kreslit).</summary>
        [ObservableProperty] private long cursorTicks;

        /// <summary>Zvysuje se pri zmene UVNITR rad - control podle toho pozna, ze ma prekreslit.</summary>
        [ObservableProperty] private int revision;

        [ObservableProperty] private string status = "Vyber údaje v telemetrii: Sloupce ▾ → graf";

        /// <summary>Casovac synchronizace s prehravanim (stejny vzor jako telemetricka tabulka).</summary>
        private DispatcherTimer watchTimer;

        private long lastCursorSeq = -1;

        public TelemetryChartDocument()
        {
            Id = "TelemetryChart";
            Title = "Graf telemetrie";

            if (Design.IsDesignMode) return;

            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            watchTimer.Tick += (_, _) => SyncFromPlayback();
            watchTimer.Start();
        }

        /// <summary>
        /// Prevezme rady z telemetricke tabulky. Nastaveni uz kreslenych rad (viditelnost,
        /// schod/rampa) se <b>zachova</b> - jinak by pridani dalsiho udaje shodilo, co si uzivatel
        /// v grafu nastavil.
        /// </summary>
        public void SetSeries(IReadOnlyList<TelemetrySeries> series)
        {
            var previous = new Dictionary<string, TelemetryChartSeries>(StringComparer.Ordinal);
            foreach (var s in Series)
                previous[s.Header] = s;

            Series.Clear();

            if (series != null)
            {
                for (int i = 0; i < series.Count; i++)
                {
                    string header = series[i].Spec?.Header ?? "?";
                    var item = new TelemetryChartSeries(series[i], Palette[i % Palette.Length], OnSeriesChanged);

                    if (previous.TryGetValue(header, out var old))
                    {
                        item.IsVisible = old.IsVisible;
                        item.IsStep = old.IsStep;
                    }

                    Series.Add(item);
                }
            }

            UpdateStatus();
            UpdateCursorValues();
            Revision++;
        }

        /// <summary>Zmena uvnitr rady (viditelnost, schod/rampa) - jen prekreslit.</summary>
        private void OnSeriesChanged(TelemetryChartSeries series)
        {
            UpdateStatus();
            Revision++;
        }

        /// <summary>Vsechny rady jako schody (drzena hodnota plati az do dalsiho prichodu).</summary>
        [RelayCommand]
        private void AllSteps() => SetAllSteps(true);

        /// <summary>Vsechny rady jako rampy (mezi prichody se interpoluje).</summary>
        [RelayCommand]
        private void AllRamps() => SetAllSteps(false);

        private void SetAllSteps(bool step)
        {
            foreach (var s in Series)
                s.IsStep = step;
        }

        /// <summary>Zpet na cely casovy rozsah dat (dvojklik v grafu dela totez).</summary>
        [RelayCommand]
        private void ResetZoom() => ResetViewRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Zadost o zruseni priblizeni - View ji predá controlu.</summary>
        public event EventHandler ResetViewRequested;

        /// <summary>
        /// Srovna kurzor s prehravanim: cas posledni prehrane zpravy vezme z indexu (<c>Cursor</c>
        /// je <c>Seq</c> NASLEDUJICI zpravy, takze posledni prehrana je <c>Cursor - 1</c> - tatáž
        /// konvence jako v tabulce).
        /// </summary>
        private void SyncFromPlayback()
        {
            var src = ARBotRuntime.Current?.FileSource;
            var index = src?.Index;
            if (index == null || index.Count == 0) return;

            long seq = src.Cursor - 1;
            if (seq == lastCursorSeq) return;
            lastCursorSeq = seq;

            if (seq < 0) { CursorTicks = 0; return; }
            if (seq > index.Count - 1) seq = index.Count - 1;

            var e = index[(int)seq];
            CursorTicks = e.CaptureTicks != 0 ? e.CaptureTicks : e.ArrivalTicks;
            UpdateCursorValues();
        }

        /// <summary>Do legendy dopise hodnotu kazde rady v case kurzoru (cte se jako schod).</summary>
        private void UpdateCursorValues()
        {
            foreach (var s in Series)
                s.UpdateCursorValue(CursorTicks);
        }

        /// <summary>
        /// Klik do grafu = skok v prehravani na ten cas. Cas se prelozi na <c>Seq</c> pulenim
        /// v indexu (posledni zprava, ktera uz v tom case byla).
        /// </summary>
        public void SeekToTime(long ticks)
        {
            var src = ARBotRuntime.Current?.FileSource;
            var index = src?.Index;
            if (index == null || index.Count == 0) return;

            int lo = 0, hi = index.Count - 1, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var e = index[mid];
                long t = e.CaptureTicks != 0 ? e.CaptureTicks : e.ArrivalTicks;
                if (t <= ticks) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            try
            {
                src.Pause();
                src.SeekTo(best);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void UpdateStatus()
        {
            int shown = 0;
            foreach (var s in Series)
                if (s.IsVisible) shown++;

            Status = Series.Count == 0
                ? "Vyber údaje v telemetrii: Sloupce ▾ → graf"
                : $"{shown} z {Series.Count} řad · kolečko = lupa času, Ctrl+kolečko = lupa hodnot, "
                  + "pravé tlačítko táhne, dvojklik = celý rozsah, klik = skok v přehrávání";
        }

        /// <summary>Skryty tab nema co synchronizovat - viz Views/README.md.</summary>
        protected override void OnActiveChanged(bool active)
        {
            if (watchTimer == null) return;

            if (active) { watchTimer.Start(); SyncFromPlayback(); }
            else watchTimer.Stop();
        }

        public void Dispose()
        {
            watchTimer?.Stop();
            watchTimer = null;
        }
    }

    /// <summary>Jedna rada v grafu: data + jak se kresli + hodnota pod kurzorem prehravani.</summary>
    public partial class TelemetryChartSeries : ObservableObject
    {
        private readonly Action<TelemetryChartSeries> onChanged;

        public TelemetryChartSeries(TelemetrySeries data, Color color,
                                    Action<TelemetryChartSeries> onChanged)
        {
            Data = data;
            Color = color;
            this.onChanged = onChanged;

            // Vyctove a logicke udaje davaji smysl jen jako schod (mezi "Driving" a "Blocked"
            // se nic neinterpoluje); u cisel je vychozi rampa.
            isStep = data?.Spec?.Text != null;

            Brush = new SolidColorBrush(color);
        }

        /// <summary>Body rady (jen skutecne prichody).</summary>
        public TelemetrySeries Data { get; }

        /// <summary>Barva rady (control i legenda).</summary>
        public Color Color { get; }

        /// <summary>Barva pro legendu v XAML.</summary>
        public IBrush Brush { get; }

        public string Header => Data?.Spec?.Header ?? "?";
        public string Description => Data?.Spec?.Description;

        /// <summary>Rozsah rady do legendy - osa Y je per rada, takze bez tohohle by nebylo
        /// poznat, jak velke zmeny krivka vlastne ukazuje.</summary>
        public string RangeText => Data == null || Data.Count == 0
            ? "(žádný příchod)"
            : $"{Data.TextOf(Data.Min)} … {Data.TextOf(Data.Max)} · {Data.Count} b.";

        [ObservableProperty] private bool isVisible = true;

        /// <summary>Schod (hodnota plati do dalsiho prichodu) vs. rampa (interpolace).</summary>
        [ObservableProperty] private bool isStep;

        /// <summary>Hodnota rady v case kurzoru prehravani (do legendy).</summary>
        [ObservableProperty] private string cursorText = "-";

        /// <summary>Prepocita hodnotu pod kurzorem prehravani.</summary>
        public void UpdateCursorValue(long ticks)
        {
            double? v = ticks > 0 ? Data?.ValueAtTime(ticks) : null;
            CursorText = v.HasValue ? Data.TextOf(v.Value) : "-";
        }

        partial void OnIsVisibleChanged(bool value) => onChanged?.Invoke(this);
        partial void OnIsStepChanged(bool value) => onChanged?.Invoke(this);
    }
}
