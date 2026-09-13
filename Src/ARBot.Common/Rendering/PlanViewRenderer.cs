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

    /// <summary>Usecka v lokalni ENU rovine [m] - jeden usek trasy globalni navigace.</summary>
    public readonly struct PlanViewSegment
    {
        public readonly PlanViewPoint A, B;
        public PlanViewSegment(PlanViewPoint a, PlanViewPoint b) { A = a; B = b; }
        public PlanViewSegment(double ax, double ay, double bx, double by)
            : this(new PlanViewPoint(ax, ay), new PlanViewPoint(bx, by)) { }
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

        /// <summary>
        /// <b>Trasa globalni navigace</b> po siti cest (useky mezi uzly). Null nebo prazdne =
        /// nekresli se. Je to „kudy chce robot jet k cili", tedy plan na desitky az stovky metru.
        /// </summary>
        public IReadOnlyList<PlanViewSegment> Route;

        /// <summary>
        /// <b>Draha z lokalniho planovace</b> (waypointy A*, prvni je u robota). Null nebo kratsi
        /// nez dva body = nekresli se. Je to „co robot udela ted", tedy plan na jednotky metru -
        /// a prave proto se kresli navrch nade vsim krome robota.
        /// </summary>
        public IReadOnlyList<PlanViewPoint> LocalPlan;

        /// <summary>
        /// <b>Zony, ktere maji byt dosazeny</b> (mista mise i s dojezdovym polomerem). Null nebo
        /// prazdne = nekresli se.
        /// </summary>
        public IReadOnlyList<PlanViewZone> Zones;
    }

    /// <summary>
    /// <b>Zona, ktera ma byt dosazena</b> - misto mise (depo, nakladka, vykladka, bod ze seznamu
    /// <c>*.track</c>, cil globalni navigace) i s <b>dojezdovym polomerem</b>, tedy s tim, jak
    /// blizko se robot musi dostat, aby se dojezd ohlasil (<c>NavigatorOptions.ArrivalRadiusMeters</c>).
    ///
    /// <para><b>Nac to je:</b> pri dohledu nad zavodem v terenu je z pudorysu potreba poznat, kam
    /// robot MUSI dojet - ne jen kam prave mine. Polomer se kresli doslova, takze je z obrazku
    /// videt i to, jestli uz je robot uvnitr zony.</para>
    /// </summary>
    public readonly struct PlanViewZone
    {
        /// <summary>Stred zony v lokalni ENU rovine [m].</summary>
        public readonly double X, Y;
        /// <summary>Dojezdovy polomer [m]; nekladny = nakresli se jen znacka stredu.</summary>
        public readonly double RadiusM;
        /// <summary>Kratky popis (bez diakritiky - viz <see cref="PlanViewRenderer"/>); muze byt prazdny.</summary>
        public readonly string Label;
        /// <summary>Je to zona, na kterou se PRAVE jede? Ta se kresli plnou carou, ostatni carkovane.</summary>
        public readonly bool Active;

        public PlanViewZone(double x, double y, double radiusM, string label, bool active = false)
        {
            X = x; Y = y; RadiusM = radiusM; Label = label; Active = active;
        }
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

                // Poradi je odzadu dopredu podle toho, jak DALEKO dopredu ten udaj mluvi:
                // sit (staticka mapa) -> co robot vidi ted (grid) -> kam chce k cili (trasa) ->
                // kudy uz jel (draha) -> co udela v pristich metrech (lokalni plan) -> mrkev ->
                // robot. Lokalni plan je tedy nade vsim krome robota: je to odpoved na otazku
                // „co se robot chysta udelat", kvuli ktere nahled vznikl.
                //
                // Zony (kam robot MUSI dojet) mluvi dopredu ze vseho nejdal, patrily by tedy uplne
                // dozadu - kresli se ale az NAD gridem, protoze grid je poloprubledny a plne pole
                // cervenych bunek by z kruzku udelalo necitelnou skvrnu. Cely jejich smysl je
                // orientace cloveka pri dohledu, takze citelnost vyhrava nad konvenci poradi.
                DrawNetwork(c, input, PX, PY, pxPerM);
                DrawGrid(c, input.Grid, PX, PY, pxPerM, n);
                DrawZones(c, input.Zones, PX, PY, pxPerM);
                DrawRoute(c, input.Route, PX, PY);
                DrawTrail(c, input.Trail, PX, PY);
                DrawLocalPlan(c, input.LocalPlan, PX, PY, pxPerM);
                DrawCarrot(c, input, PX, PY, pxPerM);
                DrawRobot(c, input, PX, PY, pxPerM);
                DrawScale(c, n, span);
                DrawLegend(c, input, n);

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

        /// <summary>Barva ujete drahy (modra) - drzi konvenci mapy v UI.</summary>
        private static readonly SKColor TrailColor = new SKColor(0x42, 0xA5, 0xF5);

        /// <summary>Barva mrkve (zluta).</summary>
        private static readonly SKColor CarrotColor = new SKColor(0xFF, 0xC1, 0x07);

        /// <summary>
        /// Barva zon, ktere maji byt dosazeny (svetle zelena). Vlastni odstin - zelena gridu je
        /// poloprubledna vypln bunek, kdezto tohle je vzdycky <b>kruznice s popisem</b>, takze se
        /// to neplete; s mrkvi (zluta) uz vubec ne, a to je podstatne: mrkev je bod, kam robot
        /// miri <i>ted</i>, zona je misto, kam <i>musi</i> dojet.
        /// </summary>
        private static readonly SKColor ZoneColor = new SKColor(0x9C, 0xCC, 0x65);

        /// <summary>
        /// <b>Zony, ktere maji byt dosazeny</b>: kruznice o dojezdovem polomeru plus krizek ve
        /// stredu a kratky popis.
        ///
        /// <para><b>Aktivni zona</b> (ta, na kterou se prave jede) je plnou carou, ostatni
        /// <b>carkovane</b> - jinak by se z obrazku nepoznalo, ktere misto robot resi ted a ktera
        /// jsou zbytek seznamu. Popis se pise nad kruznici, aby ho neprekryla trasa.</para>
        ///
        /// <para>Zona mensi nez par pixelu (velky vyrez) by byla tecka, takze se kruznice kresli
        /// nejmene o polomeru <see cref="MinZoneRadiusPx"/> - poloha zustava pravdiva, jen jeji
        /// velikost uz pri tom meritku nic nerika. Krizek ve stredu je tam prave proto, aby
        /// <b>stred</b> byl jednoznacny i tehdy.</para>
        /// </summary>
        private static void DrawZones(SKCanvas c, IReadOnlyList<PlanViewZone> zones,
                                      Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (zones == null || zones.Count == 0) return;

            using var kruznice = new SKPaint
            {
                Color = ZoneColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            };
            using var carkovane = new SKPaint
            {
                Color = ZoneColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f,
                PathEffect = SKPathEffect.CreateDash(new[] { 5f, 4f }, 0),
            };
            using var krizek = new SKPaint
            {
                Color = ZoneColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f,
            };
            using var font = new SKFont { Size = 11 };
            using var text = new SKPaint { Color = ZoneColor, IsAntialias = true };

            foreach (var z in zones)
            {
                float x = PX(z.X), y = PY(z.Y);
                float r = (float)Math.Max(MinZoneRadiusPx, z.RadiusM * pxPerM);
                c.DrawCircle(x, y, r, z.Active ? kruznice : carkovane);

                const float k = 3;
                c.DrawLine(x - k, y, x + k, y, krizek);
                c.DrawLine(x, y - k, x, y + k, krizek);

                if (!string.IsNullOrEmpty(z.Label))
                    c.DrawText(z.Label, x, y - r - 4, SKTextAlign.Center, font, text);
            }
        }

        /// <summary>Nejmensi kreslena kruznice zony [px] - pri velkem vyrezu by z ni byla tecka.</summary>
        private const float MinZoneRadiusPx = 4;

        /// <summary>Barva trasy globalni navigace (fialova) - vlastni odstin, aby si ji nikdo
        /// nespletl s ujetou drahou (modra) ani s mrkvi (zluta).</summary>
        private static readonly SKColor RouteColor = new SKColor(0xAB, 0x47, 0xBC);

        /// <summary>Barva drahy z lokalniho planovace (azurova).</summary>
        private static readonly SKColor PlanColor = new SKColor(0x00, 0xE5, 0xFF);

        /// <summary>
        /// <b>Trasa globalni navigace</b> po siti cest - lomena cara pres uzly trasy.
        ///
        /// <para>Kresli se az NAD occupancy gridem: grid rika, co robot vidi ted, trasa kam chce
        /// dojet, a kdyz se prekryvaji, je podstatnejsi, aby byla videt trasa. Useky jsou samostatne
        /// (ne jedna <c>SKPath</c>), protoze zprava nese hrany, ne serazenou lomenou caru - poradi
        /// hran neni zarucene a spojovat je do jedne cary by vyrobilo prelety pres pul mapy.</para>
        /// </summary>
        private static void DrawRoute(SKCanvas c, IReadOnlyList<PlanViewSegment> route,
                                      Func<double, float> PX, Func<double, float> PY)
        {
            if (route == null || route.Count == 0) return;

            using var paint = new SKPaint
            {
                Color = RouteColor, IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = 4, StrokeCap = SKStrokeCap.Round,
            };
            foreach (var seg in route)
                c.DrawLine(PX(seg.A.X), PY(seg.A.Y), PX(seg.B.X), PY(seg.B.Y), paint);
        }

        /// <summary>
        /// <b>Draha z lokalniho planovace</b>: lomena cara pres waypointy a kolecko v kazdem z nich.
        ///
        /// <para>Uzly se kresli schvalne - jejich ROZESTUP je vysledek vyhlazovani drahy
        /// (<c>smooth=</c>, viz doc/occupancy-and-local-planning.md) a z obrazku je tak videt
        /// i to, jestli planovac drahu slucuje, nebo ji seka na centimetry. Prvni uzel lezi
        /// u robota, posledni v dosazenem cili.</para>
        /// </summary>
        private static void DrawLocalPlan(SKCanvas c, IReadOnlyList<PlanViewPoint> plan,
                                          Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (plan == null || plan.Count < 2) return;

            using var cara = new SKPaint
            {
                Color = PlanColor, IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
            };
            using var uzel = new SKPaint { Color = PlanColor, IsAntialias = true };

            using var path = new SKPath();
            path.MoveTo(PX(plan[0].X), PY(plan[0].Y));
            for (int k = 1; k < plan.Count; k++) path.LineTo(PX(plan[k].X), PY(plan[k].Y));
            c.DrawPath(path, cara);

            // Kolecka jen kdyz je vyrez dost velky - pri 50m meritku by z nich byla soucista cara.
            float r = (float)Math.Min(3.0, Math.Max(1.5, 0.08 * pxPerM));
            foreach (var wp in plan)
                c.DrawCircle(PX(wp.X), PY(wp.Y), r, uzel);
        }

        private static void DrawTrail(SKCanvas c, IReadOnlyList<PlanViewPoint> trail,
                                      Func<double, float> PX, Func<double, float> PY)
        {
            if (trail == null || trail.Count < 2) return;

            using var paint = new SKPaint
            {
                Color = TrailColor, IsAntialias = true,
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
                Color = CarrotColor, IsAntialias = true,
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
        /// <b>Legenda</b> v pravem dolnim rohu - jen k tomu, co se na obrazku doopravdy kresli.
        ///
        /// <para><b>Nac to je:</b> po pridani trasy a lokalniho planu (12. 9. 2026) je na pudorysu
        /// pet barevnych car a bez legendy se z nich da jen hadat. Kresli se do PNG, ne do stranky,
        /// aby platila i tam, kde se obrazek jen ulozi (<c>ARBot.Analyze</c>, snimek z terenu).</para>
        ///
        /// <para>Bez diakritiky zamerne: pismo bere Skia ze systemu a na zarizeni neni jiste, ze
        /// nektery font 'a' s carkou ma - misto textu by byly obdelnicky.</para>
        ///
        /// <para>⚠️ <b>Polozka musi byt ke KAZDE kreslene care.</b> Prvni verze legendy vynechala
        /// ujetou drahu, takze modra cara za robotem zustala jedina nepopsana - a prave ta se
        /// odstinem plete s azurovym lokalnim planem.</para>
        /// </summary>
        private static void DrawLegend(SKCanvas c, PlanViewInput input, int n)
        {
            var polozky = new List<(SKColor Color, string Text)>();
            if (input.Zones != null && input.Zones.Count > 0) polozky.Add((ZoneColor, "zona"));
            if (input.Route != null && input.Route.Count > 0) polozky.Add((RouteColor, "trasa"));
            if (input.Trail != null && input.Trail.Count >= 2) polozky.Add((TrailColor, "draha"));
            if (input.LocalPlan != null && input.LocalPlan.Count >= 2) polozky.Add((PlanColor, "plan"));
            if (input.HasCarrot) polozky.Add((CarrotColor, "mrkev"));
            if (polozky.Count == 0) return;

            using var font = new SKFont { Size = 11 };
            using var text = new SKPaint { Color = new SKColor(0xB0, 0xB6, 0xBC), IsAntialias = true };
            using var vzorek = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3,
                StrokeCap = SKStrokeCap.Round,
            };

            const float radek = 14, delkaVzorku = 14, mezera = 5;
            float y = n - 8 - (polozky.Count - 1) * radek;
            foreach (var (color, popis) in polozky)
            {
                float sirka = font.MeasureText(popis);
                float x1 = n - 8 - sirka;
                vzorek.Color = color;
                c.DrawLine(x1 - mezera - delkaVzorku, y - 4, x1 - mezera, y - 4, vzorek);
                c.DrawText(popis, x1, y, SKTextAlign.Left, font, text);
                y += radek;
            }
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
