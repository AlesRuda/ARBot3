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
    /// Kazda bunka gridu se kresli jako jeji SKUTECNY pudorys = mezikruhova vysec (radialni pasmo
    /// z <see cref="PolarTraversabilityGrid.RadialEdges"/> x azimutovy slot sloupce), obarvena podle
    /// <see cref="TraversabilityClass"/> a s pruhlednosti podle <see cref="PolarCell.Confidence"/>.
    /// Vysece se diky sdilenym hranicim dokonale skladaji (zadny prekryv ani mezery) - drivejsi ctverec
    /// u teziste se u robota prekryval. Azimutove hranice grid neuklada, rekonstruuji se z lozisek bunek
    /// (<see cref="AzimuthBoundaries"/>). Vice kamer se kresli pres sebe (grid je per-kamera). Prekresli
    /// se pri zmene vrstev (<see cref="AffectsRender{T}"/>).
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
                    int A = g.AzimuthCount;
                    if (R <= 0 || A <= 0) continue;

                    // Azimutove hranice bunek rekonstruovane z lozisek (bearing per sloupec).
                    // Kdyz je nelze urcit (malo dat), fallback na puvodni ctverce u teziste.
                    var bnd = AzimuthBoundaries(g, A, R);
                    if (bnd == null)
                    {
                        DrawCellsAsSquares(ctx, g, R, A, scale, Screen);
                        continue;
                    }

                    for (int a = 0; a < A; a++)
                    {
                        double thA = bnd[a], thB = bnd[a + 1];
                        for (int r = 0; r < R; r++)
                        {
                            var cell = g.Cells[a * R + r];
                            if (cell.Count <= 0) continue;   // prazdna bunka se nekresli

                            // Bunku kreslime jako jeji SKUTECNY pudorys = mezikruhovou vysec
                            // (radialni pasmo z RadialEdges x azimutovy slot sloupce). Sdilene hranice
                            // -> vysece se dokonale skladaji (zadny prekryv ani mezery), na rozdil od
                            // drivejsiho ctverce u teziste, ktery se u robota prekryval.
                            var brush = CellBrush(cell.Class, cell.Confidence);
                            double r0 = g.RadialEdges[r].Range * scale;
                            double r1 = g.RadialEdges[r + 1].Range * scale;
                            FillSector(ctx, brush, cx, cy, r0, r1, thA, thB);
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

        // Azimutove hranice bunek (A+1 uhlu, rostouci s indexem sloupce) rekonstruovane z lozisek bunek.
        // Grid neuklada uhly paprsku (jen ColumnsPerCell) - odvodime smer kazdeho azimutoveho sloupce
        // z prumeru smeru jeho obsazenych bunek a hranice klademe do PULKY mezi sousedni sloupce.
        // Diky sdilenym hranicim se vysece dokonale skladaji. Uhel je ~linearni v indexu sloupce, takze
        // chybejici (prazdne) sloupce doplnime lin. interpolaci. Vraci null, kdyz je obsazenych sloupcu < 2.
        private static double[] AzimuthBoundaries(PolarTraversabilityGrid g, int A, int R)
        {
            var bearing = new double[A];
            var has = new bool[A];
            int populated = 0;
            for (int a = 0; a < A; a++)
            {
                // Prumer pres JEDNOTKOVE vektory smeru (robustni k rozptylu vzdalenosti v sloupci).
                double sx = 0, sy = 0; int n = 0;
                for (int r = 0; r < R; r++)
                {
                    var c = g.Cells[a * R + r];
                    if (c.Count <= 0) continue;
                    double rr = Math.Sqrt((double)c.MeanX * c.MeanX + (double)c.MeanY * c.MeanY);
                    if (rr < 1e-6) continue;
                    sx += c.MeanX / rr; sy += c.MeanY / rr; n++;
                }
                if (n > 0) { bearing[a] = Math.Atan2(sy, sx); has[a] = true; populated++; }
            }
            if (populated < 2) return null;

            FillMissingBearings(bearing, has, A);

            var bnd = new double[A + 1];
            for (int a = 1; a < A; a++) bnd[a] = 0.5 * (bearing[a - 1] + bearing[a]);
            // Kraje: zrcadli pulsirku sousedniho slotu, aby krajni bunky mely realnou sirku.
            bnd[0] = bearing[0] - (bnd[1] - bearing[0]);
            bnd[A] = bearing[A - 1] + (bearing[A - 1] - bnd[A - 1]);
            return bnd;
        }

        // Doplni bearing[] pro prazdne sloupce (has==false) linearni interpolaci pres index sloupce;
        // pred prvnim / za poslednim znamym extrapoluje smernici dvou nejblizsich znamych (nebo konst.).
        private static void FillMissingBearings(double[] bearing, bool[] has, int A)
        {
            int first = -1, last = -1;
            for (int a = 0; a < A; a++) if (has[a]) { if (first < 0) first = a; last = a; }

            // Vnitrni mezery: linearni interpolace mezi ohranicujicimi znamymi sloupci.
            int prev = first;
            for (int a = first + 1; a <= last; a++)
            {
                if (!has[a]) continue;
                if (a - prev > 1)
                {
                    double step = (bearing[a] - bearing[prev]) / (a - prev);
                    for (int k = prev + 1; k < a; k++) bearing[k] = bearing[prev] + step * (k - prev);
                }
                prev = a;
            }

            // Kraje: extrapolace smernici u prvni/posledni dvojice znamych (fallback: konstanta).
            double slopeFront = 0, slopeBack = 0;
            int next = -1; for (int a = first + 1; a <= last; a++) if (has[a]) { next = a; break; }
            if (next > first) slopeFront = (bearing[next] - bearing[first]) / (next - first);
            int prevLast = -1; for (int a = last - 1; a >= first; a--) if (has[a]) { prevLast = a; break; }
            if (prevLast >= 0 && prevLast < last) slopeBack = (bearing[last] - bearing[prevLast]) / (last - prevLast);
            for (int a = 0; a < first; a++) bearing[a] = bearing[first] - slopeFront * (first - a);
            for (int a = last + 1; a < A; a++) bearing[a] = bearing[last] + slopeBack * (a - last);
        }

        // Vyplni mezikruhovou vysec (radialni pasmo r0..r1 [px] x azimutovy klin thA..thB [rad]) danym
        // stetcem. Body: bearing 0 = vpred (+X, nahoru), kladny doleva (+Y). Sdilene rohy sousednich
        // bunek zajisti dokonale skladani. Kvuli jemnosti u robota staci rovne tetivy (ne obloucky).
        private static void FillSector(DrawingContext ctx, IBrush brush, double cx, double cy,
                                       double r0, double r1, double thA, double thB)
        {
            Point P(double rho, double th) => new Point(cx - rho * Math.Sin(th), cy - rho * Math.Cos(th));
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(P(r0, thA), isFilled: true);
                gc.LineTo(P(r1, thA));
                gc.LineTo(P(r1, thB));
                gc.LineTo(P(r0, thB));
                gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, geo);
        }

        // Fallback (degenerovana data - viz AzimuthBoundaries == null): puvodni ctverec u teziste.
        private static void DrawCellsAsSquares(DrawingContext ctx, PolarTraversabilityGrid g, int R, int A,
                                               double scale, Func<double, double, Point> screen)
        {
            for (int a = 0; a < A; a++)
                for (int r = 0; r < R; r++)
                {
                    var cell = g.Cells[a * R + r];
                    if (cell.Count <= 0) continue;
                    var brush = CellBrush(cell.Class, cell.Confidence);
                    double side = Math.Max(2.0, (g.RadialEdges[r + 1].Range - g.RadialEdges[r].Range) * scale);
                    var p = screen(cell.MeanX, cell.MeanY);
                    ctx.DrawRectangle(brush, null, new Rect(p.X - side / 2, p.Y - side / 2, side, side));
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
