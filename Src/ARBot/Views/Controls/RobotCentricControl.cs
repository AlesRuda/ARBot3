using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Vision;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Robot-centricke (ptaci) platno: robot dole uprostred, smer vpred nahoru
    /// (robot-rel. ramec: X vpred -&gt; nahoru, Y vlevo -&gt; vlevo). Vykresluje sdilenou
    /// scenu (dosahove kruznice, osa, robot) a nad ni jednotlive robot-centricke VRSTVY.
    /// Zatim je vrstvou polarni grid sjizdnosti (<see cref="Grids"/>); dalsi vrstvy
    /// (sjizdnost z RGB, okraje vozovky, ...) pribudou jako dalsi vlastnosti + render metody.
    ///
    /// Kazda bunka gridu se kresli jako vyplneny ctverec u sveho teziste, obarveny podle
    /// <see cref="TraversabilityClass"/> a s pruhlednosti podle <see cref="PolarCell.Confidence"/>.
    /// Vice kamer se kresli pres sebe (grid je per-kamera). Prekresli se pri zmene vrstev
    /// (<see cref="AffectsRender{T}"/>).
    /// </summary>
    public class RobotCentricControl : Control
    {
        /// <summary>Vrstva: polarni grid(y) sjizdnosti per kamera. Nastavuje ViewModel.</summary>
        public static readonly StyledProperty<IReadOnlyList<PolarTraversabilityGrid>> GridsProperty =
            AvaloniaProperty.Register<RobotCentricControl, IReadOnlyList<PolarTraversabilityGrid>>(nameof(Grids));

        public IReadOnlyList<PolarTraversabilityGrid> Grids
        {
            get => GetValue(GridsProperty);
            set => SetValue(GridsProperty, value);
        }

        static RobotCentricControl()
        {
            AffectsRender<RobotCentricControl>(GridsProperty);
        }

        // Barvy trid.
        private static readonly Color FreeColor = Color.FromRgb(0x4C, 0xAF, 0x50);
        private static readonly Color ObstacleColor = Color.FromRgb(0xE5, 0x39, 0x35);
        private static readonly Color UnknownColor = Color.FromRgb(0x9E, 0x9E, 0x9E);
        private static readonly IBrush Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        private static readonly IBrush RingBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush AxisBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        private static readonly IBrush TextBrush = Brushes.White;

        // Predpocitane pery (misto new Pen per render).
        private static readonly Pen RingPen = new Pen(RingBrush, 1);
        private static readonly Pen AxisPen = new Pen(AxisBrush, 1);

        // Cache stetcu bunek: 3 tridy x AlphaLevels+1 kvantovanych pruhlednosti. Puvodne se alokoval
        // novy SolidColorBrush pro KAZDOU bunku (az 1650/render) -> zbytecny churn na UI vlakne.
        private const int AlphaLevels = 24;
        private static readonly IBrush[] FreeBrushes = BuildBrushes(FreeColor);
        private static readonly IBrush[] ObstacleBrushes = BuildBrushes(ObstacleColor);
        private static readonly IBrush UnknownBrush = new SolidColorBrush(UnknownColor, 0.18);

        private static IBrush[] BuildBrushes(Color c)
        {
            var arr = new IBrush[AlphaLevels + 1];
            for (int i = 0; i <= AlphaLevels; i++)
                arr[i] = new SolidColorBrush(c, (double)i / AlphaLevels);
            return arr;
        }

        // Stetec bunky dle tridy + duvery (znovupouzity z cache).
        private static IBrush CellBrush(TraversabilityClass cls, double confidence)
        {
            if (cls == TraversabilityClass.Unknown) return UnknownBrush;
            double alpha = 0.30 + 0.70 * Clamp01(confidence);
            int i = (int)Math.Round(alpha * AlphaLevels);
            if (i < 0) i = 0; else if (i > AlphaLevels) i = AlphaLevels;
            return (cls == TraversabilityClass.Obstacle ? ObstacleBrushes : FreeBrushes)[i];
        }

        /// <summary>DIAGNOSTIKA (self-test): kolikrát proběhl Render (jen když je control viditelný).</summary>
        public static long DiagRenders;

        public override void Render(DrawingContext ctx)
        {
            DiagRenders++;
            var b = Bounds;
            double W = b.Width, H = b.Height;
            if (W <= 4 || H <= 4) return;

            ctx.DrawRectangle(Background, null, new Rect(0, 0, W, H));

            var grids = Grids;

            // Maximalni dosah z hran gridu (fallback 5 m).
            float maxR = 5f;
            bool haveData = false;
            if (grids != null)
                foreach (var g in grids)
                {
                    if (g?.RadialEdges != null && g.RadialEdges.Length > 0)
                    {
                        maxR = Math.Max(maxR, g.RadialEdges[g.RadialEdges.Length - 1].Range);
                        if (g.Cells != null && g.Cells.Length > 0) haveData = true;
                    }
                }

            const double margin = 16;
            double rearM = RobotGlyph.RearExtentMeters;   // robot je dozadu delsi - nechat dole misto
            double cx = W / 2.0;
            double usableWHalf = W / 2.0 - margin;
            // Vertikalne se musi vejit dopredny dosah (maxR) NAD pocatkem + zadni cast robotu (rearM)
            // POD nim; dopredny oblouk je +-~70 deg -> lateralni dosah ~ maxR*sin(70).
            double scale = Math.Min((H - 2 * margin) / (maxR + rearM), usableWHalf / (maxR * 0.95));
            if (scale <= 0) scale = 1;
            double cy = H - margin - rearM * scale;       // pocatek robotu (osa otaceni) zvednut o zadni dosah

            // Prevod robot-rel. (X vpred, Y vlevo) [m] na obrazovku [px].
            Point Screen(double x, double y) => new Point(cx - y * scale, cy - x * scale);

            // Dosahove kruznice po metrech + popisky.
            for (int m = 1; m <= (int)Math.Ceiling(maxR); m++)
            {
                double rr = m * scale;
                ctx.DrawEllipse(null, RingPen, new Point(cx, cy), rr, rr);
                var ft = Fmt($"{m} m", 10, RingBrush);
                ctx.DrawText(ft, new Point(cx + 2, cy - rr - ft.Height));
            }

            // Osa vpred (nahoru).
            ctx.DrawLine(AxisPen, new Point(cx, cy), new Point(cx, cy - maxR * scale));

            if (grids != null)
            {
                foreach (var g in grids)
                {
                    if (g?.Cells == null || g.RadialEdges == null) continue;
                    int R = g.RadialCount;
                    if (R <= 0) continue;

                    for (int a = 0; a < g.AzimuthCount; a++)
                    {
                        for (int r = 0; r < R; r++)
                        {
                            var cell = g.Cells[a * R + r];
                            if (cell.Count <= 0) continue;   // prazdna bunka nema teziste

                            // Stetec dle tridy + duvery (znovupouzity z cache, misto new per bunku).
                            var brush = CellBrush(cell.Class, cell.Confidence);

                            double side = Math.Max(2.0, (g.RadialEdges[r + 1].Range - g.RadialEdges[r].Range) * scale);
                            var p = Screen(cell.MeanX, cell.MeanY);
                            ctx.DrawRectangle(brush, null,
                                new Rect(p.X - side / 2, p.Y - side / 2, side, side));
                        }
                    }
                }
            }

            // Robot (sdileny tvar): v robot-centrickem pohledu miri vpred = nahoru (orientace PI/2),
            // v meritku gridu.
            RobotGlyph.Draw(ctx, cx, cy, scale, Math.PI / 2);

            DrawLegend(ctx);

            if (!haveData)
            {
                var ft = Fmt("Čekám na data…", 14, TextBrush);
                ctx.DrawText(ft, new Point(cx - ft.Width / 2, H / 2 - ft.Height / 2));
            }
        }

        private void DrawLegend(DrawingContext ctx)
        {
            double x = 10, y = 10;
            (Color c, string t)[] items =
            {
                (FreeColor, "Sjízdné"),
                (ObstacleColor, "Překážka"),
                (UnknownColor, "Neznámé"),
            };
            foreach (var (c, t) in items)
            {
                ctx.DrawRectangle(new SolidColorBrush(c, 0.85), null, new Rect(x, y, 12, 12));
                var ft = Fmt(t, 11, TextBrush);
                ctx.DrawText(ft, new Point(x + 16, y - 1));
                y += 16;
            }
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static FormattedText Fmt(string text, double size, IBrush brush) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("sans-serif"), size, brush);
    }
}
