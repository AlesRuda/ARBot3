using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Co by dala JEDNA HRANA?</b> Prehraje hranicni body snimku ze zaznamu pres dnesni
    /// <see cref="CorridorFinder"/> (vcetne mereni z jedne hrany, od 24. 9. 2026) a kurz, ktery by
    /// z jedne hrany sel do fuze, porovna s <b>GPS kurzem</b> — nezavislou referenci, ktera na
    /// kompasu ani na fuzi nezavisi (Doppler, overeny proti smeru posunu polohy).
    ///
    /// <para><b>Nacpak to je:</b> na zaznamech z 23. 9. 2026 (siroka cyklostezka) nedal oboustranny
    /// koridor ani jedno merenie a kurz VN100 ujel o desitky az 180°. Otazka je, jestli by jedna
    /// hrana ten drift chytila — tedy jestli kurz z ni sedi na GPS kurz, zatimco odhad fuze ne.</para>
    ///
    /// <para><b>Zjednoduseni proti runtime:</b> parovani kamer je stejne (posledni snimek druhe
    /// kamery v okne), ale BEZ kompenzace pohybu mezi snimky (potrebovala by fuzi); hrana site se
    /// vybira nejblizsi k GPS poloze s azimutem nejblize videne hrane (veto 45°), ne pres
    /// chi-kvadrat s kovarianci pozy. Smysl kurzu (primka nema orientaci) se rozhoduje podle
    /// ZAZNAMENANEHO odhadu, stejne jako v runtime — kdyz odhad ujel o vic nez 90°, vyjde kurz
    /// z hrany otoceny; proto se tiskne i shoda PRIMEK (slozena na ±90°).</para>
    ///
    /// <para>Pouziti: <c>ARBot.Analyze singleedge zaznam.rec --map=OSM/x.osm [--bin=30]</c>.</para>
    /// </summary>
    public static class SingleEdgeReport
    {
        private const double MinSpeedMps = 0.3;
        private const double R = 6378137.0;

        public static void Run(RecordFile rec, string mapPath, double roadWidth, double binSec, double maxSkewMs,
                               string sweep = null)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
            {
                Console.Error.WriteLine("singleedge: --map=<cesta.osm> je povinne (mapa, podle ktere robot jel).");
                return;
            }
            RoadNetwork net;
            using (var fs = File.OpenRead(mapPath))
            {
                var data = OsmXmlReader.Read(fs);
                data = NetworkIslands.Prune(data, TravelProfile.Robot(), out _);
                net = GraphBuilder.BuildNetwork(data, TravelProfile.Robot(), roadWidth);
            }

            var frames = new List<(string Cam, DateTime T, List<Point2D> L, List<Point2D> Rt)>();
            var gps = new List<(DateTime T, double Lat, double Lon, double Course, double V)>();
            var est = new List<(DateTime T, double Th)>();
            foreach (var e in rec.Index)
            {
                switch (e.MsgName)
                {
                    case "CameraFrame":
                        if (rec.Read(e) is CameraFrame f && f.PathEdges != null)
                        {
                            var l = new List<Point2D>(); var r = new List<Point2D>();
                            foreach (var p in f.PathEdges)
                            {
                                if (p.LeftPoint.A != 0) l.Add(new Point2D(p.LeftPoint.X, p.LeftPoint.Y));
                                if (p.RightPoint.A != 0) r.Add(new Point2D(p.RightPoint.X, p.RightPoint.Y));
                            }
                            frames.Add((f.Name ?? "", f.TimeStamp, l, r));
                        }
                        break;
                    case "GPSState":
                        if (rec.Read(e) is GPSState g && g.DynamicOrientation.HasValue)
                            gps.Add((g.TimeStamp, g.Latitude, g.Longitude, g.DynamicOrientation.Value,
                                     g.Speed ?? g.DynamicSpeed ?? 0));
                        break;
                    case "RobotStateMsg":
                        if (rec.Read(e) is RobotStateMsg s) est.Add((s.TimeStamp, s.Theta));
                        break;
                }
            }
            frames.Sort((a, b) => a.T.CompareTo(b.T));
            gps.Sort((a, b) => a.T.CompareTo(b.T));
            est.Sort((a, b) => a.T.CompareTo(b.T));
            Console.WriteLine($"Mapa: {mapPath}, snimku s hranicemi {frames.Count}, GPS s kurzem {gps.Count}, odhadu {est.Count}");
            if (frames.Count == 0 || gps.Count == 0 || est.Count == 0) { Console.WriteLine("Malo dat."); return; }

            var finder = new CorridorFinder(new CorridorConfig());
            var last = new Dictionary<string, (DateTime T, List<Point2D> L, List<Point2D> Rt)>();
            int both = 0, singleL = 0, singleR = 0, none = 0, lone = 0, wrongSide = 0;
            int noGps = 0, noEdge = 0;
            var samples = new List<(DateTime T, double ErrEdge, double ErrHalf, double ErrEst, double Offset, CorridorSide Side)>();
            DateTime t0 = frames[0].T;

            foreach (var fr in frames)
            {
                last[fr.Cam] = (fr.T, fr.L, fr.Rt);
                (DateTime T, List<Point2D> L, List<Point2D> Rt)? other = null;
                double best = double.MaxValue;
                foreach (var kv in last)
                {
                    if (kv.Key == fr.Cam) continue;
                    double dt = Math.Abs((kv.Value.T - fr.T).TotalMilliseconds);
                    if (dt <= maxSkewMs && dt < best) { best = dt; other = kv.Value; }
                }

                RoadCorridor c;
                if (other == null) { c = finder.Find(fr.L, fr.Rt); lone++; }
                else
                {
                    var o = other.Value;
                    c = finder.Find(fr.L.Count >= o.L.Count ? fr.L : o.L, fr.Rt.Count >= o.Rt.Count ? fr.Rt : o.Rt);
                }

                if (c.Ok) { if (other != null) both++; else none++; continue; }
                if (!c.HasSingleEdge) { none++; continue; }
                bool sideOk = c.SingleSide == CorridorSide.Left ? c.EdgeOffset > -0.5 : c.EdgeOffset < 0.5;
                if (!sideOk) { wrongSide++; continue; }
                if (c.SingleSide == CorridorSide.Left) singleL++; else singleR++;

                if (!Nearest(est, fr.T, 0.2, out var th)) continue;
                int gi = NearestIndex(gps, fr.T, 0.2);
                if (gi < 0 || gps[gi].V < MinSpeedMps) { noGps++; continue; }
                var gp = gps[gi];

                // Hrana site u GPS polohy s azimutem nejblize videne hrane (primky, ±90°).
                var cands = net.NearestEdges(new LLA(gp.Lat, gp.Lon, 0), 6, 15.0);
                double bestD = double.MaxValue, alpha = double.NaN;
                foreach (var cand in cands)
                {
                    double a = Azimuth(cand.Edge);
                    double d = Math.Abs(Conversions.NormalizeHalfOrientation(a - (th + c.DirectionRad)));
                    if (d < bestD) { bestD = d; alpha = a; }
                }
                if (double.IsNaN(alpha) || bestD > 45 * Math.PI / 180) { noEdge++; continue; }

                // Presne jako CorridorLocalizer.Send: kurz = odhad + slozeny rozdil primek.
                double rel = Conversions.NormalizeHalfOrientation(alpha - th);
                double dd = Conversions.NormalizeHalfOrientation(rel - c.DirectionRad);
                double heading = th + dd;
                samples.Add((fr.T, Wrap(heading - gp.Course),
                             Conversions.NormalizeHalfOrientation(heading - gp.Course),
                             Wrap(th - gp.Course), c.EdgeOffset, c.SingleSide));
            }

            int total = frames.Count;
            Console.WriteLine();
            Console.WriteLine("CO BY DAL KORIDOR (prehrano dnesnim CorridorFinderem, bez kompenzace pohybu):");
            Console.WriteLine($"  snimku celkem             {total}");
            Console.WriteLine($"  oboustranny koridor       {both,6} ({100.0 * both / total:F1} %)");
            Console.WriteLine($"  JEDNA hrana - leva        {singleL,6} ({100.0 * singleL / total:F1} %)");
            Console.WriteLine($"  JEDNA hrana - prava       {singleR,6} ({100.0 * singleR / total:F1} %)");
            Console.WriteLine($"  jedna hrana na spatne strane robotu {wrongSide}");
            Console.WriteLine($"  nic                       {none,6} ({100.0 * none / total:F1} %)");
            Console.WriteLine($"  (bez druhe kamery v okne {lone} snimku - z nich jde jen jedna hrana)");
            Console.WriteLine($"  jedna hrana bez GPS kurzu (stani / pod {MinSpeedMps} m/s): {noGps}, bez hrany site do 45°: {noEdge}");

            if (samples.Count == 0) { Console.WriteLine("  Zadne srovnatelne vzorky."); return; }

            var eEdge = new Stats("kurz z JEDNE hrany - GPS kurz [deg]");
            var eHalf = new Stats("totez jako PRIMKY (+-90) [deg]");
            var eEst = new Stats("odhad fuze - GPS kurz [deg]");
            var off = new Stats("|odstup hrany od robotu| [m]");
            foreach (var s in samples)
            {
                eEdge.Add(Deg(s.ErrEdge)); eHalf.Add(Deg(s.ErrHalf)); eEst.Add(Deg(s.ErrEst));
                off.Add(Math.Abs(s.Offset));
            }
            Console.WriteLine();
            Console.WriteLine($"KURZ Z JEDNE HRANY PROTI GPS KURZU (n={samples.Count}, jizda nad {MinSpeedMps} m/s):");
            Console.WriteLine("  " + eHalf.Line("deg"));
            Console.WriteLine("  " + eEdge.Line("deg"));
            Console.WriteLine("  " + eEst.Line("deg"));
            Console.WriteLine("  " + off.Line("m"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  robustni sd primek {0:F2} deg (MAD*1,48)", RobustSd(samples.Select(x => Deg(x.ErrHalf)).ToList())));

            Console.WriteLine();
            Console.WriteLine("PO KOSECH (primka hrany a odhad fuze proti GPS kurzu; stredni hodnoty):");
            Console.WriteLine("    cas [s]      n   hrana (primka)   hrana (smysl dle odhadu)   odhad fuze");
            double end = (samples[samples.Count - 1].T - t0).TotalSeconds;
            for (double b = 0; b <= end; b += binSec)
            {
                var bin = samples.Where(x => (x.T - t0).TotalSeconds >= b && (x.T - t0).TotalSeconds < b + binSec).ToList();
                if (bin.Count < 3) continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0}-{1,4:F0}  {2,5}   {3,10:F2}      {4,14:F2}          {5,10:F2}",
                    b, b + binSec, bin.Count,
                    Deg(CircMean(bin.Select(x => 2 * x.ErrHalf)) / 2),
                    Deg(CircMean(bin.Select(x => x.ErrEdge))),
                    Deg(CircMean(bin.Select(x => x.ErrEst)))));
            }
            Console.WriteLine("  Hrana blizko nule a odhad daleko = jedna hrana by drift chytila.");
            Console.WriteLine("  ⚠️ Chyba mapy (azimut OSM hrany) jde do kurzu z hrany 1:1.");

            if (!string.IsNullOrWhiteSpace(sweep))
                Sweep(frames, gps, est, net, maxSkewMs,
                      sweep.Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray());
        }

        /// <summary>
        /// <b>Pomuze OBOUSTRANNEMU koridoru nizsi prah inlieru?</b> (<c>--sweep=25,20,15,10</c>)
        /// Pro kazdy prah se prehraji tytez dvojice snimku BEZ jedne hrany a vypise se, kolik
        /// koridoru vznikne, proc zbytek padl a hlavne jak KVALITNI jsou: sirka (na tychz mistech
        /// ma byt stejna - jeji rozptyl je meritko nesmyslu), nerovnobeznost a kurz koridoru proti
        /// GPS kurzu. Vic koridoru s horsi kvalitou neni zisk.
        ///
        /// <para>Pridano 24. 9. 2026 (otazka autora nad zaznamy z 23. 9.).</para>
        /// </summary>
        private static void Sweep(List<(string Cam, DateTime T, List<Point2D> L, List<Point2D> Rt)> frames,
                                  List<(DateTime T, double Lat, double Lon, double Course, double V)> gps,
                                  List<(DateTime T, double Th)> est, RoadNetwork net, double maxSkewMs,
                                  int[] prahy)
        {
            // Dvojice se sestavi jednou - prah na parovani nema vliv.
            var pary = new List<(DateTime T, List<Point2D> L, List<Point2D> Rt)>();
            var last = new Dictionary<string, (DateTime T, List<Point2D> L, List<Point2D> Rt)>();
            foreach (var fr in frames)
            {
                last[fr.Cam] = (fr.T, fr.L, fr.Rt);
                (DateTime T, List<Point2D> L, List<Point2D> Rt)? other = null;
                double best = double.MaxValue;
                foreach (var kv in last)
                {
                    if (kv.Key == fr.Cam) continue;
                    double dt = Math.Abs((kv.Value.T - fr.T).TotalMilliseconds);
                    if (dt <= maxSkewMs && dt < best) { best = dt; other = kv.Value; }
                }
                if (other == null) continue;
                var o = other.Value;
                pary.Add((fr.T, fr.L.Count >= o.L.Count ? fr.L : o.L, fr.Rt.Count >= o.Rt.Count ? fr.Rt : o.Rt));
            }

            Console.WriteLine();
            Console.WriteLine($"PRAH INLIERU PRO OBOUSTRANNY KORIDOR (dvojic {pary.Count}, jedna hrana vypnuta, RANSAC nedeterministicky):");
            Console.WriteLine("  prah     Ok  TooFewInl  OneSide  NotPar  WidthOut | sirka p10/p50/p90 [m]  rsd  | nerovnob. p50/p90 | kurz-GPS n   p50   rsd [deg]");
            foreach (int k in prahy)
            {
                var finder = new CorridorFinder(new CorridorConfig { MinInliers = k, SingleEdge = false });
                var reasons = new Dictionary<CorridorReason, int>();
                var w = new List<double>(); var par = new List<double>(); var hdg = new List<double>();
                foreach (var p in pary)
                {
                    var c = finder.Find(p.L, p.Rt);
                    reasons[c.Reason] = reasons.TryGetValue(c.Reason, out int n) ? n + 1 : 1;
                    if (!c.Ok) continue;
                    w.Add(c.Width);
                    par.Add(c.ParallelErrorRad * 180 / Math.PI);
                    if (HeadingVsGps(p.T, c.DirectionRad, gps, est, net, out double e)) hdg.Add(e * 180 / Math.PI);
                }
                int Rn(CorridorReason r) => reasons.TryGetValue(r, out int n) ? n : 0;
                string Pct(int v) => string.Format(CultureInfo.InvariantCulture, "{0,5:F1}%", 100.0 * v / Math.Max(1, pary.Count));
                w.Sort(); par.Sort(); hdg.Sort();
                double P(List<double> a, double q) => a.Count == 0 ? double.NaN : a[Math.Min(a.Count - 1, (int)(q * a.Count))];
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4}  {1}  {2}     {3}  {4}  {5}   | {6,5:F2}/{7,5:F2}/{8,5:F2}  {9,5:F2} | {10,5:F1}/{11,5:F1}       | {12,5} {13,6:F2} {14,5:F2}",
                    k, Pct(Rn(CorridorReason.Ok)), Pct(Rn(CorridorReason.TooFewInliers)), Pct(Rn(CorridorReason.OneSideOnly)),
                    Pct(Rn(CorridorReason.NotParallel)), Pct(Rn(CorridorReason.WidthOutOfRange)),
                    P(w, 0.1), P(w, 0.5), P(w, 0.9), RobustSd(w), P(par, 0.5), P(par, 0.9),
                    hdg.Count, P(hdg, 0.5), RobustSd(hdg)));
            }
            Console.WriteLine("  Zisk je jen tam, kde s poctem Ok NEROSTE rozptyl sirky a kurzu - jinak prah pousti nesmysl.");
        }

        /// <summary>Kurz ze smeru koridoru/hrany proti GPS kurzu jako PRIMKY (±90°) - viz Run.</summary>
        private static bool HeadingVsGps(DateTime t, double dirRad,
                                         List<(DateTime T, double Lat, double Lon, double Course, double V)> gps,
                                         List<(DateTime T, double Th)> est, RoadNetwork net, out double errHalf)
        {
            errHalf = double.NaN;
            if (!Nearest(est, t, 0.2, out var th)) return false;
            int gi = NearestIndex(gps, t, 0.2);
            if (gi < 0 || gps[gi].V < MinSpeedMps) return false;
            var gp = gps[gi];
            double bestD = double.MaxValue, alpha = double.NaN;
            foreach (var cand in net.NearestEdges(new LLA(gp.Lat, gp.Lon, 0), 6, 15.0))
            {
                double a = Azimuth(cand.Edge);
                double d = Math.Abs(Conversions.NormalizeHalfOrientation(a - (th + dirRad)));
                if (d < bestD) { bestD = d; alpha = a; }
            }
            if (double.IsNaN(alpha) || bestD > 45 * Math.PI / 180) return false;
            double heading = th + Conversions.NormalizeHalfOrientation(Conversions.NormalizeHalfOrientation(alpha - th) - dirRad);
            errHalf = Conversions.NormalizeHalfOrientation(heading - gp.Course);
            return true;
        }

        /// <summary>Azimut hrany site v matematicke orientaci (0 = vychod, +CCW) [rad].</summary>
        private static double Azimuth(Edge e)
        {
            var a = e.From.Location; var b = e.To.Location;
            double dE = (b.Longitude - a.Longitude) * R * Math.Cos(a.Latitude);
            double dN = (b.Latitude - a.Latitude) * R;
            return Math.Atan2(dN, dE);
        }

        private static bool Nearest(List<(DateTime T, double Th)> list, DateTime t, double tolSec, out double v)
        {
            v = 0;
            int lo = 0, hi = list.Count - 1;
            while (lo < hi) { int m = (lo + hi) / 2; if (list[m].T < t) lo = m + 1; else hi = m; }
            int bestI = -1; double bestD = double.MaxValue;
            for (int i = Math.Max(0, lo - 1); i <= Math.Min(list.Count - 1, lo); i++)
            {
                double d = Math.Abs((list[i].T - t).TotalSeconds);
                if (d < bestD) { bestD = d; bestI = i; }
            }
            if (bestI < 0 || bestD > tolSec) return false;
            v = list[bestI].Th;
            return true;
        }

        private static int NearestIndex(List<(DateTime T, double Lat, double Lon, double Course, double V)> list,
                                        DateTime t, double tolSec)
        {
            int lo = 0, hi = list.Count - 1;
            while (lo < hi) { int m = (lo + hi) / 2; if (list[m].T < t) lo = m + 1; else hi = m; }
            int bestI = -1; double bestD = double.MaxValue;
            for (int i = Math.Max(0, lo - 1); i <= Math.Min(list.Count - 1, lo); i++)
            {
                double d = Math.Abs((list[i].T - t).TotalSeconds);
                if (d < bestD) { bestD = d; bestI = i; }
            }
            return bestD <= tolSec ? bestI : -1;
        }

        private static double CircMean(IEnumerable<double> a)
        {
            double s = 0, c = 0;
            foreach (var x in a) { s += Math.Sin(x); c += Math.Cos(x); }
            return Math.Atan2(s, c);
        }

        private static double RobustSd(List<double> a)
        {
            if (a.Count == 0) return double.NaN;
            var s = a.OrderBy(x => x).ToList();
            double med = s[s.Count / 2];
            var dev = a.Select(x => Math.Abs(x - med)).OrderBy(x => x).ToList();
            return 1.4826 * dev[dev.Count / 2];
        }

        private static double Wrap(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));
        private static double Deg(double r) => r * 180.0 / Math.PI;
    }
}
