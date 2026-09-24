using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Vision;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Graf profilu sceny (<see cref="SceneProfile"/>): osa X = vodorovna vzdalenost od robotu [m],
    /// osa Y = vyska [m]. Kresli se vlastnim <see cref="Render"/> jako <see cref="TelemetryChartControl"/>.
    ///
    /// <para>Vrstvy odzadu: pozadi prstencu gridu podle tridy (sytost = <see cref="PolarCell.Confidence"/>),
    /// pas tolerance vysky kolem referencni roviny (<c>rovina ± MaxHeightDev</c>) a rovina sama, surove
    /// body (barva podle tridy bunky, sede = mimo grid), a nakonec agregaty bunky: vodorovna cara
    /// <c>MeanZ</c>, svisla usecka <c>±StdZ</c> v tezisti a trojuhelnik <c>MaxZ</c>. Pod mysi se ukaze
    /// <b>proc</b> ma bunka svou tridu - hodnoty proti prahum, ktere prekrocila, jsou cervene.</para>
    ///
    /// <para>Ovladani: kolecko = lupa vzdalenosti, Ctrl+kolecko = lupa vysky, tazeni pravym = posun,
    /// dvojklik = zpet na automaticky rozsah. Dokud uzivatel nezoomuje, rozsah se prizpusobuje datum.
    /// <see cref="EqualScale"/> = obe osy ve stejnem meritku (skutecny tvar; jinak je vyska
    /// zvetsena, protoze centimetry na metrech by nebyly videt).</para>
    /// </summary>
    public class SceneProfileControl : Control
    {
        public static readonly StyledProperty<SceneProfile> ProfileProperty =
            AvaloniaProperty.Register<SceneProfileControl, SceneProfile>(nameof(Profile));

        public static readonly StyledProperty<bool> EqualScaleProperty =
            AvaloniaProperty.Register<SceneProfileControl, bool>(nameof(EqualScale));

        public SceneProfile Profile
        {
            get => GetValue(ProfileProperty);
            set => SetValue(ProfileProperty, value);
        }

        public bool EqualScale
        {
            get => GetValue(EqualScaleProperty);
            set => SetValue(EqualScaleProperty, value);
        }

        private const double PadLeft = 48, PadRight = 10, PadTop = 8, PadBottom = 22;

        // Zobrazeny vyrez [m]. auto = prizpusobuje se datum, dokud uzivatel nezoomuje/neposune.
        private bool auto = true;
        private double xMin, xMax, zMin, zMax;

        private bool dragging;
        private Point dragStart;
        private double dragX0, dragX1, dragZ0, dragZ1;
        private Point? hover;

        private static readonly IBrush Back = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly IBrush TextBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
        private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
        private static readonly IBrush BadBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));
        private static readonly Pen GridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
        private static readonly Pen AxisPen = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF)), 1);
        private static readonly Pen EdgePen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1);
        private static readonly Pen PlanePen = new Pen(new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7)), 1.5,
                                                      new DashStyle(new double[] { 4, 3 }, 0));
        private static readonly IBrush TolBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x4F, 0xC3, 0xF7));
        private static readonly Pen MeanPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), 2);
        private static readonly Pen StdPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), 1);
        private static readonly IBrush MaxBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
        private static readonly Pen HoverPen = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), 1,
                                                      new DashStyle(new double[] { 2, 2 }, 0));
        private static readonly IBrush TipBack = new SolidColorBrush(Color.FromArgb(0xE0, 0x20, 0x20, 0x20));

        // Body: barva podle tridy bunky, ve ktere lezi (mimo grid sede).
        private static readonly IBrush PtFree = new SolidColorBrush(Color.FromRgb(0x81, 0xC7, 0x84));
        private static readonly IBrush PtObstacle = new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73));
        private static readonly IBrush PtUnknown = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
        private static readonly IBrush PtOutside = new SolidColorBrush(Color.FromArgb(0x80, 0x80, 0x80, 0x80));

        static SceneProfileControl()
        {
            AffectsRender<SceneProfileControl>(ProfileProperty, EqualScaleProperty);
        }

        public SceneProfileControl()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == EqualScaleProperty) auto = true;   // jine meritko = novy vyrez
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(Back, new Rect(0, 0, w, h));
            var plot = PlotRect();
            if (plot.Width < 20 || plot.Height < 20) return;

            var p = Profile;
            if (p == null || p.Points.Length == 0)
            {
                var ft = Text(p == null ? "Žádný profil" : "V tomhle azimutu nejsou platné body hloubky", 13, DimBrush);
                ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
                return;
            }

            if (auto) AutoRange(p, plot);

            double X(double r) => plot.X + (r - xMin) / (xMax - xMin) * plot.Width;
            double Y(double z) => plot.Bottom - (z - zMin) / (zMax - zMin) * plot.Height;

            using (ctx.PushClip(plot))
            {
                DrawCells(ctx, p, plot, X, Y);
                DrawPoints(ctx, p, X, Y);
                DrawAggregates(ctx, p, plot.Y, X, Y);
            }
            DrawAxes(ctx, plot, X, Y);
            DrawHover(ctx, p, plot, X, Y);
        }

        // ---- vrstvy ----

        private static void DrawCells(DrawingContext ctx, SceneProfile p, Rect plot,
                                      Func<double, double> X, Func<double, double> Y)
        {
            var e = p.Edges;
            if (e == null || p.Cells == null) return;
            for (int r = 0; r < p.Cells.Length; r++)
            {
                var c = p.Cells[r];
                double x0 = X(e[r].Range), x1 = X(e[r + 1].Range);
                byte alpha = (byte)(0x14 + 0x40 * Math.Clamp(c.Confidence, 0f, 1f));
                var col = c.Class switch
                {
                    TraversabilityClass.Free => Color.FromArgb(alpha, 0x4C, 0xAF, 0x50),
                    TraversabilityClass.Obstacle => Color.FromArgb(alpha, 0xF4, 0x43, 0x36),
                    _ => Color.FromArgb(0x10, 0x9E, 0x9E, 0x9E),
                };
                ctx.FillRectangle(new SolidColorBrush(col), new Rect(x0, plot.Y, Math.Max(1, x1 - x0), plot.Height));
                ctx.DrawLine(EdgePen, new Point(x0, plot.Bottom - 6), new Point(x0, plot.Bottom));

                var v = p.Verdicts[r];
                if (!v.Evaluated) continue;
                // Pas tolerance vysky kolem roviny (pod tezistem bunky) a rovina sama.
                double yTop = Y(v.PlaneZ + v.DeviationLimit), yBot = Y(v.PlaneZ - v.DeviationLimit);
                ctx.FillRectangle(TolBrush, new Rect(x0, yTop, Math.Max(1, x1 - x0), Math.Max(1, yBot - yTop)));
                ctx.DrawLine(PlanePen, new Point(x0, Y(v.PlaneZ)), new Point(x1, Y(v.PlaneZ)));
            }
        }

        private static void DrawPoints(DrawingContext ctx, SceneProfile p,
                                       Func<double, double> X, Func<double, double> Y)
        {
            foreach (var q in p.Points)
            {
                IBrush b = PtOutside;
                if (q.Radial >= 0 && p.Cells != null && q.Radial < p.Cells.Length)
                    b = p.Cells[q.Radial].Class switch
                    {
                        TraversabilityClass.Free => PtFree,
                        TraversabilityClass.Obstacle => PtObstacle,
                        _ => PtUnknown,
                    };
                ctx.FillRectangle(b, new Rect(X(q.Range) - 1, Y(q.Z) - 1, 2, 2));
            }
        }

        private static void DrawAggregates(DrawingContext ctx, SceneProfile p, double top,
                                           Func<double, double> X, Func<double, double> Y)
        {
            var e = p.Edges;
            if (e == null || p.Cells == null) return;
            for (int r = 0; r < p.Cells.Length; r++)
            {
                var c = p.Cells[r];
                if (c.Count == 0) continue;
                double x0 = X(e[r].Range), x1 = X(e[r + 1].Range);
                double xc = X(p.Verdicts[r].Evaluated ? p.Verdicts[r].Range : (e[r].Range + e[r + 1].Range) / 2);
                ctx.DrawLine(MeanPen, new Point(x0, Y(c.MeanZ)), new Point(x1, Y(c.MeanZ)));
                ctx.DrawLine(StdPen, new Point(xc, Y(c.MeanZ - c.StdZ)), new Point(xc, Y(c.MeanZ + c.StdZ)));
                // MaxZ nad vyrezem (autorozsah jde jen po 98. percentil) se pritahne k hornimu okraji,
                // jinak by zmizel prave ten nejvyssi bod, kvuli kteremu se na bunku divam.
                double ym = Math.Max(Y(c.MaxZ), top + 6);
                var tri = new StreamGeometry();
                using (var g = tri.Open())
                {
                    g.BeginFigure(new Point(xc - 4, ym - 6), true);
                    g.LineTo(new Point(xc + 4, ym - 6));
                    g.LineTo(new Point(xc, ym));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(MaxBrush, null, tri);
            }
        }

        private void DrawAxes(DrawingContext ctx, Rect plot, Func<double, double> X, Func<double, double> Y)
        {
            ctx.DrawLine(AxisPen, plot.BottomLeft, plot.BottomRight);
            ctx.DrawLine(AxisPen, plot.BottomLeft, plot.TopLeft);

            double stepX = NiceStep((xMax - xMin) / Math.Max(2, plot.Width / 70));
            for (double r = Math.Ceiling(xMin / stepX) * stepX; r <= xMax + 1e-9; r += stepX)
            {
                double x = X(r);
                ctx.DrawLine(GridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));
                var ft = Text(r.ToString(stepX < 1 ? "0.0#" : "0", CultureInfo.InvariantCulture) + " m", 10, DimBrush);
                ctx.DrawText(ft, new Point(Math.Min(x - ft.Width / 2, Bounds.Width - ft.Width - 2), plot.Bottom + 3));
            }

            double stepZ = NiceStep((zMax - zMin) / Math.Max(2, plot.Height / 40));
            for (double z = Math.Ceiling(zMin / stepZ) * stepZ; z <= zMax + 1e-9; z += stepZ)
            {
                double y = Y(z);
                ctx.DrawLine(Math.Abs(z) < stepZ / 2 ? AxisPen : GridPen, new Point(plot.X, y), new Point(plot.Right, y));
                var ft = Text(FormatZ(z, stepZ), 10, DimBrush);
                ctx.DrawText(ft, new Point(plot.X - ft.Width - 4, y - ft.Height / 2));
            }
        }

        private static string FormatZ(double z, double step)
            => step < 0.1 ? (z * 100).ToString("0", CultureInfo.InvariantCulture) + " cm"
                          : z.ToString("0.0#", CultureInfo.InvariantCulture) + " m";

        private void DrawHover(DrawingContext ctx, SceneProfile p, Rect plot,
                               Func<double, double> X, Func<double, double> Y)
        {
            if (hover is not Point m || !plot.Contains(m)) return;

            double r = xMin + (m.X - plot.X) / plot.Width * (xMax - xMin);
            double z = zMin + (plot.Bottom - m.Y) / plot.Height * (zMax - zMin);
            ctx.DrawLine(HoverPen, new Point(m.X, plot.Y), new Point(m.X, plot.Bottom));

            var lines = new List<(string text, IBrush brush)>
            {
                (string.Format(CultureInfo.InvariantCulture, "r = {0:F2} m   z = {1:+0.000;-0.000} m", r, z), TextBrush),
            };

            int ring = -1;
            if (p.Edges != null && p.Cells != null)
                for (int k = 0; k < p.Cells.Length; k++)
                    if (r >= p.Edges[k].Range && r < p.Edges[k + 1].Range) { ring = k; break; }

            if (ring >= 0)
            {
                var c = p.Cells[ring];
                var v = p.Verdicts[ring];
                int shown = 0;
                foreach (var q in p.Points) if (q.Radial == ring) shown++;
                lines.Add((string.Format(CultureInfo.InvariantCulture,
                    "prstenec {0}: {1:F2}–{2:F2} m   {3}   důvěra {4:F2}",
                    ring, p.Edges[ring].Range, p.Edges[ring + 1].Range, ClassName(c.Class), c.Confidence), TextBrush));
                lines.Add((string.Format(CultureInfo.InvariantCulture,
                    "bodů {0}{1}   MeanZ {2:+0.000;-0.000}   MaxZ {3:+0.000;-0.000}   StdZ {4:F3} m",
                    c.Count, shown != c.Count ? $" (v profilu {shown}!)" : "", c.MeanZ, c.MaxZ, c.StdZ), TextBrush));
                if (v.Evaluated)
                {
                    lines.Add((string.Format(CultureInfo.InvariantCulture,
                        "odchylka od roviny {0:+0.000;-0.000} m  (limit ±{1:F3}){2}",
                        v.Deviation, v.DeviationLimit, v.TooHigh ? "  ✗" : ""), v.TooHigh ? BadBrush : DimBrush));
                    lines.Add((string.Format(CultureInfo.InvariantCulture,
                        "drsnost {0:F3} m  (limit {1:F3}){2}",
                        v.Rough, v.RoughLimit, v.TooRough ? "  ✗" : ""), v.TooRough ? BadBrush : DimBrush));
                    lines.Add((string.Format(CultureInfo.InvariantCulture,
                        "stoupání k sousedům {0:F2}  (limit {1:F2}){2}",
                        v.Slope, v.SlopeLimit, v.TooSteep ? "  ✗" : ""), v.TooSteep ? BadBrush : DimBrush));
                    if (v.ImpliedClass != c.Class)
                        lines.Add(($"⚠ přepočet dává {ClassName(v.ImpliedClass)}, grid má {ClassName(c.Class)}", BadBrush));
                }
                else
                    lines.Add(("pod podlahou počtu bodů → Unknown", DimBrush));
            }

            var texts = new List<FormattedText>();
            double tw = 0, th = 0;
            foreach (var (text, brush) in lines)
            {
                var ft = Text(text, 11, brush);
                texts.Add(ft);
                tw = Math.Max(tw, ft.Width);
                th += ft.Height;
            }
            double bx = m.X + 12, by = m.Y + 12;
            if (bx + tw + 10 > Bounds.Width) bx = m.X - tw - 22;
            if (by + th + 8 > Bounds.Height) by = Math.Max(0, Bounds.Height - th - 8);
            ctx.FillRectangle(TipBack, new Rect(bx, by, tw + 10, th + 8));
            double ty = by + 4;
            foreach (var ft in texts)
            {
                ctx.DrawText(ft, new Point(bx + 5, ty));
                ty += ft.Height;
            }
        }

        private static string ClassName(TraversabilityClass c) => c switch
        {
            TraversabilityClass.Free => "Free",
            TraversabilityClass.Obstacle => "Obstacle",
            _ => "Unknown",
        };

        // ---- rozsah ----

        /// <summary>Automaticky vyrez: vzdalenost od 0 po posledni hranu gridu (nebo po 98. percentil
        /// bodu), vyska z 2.–98. percentilu bodu v tom rozsahu - jednotlive odlehle body (odlesky,
        /// nebe) nesmi smrsknout teren do jedne cary.</summary>
        private void AutoRange(SceneProfile p, Rect plot)
        {
            var ranges = new List<float>(p.Points.Length);
            foreach (var q in p.Points) ranges.Add(q.Range);
            ranges.Sort();
            double rEnd = p.Edges != null && p.Edges.Length > 1
                ? p.Edges[^1].Range
                : ranges[(int)(0.98 * (ranges.Count - 1))];
            xMin = 0;
            xMax = Math.Max(0.5, rEnd * 1.05);

            var zs = new List<float>(p.Points.Length);
            foreach (var q in p.Points) if (q.Range <= xMax) zs.Add(q.Z);
            if (zs.Count == 0) foreach (var q in p.Points) zs.Add(q.Z);
            zs.Sort();
            double lo = Math.Min(0, zs[(int)(0.02 * (zs.Count - 1))]);
            double hi = Math.Max(0, zs[(int)(0.98 * (zs.Count - 1))]);
            double span = Math.Max(0.10, hi - lo);
            zMin = lo - 0.15 * span;
            zMax = hi + 0.25 * span;   // nahore misto na trojuhelniky MaxZ

            if (EqualScale)
            {
                // Stejne m/px na obou osach: vyska se dopocita z sirky, vystredena na datech.
                double mPerPx = (xMax - xMin) / plot.Width;
                double half = plot.Height * mPerPx / 2, mid = (zMin + zMax) / 2;
                zMin = mid - half;
                zMax = mid + half;
            }
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0 || double.IsNaN(raw)) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / mag;
            return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * mag;
        }

        // ---- ovladani ----

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var plot = PlotRect();
            if (plot.Width <= 0 || plot.Height <= 0 || Profile == null) return;
            auto = false;

            var pos = e.GetPosition(this);
            double factor = e.Delta.Y > 0 ? 1 / 1.25 : 1.25;
            bool zOnly = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // Lupa kolem bodu pod mysi. Pri stejnem meritku se zoomuji obe osy zaroven.
            if (!zOnly || EqualScale)
            {
                double ax = xMin + (pos.X - plot.X) / plot.Width * (xMax - xMin);
                xMin = ax - (ax - xMin) * factor;
                xMax = ax + (xMax - ax) * factor;
            }
            if (zOnly || EqualScale)
            {
                double az = zMin + (plot.Bottom - pos.Y) / plot.Height * (zMax - zMin);
                zMin = az - (az - zMin) * factor;
                zMax = az + (zMax - az) * factor;
            }
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            if (e.ClickCount == 2)
            {
                auto = true;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (pt.Properties.IsRightButtonPressed)
            {
                dragging = true;
                dragStart = pt.Position;
                dragX0 = xMin; dragX1 = xMax; dragZ0 = zMin; dragZ1 = zMax;
                e.Pointer.Capture(this);
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
                auto = false;
                double dx = (pos.X - dragStart.X) / plot.Width * (dragX1 - dragX0);
                double dz = (pos.Y - dragStart.Y) / plot.Height * (dragZ1 - dragZ0);
                xMin = dragX0 - dx; xMax = dragX1 - dx;
                zMin = dragZ0 + dz; zMax = dragZ1 + dz;
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

        private static FormattedText Text(string text, double size, IBrush brush)
            => new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                 Typeface.Default, size, brush);
    }
}
