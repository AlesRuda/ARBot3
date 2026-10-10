using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// <b>Prahy klasifikace a sumovy model polarniho gridu nad skutecnymi zaznamy</b> (prikaz
    /// <c>prahy</c>, tema <c>vid-grid-prahy-realna-data</c>).
    ///
    /// <para><b>Pravda o sjizdnosti = kudy robot vzapeti projel.</b> Pro kazdy snimek se z fuzovanych
    /// rychlosti (<c>RobotStateMsg.V</c>, <c>Omega</c>) integruje budouci draha robotu v TELESOVEM
    /// ramci snimku - tamtez, kde lezi bunky polarniho gridu - takze korekce pozy do ni neskacou
    /// (tytez duvody jako v <see cref="BumpDepthReport"/>). Bunka, jejiz teziste lezi do
    /// <c>--lateral</c> od teto drahy, je "projeta": robot pres ni jel, mela tedy vyjit <c>Free</c>.
    /// Projeta prekazka je falesna (nebo skutecny hrbol, pres ktery robot prejel - proto se tiskne
    /// i jeji vyska).</para>
    ///
    /// <para><b>Co se meri:</b> pro kazdou bunku se znovu spocte klasifikace tymz kodem jako
    /// <c>CameraFrameProcessor</c> (rovina z blizkych nizkych bunek, odchylka, drsnost <c>StdZ</c>,
    /// stoupani k sousedum) a overi proti tride ulozene v zaznamu. Do histogramu podle vzdalenosti
    /// jdou surove hodnoty (sumovy model) a pomery k prahum <c>q = hodnota / prah</c> (bunka je
    /// prekazka, kdyz nektery q &gt; 1). Pomer daneho kriteria se uklada jen u bunek, ktere ostatni
    /// kriteria propoustenji, takze z nej jde primo odecist, co udela zmena toho jednoho prahu.
    /// Populace: <c>T</c> projete, <c>TC</c> projete a barva rika cesta, <c>TM</c> projete mimo
    /// cestu, <c>A</c> vsechny bunky. Histogramy jdou do CSV (<c>--csv=</c>), aby se daly secist
    /// pres zaznamy.</para>
    /// </summary>
    public static class PrahyReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        // Vzdalenostni kose [m]: [0,25 ; 5,5) po 0,25 m.
        private const double RBinStart = 0.25, RBinStep = 0.25;
        private const int RBins = 21;

        // T1 = projeto do 0,1 m od drahy; TS = projeto a v okoli 3x3 (vcetne sebe) zadna NEPREJETELNA bunka
        // (vrchol nad NeprejetelnaM) - bez lidi pred robotem a bez zeme hned vedle nich. Mez 0,25 m je nad
        // vsemi prahy odchylky (do 5,5 m nejvys 0,14 m), takze filtr nevyrazuje bunky podle kriteria, ktere
        // se meri (kontrola 10. 10. 2026 nasla, ze filtr na odchylku > 0,15 m byl kruhovy a jednostranny).
        private static readonly string[] Pop = { "T", "TC", "TM", "A", "T1", "TS" };

        /// <summary>Vrchol nad rovinou, pres ktery robot prejet nemohl [m] - projeta bunka s nim je pohyblivy
        /// predmet (clovek pred robotem) nebo chyba drahy, ne chyba prahu.</summary>
        private const float NeprejetelnaM = 0.25f;

        /// <summary>Histogram s pevnou sirkou kose a pretecenim v poslednim.</summary>
        private sealed class Hist
        {
            public readonly double Step;
            public readonly long[] N;
            public Hist(double step, int bins) { Step = step; N = new long[bins + 1]; }
            public void Add(double v)
            {
                if (double.IsNaN(v)) return;
                int i = v <= 0 ? 0 : (int)(v / Step);
                if (i >= N.Length - 1) i = N.Length - 1;
                N[i]++;
            }
        }

        private sealed class Skupina
        {
            public long Bunek, Unknown, Free, Obstacle;
            public long UnknownGeom;   // Unknown, ackoli geometrie dava >= MinPointsPerCell pixelu (vypadek hloubky)
            public long SGeom;         // bunek se znamou geometrii >= MinPointsPerCell
            public long ObsDev, ObsRough, ObsSlope, OnlyDev, OnlyRough, OnlySlope;
            public readonly Hist Dev = new Hist(0.001, 500);      // |odchylka od roviny| [m]
            public readonly Hist Rough = new Hist(0.0005, 600);   // StdZ [m]
            public readonly Hist Slope = new Hist(0.01, 300);     // stoupani k sousedum
            public readonly Hist MaxH = new Hist(0.01, 100);      // MaxZ nad rovinou [m]
            public readonly Hist Prot = new Hist(0.002, 250);     // vycnelek MaxZ - MeanZ v bunce [m] (hrbet uvnitr bunky)
            public readonly Hist QDev = new Hist(0.025, 200);     // adev/prah, jen kdyz ostatni propousti
            public readonly Hist QRough = new Hist(0.025, 200);
            public readonly Hist QSlope = new Hist(0.025, 200);
            public readonly Hist QMax = new Hist(0.025, 200);     // max pomer (spolecne skalovani prahu)
            public readonly Hist Count = new Hist(1, 400);        // bodu v bunce (i pod podlahou)
            public readonly Hist ObsMaxH = new Hist(0.01, 100);   // MaxZ nad rovinou u PREKAZEK
            public readonly Hist Valid = new Hist(0.02, 75);      // bodu / geometrickych pixelu bunky (podil platnych)
            // Stoupani: vzdalenost tezist a rozdil odchylek u souseda, ktery dal maximum - u bunek,
            // ktere stoupanim padly (q > 1), a u vsech; smer rozhodujiciho souseda.
            public readonly Hist SlopeDistObs = new Hist(0.005, 200), SlopeDistAll = new Hist(0.005, 200);
            public readonly Hist SlopeDdObs = new Hist(0.002, 250);
            public long SlopeObsRadial, SlopeObsAzim;
            // Druhy prekazek: K1 vrchol nad NeprejetelnaM; K2 vlastni odchylka/drsnost; K3 jen stoupani
            // k sousedovi, ktery je sam prekazkou podle odchylky (zem vedle skutecne prekazky);
            // K4 jen stoupani a soused nizky (sum).
            public long Kat1, Kat2, Kat3, Kat4;
            // Protifakt: stoupani se spodni mezi vzdalenosti sousedu 0,1 / 0,2 m (pomer k prahu, jen kdyz
            // odchylka a drsnost propousti - jako QSlope).
            public readonly Hist QSlope10 = new Hist(0.025, 200), QSlope20 = new Hist(0.025, 200);
        }

        private sealed class Kamera
        {
            public ICameraProjection C;
            public CameraProjectionInfo Info;
            public int[] G;        // geometricky pocet pixelu bunky (paprsek protne z = 0 v jejim prstenci)
            public int GA, GR;
        }

        /// <summary>
        /// Kolik pixelu hloubky by do bunky padlo na rovne zemi z = 0 (tymz modelem jako
        /// <c>PolarGridConfig.BuildRadialEdges</c>) - jmenovatel skutecneho podilu platnych pixelu.
        /// </summary>
        private static int[] Geometrie(Kamera k, PolarTraversabilityGrid polar, PolarGridConfig cfg)
        {
            int A = polar.AzimuthCount, R = polar.RadialCount;
            if (k.G != null && k.GA == A && k.GR == R) return k.G;
            k.G = null;
            if (k.Info == null || k.Info.Intrinsics == null) return null;
            IDepthCameraProjection proj = k.Info.CreateProjection();
            var table = proj.Camera2DToCamera3D;
            var m = proj.Transformation;
            var origin = new Point4D { A = 1 }.Transform(m);
            int H = table.GetLength(0), W = table.GetLength(1);
            if (W != A * polar.ColumnsPerCell) return null;
            var g = new int[A * R];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var ray = table[y, x];
                    var p1 = new Point4D { X = ray.X, Y = ray.Y, Z = 1, A = 1 }.Transform(m);
                    float dirZ = p1.Z - origin.Z;
                    if (dirZ >= 0) continue;
                    float t = -origin.Z / dirZ;
                    if (t <= 0) continue;
                    float gx = origin.X + t * (p1.X - origin.X), gy = origin.Y + t * (p1.Y - origin.Y);
                    float r = MathF.Sqrt(gx * gx + gy * gy);
                    if (r < cfg.MinRangeM || r > cfg.MaxRangeM) continue;
                    int rb = polar.RadialBin(r);
                    if (rb < 0) continue;
                    g[(x / polar.ColumnsPerCell) * R + rb]++;
                }
            k.G = g; k.GA = A; k.GR = R;
            return g;
        }

        public static void Run(RecordFile rec, int frameStep, double lateral, double horizon, string csv,
                               int priklady = 0, int prikladyKrok = 50, double planeFit = double.NaN,
                               string od = null, string doCasu = null)
        {
            var konf = LogConfig.Read(rec);
            if (konf.Version != null) Console.WriteLine(konf.Version);
            if (frameStep < 1) frameStep = 1;
            var pcfg = new PolarGridConfig();
            TimeSpan? oknoOd = string.IsNullOrWhiteSpace(od) ? (TimeSpan?)null : TimeSpan.Parse(od, CultureInfo.InvariantCulture);
            TimeSpan? oknoDo = string.IsNullOrWhiteSpace(doCasu) ? (TimeSpan?)null : TimeSpan.Parse(doCasu, CultureInfo.InvariantCulture);
            if (oknoOd.HasValue || oknoDo.HasValue) Console.WriteLine($"OKNO: {od ?? "zacatek"} - {doCasu ?? "konec"}");
            // Pokus: rovina prolozena z jineho dosahu (zlom odchylky ve 2 m = dnesni PlaneFitMaxRangeM?).
            // Replika pak ze zaznamu NEsedi - to je zamer, overeni plati jen pro vychozi hodnotu.
            if (!double.IsNaN(planeFit)) { pcfg.PlaneFitMaxRangeM = (float)planeFit; Console.WriteLine($"POKUS: PlaneFitMaxRangeM = {planeFit} (replika nebude sedet se zaznamem)"); }
            var icfg = new OccupancyIntegratorConfig();

            // Rychlosti z fuze, serazene podle casu.
            var stavy = new List<RobotStateMsg>();
            foreach (var e in rec.Index)
                if (e.MsgName == "RobotStateMsg" && rec.Read(e) is RobotStateMsg r) stavy.Add(r);
            stavy.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            var casy = stavy.Select(s => s.TimeStamp).ToList();

            var sk = new Skupina[Pop.Length, RBins];
            for (int p = 0; p < Pop.Length; p++)
                for (int b = 0; b < RBins; b++) sk[p, b] = new Skupina();

            var kamery = new Dictionary<string, Kamera>();
            var snimky = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            long nSnimku = 0, nSDrahou = 0, nBunek = 0, nesedi = 0, nesediObs = 0;
            long nPrikladu = 0, nProjetychPrekazek = 0;
            var posunyCasu = new List<double>();
            DateTime tZac = snimky.Count > 0 && rec.Read(snimky[0]) is CameraFrame f0 ? f0.TimeStamp : DateTime.MinValue;
            if (priklady > 0)
                Console.WriteLine("PRIKLADY projetych prekazek: t[s] kamera azimut/prstenec r[m] odDrahy | odchylka StdZ stoupani (soused: vzdal, rozdil, smer) MaxZnadRov | odchylky sousedu r-1 r+1 a-1 a+1 [tridy]");
            var draha = new List<(double x, double y)>();

            for (int fi = 0; fi < snimky.Count; fi += frameStep)
            {
                if (!(rec.Read(snimky[fi]) is CameraFrame f)) continue;
                // Casove okno (cas dne snimku, HH:mm:ss) - useky s jinym povrchem v jednom zaznamu.
                if (oknoOd.HasValue && f.TimeStamp.TimeOfDay < oknoOd.Value) continue;
                if (oknoDo.HasValue && f.TimeStamp.TimeOfDay > oknoDo.Value) continue;
                var polar = f.Grid;
                if (polar == null || polar.RadialCount == 0 || polar.AzimuthCount == 0) continue;
                nSnimku++;

                // --- budouci draha v telesovem ramci snimku ---
                draha.Clear();
                // Zacatek drahy = cas EXPOZICE hloubky (DepthTimeStamp, razitko senzoru v TimeBase), ne
                // vyzvednuti (TimeStamp) - pri otaceni by posun v case posunul drahu bokem.
                var tSnimku = f.DepthTimeStamp != default && Math.Abs((f.DepthTimeStamp - f.TimeStamp).TotalSeconds) < 1.0
                    ? f.DepthTimeStamp : f.TimeStamp;
                if (tSnimku != f.TimeStamp) posunyCasu.Add((f.TimeStamp - tSnimku).TotalMilliseconds);
                Draha(stavy, casy, tSnimku, horizon, draha);
                bool sDrahou = draha.Count >= 2 && DelkaDrahy(draha) >= 0.5;
                if (sDrahou) nSDrahou++;

                // --- barva (pro rozdeleni projetych na cestu / mimo cestu) ---
                var kam = Projekce(f, kamery);
                var geom = Geometrie(kam, polar, pcfg);
                var prob = f.ImageProbability;
                double sx = 1, sy = 1;
                if (prob != null && f.ImageRGB != null && prob.Width > 0) { sx = (double)f.ImageRGB.Width / prob.Width; sy = (double)f.ImageRGB.Height / prob.Height; }

                // --- replika klasifikace ---
                int A = polar.AzimuthCount, R = polar.RadialCount;
                var cells = polar.Cells;
                var plane = Rovina(cells, pcfg);
                var dev = new float[cells.Length];
                for (int i = 0; i < cells.Length; i++)
                    dev[i] = cells[i].Count >= pcfg.MinPointsPerCell ? Odchylka(cells[i], plane) : 0f;
                // Smer azimutu (prumer pres bunky s body) - poloha PRAZDNE bunky se odhadne z nej
                // a ze stredu prstence, aby se dalo rict, jestli dira v hloubce lezi na projete draze.
                var smer = new double[A];
                for (int a = 0; a < A; a++)
                {
                    double sx0 = 0, sy0 = 0;
                    for (int r = 0; r < R; r++)
                    {
                        var c = cells[a * R + r];
                        if (c.Count == 0) continue;
                        double l = Math.Sqrt(c.MeanX * c.MeanX + c.MeanY * c.MeanY);
                        if (l > 1e-6) { sx0 += c.MeanX / l; sy0 += c.MeanY / l; }
                    }
                    smer[a] = sx0 == 0 && sy0 == 0 ? double.NaN : Math.Atan2(sy0, sx0);
                }

                for (int a = 0; a < A; a++)
                    for (int r = 0; r < R; r++)
                    {
                        int idx = a * R + r;
                        var c = cells[idx];
                        float rng = MathF.Sqrt(c.MeanX * c.MeanX + c.MeanY * c.MeanY);
                        double cx = c.MeanX, cy = c.MeanY;
                        if (c.Count == 0)
                        {
                            // Prazdna bunka nema teziste - stred prstence ve smeru azimutu.
                            rng = 0.5f * (polar.RadialEdges[r].Range + polar.RadialEdges[r + 1].Range);
                            cx = double.IsNaN(smer[a]) ? double.NaN : rng * Math.Cos(smer[a]);
                            cy = double.IsNaN(smer[a]) ? double.NaN : rng * Math.Sin(smer[a]);
                        }
                        int rb = (int)((rng - RBinStart) / RBinStep);
                        if (rng < RBinStart || rb >= RBins) continue;
                        nBunek++;

                        // Je projeta? (teziste bunky; u prazdne stred prstence ve stredu azimutu nejde -> jen A)
                        double odDrahy = sDrahou && !double.IsNaN(cx) ? VzdalenostOdDrahy(draha, cx, cy) : double.MaxValue;
                        bool projeta = odDrahy <= lateral;
                        int povrch = -1;   // 1 = barva rika cesta, 0 = mimo cestu, -1 = nevi
                        if (projeta && kam?.C != null && prob != null)
                        {
                            float col = 0, row = 0;
                            if (kam.C.Transform((float)cx, (float)cy, ref col, ref row))
                            {
                                int px = (int)(col / sx), py = (int)(row / sy);
                                if (px >= 0 && py >= 0 && px < prob.Width && py < prob.Height)
                                    povrch = icfg.ProbabilityToTraversable(prob[px, py].Value) > 0.5f ? 1 : 0;
                            }
                        }

                        bool unknown = c.Count < pcfg.MinPointsPerCell;
                        float adev = 0, rough = 0, slope = 0, qd = 0, qr = 0, qs = 0;
                        float sDist = 0, sDd = 0, slope10 = 0, slope20 = 0;
                        bool sRadial = false;
                        int sN = -1;
                        bool obst = false;
                        if (!unknown)
                        {
                            adev = Math.Abs(dev[idx]);
                            rough = c.StdZ;
                            slope = Stoupani(pcfg, cells, dev, a, r, A, R, idx, 0f, out sDist, out sDd, out sRadial, out sN);
                            slope10 = Stoupani(pcfg, cells, dev, a, r, A, R, idx, 0.1f, out _, out _, out _, out _);
                            slope20 = Stoupani(pcfg, cells, dev, a, r, A, R, idx, 0.2f, out _, out _, out _, out _);
                            qd = adev / pcfg.MaxHeightDev(rng);
                            qr = rough / (pcfg.RoughObstacleFactor * pcfg.RoughRef(rng));
                            qs = slope / pcfg.MaxSlope;
                            obst = qd > 1 || qr > 1 || qs > 1;
                        }
                        var ulozena = c.Class;
                        var moje = unknown ? TraversabilityClass.Unknown : obst ? TraversabilityClass.Obstacle : TraversabilityClass.Free;
                        if (moje != ulozena) { nesedi++; if (ulozena == TraversabilityClass.Obstacle || moje == TraversabilityClass.Obstacle) nesediObs++; }

                        float maxH = unknown ? float.NaN : Odchylka(new PolarCell { MeanX = c.MeanX, MeanY = c.MeanY, MeanZ = c.MaxZ }, plane);

                        // Neprejetelna bunka v okoli 3x3 (vcetne sebe)?
                        bool vysokoOkoli = false;
                        for (int da = -1; da <= 1 && !vysokoOkoli; da++)
                            for (int dr = -1; dr <= 1; dr++)
                            {
                                int na = a + da, nr = r + dr;
                                if (na < 0 || na >= A || nr < 0 || nr >= R) continue;
                                int ni = na * R + nr;
                                var nc = cells[ni];
                                if (nc.Count >= pcfg.MinPointsPerCell
                                    && Odchylka(new PolarCell { MeanX = nc.MeanX, MeanY = nc.MeanY, MeanZ = nc.MaxZ }, plane) > NeprejetelnaM)
                                { vysokoOkoli = true; break; }
                            }
                        // Druh prekazky.
                        int kat = 0;
                        if (obst)
                        {
                            if (maxH > NeprejetelnaM) kat = 1;
                            else if (qd > 1 || qr > 1) kat = 2;
                            else
                            {
                                var n = sN >= 0 ? cells[sN] : default;
                                float nr2 = MathF.Sqrt(n.MeanX * n.MeanX + n.MeanY * n.MeanY);
                                kat = sN >= 0 && Math.Abs(dev[sN]) > pcfg.MaxHeightDev(nr2) ? 3 : 4;
                            }
                        }

                        void Pridej(Skupina g)
                        {
                            g.Bunek++;
                            g.Count.Add(c.Count);
                            bool geomOk = geom != null && geom[idx] >= pcfg.MinPointsPerCell;
                            if (geomOk) { g.SGeom++; g.Valid.Add((double)c.Count / geom[idx]); }
                            if (unknown) { g.Unknown++; if (geomOk) g.UnknownGeom++; return; }
                            g.Dev.Add(adev); g.Rough.Add(rough); g.Slope.Add(slope); g.MaxH.Add(maxH);
                            g.Prot.Add(c.MaxZ - c.MeanZ);
                            g.QMax.Add(Math.Max(qd, Math.Max(qr, qs)));
                            if (qr <= 1 && qs <= 1) g.QDev.Add(qd);
                            if (qd <= 1 && qs <= 1) g.QRough.Add(qr);
                            if (qd <= 1 && qr <= 1)
                            {
                                g.QSlope.Add(qs);
                                g.QSlope10.Add(slope10 / pcfg.MaxSlope);
                                g.QSlope20.Add(slope20 / pcfg.MaxSlope);
                            }
                            if (sDist > 0) g.SlopeDistAll.Add(sDist);
                            if (qs > 1)
                            {
                                g.SlopeDistObs.Add(sDist); g.SlopeDdObs.Add(sDd);
                                if (sRadial) g.SlopeObsRadial++; else g.SlopeObsAzim++;
                            }
                            if (!obst) { g.Free++; return; }
                            g.Obstacle++;
                            g.ObsMaxH.Add(maxH);
                            if (kat == 1) g.Kat1++; else if (kat == 2) g.Kat2++; else if (kat == 3) g.Kat3++; else g.Kat4++;
                            if (qd > 1) g.ObsDev++;
                            if (qr > 1) g.ObsRough++;
                            if (qs > 1) g.ObsSlope++;
                            if (qd > 1 && qr <= 1 && qs <= 1) g.OnlyDev++;
                            if (qr > 1 && qd <= 1 && qs <= 1) g.OnlyRough++;
                            if (qs > 1 && qd <= 1 && qr <= 1) g.OnlySlope++;
                        }
                        Pridej(sk[3, rb]);
                        if (projeta)
                        {
                            Pridej(sk[0, rb]);
                            if (obst && priklady > 0 && nPrikladu < priklady && (nProjetychPrekazek++ % prikladyKrok) == 0)
                            {
                                nPrikladu++;
                                string Soused(int na, int nr)
                                {
                                    if (na < 0 || na >= A || nr < 0 || nr >= R) return "-";
                                    var n = cells[na * R + nr];
                                    if (n.Count < pcfg.MinPointsPerCell) return "U";
                                    return string.Format(Ci, "{0:+0.000;-0.000}{1}", dev[na * R + nr], n.Class == TraversabilityClass.Obstacle ? "O" : "F");
                                }
                                Console.WriteLine(string.Format(Ci,
                                    "  {0,7:F1} {1,-6} {2,2}/{3,2} {4:F2} {5:F2} | {6:+0.000;-0.000} {7:F4} {8:F2} ({9:F3}, {10:F3}, {11}) {12:F2}{13}{14}{15} | {16} {17} {18} {19}",
                                    (f.TimeStamp - tZac).TotalSeconds, (f.Name ?? "").Split(' ')[0], a, r, rng, odDrahy,
                                    dev[idx], rough, slope, sDist, sDd, sRadial ? "rad" : "az", maxH,
                                    qd > 1 ? " ODCH" : "", qr > 1 ? " DRS" : "", qs > 1 ? " STOUP" : "",
                                    Soused(a, r - 1), Soused(a, r + 1), Soused(a - 1, r), Soused(a + 1, r)));
                            }
                            if (povrch == 1) Pridej(sk[1, rb]);
                            else if (povrch == 0) Pridej(sk[2, rb]);
                            if (odDrahy <= 0.1) Pridej(sk[4, rb]);
                            if (!vysokoOkoli) Pridej(sk[5, rb]);
                        }
                    }
            }

            Console.WriteLine(string.Format(Ci,
                "snimku s gridem {0} (kazdy {1}.), s budouci drahou >= 0,5 m {2}; bunek v dosahu {3}; projeto = do {4:F2} m od drahy, horizont {5:F1} m",
                nSnimku, frameStep, nSDrahou, nBunek, lateral, horizon));
            Console.WriteLine(string.Format(Ci,
                "OVERENI repliky klasifikace proti tride v zaznamu: nesedi {0} bunek z {1} ({2:F4} %), z toho s prekazkou {3}",
                nesedi, nBunek, 100.0 * nesedi / Math.Max(1, nBunek), nesediObs));
            if (posunyCasu.Count > 0)
            {
                posunyCasu.Sort();
                Console.WriteLine(string.Format(Ci, "cas vyzvednuti - cas expozice hloubky: p50 {0:F0} ms (p10 {1:F0}, p90 {2:F0}); draha zacina v case expozice",
                    posunyCasu[posunyCasu.Count / 2], posunyCasu[posunyCasu.Count / 10], posunyCasu[posunyCasu.Count * 9 / 10]));
            }
            Tisk(sk, pcfg);
            if (!string.IsNullOrEmpty(csv)) Csv(sk, csv, Path.GetFileNameWithoutExtension(rec.Path));
        }

        // ---------------- draha ----------------

        /// <summary>Budouci draha v telesovem ramci snimku: integrace V, Omega od casu snimku, dokud
        /// ujeta draha nepresahne <paramref name="horizon"/> (nejvys 20 s).</summary>
        private static void Draha(List<RobotStateMsg> stavy, List<DateTime> casy, DateTime t0, double horizon,
                                  List<(double x, double y)> draha)
        {
            int i = casy.BinarySearch(t0);
            if (i < 0) i = ~i;
            if (i >= stavy.Count) return;
            double x = 0, y = 0, th = 0, s = 0;
            draha.Add((0, 0));
            DateTime tPrev = t0;
            var prev = i > 0 ? stavy[i - 1] : stavy[i];
            for (int k = i; k < stavy.Count; k++)
            {
                var st = stavy[k];
                double dt = (st.TimeStamp - tPrev).TotalSeconds;
                if ((st.TimeStamp - t0).TotalSeconds > 20) break;
                if (dt <= 0 || dt > 1.0) { tPrev = st.TimeStamp; prev = st; continue; }   // mezera v datech
                double v = 0.5 * (prev.V + st.V), w = 0.5 * (prev.Omega + st.Omega);
                double thm = th + 0.5 * w * dt;
                x += v * Math.Cos(thm) * dt;
                y += v * Math.Sin(thm) * dt;
                th += w * dt;
                s += Math.Abs(v) * dt;
                draha.Add((x, y));
                tPrev = st.TimeStamp; prev = st;
                if (s >= horizon) break;
            }
        }

        private static double DelkaDrahy(List<(double x, double y)> d)
        {
            double s = 0;
            for (int i = 1; i < d.Count; i++) s += Math.Sqrt(Sq(d[i].x - d[i - 1].x) + Sq(d[i].y - d[i - 1].y));
            return s;
        }

        private static double VzdalenostOdDrahy(List<(double x, double y)> d, double px, double py)
        {
            double best = double.MaxValue;
            for (int i = 1; i < d.Count; i++)
            {
                double ax = d[i - 1].x, ay = d[i - 1].y, bx = d[i].x, by = d[i].y;
                double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
                double t = l2 > 1e-12 ? Math.Clamp(((px - ax) * vx + (py - ay) * vy) / l2, 0, 1) : 0;
                double dd = Sq(ax + t * vx - px) + Sq(ay + t * vy - py);
                if (dd < best) best = dd;
            }
            return Math.Sqrt(best);
        }

        private static double Sq(double v) => v * v;

        // ---------------- replika klasifikace (CameraFrameProcessor) ----------------

        private static ARBot.Common.Algorithms.ComputeUnit.PlaneParams Rovina(PolarCell[] cells, PolarGridConfig cfg)
        {
            var pts = new List<Point4D>();
            float maxR2 = cfg.PlaneFitMaxRangeM * cfg.PlaneFitMaxRangeM;
            foreach (var c in cells)
            {
                if (c.Count < cfg.MinPointsPerCell) continue;
                float r2 = c.MeanX * c.MeanX + c.MeanY * c.MeanY;
                if (r2 > maxR2) continue;
                if (Math.Abs(c.MeanZ) > cfg.PlaneFitMaxAbsHeightM) continue;
                pts.Add(new Point4D { X = c.MeanX, Y = c.MeanY, Z = c.MeanZ, A = 1 });
            }
            return pts.Count >= 3
                ? new ARBot.Common.Algorithms.ComputeUnit.PlaneParams(pts)
                : new ARBot.Common.Algorithms.ComputeUnit.PlaneParams { v = new Point4D { X = 0, Y = 0, Z = 1, A = 0 } };
        }

        private static float Odchylka(in PolarCell c, in ARBot.Common.Algorithms.ComputeUnit.PlaneParams plane)
            => new Point4D { X = c.MeanX, Y = c.MeanY, Z = c.MeanZ, A = 1 } * plane.v;

        /// <summary>Stoupani jako <c>CameraFrameProcessor.MaxNeighborSlope</c>; s <paramref name="minDist"/> &gt; 0
        /// protifakt se spodni mezi vzdalenosti sousedu. Vraci i vzdalenost, rozdil odchylek a smer
        /// souseda, ktery dal maximum.</summary>
        private static float Stoupani(PolarGridConfig cfg, PolarCell[] cells, float[] dev, int a, int r, int A, int R, int idx,
                                      float minDist, out float bestDist, out float bestDd, out bool bestRadial, out int bestN)
        {
            float max = 0f, bd = 0f, bdd = 0f;
            bool brad = false;
            int bn = -1;
            var self = cells[idx];
            void Consider(int na, int nr, bool radial)
            {
                if (na < 0 || na >= A || nr < 0 || nr >= R) return;
                int nidx = na * R + nr;
                var n = cells[nidx];
                if (n.Count < cfg.MinPointsPerCell) return;
                float dx = self.MeanX - n.MeanX;
                float dy = self.MeanY - n.MeanY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < 1e-4f) return;
                float dd = Math.Abs(dev[idx] - dev[nidx]);
                float s = dd / Math.Max(dist, minDist);
                if (s > max) { max = s; bd = dist; bdd = dd; brad = radial; bn = nidx; }
            }
            Consider(a, r - 1, true);
            Consider(a, r + 1, true);
            Consider(a - 1, r, false);
            Consider(a + 1, r, false);
            bestDist = bd; bestDd = bdd; bestRadial = brad; bestN = bn;
            return max;
        }

        // ---------------- vystup ----------------

        private static double Pct(long a, long b) => b > 0 ? 100.0 * a / b : double.NaN;

        private static double Percentil(Hist h, double q)
        {
            long n = h.N.Sum();
            if (n == 0) return double.NaN;
            long cil = (long)Math.Ceiling(q * n), acc = 0;
            for (int i = 0; i < h.N.Length; i++)
            {
                acc += h.N[i];
                if (acc >= cil) return (i + 0.5) * h.Step;
            }
            return double.NaN;
        }

        private static void Tisk(Skupina[,] sk, PolarGridConfig pcfg)
        {
            string[] nazvy = { "PROJETO (robot pres bunku vzapeti jel - ma byt Free)",
                               "PROJETO, barva rika cesta", "PROJETO, barva rika mimo cestu", "VSECHNY BUNKY",
                               "PROJETO do 0,1 m od drahy", "PROJETO bez vysoke bunky v okoli 3x3" };
            for (int p = 0; p < Pop.Length; p++)
            {
                Console.WriteLine();
                Console.WriteLine(nazvy[p] + ":");
                Console.WriteLine("  r [m]     bunek  unknown  prekazka (odchylka/drsnost/stoupani; jen) | |odchylka| p50/p99 prah | StdZ p50/p99 prah | stoupani p50/p99 prah | bodu p50 | platnych p50");
                for (int b = 0; b < RBins; b++)
                {
                    var g = sk[p, b];
                    if (g.Bunek == 0) continue;
                    double r = RBinStart + (b + 0.5) * RBinStep;
                    long kl = g.Free + g.Obstacle;
                    Console.WriteLine(string.Format(Ci,
                        "  {0,4:F2} {1,10} {2,7:F1}% {3,7:F2}% ({4:F2}/{5:F2}/{6:F2}; {7:F2}/{8:F2}/{9:F2}) | {10:F3}/{11:F3} {12:F3} | {13:F4}/{14:F4} {15:F3} | {16:F2}/{17:F2} {18:F2} | {19:F0} | {20:F2}",
                        r, g.Bunek, Pct(g.Unknown, g.Bunek), Pct(g.Obstacle, kl),
                        Pct(g.ObsDev, kl), Pct(g.ObsRough, kl), Pct(g.ObsSlope, kl),
                        Pct(g.OnlyDev, kl), Pct(g.OnlyRough, kl), Pct(g.OnlySlope, kl),
                        Percentil(g.Dev, 0.5), Percentil(g.Dev, 0.99), pcfg.MaxHeightDev((float)r),
                        Percentil(g.Rough, 0.5), Percentil(g.Rough, 0.99), pcfg.RoughObstacleFactor * pcfg.RoughRef((float)r),
                        Percentil(g.Slope, 0.5), Percentil(g.Slope, 0.99), pcfg.MaxSlope,
                        Percentil(g.Count, 0.5), Percentil(g.Valid, 0.5)));
                }
            }
        }

        private static void Csv(Skupina[,] sk, string path, string zaznam)
        {
            bool nova = !File.Exists(path);
            using var w = new StreamWriter(path, append: true);
            if (nova) w.WriteLine("zaznam,pop,rbin,klic,kos,n");
            for (int p = 0; p < Pop.Length; p++)
                for (int b = 0; b < RBins; b++)
                {
                    var g = sk[p, b];
                    if (g.Bunek == 0) continue;
                    void C(string k, long n) { if (n != 0) w.WriteLine($"{zaznam},{Pop[p]},{b},{k},-1,{n}"); }
                    C("bunek", g.Bunek); C("unknown", g.Unknown); C("unknowngeom", g.UnknownGeom); C("sgeom", g.SGeom); C("free", g.Free); C("obstacle", g.Obstacle);
                    C("obsdev", g.ObsDev); C("obsrough", g.ObsRough); C("obsslope", g.ObsSlope);
                    C("onlydev", g.OnlyDev); C("onlyrough", g.OnlyRough); C("onlyslope", g.OnlySlope);
                    void H(string k, Hist h) { for (int i = 0; i < h.N.Length; i++) if (h.N[i] != 0) w.WriteLine($"{zaznam},{Pop[p]},{b},{k},{i},{h.N[i]}"); }
                    H("dev", g.Dev); H("rough", g.Rough); H("slope", g.Slope); H("maxh", g.MaxH); H("prot", g.Prot);
                    H("qdev", g.QDev); H("qrough", g.QRough); H("qslope", g.QSlope); H("qmax", g.QMax);
                    H("count", g.Count); H("obsmaxh", g.ObsMaxH); H("valid", g.Valid);
                    H("slopedistobs", g.SlopeDistObs); H("slopedistall", g.SlopeDistAll); H("slopeddobs", g.SlopeDdObs);
                    H("qslope10", g.QSlope10); H("qslope20", g.QSlope20);
                    C("slopeobsradial", g.SlopeObsRadial); C("slopeobsazim", g.SlopeObsAzim);
                    C("kat1", g.Kat1); C("kat2", g.Kat2); C("kat3", g.Kat3); C("kat4", g.Kat4);
                }
        }

        /// <summary>Barevna projekce jako driver (tataz replika jako v <c>OkluzeReport</c>).</summary>
        private static Kamera Projekce(CameraFrame f, Dictionary<string, Kamera> cache)
        {
            string jmeno = f.Name ?? string.Empty;
            if (cache.TryGetValue(jmeno, out var k)) return k;
            var info = f.Projection;
            k = new Kamera { Info = info };
            if (info != null && info.ColorIntrinsics != null)
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
                k.C = cp;
            }
            cache[jmeno] = k;
            return k;
        }
    }
}
