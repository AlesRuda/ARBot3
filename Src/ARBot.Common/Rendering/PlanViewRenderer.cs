using System;
using System.Collections.Generic;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Occupancy;
using SkiaSharp;

namespace ARBot.Common.Rendering
{
    /// <summary>Bod trajektorie v lokalni ENU rovine [m].</summary>
    public readonly struct PlanViewPoint
    {
        public readonly double X, Y;
        public PlanViewPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// Co se ma na pudorys nakreslit. Vsechno v lokalni ENU rovine - krome uzlu mapy, ktere jsou
    /// v LLA a prevadi je <see cref="Origin"/>.
    /// </summary>
    public sealed class PlanViewInput
    {
        /// <summary>Lokalni mapa (to, co robot vidi). Null = nekresli se.</summary>
        public OccupancyGridMsg Grid;
        /// <summary>Sit cest z mapy (uzly v LLA). Null = nekresli se.</summary>
        public RoadNetwork Network;
        /// <summary>Pocatek lokalni ENU roviny - bez nej se sit nakreslit neda.</summary>
        public GeoReference Origin;

        public bool HasPose;
        public double PoseX, PoseY, PoseTheta;

        public bool HasCarrot;
        public double CarrotX, CarrotY;

        /// <summary>Ujeta draha (nejstarsi prvni). Null nebo prazdne = nekresli se.</summary>
        public IReadOnlyList<PlanViewPoint> Trail;
    }

    /// <summary>Rozmery vykresu.</summary>
    public sealed class PlanViewOptions
    {
        /// <summary>Strana obrazku [px].</summary>
        public int SizePx = 512;
        /// <summary>Sirka vyrezu [m] - kolik metru se vejde na stranu obrazku.</summary>
        public double SpanM = 40;
    }

    /// <summary>
    /// <b>Pudorys okoli robota</b> do PNG: occupancy grid nad siti cest, plus poza, mrkev a ujeta
    /// draha. Sever nahoru, robot ve stredu vyrezu; kdyz poza neni, stred je pocatek lokalni roviny.
    ///
    /// <para><b>Nac to je:</b> webovy nahled headless runtime (doc/headless.md) - jeden obrazek,
    /// ze ktereho se pozna, jestli robot vidi cestu, kam mu ukazuje mrkev a proc pripadne stoji.
    /// Kresli se <b>ze zprav</b>, takze na to vidi i <c>ARBot.Analyze</c> nad zaznamem.</para>
    ///
    /// <para>Bez UI a bez HAL (SkiaSharp je v Common kvuli <see cref="ImageMsg"/>). Barvy drzi
    /// stejnou konvenci jako <see cref="OccupancyPng"/> a mapa v UI: neprujezdne cervene,
    /// potvrzene volne zelene, mrkev zluta, ujeta draha modra.</para>
    /// </summary>
    public static class PlanViewRenderer
    {
        /// <summary>Nakresli pudorys. Vraci <c>null</c>, kdyz kresleni selhalo (volajici z toho udela 503).</summary>
        public static byte[] Render(PlanViewInput input, PlanViewOptions options = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var opt = options ?? new PlanViewOptions();
            int n = Math.Max(32, opt.SizePx);
            double span = opt.SpanM > 0 ? opt.SpanM : 40;
            double pxPerM = n / span;

            // Stred vyrezu: robot, nebo pocatek lokalni roviny, kdyz poza jeste neni.
            double cx = input.HasPose ? input.PoseX : 0;
            double cy = input.HasPose ? input.PoseY : 0;

            // ENU -> pixely. Sever nahoru, takze y je obracene.
            float PX(double x) => (float)(n / 2.0 + (x - cx) * pxPerM);
            float PY(double y) => (float)(n / 2.0 - (y - cy) * pxPerM);

            try
            {
                using var surface = SKSurface.Create(new SKImageInfo(n, n, SKColorType.Bgra8888, SKAlphaType.Premul));
                var c = surface.Canvas;
                c.Clear(new SKColor(0x14, 0x18, 0x1C));

                DrawNetwork(c, input, PX, PY, pxPerM);
                DrawGrid(c, input.Grid, PX, PY, pxPerM, n);
                DrawTrail(c, input.Trail, PX, PY);
                DrawCarrot(c, input, PX, PY, pxPerM);
                DrawRobot(c, input, PX, PY, pxPerM);
                DrawScale(c, n, span);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"PlanViewRenderer: kresleni selhalo: {ex.Message}");
                return null;
            }
        }

        /// <summary>Nejmensi kreslena sirka cesty [m] - uzel s neurcenou sirkou (0) by byl nevidet.</summary>
        private const double MinDrawnWidthM = 0.5;

        /// <summary>
        /// Pruhy cest ze site: uzly jsou v LLA, prevod dela <see cref="GeoReference"/>.
        ///
        /// <para><b>Kazdy usek je kapsle s LINEARNE interpolovanou polosirkou</b> - presne jako
        /// mapova „pravda" <see cref="RoadScene"/> (<c>HalfWidthA = From.Width * 0,5</c>,
        /// <c>HalfWidthB = To.Width * 0,5</c>). Cesta, ktera se rozsiruje, je proto <b>trychtyr</b>,
        /// ne pruh konstantni sirky; do 4. 9. 2026 se kreslila jednou carou o sirce <c>max</c> z obou
        /// uzlu, takze na mape s nalevkou neodpovidala skutecnosti (nalezeno pohledem na nahled).</para>
        ///
        /// <para>Kapsle = trapez mezi uzly plus <b>kruh v kazdem uzlu</b> o polomeru jeho polosirky;
        /// tim se hrany v krizovatce hladce napoji, protoze ji sdileji. Kresli se neprubledne, takze
        /// prekryv kruhu a trapezu nic neztmavi.</para>
        /// </summary>
        private static void DrawNetwork(SKCanvas c, PlanViewInput input,
                                        Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (input.Network?.Edges == null || input.Origin == null) return;

            using var pruh = new SKPaint
            {
                Color = new SKColor(0x55, 0x5A, 0x60), IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var osa = new SKPaint
            {
                Color = new SKColor(0x8A, 0x90, 0x98), IsAntialias = true,
                Style = SKPaintStyle.Stroke, StrokeWidth = 1,
            };

            foreach (var e in input.Network.Edges)
            {
                var a = input.Origin.ToLocal(e.From.Location);
                var b = input.Origin.ToLocal(e.To.Location);
                float ax = PX(a.X), ay = PY(a.Y), bx = PX(b.X), by = PY(b.Y);

                // Polosirky konců v pixelech (Node.Width je CELA sirka cesty v tom uzlu).
                float ha = (float)(Math.Max(e.From.Width, MinDrawnWidthM) * 0.5 * pxPerM);
                float hb = (float)(Math.Max(e.To.Width, MinDrawnWidthM) * 0.5 * pxPerM);

                float dx = bx - ax, dy = by - ay;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 0.01f)
                {
                    // Degenerovana hrana (oba uzly na temze pixelu) - staci kruh.
                    c.DrawCircle(ax, ay, MathF.Max(ha, hb), pruh);
                    continue;
                }

                // Normala k ose useku; trapez ma na kazdem konci svou polosirku.
                float nx = -dy / len, ny = dx / len;
                using (var path = new SKPath())
                {
                    path.MoveTo(ax + nx * ha, ay + ny * ha);
                    path.LineTo(bx + nx * hb, by + ny * hb);
                    path.LineTo(bx - nx * hb, by - ny * hb);
                    path.LineTo(ax - nx * ha, ay - ny * ha);
                    path.Close();
                    c.DrawPath(path, pruh);
                }

                // Zaoblene konce - dohromady s trapezem je to kapsle jako v RoadScene.
                c.DrawCircle(ax, ay, ha, pruh);
                c.DrawCircle(bx, by, hb, pruh);

                c.DrawLine(ax, ay, bx, by, osa);
            }
        }

        /// <summary>Bunky lokalni mapy: neprujezdne cervene, potvrzene volne zelene, nezname nic.</summary>
        private static void DrawGrid(SKCanvas c, OccupancyGridMsg og,
                                     Func<double, float> PX, Func<double, float> PY, double pxPerM, int n)
        {
            if (og?.Occ == null || og.Size <= 0) return;

            using var blocked = new SKPaint { Color = new SKColor(0xE5, 0x39, 0x35, 0xB0) };
            using var free = new SKPaint { Color = new SKColor(0x4C, 0xAF, 0x50, 0x70) };

            // +1 px, aby mezi bunkami nezustaly spary ze zaokrouhleni.
            float side = (float)(og.Resolution * pxPerM) + 1f;
            for (int j = 0; j < og.Size; j++)
            {
                for (int i = 0; i < og.Size; i++)
                {
                    var st = og.State(i, j);
                    if (st == CellState.Unknown) continue;

                    float x = PX(og.CenterX(i)), y = PY(og.CenterY(j));
                    // Hruby vyrez: co je mimo obrazek, se nekresli (Skia by to zahodila sama,
                    // ale u 256x256 bunek se vyplati to nezkouset).
                    if (x < -side || y < -side || x > n + side || y > n + side) continue;

                    var rect = new SKRect(x - side / 2, y - side / 2, x + side / 2, y + side / 2);
                    c.DrawRect(rect, st == CellState.Blocked ? blocked : free);
                }
            }
        }

        private static void DrawTrail(SKCanvas c, IReadOnlyList<PlanViewPoint> trail,
                                      Func<double, float> PX, Func<double, float> PY)
        {
            if (trail == null || trail.Count < 2) return;

            using var paint = new SKPaint
            {
                Color = new SKColor(0x42, 0xA5, 0xF5), IsAntialias = true,
                Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            };
            using var path = new SKPath();
            path.MoveTo(PX(trail[0].X), PY(trail[0].Y));
            for (int k = 1; k < trail.Count; k++) path.LineTo(PX(trail[k].X), PY(trail[k].Y));
            c.DrawPath(path, paint);
        }

        /// <summary>Mrkev (cil lokalni vrstvy) jako kruzek a spojnice od robota.</summary>
        private static void DrawCarrot(SKCanvas c, PlanViewInput input,
                                       Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (!input.HasCarrot) return;

            using var paint = new SKPaint
            {
                Color = new SKColor(0xFF, 0xC1, 0x07), IsAntialias = true,
                Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            };
            float x = PX(input.CarrotX), y = PY(input.CarrotY);
            c.DrawCircle(x, y, (float)Math.Max(4, 0.3 * pxPerM), paint);
            if (input.HasPose)
                c.DrawLine(PX(input.PoseX), PY(input.PoseY), x, y, paint);
        }

        /// <summary>
        /// Robot jako trojuhelnik miric po kurzu. Kurz je matematicky (0 = vychod, +CCW - viz
        /// doc/imu-and-frames.md), takze se do pixelu prepocitava s obracenym smyslem y.
        /// </summary>
        private static void DrawRobot(SKCanvas c, PlanViewInput input,
                                      Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (!input.HasPose) return;

            float x = PX(input.PoseX), y = PY(input.PoseY);
            float r = (float)Math.Max(6, 0.5 * pxPerM);
            double th = input.PoseTheta;

            using var body = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), IsAntialias = true };
            using var path = new SKPath();
            // Vrchol ve smeru kurzu, dva zadni rohy o +-140 stupnu (2,44 rad).
            path.MoveTo(x + (float)(r * Math.Cos(th)), y - (float)(r * Math.Sin(th)));
            path.LineTo(x + (float)(r * 0.7 * Math.Cos(th + 2.44)), y - (float)(r * 0.7 * Math.Sin(th + 2.44)));
            path.LineTo(x + (float)(r * 0.7 * Math.Cos(th - 2.44)), y - (float)(r * 0.7 * Math.Sin(th - 2.44)));
            path.Close();
            c.DrawPath(path, body);
        }

        /// <summary>
        /// Meritko v levem dolnim rohu - bez nej se z obrazku nepozna vzdalenost. Delka usecky je
        /// <see cref="ScaleBarMeters"/>, tedy ctvrtina vyrezu zaokrouhlena na hezke cislo.
        /// </summary>
        private static void DrawScale(SKCanvas c, int n, double span)
        {
            using var linka = new SKPaint
            {
                Color = new SKColor(0xB0, 0xB6, 0xBC), IsAntialias = true,
                Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            };
            using var text = new SKPaint { Color = new SKColor(0xB0, 0xB6, 0xBC), IsAntialias = true };
            using var font = new SKFont { Size = 12 };

            double metry = ScaleBarMeters(span);
            float len = (float)(metry * n / span);
            float y = n - 14, x0 = 12;
            c.DrawLine(x0, y, x0 + len, y, linka);
            c.DrawText(metry < 1 ? $"{metry:0.#} m" : $"{metry:0} m", x0, y - 6, SKTextAlign.Left, font, text);
        }

        /// <summary>
        /// Delka meritkove usecky [m] pro dany vyrez: <b>ctvrtina sirky vyrezu</b> zaokrouhlena dolu
        /// na nejblizsi hezke cislo z rady 0,5 / 1 / 2 / 5 / 10 / 20 / 50 / 100 / 200.
        ///
        /// <para>Ta ctvrtina je konvence, na ktere stoji volba meritka na strance nahledu: tlacitko
        /// „10 m" nastavi vyrez 40 m a usecka pak vyjde presne na 10 m. Verejne kvuli testum a proto,
        /// aby si volajici mohl spocitat, jaky vyrez chce.</para>
        /// </summary>
        public static double ScaleBarMeters(double spanM)
        {
            double cil = (spanM > 0 ? spanM : 40) / 4;
            var hezke = new[] { 0.5, 1, 2, 5, 10, 20, 50, 100, 200 };
            double metry = hezke[0];
            foreach (double h in hezke)
                if (h <= cil) metry = h;
            return metry;
        }

        /// <summary>
        /// Vyrez [m] pro pozadovanou delku meritkove usecky - inverze <see cref="ScaleBarMeters"/>
        /// (usecka je ctvrtina vyrezu). Nesmyslna hodnota spadne na 10 m, tedy vyrez 40 m.
        /// </summary>
        public static double SpanForScaleBar(double scaleBarM)
        {
            if (!(scaleBarM > 0) || !double.IsFinite(scaleBarM) || scaleBarM > 200) scaleBarM = 10;
            return scaleBarM * 4;
        }
    }
}
