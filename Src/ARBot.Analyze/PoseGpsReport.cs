using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kde je póza fúze proti GPS a proti mapě — a kdo ji v tu chvíli opravoval.</b> Časová osa
    /// po oknech: odchylka odhadu od GPS rozložená na podélnou a příčnou složku (vůči směru jízdy
    /// podle GPS), měřítko odometrie proti Doppleru, odstup pózy i GPS od mapové sítě, stav koridoru
    /// (co měřil, co poslal do fúze) a lokálního plánu.
    ///
    /// <para>Motivace (26. 9. 2026, Track <c>20260925-142428.rec</c>): na rovince se póza postupně
    /// vzdalovala od GPS (podezření na obvod kola), po zatáčce byla vedle cesty a koridor ji
    /// neopravoval, pak ji něco skokem vrátilo a robot se ocitl v historicky nesjízdném místě
    /// lokální mapy. S <c>gpsposstd=30</c> určuje podélnou polohu téměř jen odometrie, takže chyba
    /// měřítka kola se v ní přímo integruje; příčnou polohu po zatáčce smí opravit jen koridor.</para>
    ///
    /// <para><b>Souřadnice:</b> GPS se převádí do runtimové ENU přes počátek z <see cref="MapMsg"/>
    /// (tatáž definice jako <c>ARBotRuntime.BuildOriginFromMap</c>). Bez mapy v záznamu se
    /// odchylka od GPS nepočítá.</para>
    /// </summary>
    public static class PoseGpsReport
    {
        private sealed class Bin
        {
            public double GpsPath, PosePath, WheelPath;
            public readonly List<double> Along = new(), Cross = new(), Dist = new();
            public readonly List<double> PoseToNet = new(), GpsToNet = new();
            public readonly List<double> SpeedRatio = new();
            public int Cor2, Cor1, Sent, NoEdge, Ambig, NoCorr, Other;
            public readonly List<double> SentInnov = new();
            public int PlanOk, PlanOther;
            public readonly Dictionary<string, int> PlanStates = new();
            public readonly List<double> VCmd = new();
        }

        public static void Run(RecordFile rec, double binSec)
        {
            MapMsg map = null;
            var gps = new List<(double t, double x, double y, double? v, bool ok)>();
            var poses = new List<(double t, double x, double y, double th, double v)>();
            var wheels = new List<(double t, double v)>();
            var cors = new List<(double t, RoadCorridorMsg m)>();
            var plans = new List<(double t, string st)>();
            var drives = new List<(double t, double v)>();
            DateTime? t0 = null;
            GeoReference geo = null;
            var pendingGps = new List<GPSState>();

            foreach (var e in rec.Index)
            {
                string n = e.MsgName;
                if (n != "Map" && n != "GPSState" && n != "RobotStateMsg" && n != "MotorStateBase"
                    && n != "RoadCorridorMsg" && n != "LocalPlanMsg" && n != "DriveCommandMsg") continue;
                var m = rec.Read(e);
                switch (m)
                {
                    case MapMsg mm when map == null:
                        map = mm; geo = mm.BuildOrigin();
                        break;
                    case GPSState g:
                        t0 ??= g.TimeStamp;
                        pendingGps.Add(g);
                        break;
                    case RobotStateMsg s:
                        t0 ??= s.TimeStamp;
                        poses.Add((Sec(s.TimeStamp, t0.Value), s.X, s.Y, s.Theta, s.V));
                        break;
                    case MotorStateBase mot when mot.HasMeasurement && t0.HasValue:
                        wheels.Add((Sec(mot.TimeStamp, t0.Value), 0.5 * (mot.LeftWheelSpeed + mot.RightWheelSpeed)));
                        break;
                    case RoadCorridorMsg c when t0.HasValue:
                        cors.Add((Sec(c.TimeStamp, t0.Value), c));
                        break;
                    case LocalPlanMsg p when t0.HasValue:
                        plans.Add((Sec(p.TimeStamp, t0.Value), p.PlanStatus.ToString()));
                        break;
                    case DriveCommandMsg d when t0.HasValue:
                        drives.Add((Sec(d.TimeStamp, t0.Value), d.Speed));
                        break;
                }
            }
            if (!t0.HasValue || poses.Count == 0) { Console.WriteLine("Zaznam nenese pozu."); return; }
            if (geo == null) { Console.WriteLine("Zaznam nenese mapu (Map) - GPS nejde prevest do runtimove ENU."); return; }
            foreach (var g in pendingGps)
            {
                var p = geo.ToLocal(g.Latitude, g.Longitude);
                gps.Add((Sec(g.TimeStamp, t0.Value), p.X, p.Y, g.Speed, g.IsFixed));
            }
            gps.Sort((a, b) => a.t.CompareTo(b.t));
            poses.Sort((a, b) => a.t.CompareTo(b.t));
            wheels.Sort((a, b) => a.t.CompareTo(b.t));

            // Sit v lokalni ENU (usecky).
            var segs = new List<(double ax, double ay, double bx, double by)>();
            var nodeXY = map.Nodes.Select(nd => geo.ToLocal(nd.LatDeg * Math.PI / 180, nd.LonDeg * Math.PI / 180)).ToList();
            foreach (var ed in map.Edges)
                segs.Add((nodeXY[ed.From].X, nodeXY[ed.From].Y, nodeXY[ed.To].X, nodeXY[ed.To].Y));
            double ToNet(double x, double y)
            {
                double best = double.MaxValue;
                foreach (var s in segs)
                {
                    double dx = s.bx - s.ax, dy = s.by - s.ay, l2 = dx * dx + dy * dy;
                    double u = l2 > 0 ? Math.Clamp(((x - s.ax) * dx + (y - s.ay) * dy) / l2, 0, 1) : 0;
                    double px = s.ax + u * dx - x, py = s.ay + u * dy - y;
                    best = Math.Min(best, Math.Sqrt(px * px + py * py));
                }
                return best;
            }

            Console.WriteLine($"Map: {map.Name}, uzlu {map.Nodes.Count}, hran {map.Edges.Count}; GPS fixu {gps.Count}, poz {poses.Count}, "
                              + $"MotorStateBase {wheels.Count}, RoadCorridorMsg {cors.Count}, LocalPlanMsg {plans.Count}");
            Console.WriteLine();

            double tEnd = poses[^1].t;
            int nb = (int)Math.Ceiling(tEnd / binSec) + 1;
            var bins = Enumerable.Range(0, nb).Select(_ => new Bin()).ToArray();
            Bin B(double t) => bins[Math.Clamp((int)(t / binSec), 0, nb - 1)];

            // (1) poza proti GPS v case fixu: podelne/pricne vuci smeru jizdy (azimut GPS posunu za 1 s)
            int pi = 0;
            for (int i = 0; i < gps.Count; i++)
            {
                var g = gps[i];
                if (!g.ok) continue;
                while (pi + 1 < poses.Count && poses[pi + 1].t <= g.t) pi++;
                if (Math.Abs(poses[pi].t - g.t) > 0.15) continue;
                var ps = poses[pi];
                var bin = B(g.t);
                bin.PoseToNet.Add(ToNet(ps.x, ps.y));
                bin.GpsToNet.Add(ToNet(g.x, g.y));
                double ex = ps.x - g.x, ey = ps.y - g.y;
                bin.Dist.Add(Math.Sqrt(ex * ex + ey * ey));
                // smer jizdy z GPS: posun za +-0,5 s
                int j0 = i, j1 = i;
                while (j0 > 0 && g.t - gps[j0 - 1].t <= 0.5) j0--;
                while (j1 + 1 < gps.Count && gps[j1 + 1].t - g.t <= 0.5) j1++;
                double dx = gps[j1].x - gps[j0].x, dy = gps[j1].y - gps[j0].y, dl = Math.Sqrt(dx * dx + dy * dy);
                if (dl > 0.4)
                {
                    double ux = dx / dl, uy = dy / dl;
                    bin.Along.Add(ex * ux + ey * uy);        // + = poza PRED GPS
                    bin.Cross.Add(-ex * uy + ey * ux);       // + = poza VLEVO od GPS
                }
                if (i > 0 && gps[i - 1].ok)
                {
                    double gx = g.x - gps[i - 1].x, gy = g.y - gps[i - 1].y;
                    bin.GpsPath += Math.Sqrt(gx * gx + gy * gy);
                }
            }
            for (int i = 1; i < poses.Count; i++)
            {
                double dx = poses[i].x - poses[i - 1].x, dy = poses[i].y - poses[i - 1].y;
                B(poses[i].t).PosePath += Math.Sqrt(dx * dx + dy * dy);
            }
            for (int i = 1; i < wheels.Count; i++)
            {
                double dt = wheels[i].t - wheels[i - 1].t;
                if (dt > 0 && dt < 0.1) B(wheels[i].t).WheelPath += wheels[i].v * dt;
            }

            // (2) meritko: Doppler proti rychlosti z kol (prumer kol za +-0,1 s), jen za primocare jizdy nad 0,5 m/s
            int wi = 0;
            foreach (var g in gps)
            {
                if (!g.ok || !g.v.HasValue || g.v.Value < 0.5) continue;
                while (wi < wheels.Count && wheels[wi].t < g.t - 0.1) wi++;
                double s = 0; int k = 0;
                for (int j = wi; j < wheels.Count && wheels[j].t <= g.t + 0.1; j++) { s += wheels[j].v; k++; }
                if (k < 3 || s / k < 0.3) continue;
                B(g.t).SpeedRatio.Add(g.v.Value / (s / k));
            }

            // (3) koridor, plan, prikaz
            foreach (var (t, c) in cors)
            {
                var bin = B(t);
                var r = (ARBot.Common.Localization.CorridorFixReason)c.FixReason;
                if (r == ARBot.Common.Localization.CorridorFixReason.Ok)
                {
                    if (c.SingleSide != 0) bin.Cor1++; else bin.Cor2++;
                }
                else if (r == ARBot.Common.Localization.CorridorFixReason.NoEdge) bin.NoEdge++;
                else if (r == ARBot.Common.Localization.CorridorFixReason.AmbiguousEdge) bin.Ambig++;
                else if (r == ARBot.Common.Localization.CorridorFixReason.NoCorridor) bin.NoCorr++;
                else bin.Other++;
                if (c.EmittedLateral) { bin.Sent++; bin.SentInnov.Add(c.LateralDisagreement); }
            }
            foreach (var (t, st) in plans)
            {
                var bin = B(t);
                if (st == "Ok" || st == "Partial") bin.PlanOk++;
                else { bin.PlanOther++; bin.PlanStates[st] = bin.PlanStates.GetValueOrDefault(st) + 1; }
            }
            foreach (var (t, v) in drives) B(t).VCmd.Add(v);

            // ---- vystup ----
            var ratioAll = new Stats("Doppler / kola (primo, > 0,5 m/s)");
            foreach (var b in bins) foreach (var r in b.SpeedRatio) ratioAll.Add(r);
            Console.WriteLine("MERITKO ODOMETRIE (rychlost nad zemi z GPS / rychlost z kol):");
            Console.WriteLine("  " + ratioAll.Line());
            double gp = bins.Sum(b => b.GpsPath), wp = bins.Sum(b => b.WheelPath), pp = bins.Sum(b => b.PosePath);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  draha celkem: GPS {0:F1} m, kola {1:F1} m, poza {2:F1} m  ->  kola/GPS {3:F4}, poza/GPS {4:F4}",
                gp, wp, pp, wp / gp, pp / gp));
            Console.WriteLine("  Pomer < 1 = kola hlasi VIC, nez robot ujede (obvod kola v Profile je velky) -> poza utika dopredu.");
            Console.WriteLine("  POZOR: draha z GPS poloh po 0,1 s je sumem NAFOUKNUTA (cik-cak) a pri stani roste i bez pohybu,");
            Console.WriteLine("  proto nize tetiva na primem useku.");

            // Meritko na PRIMYCH usecich: tetiva mezi dvema polohami GPS (sum se nescita) proti draze
            // z kol a tetive pozy ve stejnem okne. Primy = smer tetivy prvni a druhe poloviny okna se
            // lisi o < 3 deg a draha z kol je nejvys o 0,5 % delsi nez tetiva pozy.
            var chordK = new Stats("kola / tetiva GPS");
            var chordP = new Stats("tetiva pozy / tetiva GPS");
            var chordD = new Stats("Doppler / kola (tataz okna)");
            const double win = 30;
            var okGps = gps.Where(g => g.ok).ToList();
            for (int i = 0; i < okGps.Count; i += 50)
            {
                var a = okGps[i];
                int j = okGps.FindIndex(i, g => g.t >= a.t + win);
                if (j < 0) break;
                var b = okGps[j];
                if (b.t - a.t > win + 0.5) continue;
                int mI = okGps.FindIndex(i, g => g.t >= a.t + win / 2);
                var mid = okGps[mI];
                double az1 = Math.Atan2(mid.y - a.y, mid.x - a.x), az2 = Math.Atan2(b.y - mid.y, b.x - mid.x);
                double dAz = Math.Abs(Math.IEEERemainder(az2 - az1, 2 * Math.PI)) * 180 / Math.PI;
                double cg = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                if (dAz > 3 || cg < 20) continue;
                double wk = 0;
                for (int k = 1; k < wheels.Count; k++)
                {
                    if (wheels[k].t <= a.t || wheels[k].t > b.t) continue;
                    double dt = wheels[k].t - wheels[k - 1].t;
                    if (dt > 0 && dt < 0.1) wk += wheels[k].v * dt;
                }
                var pa = poses.OrderBy(q => Math.Abs(q.t - a.t)).First();
                var pb = poses.OrderBy(q => Math.Abs(q.t - b.t)).First();
                double cp = Math.Sqrt((pb.x - pa.x) * (pb.x - pa.x) + (pb.y - pa.y) * (pb.y - pa.y));
                if (wk > cp * 1.005) continue;
                chordK.Add(wk / cg);
                chordP.Add(cp / cg);
                var dop = okGps.Skip(i).Take(j - i).Where(g => g.v.HasValue).Select(g => g.v.Value).ToList();
                if (dop.Count > 0 && wk > 0) chordD.Add(dop.Average() * (b.t - a.t) / wk);
            }
            Console.WriteLine($"  PRIME USEKY (okno {win:F0} s, zmena smeru < 3 deg):");
            Console.WriteLine("    " + chordK.Line());
            Console.WriteLine("    " + chordP.Line());
            Console.WriteLine("    " + chordD.Line());
            Console.WriteLine("    kola/tetiva > 1 = kola hlasi vic nez skutecny posun -> obvod kola v Profile je o tolik velky.");
            Console.WriteLine();

            Console.WriteLine($"CASOVA OSA (okno {binSec:F0} s; mediany):");
            Console.WriteLine("  t[s]  GPS[m] kola/GPS  Dop/kola  podel[m] pricne[m] |dP|[m]  poza->sit GPS->sit   kor2 kor1 poslano inov.p50  NoEdge Ambig NoKor   plan!Ok   vCmd");
            for (int i = 0; i < nb; i++)
            {
                var b = bins[i];
                if (b.Dist.Count == 0 && b.PlanOk + b.PlanOther == 0) continue;
                string ps = b.PlanStates.Count == 0 ? "" : " " + string.Join(",", b.PlanStates.Select(kv => kv.Key + ":" + kv.Value));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0} {1,6:F1} {2,8:F3} {3,8:F3} {4,9:F2} {5,9:F2} {6,7:F2} {7,9:F2} {8,7:F2}   {9,4} {10,4} {11,7} {12,8:F2}  {13,6} {14,5} {15,5}   {16,6}{17}  {18,5:F2}",
                    i * binSec, b.GpsPath, b.GpsPath > 1 ? b.WheelPath / b.GpsPath : double.NaN, Med(b.SpeedRatio),
                    Med(b.Along), Med(b.Cross), Med(b.Dist), Med(b.PoseToNet), Med(b.GpsToNet),
                    b.Cor2, b.Cor1, b.Sent, Med(b.SentInnov), b.NoEdge, b.Ambig, b.NoCorr, b.PlanOther, ps, Med(b.VCmd)));
            }
            Console.WriteLine("  podel = poza minus GPS ve smeru jizdy (+ = poza PRED GPS), pricne = + VLEVO od GPS,");
            Console.WriteLine("  poza->sit / GPS->sit = odstup od nejblizsi mapove hrany, kor2 / kor1 = prijata merenia koridoru");
            Console.WriteLine("  z obou / z jedne hrany, poslano = do fuze (pricne), inov. = pricny nesouhlas poslanych,");
            Console.WriteLine("  plan!Ok = plany mimo Ok/Partial. GPS ma v provoznim profilu sigmu 30 m, takze je tu referenci,");
            Console.WriteLine("  ne korekci - jeji vlastni chyba je ~0,2-0,5 m (A2 v reportu gps).");
        }

        private static double Sec(DateTime t, DateTime t0) => (t - t0).TotalSeconds;

        private static double Med(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = v.OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }
    }
}
