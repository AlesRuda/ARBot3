using System;
using System.Collections.Generic;
using ARBot.Common.Telemetry;
using ARBot.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Graf telemetrickych rad: osa X = cas, kazda rada svou barvou. Kresli se vlastnim
    /// <see cref="Render"/> - data uz jsou v poli (viz <see cref="TelemetrySeries"/>), takze
    /// externi grafova knihovna by jen pridala zavislost a starost s verzemi Avalonie.
    ///
    /// <para><b>Kazda rada ma vlastni meritko osy Y</b> (autoscale na svoje min/max). Spolecna osa
    /// nedava smysl, kdyz jsou v jednom grafu metry, stupne za sekundu a stav vyctu; rozsah kazde
    /// rady je videt v legende. Osa Y s cisly se kresli, jen kdyz je viditelna prave jedna rada -
    /// tam uz je jednoznacna. <b>Lupa na ose Y</b> (Ctrl+kolecko) roztahuje vsechny rady zaroven,
    /// takze porovnani mezi nimi zustava platne.</para>
    ///
    /// <para>Ovladani: kolecko = lupa casu, Ctrl+kolecko = lupa hodnot, tazeni pravym tlacitkem =
    /// posun (obema smery), dvojklik = cely rozsah, klik = skok v prehravani
    /// (<see cref="TimePicked"/>). Pohyb mysi ukazuje <b>odectitko</b> - svislou caru s hodnotami
    /// vsech rad v tom case.</para>
    /// </summary>
    public class TelemetryChartControl : Control
    {
        /// <summary>Rady ke kresleni (poradi = poradi v legende).</summary>
        public static readonly StyledProperty<IReadOnlyList<TelemetryChartSeries>> SeriesProperty =
            AvaloniaProperty.Register<TelemetryChartControl, IReadOnlyList<TelemetryChartSeries>>(nameof(Series));

        /// <summary>Cas kurzoru prehravani v tickach (0 = nekresli se).</summary>
        public static readonly StyledProperty<long> CursorTicksProperty =
            AvaloniaProperty.Register<TelemetryChartControl, long>(nameof(CursorTicks));

        /// <summary>Zvysi ho dokument pri kazde zmene UVNITR rad (viditelnost, schod/rampa) -
        /// jinak by se control o zmene nedozvedel, protoze kolekce zustava tataz.</summary>
        public static readonly StyledProperty<int> RevisionProperty =
            AvaloniaProperty.Register<TelemetryChartControl, int>(nameof(Revision));

        public IReadOnlyList<TelemetryChartSeries> Series
        {
            get => GetValue(SeriesProperty);
            set => SetValue(SeriesProperty, value);
        }

        public long CursorTicks
        {
            get => GetValue(CursorTicksProperty);
            set => SetValue(CursorTicksProperty, value);
        }

        public int Revision
        {
            get => GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        /// <summary>Uzivatel kliknul do grafu na tento cas (ticky) - dokument z toho udela seek.</summary>
        public event EventHandler<long> TimePicked;

        // Zobrazeny casovy vyrez. 0/0 = jeste nenastaveno -> pri prvnim kresleni se vezme cely rozsah.
        private long viewFrom, viewTo;

        // Lupa a posun na ose Y. Rady maji ruzne jednotky, takze se zoomuje v NORMALIZOVANE ose
        // (0 = min rady, 1 = max rady) - tim se roztahnou vsechny zaroven a jejich vzajemne
        // porovnani zustane platne.
        private double yZoom = 1.0;
        private double yPan;

        // Tazeni (posun vyrezu).
        private bool dragging;
        private Point dragStart;
        private long dragFrom, dragTo;
        private double dragPan;

        // Pozice mysi pro odectitko (null = mys mimo graf).
        private Point? hover;

        private const double PadLeft = 44;    // misto pro osu Y (kdyz se kresli)
        private const double PadRight = 10;
        private const double PadTop = 10;
        private const double PadBottom = 22;  // popisky casu

        static TelemetryChartControl()
        {
            AffectsRender<TelemetryChartControl>(SeriesProperty, CursorTicksProperty, RevisionProperty);
        }

        public TelemetryChartControl()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        /// <summary>Vrati pohled na cely casovy rozsah dat i vychozi meritko hodnot.</summary>
        public void ResetView()
        {
            viewFrom = viewTo = 0;
            yZoom = 1.0;
            yPan = 0;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            var bounds = Bounds;
            double w = bounds.Width, h = bounds.Height;
            if (w <= PadLeft + PadRight + 4 || h <= PadTop + PadBottom + 4) return;

            var back = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            ctx.FillRectangle(back, new Rect(0, 0, w, h));

            var visible = VisibleSeries();
            if (visible.Count == 0)
            {
                DrawCentered(ctx, "Žádná řada — vyber údaje v telemetrii: Sloupce ▾ → graf", w, h);
                return;
            }

            EnsureView(visible);
            if (viewTo <= viewFrom)
            {
                DrawCentered(ctx, "Vybrané údaje nemají v záznamu žádný příchod.", w, h);
                return;
            }

            var plot = new Rect(PadLeft, PadTop, w - PadLeft - PadRight, h - PadTop - PadBottom);
            DrawGrid(ctx, plot, visible);

            foreach (var s in visible)
                DrawSeries(ctx, plot, s);

            DrawCursor(ctx, plot);
            DrawHover(ctx, plot, visible);
        }

        /// <summary>Rady, ktere se maji kreslit (zapnute a s aspon jednim bodem).</summary>
        private List<TelemetryChartSeries> VisibleSeries()
        {
            var list = new List<TelemetryChartSeries>();
            var all = Series;
            if (all == null) return list;

            foreach (var s in all)
                if (s != null && s.IsVisible && s.Data != null && s.Data.Count > 0)
                    list.Add(s);
            return list;
        }

        /// <summary>
        /// Prvni vykresleni (nebo po resetu) ukazuje cely casovy rozsah vsech rad.
        /// <para>Vyrez se resetuje i tehdy, kdyz se <b>vubec neprotina s daty</b> - to nastane po
        /// prepnuti na jiny zaznam (jina cast casove osy) a jinak by graf zustal prazdny, aniz by
        /// bylo poznat proc. Priblizeni pri pouhem pridani dalsi rady se tim ale nezahodi.</para>
        /// </summary>
        private void EnsureView(List<TelemetryChartSeries> visible)
        {
            long from = long.MaxValue, to = long.MinValue;
            foreach (var s in visible)
            {
                if (s.Data.FirstTicks < from) from = s.Data.FirstTicks;
                if (s.Data.LastTicks > to) to = s.Data.LastTicks;
            }

            bool unset = viewTo <= viewFrom;
            bool disjoint = !unset && (viewTo < from || viewFrom > to);
            if (!unset && !disjoint) return;

            if (to <= from) to = from + TimeSpan.TicksPerSecond;   // jediny bod -> vteřinové okno
            viewFrom = from;
            viewTo = to;
        }

        // ---- prevody mezi hodnotou a obrazovkou ----

        /// <summary>
        /// Rozsah osy Y rady s malym odsazenim, aby se krivka nelepila na okraj.
        /// <para>Nekonečné hodnoty se do rozsahu <b>nezapočítávají</b>. Nejsou to chyby: „korel
        /// sig+ [m]“ je na přímé cestě +∞ a znamená „podélná poloha není určená“. Kdyby do rozsahu
        /// vstoupily, měla by osa nekonečné rozpětí a všechny skutečné vzorky by se slily na spodní
        /// hranu. Samotné body se pak kreslí jako <b>mezera</b> (viz <see cref="DrawSeries"/>).</para>
        /// </summary>
        private static void GetRange(TelemetryChartSeries s, out double min, out double max)
        {
            min = s.Data.Min;
            max = s.Data.Max;

            if (!double.IsFinite(min) || !double.IsFinite(max)) FiniteRange(s.Data, out min, out max);

            if (max - min < 1e-9)
            {
                // Konstantni rada by mela nulovou vysku - dej ji symetricke okoli, at je videt.
                double pad = Math.Max(Math.Abs(max) * 0.1, 0.5);
                min -= pad;
                max += pad;
                return;
            }

            double margin = (max - min) * 0.05;
            min -= margin;
            max += margin;
        }

        /// <summary>
        /// Min/max jen z konečných bodů řady (řada si předpočítané min/max drží včetně nekonečen).
        /// Když konečný bod není žádný, vrátí nulový rozsah — o ten se pak postará odsazení
        /// konstantní řady.
        /// </summary>
        private static void FiniteRange(TelemetrySeries data, out double min, out double max)
        {
            min = double.MaxValue;
            max = double.MinValue;
            for (int i = 0; i < data.Count; i++)
            {
                double v = data.ValueAt(i);
                if (!double.IsFinite(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            if (min > max) { min = 0; max = 0; }
        }

        /// <summary>
        /// Normalizovana hodnota (0 = spodek rozsahu rady, 1 = vrsek) -> pomer vysky plochy,
        /// se zapocitanou lupou a posunem osy Y. Zoom je kolem STREDU plochy.
        /// </summary>
        private double ToScreenFraction(double normalized)
            => (normalized - 0.5) * yZoom + 0.5 + yPan;

        /// <summary>Opak <see cref="ToScreenFraction"/> - z pomeru vysky zpet na normalizovanou hodnotu.</summary>
        private double FromScreenFraction(double fraction)
            => (fraction - 0.5 - yPan) / yZoom + 0.5;

        private double YOf(Rect plot, double value, double min, double span)
            => plot.Bottom - ToScreenFraction((value - min) / span) * plot.Height;

        private double XOf(Rect plot, long ticks)
            => plot.X + (double)(ticks - viewFrom) / (viewTo - viewFrom) * plot.Width;

        private long TicksOf(Rect plot, double x)
            => viewFrom + (long)((viewTo - viewFrom) * Math.Clamp((x - plot.X) / plot.Width, 0, 1));

        // ---- kresleni ----

        private void DrawGrid(DrawingContext ctx, Rect plot, List<TelemetryChartSeries> visible)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)), 1);
            var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);

            // Vodorovna mrizka po ctvrtinach; cisla jen kdyz je jedna rada (jinak by nebylo jasne,
            // ke ktere z nich patri - kazda rada ma vlastni meritko).
            var single = visible.Count == 1 ? visible[0] : null;
            double min = 0, max = 0;
            if (single != null) GetRange(single, out min, out max);

            for (int i = 0; i <= 4; i++)
            {
                double y = plot.Y + plot.Height * i / 4.0;
                ctx.DrawLine(gridPen, new Point(plot.X, y), new Point(plot.Right, y));

                if (single == null) continue;

                // Popisek musi projit stejnou lupou jako data, jinak by po zoomu lhal.
                double fraction = (plot.Bottom - y) / plot.Height;
                double value = min + FromScreenFraction(fraction) * (max - min);
                DrawText(ctx, single.Data.TextOf(value), new Point(2, y - 8), Brushes.Gray, 11);
            }

            // Svisla mrizka + casy. Pet dilku je citelny kompromis pro sirky, ktere tenhle
            // dokument realne ma.
            for (int i = 0; i <= 5; i++)
            {
                double x = plot.X + plot.Width * i / 5.0;
                ctx.DrawLine(gridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));

                long t = viewFrom + (long)((viewTo - viewFrom) * (i / 5.0));
                string label = FormatTime(t, viewTo - viewFrom);
                DrawText(ctx, label, new Point(x - 26, plot.Bottom + 3), Brushes.Gray, 11);
            }

            ctx.DrawLine(axisPen, new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));
            ctx.DrawLine(axisPen, new Point(plot.X, plot.Y), new Point(plot.X, plot.Bottom));
        }

        private void DrawSeries(DrawingContext ctx, Rect plot, TelemetryChartSeries s)
        {
            var data = s.Data;
            GetRange(s, out double min, out double max);
            double span = max - min;

            var pen = new Pen(new SolidColorBrush(s.Color), 1.5);

            // Jen body ve vyrezu (plus jeden pred a za, aby krivka nezacinala/nekoncila v prazdnu).
            int first = IndexBefore(data, viewFrom);
            int last = IndexAfter(data, viewTo);
            int count = last - first + 1;
            if (count <= 1)
            {
                // Ve vyrezu neni zadny prichod - u schodu presto plati posledni znama hodnota.
                double? held = data.ValueAtTime(viewTo);
                if (s.IsStep && held.HasValue && double.IsFinite(held.Value))
                {
                    double y = YOf(plot, held.Value, min, span);
                    ctx.DrawLine(pen, new Point(plot.X, y), new Point(plot.Right, y));
                }
                return;
            }

            // Kdyz je bodu radove vic nez pixelu, kresli se obalka min/max na pixel - jinak by se
            // desetitisice usecek slily do kase a jen zdrzovaly.
            bool envelope = count > plot.Width * 4;

            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                if (envelope)
                {
                    DrawEnvelope(g, plot, data, first, last, min, span);
                }
                else
                {
                    bool started = false;
                    double prevY = 0;
                    for (int i = first; i <= last; i++)
                    {
                        double value = data.ValueAt(i);
                        if (!double.IsFinite(value))
                        {
                            // Nekonečno se na osu umístit nedá; křivka se přeruší a mezera se čte
                            // správně jako „hodnota není určená“ (viz GetRange).
                            started = false;
                            continue;
                        }

                        double x = XOf(plot, data.TicksAt(i));
                        double y = YOf(plot, value, min, span);

                        if (!started)
                        {
                            g.BeginFigure(new Point(x, y), false);
                            started = true;
                        }
                        else if (s.IsStep)
                        {
                            g.LineTo(new Point(x, prevY));   // hodnota plati az do tohoto prichodu
                            g.LineTo(new Point(x, y));
                        }
                        else
                        {
                            g.LineTo(new Point(x, y));
                        }

                        prevY = y;
                    }
                }
            }

            ctx.DrawGeometry(null, pen, geo);
        }

        /// <summary>
        /// Obalka min/max po pixelech - pro husta data. Kresli se jako lomena cara pres minima
        /// a zpet pres maxima, takze je videt rozptyl, ne nahodne vybrany vzorek.
        /// </summary>
        private void DrawEnvelope(StreamGeometryContext g, Rect plot, TelemetrySeries data,
                                  int first, int last, double min, double span)
        {
            int pixels = Math.Max(1, (int)plot.Width);
            var lo = new double[pixels];
            var hi = new double[pixels];
            var has = new bool[pixels];
            double tspan = viewTo - viewFrom;

            for (int i = first; i <= last; i++)
            {
                double rel = (data.TicksAt(i) - viewFrom) / tspan;
                int px = (int)(rel * (pixels - 1));
                if (px < 0 || px >= pixels) continue;

                double v = data.ValueAt(i);
                if (!double.IsFinite(v)) continue;      // stejný důvod jako v DrawSeries — mezera
                if (!has[px]) { lo[px] = hi[px] = v; has[px] = true; }
                else if (v < lo[px]) lo[px] = v;
                else if (v > hi[px]) hi[px] = v;
            }

            bool started = false;
            for (int px = 0; px < pixels; px++)          // tam po maximech
            {
                if (!has[px]) continue;
                var p = new Point(plot.X + px, YOf(plot, hi[px], min, span));
                if (!started) { g.BeginFigure(p, false); started = true; }
                else g.LineTo(p);
            }
            if (!started) return;

            for (int px = pixels - 1; px >= 0; px--)     // a zpatky po minimech
            {
                if (!has[px]) continue;
                g.LineTo(new Point(plot.X + px, YOf(plot, lo[px], min, span)));
            }
        }

        private void DrawCursor(DrawingContext ctx, Rect plot)
        {
            long cursor = CursorTicks;
            if (cursor <= 0 || cursor < viewFrom || cursor > viewTo) return;

            double x = XOf(plot, cursor);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30)), 1);
            ctx.DrawLine(pen, new Point(x, plot.Y), new Point(x, plot.Bottom));
        }

        /// <summary>
        /// Odectitko: svisla cara pod mysi, tecka na kazde krivce a ramecek s hodnotami. Je to
        /// jediny zpusob, jak z grafu precist konkretni cislo - proto se kresli hodnota KAZDE
        /// viditelne rady, ne jen te nejblizsi.
        /// </summary>
        private void DrawHover(DrawingContext ctx, Rect plot, List<TelemetryChartSeries> visible)
        {
            if (hover == null) return;
            var mouse = hover.Value;
            if (mouse.X < plot.X || mouse.X > plot.Right || mouse.Y < plot.Y || mouse.Y > plot.Bottom)
                return;

            long at = TicksOf(plot, mouse.X);
            double x = XOf(plot, at);

            var linePen = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 1,
                                  new DashStyle(new double[] { 3, 3 }, 0));
            ctx.DrawLine(linePen, new Point(x, plot.Y), new Point(x, plot.Bottom));

            // Hodnoty: u schodu posledni prichod, u rampy interpolace - at odectitko ukazuje
            // presne to, co je nakreslene.
            var lines = new List<(string text, Color color)>();
            foreach (var s in visible)
            {
                double? v = s.IsStep ? s.Data.ValueAtTime(at) : s.Data.InterpolatedAt(at);
                if (!v.HasValue) continue;

                GetRange(s, out double min, out double max);
                double y = YOf(plot, v.Value, min, max - min);
                if (y >= plot.Y && y <= plot.Bottom)
                    ctx.DrawEllipse(new SolidColorBrush(s.Color), null, new Point(x, y), 3, 3);

                lines.Add(($"{s.Header}  {s.Data.TextOf(v.Value)}", s.Color));
            }

            DrawHoverBox(ctx, plot, x, mouse.Y, FormatTime(at, 0), lines);
        }

        /// <summary>Ramecek odectitka. Sklopi se na druhou stranu kurzoru, kdyz by vylezl z plochy.</summary>
        private void DrawHoverBox(DrawingContext ctx, Rect plot, double x, double mouseY,
                                  string header, List<(string text, Color color)> lines)
        {
            const double lineH = 15, padding = 6;
            double width = 130;

            var headerText = Text(header, 11, Brushes.Silver);
            width = Math.Max(width, headerText.Width);

            var texts = new List<(FormattedText ft, Color color)>();
            foreach (var (text, color) in lines)
            {
                var ft = Text(text, 12, new SolidColorBrush(color));
                width = Math.Max(width, ft.Width);
                texts.Add((ft, color));
            }

            double boxW = width + padding * 2;
            double boxH = padding * 2 + lineH * (texts.Count + 1);

            double bx = x + 12;
            if (bx + boxW > plot.Right) bx = x - 12 - boxW;      // u praveho okraje vlevo od cary
            if (bx < plot.X) bx = plot.X;

            double by = Math.Clamp(mouseY - boxH / 2, plot.Y, Math.Max(plot.Y, plot.Bottom - boxH));

            var box = new Rect(bx, by, boxW, boxH);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0xE0, 0x22, 0x22, 0x22)), box);
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1), box);

            double ty = by + padding;
            ctx.DrawText(headerText, new Point(bx + padding, ty));
            ty += lineH;

            foreach (var (ft, _) in texts)
            {
                ctx.DrawText(ft, new Point(bx + padding, ty));
                ty += lineH;
            }
        }

        /// <summary>Posledni bod pred zacatkem vyrezu (nebo 0) - aby krivka do vyrezu vstoupila.</summary>
        private static int IndexBefore(TelemetrySeries data, long ticks)
        {
            int lo = 0, hi = data.Count - 1, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (data.TicksAt(mid) <= ticks) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best;
        }

        /// <summary>Prvni bod za koncem vyrezu (nebo posledni) - aby krivka vyrez opustila.</summary>
        private static int IndexAfter(TelemetrySeries data, long ticks)
        {
            int lo = 0, hi = data.Count - 1, best = data.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (data.TicksAt(mid) >= ticks) { best = mid; hi = mid - 1; }
                else lo = mid + 1;
            }
            return best;
        }

        /// <summary>Cas na ose: u kratkych vyrezu i s milisekundami, u dlouhych bez nich.</summary>
        private static string FormatTime(long ticks, long spanTicks)
        {
            var t = new DateTime(ticks);
            return spanTicks < TimeSpan.TicksPerSecond * 20 ? t.ToString("HH:mm:ss.fff")
                                                            : t.ToString("HH:mm:ss");
        }

        private static FormattedText Text(string text, double size, IBrush brush)
            => new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                                 FlowDirection.LeftToRight, Typeface.Default, size, brush);

        private static void DrawText(DrawingContext ctx, string text, Point at, IBrush brush, double size)
            => ctx.DrawText(Text(text, size, brush), at);

        private static void DrawCentered(DrawingContext ctx, string text, double w, double h)
        {
            var ft = Text(text, 13, Brushes.Gray);
            ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
        }

        // ---- ovladani ----

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (viewTo <= viewFrom) return;

            var plot = PlotRect();
            if (plot.Width <= 0 || plot.Height <= 0) return;

            var pos = e.GetPosition(this);
            double factor = e.Delta.Y > 0 ? 1 / 1.25 : 1.25;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                ZoomValues(plot, pos.Y, factor);
            else
                ZoomTime(plot, pos.X, factor);

            InvalidateVisual();
            e.Handled = true;
        }

        /// <summary>Lupa casu kolem bodu POD MYSI - jinak by pri priblizovani ujizdelo misto,
        /// na ktere se uzivatel diva.</summary>
        private void ZoomTime(Rect plot, double mouseX, double factor)
        {
            double rel = Math.Clamp((mouseX - plot.X) / plot.Width, 0, 1);
            long anchor = viewFrom + (long)((viewTo - viewFrom) * rel);

            long span = (long)((viewTo - viewFrom) * factor);
            if (span < TimeSpan.TicksPerMillisecond * 10) span = TimeSpan.TicksPerMillisecond * 10;

            viewFrom = anchor - (long)(span * rel);
            viewTo = viewFrom + span;
        }

        /// <summary>
        /// Lupa hodnot, take kolem bodu pod mysi. Zoomuje se v normalizovane ose spolecne pro
        /// vsechny rady (kazda ma sve meritko), takze se jejich vzajemny vztah nemeni.
        /// </summary>
        private void ZoomValues(Rect plot, double mouseY, double factor)
        {
            double fraction = (plot.Bottom - mouseY) / plot.Height;   // 0 dole, 1 nahore
            double normalized = FromScreenFraction(fraction);

            double zoom = Math.Clamp(yZoom / factor, 0.05, 200);
            // Posun dopocitat tak, aby hodnota pod mysi zustala pod mysi.
            yPan = fraction - 0.5 - (normalized - 0.5) * zoom;
            yZoom = zoom;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);

            if (e.ClickCount == 2)
            {
                ResetView();
                e.Handled = true;
                return;
            }

            if (point.Properties.IsRightButtonPressed)
            {
                dragging = true;
                dragStart = point.Position;
                dragFrom = viewFrom;
                dragTo = viewTo;
                dragPan = yPan;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // Levy klik = "chci vidět tenhle okamžik" -> dokument z toho udela seek v prehravani.
            var plot = PlotRect();
            if (plot.Width > 0 && viewTo > viewFrom)
            {
                TimePicked?.Invoke(this, TicksOf(plot, point.Position.X));
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);
            var plot = PlotRect();

            if (dragging && plot.Width > 0 && plot.Height > 0)
            {
                // Pocitat v double: pri prevodu na long by se posun mensi nez jeden pixel
                // zaokrouhlil na nulu a tazeni by "drhlo".
                double dx = pos.X - dragStart.X;
                double dy = pos.Y - dragStart.Y;

                long shift = (long)((dragTo - dragFrom) * (-dx) / plot.Width);
                viewFrom = dragFrom + shift;
                viewTo = dragTo + shift;
                yPan = dragPan - dy / plot.Height;
            }

            hover = pos;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            hover = null;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!dragging) return;

            dragging = false;
            e.Pointer.Capture(null);
        }

        private Rect PlotRect()
            => new Rect(PadLeft, PadTop,
                        Math.Max(0, Bounds.Width - PadLeft - PadRight),
                        Math.Max(0, Bounds.Height - PadTop - PadBottom));
    }
}
