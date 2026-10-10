using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Vision;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Okluzni pravidlo barvy ze zaznamu</b> (prikaz <c>okluze</c>, tema
    /// <c>vid-inshadow-zahazuje-vzorky</c>): kolik barevnych vzorku zahodi stin za prvni prekazkou
    /// v azimutu (<c>OccupancyIntegrator.InShadow</c>) a CO ten stin vrha.
    ///
    /// <para><b>Proc vlastni replika smycky:</b> integrator pocita jen <c>ColorShadowed</c>, a to
    /// i bunky, kam barva vubec nedosahne (mimo zorne pole barvy, za dosahem duvery) — cislo ze
    /// simulace 14. 8. (5 200 z 12 000) je tim nadsazene. Replika prochazi tytez bunky stejnym
    /// poradim a pro kazdou rozhodne, jestli by se barva ZAPSALA, kdyby stin nebyl (kandidat),
    /// a u zahozenych zjisti vrhace stinu: vysku nad rovinou zeme (<c>MaxZ</c> proti rovine
    /// prolozene jako ve zpracovani snimku), velikost skvrny (souvisla oblast prekazek v polarnim
    /// gridu), duvod klasifikace a vzdalenost za nabeznou hranou. Spravnost repliky se overuje
    /// <b>kazdy snimek</b> proti <c>LastStats</c> skutecneho integratoru (shadow, cproj, road).</para>
    ///
    /// <para><b>Protifakty</b> (rozhodnuti autora 1. 10. 2026 ceka na tato cisla): (a) stin podle
    /// vysky prekazky — bod ve vzdalenosti <c>r</c> je zakryty, jen kdyz <c>e ≤ r &lt; e'·Cz/(Cz−zT)</c>
    /// pro nekterou prekazku azimutu (<c>e</c> / <c>e'</c> nabezna / vnejsi hrana jejiho prstence,
    /// <c>zT</c> jeji vrchol a <c>Cz</c> vyska kamery, obe ABSOLUTNE v ramci robotu - geometrie
    /// integratoru od 10. 10. 2026, viz <c>ColorShadowMode.Height</c>); (b) ignorovat skvrny do N
    /// bunek polarniho gridu. Popisne vysky (rozpad vrhacu, prahy 5 / 15 cm) jsou nad prolozenou
    /// rovinou zeme. Od 10. 10. 2026 overuje i NOVE pravidlo: druhy integrator s vychozi konfiguraci
    /// proti predpovedi repliky (vrchol <c>MeanZ + √3·StdZ</c>, bez sebestineni).</para>
    /// </summary>
    public static class OkluzeReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private static readonly List<Point4D> rovinaBody = new List<Point4D>();

        /// <summary>Konec stinu prekazky v prstenci <paramref name="r"/> s vrcholem <paramref name="zTop"/>
        /// pri kamere ve vysce <paramref name="cz"/> - TYZ vzorec jako <c>OccupancyIntegrator</c>
        /// (absolutni vysky v ramci robotu, vnejsi hrana prstence); -inf = nestini.</summary>
        private static double KonecStinu(RadialEdge[] edges, int r, double zTop, double cz)
            => zTop <= 0 ? double.NegativeInfinity
             : zTop >= cz ? double.PositiveInfinity
             : edges[r + 1].Range * cz / (cz - zTop);

        private sealed class Kamera
        {
            public ICameraProjection D, C;
            public double KameraX, KameraY, KameraZ;   // poloha kamery v ramci robotu [m]
        }

        private sealed class Souhrn
        {
            public long Snimky, BezPozy, BezProjekce, BezBarvy, Nesedi;
            public long Shadow;         // integrator: ColorShadowed (vsechny bunky ve stinu)
            public long Kandidati;      // barva by se zapsala, kdyby stin nebyl
            public long Zapsano;        // zapsano (kandidat a ne ve stinu)
            public long Ztraceno;       // kandidat ve stinu
            public long ZtracenoCesta;  // z toho barva rika „cesta" (p > 0,5)
            public long ZachranVyska;   // stin podle vysky by vzorek NEzahodil
            public long Zachran1, Zachran4, Zachran16;   // ignorovat skvrny <= N bunek
            public long ZachranVyskaA4; // vyska + skvrny <= 4
            public long ZachranVyskaStriktne; // vyska a prekazka nestini sama sebe (jen drivejsi prstence)
            public long ZachranH5, ZachranH15;   // dnesni pravidlo, ale vrha jen prekazka s vrcholem > 5 / 15 cm
            public long ZachranJenVyska;         // dnesni pravidlo, ale vrha jen prekazka klasifikovana VYSKOU (ne drsnost/sklon)
            public long ZachranGeomH5;           // geometricky stin jen od prekazek s vrcholem > 5 cm nad rovinou
            public readonly long[] VlastniTrida = new long[4]; // zahozeny vzorek: Obstacle / Free / Unknown / mimo dosah hloubky
            public readonly long[] PoVysce = new long[6];     // vrchol vrhace nad rovinou
            public readonly long[] PoSkvrne = new long[4];    // velikost skvrny vrhace
            public readonly long[] ZaHranou = new long[5];    // vzdalenost vzorku za nabeznou hranou
            public readonly long[] PoVzdalenostiVrhace = new long[5];
            public long DuvodVyska, DuvodJiny;                // klasifikace vrhace: vyska / drsnost-sklon
            public readonly List<double> PodilPoSnimcich = new List<double>();
            public readonly Dictionary<string, List<double>> VyskaKamery = new Dictionary<string, List<double>>();

            // Vypocetni narocnost: O(1) dotazy (prefixove maximum konce stinu) proti hrube sile
            // a casy jednotlivych kroku za snimek (Stopwatch ticky).
            public long NesediO1Vyska, NesediO1Striktne, NesediO1H15;   // O(1) dotaz proti hrube sile (ma byt 0)
            public long NesediO1Hrana;  // z neshod: bod pred nabeznou hranou sveho prstence (float/double zaokrouhleni)
            public long TikIntegrate, TikRovina, TikDnes, TikH15, TikPrefix;
            public int A, R;
            public long BunekVDosahu;

            // Nove pravidlo v integratoru (colorshadow=height, od 10. 10. 2026) proti predpovedi repliky.
            public long NesediNove, ZapsanoNove, TikIntegrateNove;
            public long ZalohaNove;                                   // snimky, kde integrator spadl na prvni prekazku

            // Stin podle vysky s jinou vyskou vrhace (otazka autora 10. 10. 2026: MeanZ misto MaxZ?).
            // Varianta: 0 = MaxZ, 1 = MeanZ, 2 = MeanZ + sqrt(3)*StdZ; sebestineni: 0 = ano, 1 = ne.
            public readonly long[,] ZachranZ = new long[3, 2];
            public readonly long[,] ZachranZCesta = new long[3, 2];   // z toho barva rika „cesta"
            public readonly long[] NavicZ = new long[3];              // zachranene NAVIC proti MaxZ (bez sebestineni)
            public readonly long[] NavicZCesta = new long[3];
            public readonly long[,] NavicZVrhac = new long[3, 6];     // vrchol (MaxZ) vrhace, ktery by podle MaxZ stinil
            public readonly long[,] NavicZVrhacVzd = new long[3, 5];  // jeho vzdalenost od robotu
            public readonly long[,] NavicZVrhacBodu = new long[3, 4]; // pocet bodu v jeho bunce
        }

        private static readonly string[] VyskaPopis = { "<= 5 cm", "5-15 cm", "15-30 cm", "30-60 cm", "> 60 cm (pod kamerou)", ">= kamera" };
        private static readonly string[] SkvrnaPopis = { "1 bunka", "2-4", "5-16", "> 16" };
        private static readonly string[] ZaHranouPopis = { "< 0,5 m", "0,5-1 m", "1-2 m", "2-4 m", "> 4 m" };
        private static readonly string[] VrhacPopis = { "< 1 m", "1-2 m", "2-3 m", "3-4 m", "> 4 m" };

        public static void Run(RecordFile rec, int frameStep, int limit)
        {
            var konf = LogConfig.Read(rec);
            if (konf.Version != null) Console.WriteLine(konf.Version);
            if (frameStep < 1) frameStep = 1;

            var gcfg = new OccupancyGridConfig();
            var gridMsg = rec.Index.FirstOrDefault(e => e.MsgName == "OccupancyGridMsg");
            if (gridMsg.MsgName != null && rec.Read(gridMsg) is OccupancyGridMsg g0)
            {
                gcfg = new OccupancyGridConfig
                {
                    Size = g0.Size, Resolution = g0.Resolution, Scale = g0.Scale,
                    BlockedThreshold = g0.BlockedThreshold, FreeThreshold = g0.FreeThreshold,
                };
            }
            // Replika i vsechny protifakty se merily proti PUVODNIMU pravidlu (prvni prekazka azimutu),
            // proto ho overovaci integrator ma nastavene vyslovne; od 10. 10. 2026 je vychozi stin podle
            // vysky a ten se overuje druhym integratorem proti predpovedi repliky (varianta
            // MeanZ + √3·StdZ bez sebestineni).
            var icfg = new OccupancyIntegratorConfig { ColorShadow = ColorShadowMode.FirstObstacle };
            var pcfg = new PolarGridConfig();

            // Skutecny integrator (overeni repliky) a geometrie repliky - dva gridy tehoz nastaveni.
            var gInteg = new OccupancyGrid(gcfg);
            var integ = new OccupancyIntegrator(gInteg, icfg);
            var integNove = new OccupancyIntegrator(new OccupancyGrid(gcfg), new OccupancyIntegratorConfig());
            var gRep = new OccupancyGrid(gcfg);

            var kamery = new Dictionary<string, Kamera>();
            var s = new Souhrn();
            var snimky = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            Console.WriteLine(string.Format(Ci, "snimku v zaznamu {0}, kazdy {1}.{2}", snimky.Count, frameStep,
                limit > 0 ? $", nejvys {limit}" : ""));

            int zpracovano = 0;
            for (int fi = 0; fi < snimky.Count; fi += frameStep)
            {
                if (limit > 0 && zpracovano >= limit) break;
                if (!(rec.Read(snimky[fi]) is CameraFrame f)) continue;
                if (!f.HasPose) { s.BezPozy++; continue; }
                var kam = Projekce(f, kamery);
                if (kam == null || kam.D == null) { s.BezProjekce++; continue; }
                var polar = f.Grid;
                if (polar == null || polar.RadialCount == 0 || polar.AzimuthCount == 0
                    || f.ImageProbability == null || kam.C == null) { s.BezBarvy++; continue; }

                Snimek(f, kam, polar, gRep, integ, integNove, icfg, pcfg, s);
                zpracovano++;
            }

            Tisk(s);
        }

        private static void Snimek(CameraFrame f, Kamera kam, PolarTraversabilityGrid polar,
                                   OccupancyGrid gRep, OccupancyIntegrator integ, OccupancyIntegrator integNove,
                                   OccupancyIntegratorConfig icfg, PolarGridConfig pcfg, Souhrn s)
        {
            double robotX = f.PoseAtCaptureX, robotY = f.PoseAtCaptureY, heading = f.PoseAtCaptureTheta;

            // --- skutecny integrator (overeni) ---
            long t0 = Stopwatch.GetTimestamp();
            integ.Integrate(f, kam.D, kam.C, robotX, robotY, heading);
            s.TikIntegrate += Stopwatch.GetTimestamp() - t0;
            var st = integ.LastStats;
            s.BunekVDosahu += st.CellsInRange;
            t0 = Stopwatch.GetTimestamp();
            integNove.Integrate(f, kam.D, kam.C, robotX, robotY, heading);
            s.TikIntegrateNove += Stopwatch.GetTimestamp() - t0;
            var stN = integNove.LastStats;

            // --- rovina zeme jako CameraFrameProcessor.FitReferencePlane (je internal, replika) ---
            t0 = Stopwatch.GetTimestamp();
            var pts = rovinaBody; pts.Clear();
            float maxR2 = pcfg.PlaneFitMaxRangeM * pcfg.PlaneFitMaxRangeM;
            foreach (var c in polar.Cells)
            {
                if (c.Count < pcfg.MinPointsPerCell) continue;
                float r2 = c.MeanX * c.MeanX + c.MeanY * c.MeanY;
                if (r2 > maxR2) continue;
                if (Math.Abs(c.MeanZ) > pcfg.PlaneFitMaxAbsHeightM) continue;
                pts.Add(new Point4D { X = c.MeanX, Y = c.MeanY, Z = c.MeanZ, A = 1 });
            }
            var plane = pts.Count >= 3
                ? new ARBot.Common.Algorithms.ComputeUnit.PlaneParams(pts)
                : new ARBot.Common.Algorithms.ComputeUnit.PlaneParams { v = new Point4D { X = 0, Y = 0, Z = 1, A = 0 } };
            s.TikRovina += Stopwatch.GetTimestamp() - t0;
            float Nad(float x, float y, float z) => new Point4D { X = x, Y = y, Z = z, A = 1 } * plane.v;
            double H = Nad((float)kam.KameraX, (float)kam.KameraY, (float)kam.KameraZ);   // popisne: kamera nad rovinou
            double Cz = kam.KameraZ;                                                         // geometrie stinu: v ramci robotu
            string jm = (f.Name ?? "").Split(' ')[0];
            if (!s.VyskaKamery.TryGetValue(jm, out var vk)) s.VyskaKamery[jm] = vk = new List<double>();
            if (vk.Count < 2000) vk.Add(H);

            // --- polarni grid: prekazky, skvrny, stin dnes a protifakty ---
            int A = polar.AzimuthCount, R = polar.RadialCount;
            var cells = polar.Cells;
            var komp = new int[A * R];
            var velikost = new List<int> { 0 };
            for (int a = 0; a < A; a++)
                for (int r = 0; r < R; r++)
                {
                    int idx = a * R + r;
                    if (cells[idx].Class != TraversabilityClass.Obstacle || komp[idx] != 0) continue;
                    int id = velikost.Count, n = 0;
                    var q = new Stack<int>();
                    q.Push(idx); komp[idx] = id;
                    while (q.Count > 0)
                    {
                        int k = q.Pop(); n++;
                        int ka = k / R, kr = k % R;
                        for (int da = -1; da <= 1; da++)
                            for (int dr = -1; dr <= 1; dr++)
                            {
                                int na = ka + da, nr = kr + dr;
                                if (na < 0 || na >= A || nr < 0 || nr >= R) continue;
                                int ni = na * R + nr;
                                if (komp[ni] != 0 || cells[ni].Class != TraversabilityClass.Obstacle) continue;
                                komp[ni] = id; q.Push(ni);
                            }
                    }
                    velikost.Add(n);
                }

            var prvni = new int[A];          // dnesni pravidlo (BuildShadow)
            var prvni1 = new int[A]; var prvni4 = new int[A]; var prvni16 = new int[A];
            var prvniH5 = new int[A]; var prvniH15 = new int[A]; var prvniVys = new int[A];
            var vrhaceGeomH5 = new List<(double e, double d)>[A];
            var vrhaceVyska = new List<(double e, double d)>[A];    // stin podle vysky: [e, D)
            var vrhaceVyska4 = new List<(double e, double d)>[A];   // totez bez skvrn <= 4
            var vrhaceStriktne = new List<(int r, double e, double d)>[A]; // s indexem prstence
            var edges = polar.RadialEdges;
            for (int a = 0; a < A; a++)
            {
                prvni[a] = prvni1[a] = prvni4[a] = prvni16[a] = int.MaxValue;
                prvniH5[a] = prvniH15[a] = prvniVys[a] = int.MaxValue;
                vrhaceGeomH5[a] = new List<(double, double)>();
                vrhaceVyska[a] = new List<(double, double)>();
                vrhaceVyska4[a] = new List<(double, double)>();
                vrhaceStriktne[a] = new List<(int, double, double)>();
                for (int r = 0; r < R; r++)
                {
                    int idx = a * R + r;
                    if (cells[idx].Class != TraversabilityClass.Obstacle) continue;
                    int vel = velikost[komp[idx]];
                    if (prvni[a] == int.MaxValue) prvni[a] = r;
                    if (vel > 1 && prvni1[a] == int.MaxValue) prvni1[a] = r;
                    if (vel > 4 && prvni4[a] == int.MaxValue) prvni4[a] = r;
                    if (vel > 16 && prvni16[a] == int.MaxValue) prvni16[a] = r;
                    double e = edges[r].Range;
                    double h = Nad(cells[idx].MeanX, cells[idx].MeanY, cells[idx].MaxZ);   // popisna vyska nad rovinou
                    double d = KonecStinu(edges, r, cells[idx].MaxZ, Cz);
                    vrhaceVyska[a].Add((e, d));
                    if (vel > 4) vrhaceVyska4[a].Add((e, d));
                    vrhaceStriktne[a].Add((r, e, d));
                    if (h > 0.05) { if (prvniH5[a] == int.MaxValue) prvniH5[a] = r; vrhaceGeomH5[a].Add((e, d)); }
                    if (h > 0.15 && prvniH15[a] == int.MaxValue) prvniH15[a] = r;
                    float rngC = MathF.Sqrt(cells[idx].MeanX * cells[idx].MeanX + cells[idx].MeanY * cells[idx].MeanY);
                    if (Math.Abs(Nad(cells[idx].MeanX, cells[idx].MeanY, cells[idx].MeanZ)) > pcfg.MaxHeightDev(rngC)
                        && prvniVys[a] == int.MaxValue) prvniVys[a] = r;
                }
            }

            // --- jak by to stalo v INTEGRATORU: stavba za snimek O(A*R), dotaz na bunku O(1) ---
            // Casuje se jen tohle (hruba sila vyse slouzi k rozboru, ne jako navrh implementace).
            var dnesT = new int[A]; var h15T = new int[A]; var konec = new double[A * R];
            t0 = Stopwatch.GetTimestamp();
            for (int a = 0; a < A; a++)            // dnes = BuildShadow: prvni prekazka azimutu
            {
                int k = int.MaxValue;
                for (int r = 0; r < R; r++)
                    if (cells[a * R + r].Class == TraversabilityClass.Obstacle) { k = r; break; }
                dnesT[a] = k;
            }
            s.TikDnes += Stopwatch.GetTimestamp() - t0;
            t0 = Stopwatch.GetTimestamp();
            for (int a = 0; a < A; a++)            // vrha jen prekazka s vrcholem > 15 cm (potrebuje rovinu)
            {
                int k = int.MaxValue;
                for (int r = 0; r < R; r++)
                {
                    ref readonly var c = ref cells[a * R + r];
                    if (c.Class == TraversabilityClass.Obstacle && Nad(c.MeanX, c.MeanY, c.MaxZ) > 0.15f) { k = r; break; }
                }
                h15T[a] = k;
            }
            s.TikH15 += Stopwatch.GetTimestamp() - t0;
            t0 = Stopwatch.GetTimestamp();
            for (int a = 0; a < A; a++)            // stin podle vysky: prefixove maximum konce stinu podel azimutu
            {
                double m = double.NegativeInfinity;
                for (int r = 0; r < R; r++)
                {
                    int idx = a * R + r;
                    ref readonly var c = ref cells[idx];
                    if (c.Class == TraversabilityClass.Obstacle)
                    {
                        double d = KonecStinu(edges, r, c.MaxZ, Cz);
                        if (d > m) m = d;
                    }
                    konec[idx] = m;
                }
            }
            s.TikPrefix += Stopwatch.GetTimestamp() - t0;
            s.A = A; s.R = R;

            // Tytez prefixy pro tri odhady vysky vrhace, s indexem prstence vrhace, ktery konec drzi.
            // MeanZ + sqrt(3)*StdZ: u svisle plochy s rovnomerne rozlozenymi body vrati presne jeji vrchol
            // (stred h/2, smerodatna odchylka h/sqrt(12)), u travy ~96. percentil - robustni vrchol bez MaxZ.
            var konecV = new double[3][]; var argV = new int[3][];
            for (int v = 0; v < 3; v++)
            {
                konecV[v] = new double[A * R]; argV[v] = new int[A * R];
                for (int a = 0; a < A; a++)
                {
                    double m = double.NegativeInfinity; int arg = -1;
                    for (int r = 0; r < R; r++)
                    {
                        int idx = a * R + r;
                        ref readonly var c = ref cells[idx];
                        if (c.Class == TraversabilityClass.Obstacle)
                        {
                            float z = v == 0 ? c.MaxZ : v == 1 ? c.MeanZ : c.MeanZ + 1.7320508f * c.StdZ;
                            double d = KonecStinu(edges, r, z, Cz);
                            if (d > m) { m = d; arg = r; }
                        }
                        konecV[v][idx] = m; argV[v][idx] = arg;
                    }
                }
            }
            // Prstenec vrhace, ktery bod zakryva (varianta v), jinak -1.
            int Vrhac(int v, int a, int rb, double range, bool sebe)
            {
                if (range < edges[0].Range) return -1;
                int k = rb >= 0 ? (sebe ? rb : rb - 1) : R - 1;
                if (k < 0) return -1;
                int i = a * R + k;
                return konecV[v][i] > range ? argV[v][i] : -1;
            }

            // Bod v prstenci rb je zakryty, kdyz konec stinu nektereho prstence k <= rb (nabezna hrana
            // e_k <= range) presahuje range; bez sebestineni jen k < rb. Za dosahem hloubky (rb = -1,
            // range >= posledni hrana) plati vsechny prstence.
            bool StinPrefix(int a, int rb, double range, bool sebe)
            {
                if (range < edges[0].Range) return false;
                int k = rb >= 0 ? (sebe ? rb : rb - 1) : R - 1;
                return k >= 0 && konec[a * R + k] > range;
            }

            bool StinPodle(int[] prv, int a, double range)
                => prv[a] != int.MaxValue && range >= edges[prv[a]].Range;
            bool StinVyska(List<(double e, double d)>[] v, int a, double range)
            {
                foreach (var (e, d) in v[a]) if (range >= e && range < d) return true;
                return false;
            }

            // --- replika smycky integratoru ---
            gRep.Recenter(robotX, robotY);
            var prob = f.ImageProbability;
            double probScaleX = 1, probScaleY = 1;
            if (f.ImageRGB != null && prob.Width > 0 && prob.Height > 0)
            {
                probScaleX = (double)f.ImageRGB.Width / prob.Width;
                probScaleY = (double)f.ImageRGB.Height / prob.Height;
            }
            double maxRange = icfg.MaxRangeM > 0 ? icfg.MaxRangeM
                : Math.Min(Math.Max(icfg.RoadMaxRangeM, edges[edges.Length - 1].Range), gRep.Size * gRep.Resolution * 0.5);
            double cosH = Math.Cos(heading), sinH = Math.Sin(heading);
            int span = (int)Math.Ceiling(maxRange / gRep.Resolution) + 1;
            int cx0 = gRep.CellX(robotX), cy0 = gRep.CellY(robotY);
            double maxRange2 = maxRange * maxRange;

            long shadow = 0, cproj = 0, road = 0, kand = 0, ztr = 0;
            long shadowN = 0, cprojN = 0, roadN = 0;   // predpoved noveho pravidla (MeanZ + √3·StdZ, bez sebestineni)
            for (int cy = cy0 - span; cy <= cy0 + span; cy++)
                for (int cx = cx0 - span; cx <= cx0 + span; cx++)
                {
                    if (!gRep.Contains(cx, cy)) continue;
                    double dx = gRep.CenterX(cx) - robotX, dy = gRep.CenterY(cy) - robotY;
                    double r2 = dx * dx + dy * dy;
                    if (r2 > maxRange2) continue;
                    float rx = (float)(dx * cosH + dy * sinH);
                    float ry = (float)(-dx * sinH + dy * cosH);
                    double range = Math.Sqrt(r2);

                    int azimuth = -1, rb = -1;
                    bool beyond = true;
                    float col = 0, row = 0;
                    if (kam.D.Transform(rx, ry, ref col, ref row))
                    {
                        azimuth = polar.AzimuthBinFromColumn((int)Math.Round(col), icfg.EdgeColumnTrim);
                        rb = azimuth >= 0 ? polar.RadialBin((float)range) : -1;
                        if (rb >= 0) beyond = false;
                    }
                    bool inShadow = azimuth >= 0 && StinPodle(prvni, azimuth, range);
                    if (inShadow) shadow++;
                    bool inShadowNove = azimuth >= 0 && Vrhac(2, azimuth, rb, range, false) >= 0;
                    if (inShadowNove) shadowN++;

                    // Kandidat: barva by se zapsala, kdyby stin nebyl.
                    if (!(icfg.RoadBeyondDepthRange || !beyond)) continue;
                    float conf = icfg.RoadConfidence(range);
                    if (conf <= 0) continue;
                    float ccol = 0, crow = 0;
                    bool proj = kam.C.Transform(rx, ry, ref ccol, ref crow);
                    if (proj && !inShadow) cproj++;
                    if (proj && !inShadowNove) cprojN++;
                    if (!proj) continue;
                    int px = (int)(ccol / probScaleX), py = (int)(crow / probScaleY);
                    if (px < 0 || py < 0 || px >= prob.Width || py >= prob.Height) continue;
                    kand++;
                    if (!inShadowNove) roadN++;
                    if (!inShadow)
                    {
                        road++;
                        // Varianty jsou podmnozinou dnesniho stinu - mimo nej nesmi zakryt nic.
                        if (StinPrefix(azimuth, rb, range, true))
                        {
                            s.NesediO1Vyska++;
                            if (rb >= 0 && range < edges[rb].Range) s.NesediO1Hrana++;
                        }
                        if (StinPodle(h15T, azimuth, range)) s.NesediO1H15++;
                        continue;
                    }

                    // --- ztraceny vzorek: kdo ho zakryl a co by ho zachranilo ---
                    ztr++;
                    int r0 = prvni[azimuth];
                    int ci0 = azimuth * R + r0;
                    var vrhac = cells[ci0];
                    double h0 = Nad(vrhac.MeanX, vrhac.MeanY, vrhac.MaxZ);
                    s.PoVysce[h0 >= H ? 5 : h0 <= 0.05 ? 0 : h0 <= 0.15 ? 1 : h0 <= 0.30 ? 2 : h0 <= 0.60 ? 3 : 4]++;
                    int vel = velikost[komp[ci0]];
                    s.PoSkvrne[vel <= 1 ? 0 : vel <= 4 ? 1 : vel <= 16 ? 2 : 3]++;
                    double e0 = edges[r0].Range, za = range - e0;
                    s.ZaHranou[za < 0.5 ? 0 : za < 1 ? 1 : za < 2 ? 2 : za < 4 ? 3 : 4]++;
                    s.PoVzdalenostiVrhace[e0 < 1 ? 0 : e0 < 2 ? 1 : e0 < 3 ? 2 : e0 < 4 ? 3 : 4]++;
                    float rng = MathF.Sqrt(vrhac.MeanX * vrhac.MeanX + vrhac.MeanY * vrhac.MeanY);
                    if (Math.Abs(Nad(vrhac.MeanX, vrhac.MeanY, vrhac.MeanZ)) > pcfg.MaxHeightDev(rng)) s.DuvodVyska++;
                    else s.DuvodJiny++;
                    if (icfg.ProbabilityToTraversable(prob[px, py].Value) > 0.5f) s.ZtracenoCesta++;

                    if (!StinVyska(vrhaceVyska, azimuth, range)) s.ZachranVyska++;
                    // Vlastni bunka vzorku v polarnim gridu: lezi NA prekazce (napr. uvnitr travy), nebo za ni?
                    var tr = rb >= 0 ? cells[azimuth * R + rb].Class : (TraversabilityClass?)null;
                    s.VlastniTrida[tr == TraversabilityClass.Obstacle ? 0 : tr == TraversabilityClass.Free ? 1 : tr == null ? 3 : 2]++;
                    // Geometricky stin bez sebestineni: zakryvaji jen prekazky v DRIVEJSICH prstencich.
                    int mojeR = rb >= 0 ? rb : int.MaxValue;
                    bool striktne = false;
                    foreach (var (vr, ve, vd) in vrhaceStriktne[azimuth])
                        if (vr < mojeR && range >= ve && range < vd) { striktne = true; break; }
                    if (!striktne) s.ZachranVyskaStriktne++;
                    bool cestaZ = icfg.ProbabilityToTraversable(prob[px, py].Value) > 0.5f;
                    int maxVrhac = Vrhac(0, azimuth, rb, range, false);
                    for (int v = 0; v < 3; v++)
                        for (int sb = 0; sb < 2; sb++)
                        {
                            int vr = Vrhac(v, azimuth, rb, range, sb == 0);
                            if (vr >= 0) continue;
                            s.ZachranZ[v, sb]++;
                            if (cestaZ) s.ZachranZCesta[v, sb]++;
                            if (sb == 1 && v > 0 && maxVrhac >= 0)
                            {
                                s.NavicZ[v]++;
                                if (cestaZ) s.NavicZCesta[v]++;
                                var mc = cells[azimuth * R + maxVrhac];
                                double mh = Nad(mc.MeanX, mc.MeanY, mc.MaxZ), me = edges[maxVrhac].Range;
                                s.NavicZVrhac[v, mh >= H ? 5 : mh <= 0.05 ? 0 : mh <= 0.15 ? 1 : mh <= 0.30 ? 2 : mh <= 0.60 ? 3 : 4]++;
                                s.NavicZVrhacVzd[v, me < 1 ? 0 : me < 2 ? 1 : me < 3 ? 2 : me < 4 ? 3 : 4]++;
                                s.NavicZVrhacBodu[v, mc.Count < 16 ? 0 : mc.Count < 32 ? 1 : mc.Count < 64 ? 2 : 3]++;
                            }
                        }
                    if (StinPrefix(azimuth, rb, range, true) != StinVyska(vrhaceVyska, azimuth, range))
                    {
                        s.NesediO1Vyska++;
                        if (rb >= 0 && range < edges[rb].Range) s.NesediO1Hrana++;
                    }
                    if (StinPrefix(azimuth, rb, range, false) != striktne) s.NesediO1Striktne++;
                    if (StinPodle(h15T, azimuth, range) != StinPodle(prvniH15, azimuth, range)) s.NesediO1H15++;
                    if (!StinVyska(vrhaceVyska4, azimuth, range)) s.ZachranVyskaA4++;
                    if (!StinPodle(prvni1, azimuth, range)) s.Zachran1++;
                    if (!StinPodle(prvni4, azimuth, range)) s.Zachran4++;
                    if (!StinPodle(prvni16, azimuth, range)) s.Zachran16++;
                    if (!StinPodle(prvniH5, azimuth, range)) s.ZachranH5++;
                    if (!StinPodle(prvniH15, azimuth, range)) s.ZachranH15++;
                    if (!StinPodle(prvniVys, azimuth, range)) s.ZachranJenVyska++;
                    if (!StinVyska(vrhaceGeomH5, azimuth, range)) s.ZachranGeomH5++;
                }

            s.Snimky++;
            if (shadow != st.ColorShadowed || cproj != st.ColorProjected || road != st.WroteRoad) s.Nesedi++;
            // Integrator stini podle vysky, jen kdyz vyska kamery nad prolozenou rovinou dava smysl;
            // jinak (rozbite prolozeni roviny) pada na pravidlo prvni prekazky - predpoved totez.
            bool vyskou = Cz >= OccupancyIntegrator.MinCameraHeightM;
            if (!vyskou) { shadowN = shadow; cprojN = cproj; roadN = road; s.ZalohaNove++; }
            if (stN.ShadowByHeight != vyskou || shadowN != stN.ColorShadowed || cprojN != stN.ColorProjected
                || roadN != stN.WroteRoad)
            {
                if (s.NesediNove < 5)
                    Console.WriteLine(string.Format(Ci,
                        "  nesedi nove pravidlo: snimek {0:HH:mm:ss.fff} {1}: kamera {2:F3} m, integrator {3} / replika shadow={4} cproj={5} road={6}",
                        f.TimeStamp, f.Name, Cz, stN, shadowN, cprojN, roadN));
                s.NesediNove++;
            }
            s.ZapsanoNove += stN.WroteRoad;
            s.Shadow += st.ColorShadowed;
            s.Kandidati += kand;
            s.Zapsano += road;
            s.Ztraceno += ztr;
            if (kand > 0) s.PodilPoSnimcich.Add((double)ztr / kand);
        }

        private static void Tisk(Souhrn s)
        {
            double Pct(long a, long b) => b > 0 ? 100.0 * a / b : double.NaN;
            Console.WriteLine(string.Format(Ci,
                "snimky s hloubkou i barvou: {0} (bez pozy {1}, bez projekce {2}, bez gridu/barvy {3})",
                s.Snimky, s.BezPozy, s.BezProjekce, s.BezBarvy));
            Console.WriteLine(string.Format(Ci,
                "OVERENI repliky proti integratoru (shadow, cproj, road): nesedi {0} z {1} snimku{2}",
                s.Nesedi, s.Snimky, s.Nesedi == 0 ? " - replika sedi" : " !! cisla nize neplati"));
            Console.WriteLine(string.Format(Ci,
                "OVERENI noveho pravidla (integrator colorshadow=height proti predpovedi repliky): nesedi {0} z {1} snimku{2}",
                s.NesediNove, s.Snimky, s.NesediNove == 0 ? " - sedi" : " !!"));
            if (s.ZalohaNove > 0)
                Console.WriteLine(string.Format(Ci, "  z toho bez stinu podle vysky (kamera pod {0:F1} m): {1} snimku",
                    OccupancyIntegrator.MinCameraHeightM, s.ZalohaNove));
            foreach (var kv in s.VyskaKamery.OrderBy(k => k.Key))
            {
                var v = kv.Value.OrderBy(x => x).ToList();
                Console.WriteLine(string.Format(Ci, "  vyska kamery {0} nad rovinou zeme: p50 {1:F2} m (p10 {2:F2}, p90 {3:F2})",
                    kv.Key, v[v.Count / 2], v[v.Count / 10], v[v.Count * 9 / 10]));
            }
            Console.WriteLine();
            if (s.Snimky == 0) { Console.WriteLine("Zadny snimek s hloubkou i barvou - neni co merit."); return; }

            Console.WriteLine(string.Format(Ci, "BAREVNE VZORKY (bunka x snimek), soucet pres snimky:"));
            Console.WriteLine(string.Format(Ci, "  integrator ColorShadowed (vsechny bunky ve stinu): {0}", s.Shadow));
            Console.WriteLine(string.Format(Ci, "  kandidati (barva by se zapsala, kdyby stin nebyl):  {0}", s.Kandidati));
            Console.WriteLine(string.Format(Ci, "  zapsano:                                             {0} ({1:F1} %)", s.Zapsano, Pct(s.Zapsano, s.Kandidati)));
            Console.WriteLine(string.Format(Ci, "  ZAHOZENO stinem:                                     {0} ({1:F1} % kandidatu)", s.Ztraceno, Pct(s.Ztraceno, s.Kandidati)));
            Console.WriteLine(string.Format(Ci, "  NOVE pravidlo (integrator, colorshadow=height): zapsano {0}, zahozeno {1:F1} % kandidatu",
                s.ZapsanoNove, Pct(s.Kandidati - s.ZapsanoNove, s.Kandidati)));
            Console.WriteLine(string.Format(Ci, "  (ColorShadowed proti skutecne zahozenym: {0:F1}x - pocita i bunky mimo zorne pole barvy)",
                s.Ztraceno > 0 ? (double)s.Shadow / s.Ztraceno : double.NaN));
            var p = s.PodilPoSnimcich.OrderBy(x => x).ToList();
            if (p.Count > 0)
                Console.WriteLine(string.Format(Ci, "  podil zahozenych po snimcich: p10 {0:F0} %, p50 {1:F0} %, p90 {2:F0} %",
                    100 * p[p.Count / 10], 100 * p[p.Count / 2], 100 * p[p.Count * 9 / 10]));
            Console.WriteLine();

            if (s.Ztraceno == 0) return;
            Console.WriteLine("CO STIN VRHA (zahozene vzorky podle vrhace = prvni prekazky azimutu):");
            Console.WriteLine("  vrchol vrhace nad rovinou zeme:");
            for (int i = 0; i < s.PoVysce.Length; i++)
                Console.WriteLine(string.Format(Ci, "    {0,-22} {1,6:F1} %", VyskaPopis[i], Pct(s.PoVysce[i], s.Ztraceno)));
            Console.WriteLine("  velikost skvrny vrhace (souvisle prekazky v polarnim gridu):");
            for (int i = 0; i < s.PoSkvrne.Length; i++)
                Console.WriteLine(string.Format(Ci, "    {0,-22} {1,6:F1} %", SkvrnaPopis[i], Pct(s.PoSkvrne[i], s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  duvod klasifikace vrhace: vyska {0:F1} %, drsnost/sklon {1:F1} %",
                Pct(s.DuvodVyska, s.Ztraceno), Pct(s.DuvodJiny, s.Ztraceno)));
            Console.WriteLine("  vzdalenost vrhace od robotu:");
            for (int i = 0; i < s.PoVzdalenostiVrhace.Length; i++)
                Console.WriteLine(string.Format(Ci, "    {0,-22} {1,6:F1} %", VrhacPopis[i], Pct(s.PoVzdalenostiVrhace[i], s.Ztraceno)));
            Console.WriteLine("  vzorek za nabeznou hranou vrhace:");
            for (int i = 0; i < s.ZaHranou.Length; i++)
                Console.WriteLine(string.Format(Ci, "    {0,-22} {1,6:F1} %", ZaHranouPopis[i], Pct(s.ZaHranou[i], s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  barva zahozeneho vzorku rika „cesta“ (p > 0,5): {0:F1} %", Pct(s.ZtracenoCesta, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  zahozeny vzorek sam lezi v bunce: prekazka {0:F1} %, volno {1:F1} %, neznamo {2:F1} %, mimo dosah hloubky {3:F1} %",
                Pct(s.VlastniTrida[0], s.Ztraceno), Pct(s.VlastniTrida[1], s.Ztraceno), Pct(s.VlastniTrida[2], s.Ztraceno), Pct(s.VlastniTrida[3], s.Ztraceno)));
            Console.WriteLine();

            Console.WriteLine("PROTIFAKTY - kolik zahozenych by se zapsalo:");
            Console.WriteLine(string.Format(Ci, "  stin podle vysky [e, e'*Cz/(Cz-MaxZ)):       {0,6:F1} %", Pct(s.ZachranVyska, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  totez, prekazka nestini sama sebe:           {0,6:F1} %", Pct(s.ZachranVyskaStriktne, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  ignorovat skvrny 1 bunky:                    {0,6:F1} %", Pct(s.Zachran1, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  ignorovat skvrny <= 4 bunek:                 {0,6:F1} %", Pct(s.Zachran4, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  ignorovat skvrny <= 16 bunek:                {0,6:F1} %", Pct(s.Zachran16, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  vyska + ignorovat skvrny <= 4:              {0,6:F1} %", Pct(s.ZachranVyskaA4, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  stin vrha jen prekazka s vrcholem > 5 cm:    {0,6:F1} %", Pct(s.ZachranH5, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  stin vrha jen prekazka s vrcholem > 15 cm:   {0,6:F1} %", Pct(s.ZachranH15, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  stin vrha jen prekazka podle VYSKY (ne drsnost/sklon): {0,6:F1} %", Pct(s.ZachranJenVyska, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  geometricky stin jen od prekazek > 5 cm:     {0,6:F1} %", Pct(s.ZachranGeomH5, s.Ztraceno)));
            Console.WriteLine(string.Format(Ci, "  (podil vsech kandidatu, ktery by se pak zahodil: vyska {0:F1} %, vyska bez sebestineni {1:F1} %, skvrny<=4 {2:F1} %)",
                Pct(s.Ztraceno - s.ZachranVyska, s.Kandidati), Pct(s.Ztraceno - s.ZachranVyskaStriktne, s.Kandidati),
                Pct(s.Ztraceno - s.Zachran4, s.Kandidati)));
            Console.WriteLine();

            string[] vNazev = { "MaxZ", "MeanZ", "MeanZ + sqrt(3)*StdZ" };
            Console.WriteLine("STIN PODLE VYSKY - JAKOU VYSKU VRHACE VZIT (zachrani z zahozenych; v zavorce barva zachranenych rika „cesta“):");
            Console.WriteLine("  vyska vrhace               se sebestinenim        bez sebestineni     zahozeno pak (bez sebest.)");
            for (int v = 0; v < 3; v++)
                Console.WriteLine(string.Format(Ci, "  {0,-24} {1,6:F1} % ({2,4:F1} %)     {3,6:F1} % ({4,4:F1} %)     {5,6:F1} % kandidatu",
                    vNazev[v], Pct(s.ZachranZ[v, 0], s.Ztraceno), Pct(s.ZachranZCesta[v, 0], s.ZachranZ[v, 0]),
                    Pct(s.ZachranZ[v, 1], s.Ztraceno), Pct(s.ZachranZCesta[v, 1], s.ZachranZ[v, 1]),
                    Pct(s.Ztraceno - s.ZachranZ[v, 1], s.Kandidati)));
            string[] bodyPopis = { "< 16 bodu", "16-31", "32-63", ">= 64" };
            for (int v = 1; v < 3; v++)
            {
                if (s.NavicZ[v] == 0) continue;
                Console.WriteLine(string.Format(Ci, "  {0}: zachrani NAVIC proti MaxZ (bez sebestineni) {1:F1} % zahozenych, barva z nich rika „cesta“ {2:F1} %",
                    vNazev[v], Pct(s.NavicZ[v], s.Ztraceno), Pct(s.NavicZCesta[v], s.NavicZ[v])));
                Console.WriteLine("    vrhac, ktery by podle MaxZ stinil - vrchol nad rovinou / vzdalenost / bodu v bunce:");
                for (int i = 0; i < 6; i++)
                    Console.WriteLine(string.Format(Ci, "      {0,-22} {1,6:F1} %", VyskaPopis[i], Pct(s.NavicZVrhac[v, i], s.NavicZ[v])));
                for (int i = 0; i < 5; i++)
                    Console.WriteLine(string.Format(Ci, "      {0,-22} {1,6:F1} %", VrhacPopis[i], Pct(s.NavicZVrhacVzd[v, i], s.NavicZ[v])));
                for (int i = 0; i < 4; i++)
                    Console.WriteLine(string.Format(Ci, "      {0,-22} {1,6:F1} %", bodyPopis[i], Pct(s.NavicZVrhacBodu[v, i], s.NavicZ[v])));
            }
            Console.WriteLine();

            double Us(long tik) => 1e6 * tik / Stopwatch.Frequency / s.Snimky;
            Console.WriteLine(string.Format(Ci, "VYPOCETNI NAROCNOST (za snimek, polarni grid {0} x {1}, bunek v dosahu {2:F0}):",
                s.A, s.R, (double)s.BunekVDosahu / s.Snimky));
            Console.WriteLine(string.Format(Ci, "  OccupancyIntegrator.Integrate (colorshadow=first):  {0,8:F1} us", Us(s.TikIntegrate)));
            Console.WriteLine(string.Format(Ci, "  OccupancyIntegrator.Integrate (colorshadow=height): {0,8:F1} us", Us(s.TikIntegrateNove)));
            Console.WriteLine(string.Format(Ci, "  stavba stinu dnes (prvni prekazka azimutu):    {0,8:F1} us", Us(s.TikDnes)));
            Console.WriteLine(string.Format(Ci, "  prolozeni roviny zeme (pro vysku vrhace):      {0,8:F1} us", Us(s.TikRovina)));
            Console.WriteLine(string.Format(Ci, "  stavba: vrha jen prekazka > 15 cm:             {0,8:F1} us", Us(s.TikH15)));
            Console.WriteLine(string.Format(Ci, "  stavba: stin podle vysky (prefixove maximum):  {0,8:F1} us  (slouzi i variante bez sebestineni)", Us(s.TikPrefix)));
            Console.WriteLine(string.Format(Ci, "  dotaz na bunku: u vsech variant jedno cteni z pole a porovnani (jako dnes)"));
            Console.WriteLine(string.Format(Ci, "  OVERENI O(1) dotazu proti hrube sile: nesedi vyska {0} (z toho bod pred nabeznou hranou sveho prstence {4}), bez sebestineni {1}, > 15 cm {2}{3}",
                s.NesediO1Vyska, s.NesediO1Striktne, s.NesediO1H15,
                s.NesediO1Vyska + s.NesediO1Striktne + s.NesediO1H15 == 0 ? " - shodne" : " !!", s.NesediO1Hrana));
        }

        /// <summary>Projekce hloubky a barvy jako driver (tataz replika jako v <c>ZasekReport</c>) + poloha kamery.</summary>
        private static Kamera Projekce(CameraFrame f, Dictionary<string, Kamera> cache)
        {
            string jmeno = f.Name ?? string.Empty;
            if (cache.TryGetValue(jmeno, out var p)) return p;
            var info = f.Projection;
            if (info == null || info.Intrinsics == null) return null;

            ICameraProjection d = info.CreateProjection();
            ICameraProjection c = null;
            if (info.ColorIntrinsics != null)
            {
                var src = info.ColorIntrinsics;
                var i1 = new Intrinsics
                {
                    Width = src.Width, Height = src.Height, PPx = src.PPx, PPy = src.PPy,
                    Fx = src.Fx, Fy = src.Fy, Model = src.Model,
                    Coeffs = src.Coeffs != null ? (float[])src.Coeffs.Clone() : new float[5],
                };
                var ii = i1.Inverse();
                if (jmeno.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
                {
                    i1.PPx = i1.Width - i1.PPx;
                    i1.PPy = i1.Height - i1.PPy;
                    ii.PPx = ii.Width - ii.PPx;
                    ii.PPy = ii.Height - ii.PPy;
                }
                var cp = new CameraProjection(i1, ii, info.ColorToDepth, info.DepthToColor);
                cp.SetOrientation(info.Transformation);
                c = cp;
            }
            var t = Vector3.Transform(Vector3.Zero, info.Transformation);
            var k = new Kamera { D = d, C = c, KameraX = t.X, KameraY = t.Y, KameraZ = t.Z };
            cache[jmeno] = k;
            return k;
        }
    }
}
