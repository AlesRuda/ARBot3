using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Znovupoužitelný řádek „Snímek": <b>číslo snímku · frekvence · čas</b> (a volitelná
    /// poznámka), společný pro dokumenty všech senzorů — kamera, IMU, GPS, motor.
    ///
    /// <para><b>Proč vlastní control a ne tři <c>TextBlock</c>y v každém pohledu:</b> ten řádek
    /// byl původně jeden slepený <c>TextBlock</c> (<c>"#8991   0.3 Hz   07:12:17.097"</c>) a údaje
    /// v něm po sobě <b>poskakovaly</b> — mění se totiž počet znaků (číslo snímku přeteče o řád,
    /// frekvence z „0.8" na „30.0"), takže ani neproporcionální font nepomůže. Tady se kreslí na
    /// <b>pevné souřadnice</b>, takže se sloupce nemohou navzájem posunout z principu, a je to
    /// na jednom místě místo čtyřikrát opsaného gridu.</para>
    ///
    /// <para>Hodnoty se předávají <b>syrové</b> (číslo, perioda, čas) a formátuje si je control —
    /// jinak by se stejné formátování opisovalo v každém ViewModelu.</para>
    /// </summary>
    public class SensorFrameInfoControl : Control
    {
        /// <summary>Pořadové číslo snímku (<c>SensorStateBase.FrameNum</c>).</summary>
        public static readonly StyledProperty<long> FrameNumProperty =
            AvaloniaProperty.Register<SensorFrameInfoControl, long>(nameof(FrameNum));

        /// <summary>
        /// Perioda příjmu snímku. Frekvence se z ní počítá jako 1/perioda; nekladná perioda
        /// znamená „není známá" a místo čísla zůstane prázdno (ne nula, ne nekonečno).
        /// </summary>
        public static readonly StyledProperty<TimeSpan> PeriodProperty =
            AvaloniaProperty.Register<SensorFrameInfoControl, TimeSpan>(nameof(Period));

        /// <summary>Čas snímku.</summary>
        public static readonly StyledProperty<DateTime> TimeProperty =
            AvaloniaProperty.Register<SensorFrameInfoControl, DateTime>(nameof(Time));

        /// <summary>Poznámka za časem (např. „bez měření"); prázdná se nekreslí.</summary>
        public static readonly StyledProperty<string?> NoteProperty =
            AvaloniaProperty.Register<SensorFrameInfoControl, string?>(nameof(Note));

        /// <summary>Popisek vlevo; prázdný se nekreslí (overlay v kameře ho nechce).</summary>
        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<SensorFrameInfoControl, string?>(nameof(Label), "Snímek");

        /// <summary>Barva hodnot (overlay nad obrazem chce průsvitnou bílou).</summary>
        public static readonly StyledProperty<IBrush?> ForegroundProperty =
            AvaloniaProperty.Register<SensorFrameInfoControl, IBrush?>(nameof(Foreground));

        public long FrameNum { get => GetValue(FrameNumProperty); set => SetValue(FrameNumProperty, value); }
        public TimeSpan Period { get => GetValue(PeriodProperty); set => SetValue(PeriodProperty, value); }
        public DateTime Time { get => GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
        public string? Note { get => GetValue(NoteProperty); set => SetValue(NoteProperty, value); }
        public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

        static SensorFrameInfoControl()
        {
            AffectsRender<SensorFrameInfoControl>(
                FrameNumProperty, PeriodProperty, TimeProperty, NoteProperty,
                LabelProperty, ForegroundProperty);
            AffectsMeasure<SensorFrameInfoControl>(LabelProperty);
        }

        // Pevné sloupce [px] od levého okraje. Tohle je celý smysl controlu: hodnota se kreslí
        // VZDY na stejné x, takže delší sousední údaj s ní nepohne.
        private const double LabelW = 96;   // popisek "Snímek"
        private const double NumW = 78;     // "#123456"
        private const double HzW = 52;      // "123.4"
        private const double HzUnitW = 26;  // "Hz"
        private const double TimeW = 104;   // "07:12:17.097"
        private const double FontSize = 13;

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = LabelX0() + NumW + HzW + HzUnitW + TimeW + 90 /* místo na poznámku */;
            return new Size(Math.Min(w, availableSize.Width), FontSize + 6);
        }

        private double LabelX0() => string.IsNullOrEmpty(Label) ? 0 : LabelW;

        public override void Render(DrawingContext ctx)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
                return;

            var val = Foreground ?? Brushes.White;
            IBrush dim = new SolidColorBrush(Color.FromArgb(0xA0, 0xA0, 0xA0, 0xA0));
            double cy = Bounds.Height / 2;
            double x = 0;

            if (!string.IsNullOrEmpty(Label))
            {
                Draw(ctx, Label!, x, cy, dim, mono: false, rightEdge: null);
                x += LabelW;
            }

            // Jeste nedorazil zadny snimek: pomlcka, ne "#0  00:00:00.000" - nula neni mereni.
            if (Time == default && FrameNum == 0)
            {
                Draw(ctx, "-", x, cy, dim, mono: true, rightEdge: x + NumW);
                return;
            }

            // číslo snímku - zarovnané doprava ve svém sloupci
            Draw(ctx, "#" + FrameNum.ToString(CultureInfo.InvariantCulture), x, cy, val,
                 mono: true, rightEdge: x + NumW);
            x += NumW;

            // frekvence; neplatná perioda = prázdno
            string hz = Period.TotalSeconds > 0
                ? (1.0 / Period.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)
                : "";
            Draw(ctx, hz, x, cy, val, mono: true, rightEdge: x + HzW);
            x += HzW;
            if (hz.Length > 0)
                Draw(ctx, " Hz", x, cy, dim, mono: false, rightEdge: null);
            x += HzUnitW;

            Draw(ctx, Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), x, cy, val,
                 mono: true, rightEdge: null);
            x += TimeW;

            if (!string.IsNullOrEmpty(Note))
                Draw(ctx, Note!, x, cy, Brushes.Orange, mono: false, rightEdge: null);
        }

        /// <summary>
        /// Vykreslí text na <paramref name="x"/>; když je zadaná <paramref name="rightEdge"/>,
        /// zarovná ho k ní doprava (tím se čísla zarovnají pod sebe bez ohledu na počet číslic).
        /// </summary>
        private static void Draw(DrawingContext ctx, string text, double x, double cy,
                                 IBrush brush, bool mono, double? rightEdge)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(mono ? "Consolas,monospace" : "sans-serif"), FontSize, brush);
            double px = rightEdge is double r ? r - ft.Width : x;
            ctx.DrawText(ft, new Point(px, cy - ft.Height / 2));
        }
    }
}
