using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Co dělala globální navigace za jízdy: skoky pózy, stavy lokálního plánu, uzavírání hran.</b>
    ///
    /// <para><b>Nač to je (Robotour 19. 9. 2026):</b> autor viděl na rovných úsecích skoky pózy
    /// (robot se skokem dostal mimo sjízdnou část lokální mapy a přešel do úniku nebo se zastavil),
    /// při návratu do depa „zamítl pěknou cestu a přeplánoval", ve 4. kole postupně uzavřel všechny
    /// cesty a nakonec uvázl v úzké pěšince. <c>GlobalNavigator</c> přitom uzavírání hran
    /// (<c>CloseEdge</c> / <c>PenalizeEdge</c>) <b>nikam neloguje</b> — jediná stopa je
    /// <c>GlobalNavMsg.ClosureCount</c> a hrany s <c>Collision=true</c> ve zprávě <c>GN</c>. Tady
    /// se z toho, co v záznamu je, rekonstruuje: kdy se co uzavřelo, který ze tří detektorů
    /// (A bez pohybu / B bez postupu φ / C přehrazená cesta = 20 selhání plánu po sobě) to podle
    /// okolností byl, a jestli tomu předcházel skok pózy.</para>
    ///
    /// <para>Skok pózy = posun mezi dvěma po sobě jdoucími <c>RobotStateMsg</c>, o kolik převyšuje
    /// to, co robot za tu dobu mohl ujet (<c>|v|·dt</c>). Práh <c>--jump=</c> [m], výchozí 0,5.</para>
    /// </summary>
    public static class NavReport
    {
        public static void Run(RecordFile rec, double jumpM, int topN, string phiWindow = null)
        {
            if (!string.IsNullOrEmpty(phiWindow)) { PhiTrace(rec, phiWindow); return; }
            var states = rec.ReadAll<RobotStateMsg>("RobotStateMsg").OrderBy(s => s.TimeStamp).ToList();
            var plans = rec.ReadAll<LocalPlanMsg>("LocalPlanMsg").OrderBy(p => p.TimeStamp).ToList();
            var navs = rec.ReadAll<GlobalNavMsg>("GlobalNavMsg").OrderBy(n => n.TimeStamp).ToList();
            var diags = rec.ReadAll<MeasurementDiagMsg>("MeasurementDiagMsg").Where(d => d.Accepted).OrderBy(d => d.TimeStamp).ToList();
            var gps = rec.ReadAll<ARBot.Common.Devices.GPSState>("GPSState").OrderBy(g => g.TimeStamp).ToList();
            var drives = rec.ReadAll<DriveCommandMsg>("DriveCommandMsg").OrderBy(d => d.TimeStamp).ToList();

            Console.WriteLine($"RobotStateMsg {states.Count}, LocalPlanMsg {plans.Count}, GlobalNavMsg {navs.Count}, "
                              + $"prijatych mereni (MeasurementDiagMsg) {diags.Count}, GPSState {gps.Count}");
            if (states.Count < 2) { Console.WriteLine("Bez poz nejde merit nic."); return; }

            // ---------- 1. Skoky pózy ----------
            Console.WriteLine();
            Console.WriteLine($"=== 1. SKOKY POZY (posun mezi RobotStateMsg minus |v|*dt > {jumpM:F2} m) ===");
            var jumps = new List<(DateTime t, double size, double v, double dx, double dy)>();
            double travelled = 0, travelledBezSkoku = 0;
            for (int i = 1; i < states.Count; i++)
            {
                var a = states[i - 1]; var b = states[i];
                double dt = (b.TimeStamp - a.TimeStamp).TotalSeconds;
                if (dt <= 0 || dt > 2) continue;
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double mohl = Math.Abs(a.V) * dt + 0.05;
                travelled += d;
                if (d - mohl > jumpM) jumps.Add((b.TimeStamp, d, a.V, dx, dy));
                else travelledBezSkoku += d;
            }
            Console.WriteLine($"  ujeta draha podle poz {travelled:F0} m, z toho ve skocich {travelled - travelledBezSkoku:F1} m; skoku {jumps.Count}");
            if (jumps.Count > 0)
            {
                var bins = new (string n, double lo, double hi)[] { ("0,5-1 m", 0.5, 1), ("1-2 m", 1, 2), ("2-5 m", 2, 5), (">5 m", 5, 1e9) };
                Console.WriteLine("  velikost: " + string.Join(", ", bins.Select(b => $"{b.n} {jumps.Count(j => j.size >= b.lo && j.size < b.hi)}")));
                Console.WriteLine($"  za jizdy (|v| > 0,1 m/s): {jumps.Count(j => Math.Abs(j.v) > 0.1)}, ve stani: {jumps.Count(j => Math.Abs(j.v) <= 0.1)}");
                Console.WriteLine($"  {"cas",-11} {"skok",6} {"v",6}  {"smer skoku",-14} {"koridor <=1,5 s pred",-22} GPS (sat/HDOP)");
                foreach (var j in jumps.OrderByDescending(j => j.size).Take(topN).OrderBy(j => j.t))
                {
                    var d = diags.LastOrDefault(x => x.TimeStamp <= j.t && (j.t - x.TimeStamp).TotalSeconds <= 1.5);
                    var g = gps.LastOrDefault(x => x.TimeStamp <= j.t);
                    string kor = d == null ? "-" : $"{d.Source} NIS {d.Nis:F1} ({(j.t - d.TimeStamp).TotalSeconds:F1} s)";
                    Console.WriteLine($"  {Cas(j.t),-11} {j.size,6:F2} {j.v,6:F2}  {Smer(j.dx, j.dy),-14} {kor,-22} {(g == null ? "-" : $"{g.NumberOfSatellites}/{g.Hdop:F1}")}");
                }
                int sKoridorem = jumps.Count(j => diags.Any(x => x.TimeStamp <= j.t && (j.t - x.TimeStamp).TotalSeconds <= 1.5));
                Console.WriteLine($"  skoku s prijatym merenim koridoru do 1,5 s pred: {sKoridorem} z {jumps.Count}"
                                  + (diags.Count == 0 ? " (v zaznamu zadna MeasurementDiagMsg - measdiag= vypnute?)" : ""));
            }

            // ---------- 2. Stavy lokálního plánu ----------
            Console.WriteLine();
            Console.WriteLine("=== 2. STAVY LOKALNIHO PLANU ===");
            if (plans.Count > 0)
            {
                foreach (var g in plans.GroupBy(p => p.PlanStatus).OrderByDescending(g => g.Count()))
                    Console.WriteLine($"  {g.Key,-18} {g.Count(),6}  {100.0 * g.Count() / plans.Count,5:F1} %");
                // Epizody stavu, ve kterem robot nejede k mrkvi.
                var spatne = new HashSet<LocalPlanStatus> { LocalPlanStatus.RobotOutsideGrid, LocalPlanStatus.RobotBlocked,
                                                            LocalPlanStatus.NoRoute, LocalPlanStatus.EscapingBlocked,
                                                            LocalPlanStatus.AbortedCollision, LocalPlanStatus.GoalBlocked };
                Console.WriteLine("  epizody (>= 2 s) stavu mimo Ok/Partial:");
                LocalPlanStatus? cur = null; DateTime from = default; int n = 0;
                var epizody = new List<(LocalPlanStatus s, DateTime from, DateTime to, int n)>();
                foreach (var p in plans)
                {
                    var s = spatne.Contains(p.PlanStatus) ? p.PlanStatus : (LocalPlanStatus?)null;
                    if (s != cur)
                    {
                        if (cur != null) epizody.Add((cur.Value, from, p.TimeStamp, n));
                        cur = s; from = p.TimeStamp; n = 0;
                    }
                    n++;
                }
                if (cur != null) epizody.Add((cur.Value, from, plans[^1].TimeStamp, n));
                foreach (var e in epizody.Where(e => (e.to - e.from).TotalSeconds >= 2).OrderByDescending(e => (e.to - e.from).TotalSeconds).Take(topN).OrderBy(e => e.from))
                {
                    var st = states.LastOrDefault(x => x.TimeStamp <= e.from);
                    int skokyPred = jumps.Count(j => j.t <= e.from && (e.from - j.t).TotalSeconds <= 5);
                    Console.WriteLine($"    {Cas(e.from)} - {Cas(e.to)}  {e.s,-18} {(e.to - e.from).TotalSeconds,6:F1} s  ({e.n} planu)"
                                      + (skokyPred > 0 ? $"  <- {skokyPred} skok(y) pozy do 5 s predtim" : ""));
                }
            }

            // ---------- 3. Uzavírání hran ----------
            Console.WriteLine();
            Console.WriteLine("=== 3. UZAVIRANI / PENALIZACE HRAN (ClosureCount v GlobalNavMsg) ===");
            var cfg = new GlobalNavigatorConfig();
            Console.WriteLine($"  detektory: A bez pohybu (< {cfg.MinMotionM} m za {(cfg.NoMotionSec + cfg.EscalateSec).TotalSeconds:F0} s, {cfg.MaxRecoveries + 1}x -> uzavreni), "
                              + $"B bez postupu (pokles phi < {cfg.RequiredPhiDrop:F0} s na {cfg.ProgressWindowM:F0} m drahy: 1. penalizace x{cfg.PenaltyFactor}, 2. uzavreni), "
                              + $"C prehrazeno ({cfg.BlockedPlanCount} selhani planu NoRoute/RobotBlocked po sobe -> uzavreni). TTL {cfg.ClosureTtl.TotalSeconds:F0} s.");
            var gnIdx = rec.Index.Where(e => e.MsgName == "GN").ToList();
            var znameZavrene = new HashSet<long>();
            int prev = 0; int udalosti = 0;
            foreach (var nv in navs)
            {
                if (nv.ClosureCount <= prev) { prev = nv.ClosureCount; continue; }
                udalosti++;
                var t = nv.TimeStamp;
                var okno = plans.Where(p => p.TimeStamp <= t && (t - p.TimeStamp).TotalSeconds <= 5).ToList();
                int streak = 0;
                foreach (var p in plans.Where(p => p.TimeStamp <= t).Reverse())
                {
                    if (p.PlanStatus == LocalPlanStatus.NoRoute || p.PlanStatus == LocalPlanStatus.RobotBlocked) streak++; else break;
                    if (streak > 200) break;
                }
                var poz15 = states.Where(s => s.TimeStamp <= t && (t - s.TimeStamp).TotalSeconds <= 15).ToList();
                double ujel15 = Ujeto(poz15);
                var nav20 = navs.Where(x => x.TimeStamp <= t).ToList();
                double phiPred = PhiPredDrahou(nav20, states, t, cfg.ProgressWindowM, out double drahaOkna);
                int skoky20 = jumps.Count(j => j.t <= t && (t - j.t).TotalSeconds <= 30);
                var g = gps.LastOrDefault(x => x.TimeStamp <= t);

                // Nove hrany s Collision=true v nasledujici GN zprave.
                var noveWay = new List<long>();
                foreach (var e in gnIdx.Where(e => new DateTime(e.CaptureTicks == 0 ? e.ArrivalTicks : e.CaptureTicks) >= t).Take(2))
                {
                    if (rec.Read(e) is GraphNavigationMsg gn)
                        foreach (var ed in gn.Edges.Where(ed => ed.Collision))
                            if (znameZavrene.Add(ed.ID)) noveWay.Add(ed.ID);
                }

                string odhad;
                if (streak >= cfg.BlockedPlanCount) odhad = $"C prehrazeno ({streak} selhani planu po sobe)";
                else if (ujel15 < cfg.MinMotionM) odhad = $"A bez pohybu ({ujel15:F2} m za 15 s)";
                else if (!double.IsNaN(phiPred) && phiPred - nv.Phi < cfg.RequiredPhiDrop) odhad = $"B bez postupu (pokles phi {phiPred - nv.Phi:F1} s na {drahaOkna:F0} m)";
                else odhad = "nejasne (zadny detektor podle zaznamu nesedi - mozna kombinace)";

                Console.WriteLine($"  {Cas(t)} ClosureCount {prev}->{nv.ClosureCount}  stav {(GlobalNavStatus)nv.Status}  phi {nv.Phi:F0} s  trasa {nv.RouteLengthM:F0} m / {nv.RouteEdgeCount} hran  offRoute {nv.OffRouteDist:F1} m"
                                  + (noveWay.Count > 0 ? $"  nove uzavrene/penalizovane way: {string.Join(",", noveWay)}" : ""));
                Console.WriteLine($"      odhad detektoru: {odhad}");
                Console.WriteLine($"      poslednich 5 s planu: {string.Join(", ", okno.GroupBy(p => p.PlanStatus).OrderByDescending(x => x.Count()).Select(x => $"{x.Key} {x.Count()}"))}; "
                                  + $"ujeto za 15 s {ujel15:F1} m; skoku pozy za 30 s {skoky20}; GPS {(g == null ? "-" : $"{g.NumberOfSatellites}/{g.Hdop:F1}")}");
                prev = nv.ClosureCount;
            }
            if (udalosti == 0) Console.WriteLine("  zadne uzavreni ani penalizace.");
            else
            {
                Console.WriteLine($"  celkem udalosti {udalosti}; TTL {cfg.ClosureTtl.TotalSeconds:F0} s znamena, ze pocet muze i klesat (expirace).");
                Console.WriteLine("  POZOR: penalizace (x5, mekka) a tvrde uzavreni jsou ve zprave NEROZLISENE - obe zvysi ClosureCount a obe se kresli jako Collision.");
            }

            // ---------- 3b. Přeplánování ----------
            Console.WriteLine();
            Console.WriteLine("=== 3b. PREPLANOVANI (skok delky trasy o > 20 m za jizdy) ===");
            GlobalNavMsg pn = null; int replans = 0;
            foreach (var nv in navs)
            {
                if (pn != null && pn.HasGoal && nv.HasGoal && (GlobalNavStatus)nv.Status == GlobalNavStatus.Driving
                    && Math.Abs(nv.RouteLengthM - pn.RouteLengthM) > 20 && (nv.TimeStamp - pn.TimeStamp).TotalSeconds < 2)
                {
                    replans++;
                    if (replans <= topN)
                        Console.WriteLine($"  {Cas(nv.TimeStamp)} trasa {pn.RouteLengthM:F0} -> {nv.RouteLengthM:F0} m ({pn.RouteEdgeCount} -> {nv.RouteEdgeCount} hran), closures {pn.ClosureCount} -> {nv.ClosureCount}, offRoute {nv.OffRouteDist:F1} m");
                }
                pn = nv;
            }
            Console.WriteLine($"  celkem {replans}");

            // ---------- 4. Nouzová zastavení (kolize) a drženo ----------
            Console.WriteLine();
            Console.WriteLine("=== 4. RIDICI PRIKAZY ===");
            if (drives.Count > 0)
            {
                int held = drives.Count(d => d.Held);
                Console.WriteLine($"  DriveCommandMsg {drives.Count}, z toho Held {held} ({100.0 * held / drives.Count:F1} %)");
            }
            Console.WriteLine($"  phi na konci: {(navs.Count > 0 ? navs[^1].Phi.ToString("F0") : "-")} s, stav {(navs.Count > 0 ? ((GlobalNavStatus)navs[^1].Status).ToString() : "-")}, closures {(navs.Count > 0 ? navs[^1].ClosureCount : 0)}");
        }

        /// <summary>
        /// Casova rada phi, delky trasy a pozy v okne <c>HH:mm:ss,HH:mm:ss</c> - k overeni, jestli phi
        /// pri jizde po trase KLESA (ma), nebo roste (chyba vypoctu / spatna orientace hrany).
        /// </summary>
        private static void PhiTrace(RecordFile rec, string window)
        {
            var p = window.Split(',');
            if (p.Length != 2 || !TimeSpan.TryParse(p[0], CultureInfo.InvariantCulture, out var od) || !TimeSpan.TryParse(p[1], CultureInfo.InvariantCulture, out var @do))
            { Console.WriteLine("--phi=HH:mm:ss,HH:mm:ss"); return; }
            var navs = rec.ReadAll<GlobalNavMsg>("GlobalNavMsg").Where(n => n.TimeStamp.TimeOfDay >= od && n.TimeStamp.TimeOfDay <= @do).OrderBy(n => n.TimeStamp).ToList();
            var states = rec.ReadAll<RobotStateMsg>("RobotStateMsg").Where(s => s.TimeStamp.TimeOfDay >= od.Subtract(TimeSpan.FromSeconds(1)) && s.TimeStamp.TimeOfDay <= @do).OrderBy(s => s.TimeStamp).ToList();
            Console.WriteLine("=== PHI V CASE (GlobalNavMsg 5 Hz) ===");
            Console.WriteLine($"  {"cas",-11} {"stav",-9} {"phi[s]",8} {"dPhi",7} {"trasa[m]",9} {"hran",4} {"offR",6} {"closures",8} {"x",8} {"y",8} {"v",6} {"ujeto",7}");
            GlobalNavMsg prev = null; double ujeto = 0; RobotStateMsg ps = null;
            foreach (var n in navs)
            {
                var s = states.LastOrDefault(x => x.TimeStamp <= n.TimeStamp);
                if (s != null && ps != null) { double dx = s.X - ps.X, dy = s.Y - ps.Y; ujeto += Math.Sqrt(dx * dx + dy * dy); }
                if (s != null) ps = s;
                Console.WriteLine($"  {Cas(n.TimeStamp),-11} {(GlobalNavStatus)n.Status,-9} {n.Phi,8:F1} {(prev == null ? 0 : n.Phi - prev.Phi),7:F1} {n.RouteLengthM,9:F1} {n.RouteEdgeCount,4} {n.OffRouteDist,6:F1} {n.ClosureCount,8} {(s?.X ?? 0),8:F1} {(s?.Y ?? 0),8:F1} {(s?.V ?? 0),6:F2} {ujeto,7:F1}");
                prev = n;
            }
        }

        /// <summary>Phi z GlobalNavMsg, ktera byla o <paramref name="windowM"/> ujete drahy (podle poz, vcetne skoku) drive.</summary>
        private static double PhiPredDrahou(List<GlobalNavMsg> navs, List<RobotStateMsg> states, DateTime t, double windowM, out double draha)
        {
            draha = 0;
            var poz = states.Where(s => s.TimeStamp <= t).ToList();
            DateTime? tStart = null;
            for (int i = poz.Count - 1; i >= 1; i--)
            {
                double dx = poz[i].X - poz[i - 1].X, dy = poz[i].Y - poz[i - 1].Y;
                draha += Math.Sqrt(dx * dx + dy * dy);
                if (draha >= windowM) { tStart = poz[i - 1].TimeStamp; break; }
            }
            if (tStart == null) return double.NaN;
            var n = navs.LastOrDefault(x => x.TimeStamp <= tStart.Value && (GlobalNavStatus)x.Status == GlobalNavStatus.Driving);
            return n?.Phi ?? double.NaN;
        }

        private static double Ujeto(List<RobotStateMsg> s)
        {
            double d = 0;
            for (int i = 1; i < s.Count; i++)
            {
                double dx = s[i].X - s[i - 1].X, dy = s[i].Y - s[i - 1].Y;
                d += Math.Sqrt(dx * dx + dy * dy);
            }
            return d;
        }

        private static string Smer(double dx, double dy)
        {
            double a = Math.Atan2(dy, dx) * 180 / Math.PI;
            return $"{a,6:F0} deg ENU";
        }

        private static string Cas(DateTime t) => t.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);
    }
}
