using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Co dělal lokální plánovač v čase</b> — a hlavně: <b>byla mrkev dosažitelná</b> a <b>měl robot
    /// před sebou něco potvrzeně sjízdného?</b> Viz doc/occupancy-and-local-planning.md a
    /// doc/mission-freerun.md.
    ///
    /// <para>Motivace: první jízda FreeRun na železe (záznam 20260902-222601) skončila nárazem.
    /// Hypotéza autora byla, že mrkev ležela v nedostupném místě, A* dojel „co nejblíž" a robot se
    /// pak plazil <c>MinCostSpeed</c> skrz neověřený prostor. Tenhle rozbor to má potvrdit nebo
    /// vyvrátit z dat, ne z dojmu — a oddělit od sebe tři možné příčiny plazení: nedosažitelnou
    /// mrkev, chybějící potvrzení sjízdnosti (nic není <c>Free</c>) a nouzové zastavení.</para>
    ///
    /// <para><b>Ukazatel nedosažitelnosti:</b> <c>|RequestedGoal − ReachedGoal|</c>. Mrkev FreeRunu
    /// leží 3 m před robotem, tedy hluboko uvnitř gridu (poloviční hrana 6,4 m), takže ořez na grid
    /// nenastává a stav <see cref="LocalPlanStatus.Partial"/> s nenulovým rozdílem znamená právě
    /// „cíl je uvnitř mapy, ale nevede k němu průjezdná cesta" — A* vrátil nejbližší dosažitelnou
    /// buňku (fallback „jeď alespoň co nejblíž").</para>
    ///
    /// <para><b>Dosah potvrzeného:</b> délka dráhy od robota k prvnímu uzlu, jehož strop je na podlaze
    /// <c>MinCostSpeed</c> (0,05 m/s). Za hranicí potvrzeně <c>Free</c> terénu plánovač tu podlahu
    /// předepisuje schválně (viz doc), takže 0 m znamená „leze hned od sebe". <b>Rychlost 1. uzlu</b>
    /// je to, co robot skutečně dostane u sebe; minimum přes celou dráhu je vždy 0,05 (konec dráhy je
    /// z definice hranicí známého), takže samo o sobě nic neříká.</para>
    /// </summary>
    public static class LocalPlanReport
    {
        private const double Floor = 0.0501;   // MinCostSpeed s rezervou na float

        /// <param name="binSeconds">Šířka časového okna pro časovou osu [s].</param>
        /// <param name="unreachM">Od jakého rozdílu požadovaný−dosažený cíl se mrkev bere jako
        /// nedosažitelná [m].</param>
        public static void Run(RecordFile rec, double binSeconds, double unreachM, double detailFrom, double detailTo)
        {
            var plans = rec.ReadAll<LocalPlanMsg>("LocalPlanMsg").OrderBy(p => p.TimeStamp).ToList();
            var states = rec.ReadAll<RobotStateMsg>("RobotStateMsg").OrderBy(s => s.TimeStamp).ToList();
            var runs = rec.ReadAll<FreeRunMsg>("FreeRunMsg").OrderBy(f => f.TimeStamp).ToList();
            var drives = rec.ReadAll<DriveCommandMsg>("DriveCommandMsg").OrderBy(d => d.TimeStamp).ToList();

            Console.WriteLine($"LocalPlanMsg {plans.Count}, RobotStateMsg {states.Count}, "
                              + $"FreeRunMsg {runs.Count}, DriveCommandMsg {drives.Count}");
            if (plans.Count == 0) { Console.WriteLine("Zaznam lokalni plany nenese."); return; }

            var t0 = plans[0].TimeStamp;
            double dur = (plans[plans.Count - 1].TimeStamp - t0).TotalSeconds;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "usek: {0:F1} s ({1:F1} planu/s), zacatek {2:HH:mm:ss.fff}", dur, plans.Count / Math.Max(0.001, dur), t0));
            Console.WriteLine();

            // (1) Stavy celkem.
            Console.WriteLine("STAVY PLANU:");
            foreach (var g in plans.GroupBy(p => p.PlanStatus).OrderByDescending(g => g.Count()))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-18} {1,6}  ({2,5:F1} %)", g.Key, g.Count(), 100.0 * g.Count() / plans.Count));
            Console.WriteLine();

            // (2) Kazdy plan obohatit o stav fuze, ridici prikaz a puvod mrkve (nejblizsi predchozi zprava).
            var rows = new List<Row>(plans.Count);
            int si = 0, ri = 0, di = 0;
            foreach (var p in plans)
            {
                while (si + 1 < states.Count && states[si + 1].TimeStamp <= p.TimeStamp) si++;
                while (ri + 1 < runs.Count && runs[ri + 1].TimeStamp <= p.TimeStamp) ri++;
                while (di + 1 < drives.Count && drives[di + 1].TimeStamp <= p.TimeStamp) di++;
                var s = states.Count > 0 ? states[si] : null;
                var f = runs.Count > 0 ? runs[ri] : null;
                var d = drives.Count > 0 ? drives[di] : null;

                var r = new Row { Plan = p, T = (p.TimeStamp - t0).TotalSeconds };
                if (s != null)
                {
                    r.V = Math.Abs(s.V);
                    double dx = p.RequestedGoalX - s.X, dy = p.RequestedGoalY - s.Y;
                    r.GoalDist = Math.Sqrt(dx * dx + dy * dy);
                    r.X = s.X; r.Y = s.Y;
                }
                if (d != null) { r.VCmd = Math.Abs(d.Speed); r.Estop = d.EmergencyStop; }
                double ux = p.RequestedGoalX - p.ReachedGoalX, uy = p.RequestedGoalY - p.ReachedGoalY;
                r.Unreach = Math.Sqrt(ux * ux + uy * uy);
                r.FromCorridor = f != null && f.FromCorridor;
                r.V0 = FirstSpeed(p.WayPoints);
                r.FreeAhead = FreeAheadAlongPath(p.WayPoints);
                r.PathLen = p.LengthM;
                rows.Add(r);
            }

            var hasPath = rows.Where(HasPath).ToList();
            var goalDist = new Stats("vzdalenost robot -> mrkev [m]");
            var unreach = new Stats("|pozadovany - dosazeny cil| [m]");
            var pathLen = new Stats("delka planu [m]");
            var freeAhead = new Stats("dosah potvrzene volneho [m]");
            var v0 = new Stats("rychlost 1. uzlu [m/s]");
            var vCmd = new Stats("prikazana rychlost [m/s]");
            var vRobot = new Stats("rychlost fuze |V| [m/s]");
            var minClr = new Stats("min odstup na draze [m]");
            foreach (var r in hasPath)
            {
                goalDist.Add(r.GoalDist); unreach.Add(r.Unreach); pathLen.Add(r.PathLen);
                freeAhead.Add(r.FreeAhead); v0.Add(r.V0); vCmd.Add(r.VCmd); vRobot.Add(r.V);
                minClr.Add(r.Plan.MinClearanceM);
            }
            Console.WriteLine("PLANY S DRAHOU (Ok + Partial):");
            Console.WriteLine("  " + goalDist.Line("m"));
            Console.WriteLine("  " + unreach.Line("m"));
            Console.WriteLine("  " + pathLen.Line("m"));
            Console.WriteLine("  " + freeAhead.Line("m"));
            Console.WriteLine("  " + v0.Line("m/s"));
            Console.WriteLine("  " + vCmd.Line("m/s"));
            Console.WriteLine("  " + vRobot.Line("m/s"));
            Console.WriteLine("  " + minClr.Line("m"));
            int n = Math.Max(1, hasPath.Count);
            int nTight = hasPath.Count(r => r.Plan.MinClearanceM < ARBot.Common.Configuration.Profile.SafeDist);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  draha s odstupem POD SafeDist {0:F2} m (eskapovaci zona): {1,5} z {2} ({3:F0} %)",
                ARBot.Common.Configuration.Profile.SafeDist, nTight, hasPath.Count, 100.0 * nTight / n));
            int nUnreach = hasPath.Count(r => r.Unreach > unreachM);
            int nCrawl0 = hasPath.Count(r => r.V0 > 0 && r.V0 <= Floor);
            int nEstop = hasPath.Count(r => r.Estop);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  mrkev NEDOSAZITELNA (rozdil > {0:F2} m):          {1,5} z {2} ({3:F0} %)",
                unreachM, nUnreach, hasPath.Count, 100.0 * nUnreach / n));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  robot LEZE hned od sebe (1. uzel <= 0,05 m/s):   {0,5} z {1} ({2:F0} %)",
                nCrawl0, hasPath.Count, 100.0 * nCrawl0 / n));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  aktivni NOUZOVE ZASTAVENI v case planu:         {0,5} z {1} ({2:F0} %)",
                nEstop, hasPath.Count, 100.0 * nEstop / n));
            Console.WriteLine();

            GridResets(rec, t0);
            PoseSteps(states);

            // (3) Casova osa.
            Console.WriteLine($"CASOVA OSA (okno {binSeconds:F0} s; mediany v okne):");
            Console.WriteLine("    t[s]   n  Part% Esc Blk NoRt Abrt korid%  dMrkev nedosaz  delka  volno    v0   vCmd  estop%  ujeto");
            int bins = (int)Math.Ceiling(dur / binSeconds) + 1;
            for (int b = 0; b < bins; b++)
            {
                double a = b * binSeconds, z = a + binSeconds;
                var w = rows.Where(r => r.T >= a && r.T < z).ToList();
                if (w.Count == 0) continue;
                int part = w.Count(r => r.Plan.PlanStatus == LocalPlanStatus.Partial || r.Plan.PlanStatus == LocalPlanStatus.Ok);
                int noRoute = w.Count(r => r.Plan.PlanStatus == LocalPlanStatus.NoRoute);
                int abort = w.Count(r => r.Plan.PlanStatus == LocalPlanStatus.AbortedCollision);
                int esc = w.Count(r => r.Plan.PlanStatus == LocalPlanStatus.EscapingBlocked);
                int blk = w.Count(r => r.Plan.PlanStatus == LocalPlanStatus.RobotBlocked);
                int cor = w.Count(r => r.FromCorridor);
                int es = w.Count(r => r.Estop);
                var wp = w.Where(HasPath).ToList();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0} {1,3}  {2,4:F0} {3,3} {4,3} {5,4} {6,4}  {7,5:F0}  {8,6} {9,7}  {10,5}  {11,5}  {12,4}  {13,5}  {14,5:F0}  {15,5:F2}",
                    a, w.Count, 100.0 * part / w.Count, esc, blk, noRoute, abort, 100.0 * cor / w.Count,
                    Med(wp, r => r.GoalDist), Med(wp, r => r.Unreach), Med(wp, r => r.PathLen), Med(wp, r => r.FreeAhead),
                    Med(wp, r => r.V0), Med(w, r => r.VCmd), 100.0 * es / w.Count, Traveled(w)));
            }
            Console.WriteLine("  Part% = plany s drahou (Ok+Partial), Esc/Blk/NoRt/Abrt = pocty EscapingBlocked/RobotBlocked/");
            Console.WriteLine("  NoRoute/AbortedCollision, dMrkev = robot->mrkev, nedosaz = |pozadovany-dosazeny|, delka = delka");
            Console.WriteLine("  planu, volno = dosah potvrzene sjizdneho po draze, v0 = rychlost 1. uzlu, vCmd = prikazana");
            Console.WriteLine("  rychlost, estop% = podil casu s nouzovym zastavenim, ujeto = CISTY posun pozy fuze v okne [m]");
            Console.WriteLine();

            // (4) Epizody nedosazitelne mrkve.
            Console.WriteLine($"EPIZODY NEDOSAZITELNE MRKVE (rozdil > {unreachM:F2} m, aspon 2 s, vypadky do 1 s se preklenou):");
            Console.WriteLine("    od[s]  trvani  nedosaz p50  delka p50  volno p50  vCmd p50  ujeto   co nasledovalo");
            int i = 0, episodes = 0;
            while (i < rows.Count)
            {
                if (!(rows[i].Unreach > unreachM && HasPath(rows[i]))) { i++; continue; }
                int j = i, lastHit = i;
                while (j + 1 < rows.Count && rows[j + 1].T - rows[lastHit].T < 1.0)
                {
                    j++;
                    if (rows[j].Unreach > unreachM && HasPath(rows[j])) lastHit = j;
                }
                j = lastHit;
                var ep = rows.GetRange(i, j - i + 1);
                double len = rows[j].T - rows[i].T;
                if (len >= 2.0)
                {
                    episodes++;
                    var hits = ep.Where(r => r.Unreach > unreachM && HasPath(r)).ToList();
                    string after = "-";
                    for (int k = j + 1; k < rows.Count && rows[k].T - rows[j].T < 3.0; k++)
                        if (!HasPath(rows[k])) { after = rows[k].Plan.PlanStatus + $" za {rows[k].T - rows[j].T:F1} s"; break; }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,7:F1}  {1,5:F1}s  {2,10}  {3,9}  {4,9}  {5,8}  {6,5:F2}   {7}",
                        rows[i].T, len, Med(hits, r => r.Unreach), Med(hits, r => r.PathLen), Med(hits, r => r.FreeAhead),
                        Med(ep, r => r.VCmd), Traveled(ep), after));
                }
                i = j + 1;
            }
            if (episodes == 0) Console.WriteLine("  zadna");
            Console.WriteLine();

            // (5) Detail okna (--from/--to); bez nich poslednich 20 s - tam se obvykle stalo to,
            //     proc se zaznam rozebira.
            double from = double.IsNaN(detailFrom) ? dur - 20 : detailFrom;
            double to = double.IsNaN(detailTo) ? dur + 1 : detailTo;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "DETAIL {0:F1}-{1:F1} s (kazdy 5. plan):", from, Math.Min(to, dur)));
            Console.WriteLine("    t[s]  stav              dMrkev  nedosaz  delka  volno    v0  vCmd  vFuze  minClr  estop");
            var tail = rows.Where(r => r.T >= from && r.T < to).ToList();
            for (int k = 0; k < tail.Count; k += 5)
            {
                var r = tail[k];
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F1}  {1,-17} {2,6:F2}  {3,7:F2}  {4,5:F2}  {5,5:F2}  {6,4:F2}  {7,4:F2}  {8,5:F2}  {9,6:F2}  {10}",
                    r.T, r.Plan.PlanStatus, r.GoalDist, r.Unreach, r.PathLen, r.FreeAhead, r.V0, r.VCmd, r.V,
                    r.Plan.MinClearanceM, r.Estop ? "ESTOP" : ""));
            }
        }

        private sealed class Row
        {
            public LocalPlanMsg Plan;
            public double T, V = double.NaN, X = double.NaN, Y = double.NaN;
            public double GoalDist = double.NaN, Unreach, PathLen;
            /// <summary>Rychlost PRVNIHO uzlu = co robot dostane hned u sebe.</summary>
            public double V0 = double.NaN;
            /// <summary>Delka drahy, nez strop klesne na podlahu MinCostSpeed = dosah potvrzene sjizdneho [m].</summary>
            public double FreeAhead = double.NaN;
            public double VCmd = double.NaN;
            public bool Estop;
            public bool FromCorridor;
        }

        private static bool HasPath(Row r)
            => r.Plan.PlanStatus == LocalPlanStatus.Ok || r.Plan.PlanStatus == LocalPlanStatus.Partial;

        /// <summary>Rychlost prvniho uzlu drahy (co robot dostane hned u sebe).</summary>
        private static double FirstSpeed(RegulatorWayPoint[] wps)
            => wps == null || wps.Length < 2 ? double.NaN : wps[0].Speed;

        /// <summary>
        /// Delka drahy od robota k prvnimu uzlu, jehoz strop je na podlaze MinCostSpeed (0,05) -
        /// tedy jak daleko pred robotem konci POTVRZENE sjizdny teren. 0 = leze hned od sebe.
        /// Posledni uzel (Speed 0 = zastaveni) se nepocita; kdyz podlaha na draze neni, vraci celou delku.
        /// </summary>
        private static double FreeAheadAlongPath(RegulatorWayPoint[] wps)
        {
            if (wps == null || wps.Length < 2) return double.NaN;
            double d = 0;
            for (int i = 0; i < wps.Length - 1; i++)
            {
                if (wps[i].Speed > 0 && wps[i].Speed <= Floor) return d;
                double dx = wps[i + 1].X - wps[i].X, dy = wps[i + 1].Y - wps[i].Y;
                d += Math.Sqrt(dx * dx + dy * dy);
            }
            return d;
        }

        /// <summary>
        /// Lokalni mapa v case ze snapshotu: kolik je znamych / Free / Blocked bunek, a kdy pocet znamych
        /// skokem spadl - grid se maze pri skoku pozy (<c>PoseJumpDetector</c>) a na zelezu bez ground
        /// truth to jinak nez z dat videt nejde. Po resetu robot o prekazkach, ktere uz videl, nevi.
        /// </summary>
        private static void GridResets(RecordFile rec, DateTime t0)
        {
            var grids = rec.ReadAll<OccupancyGridMsg>("OccupancyGridMsg").OrderBy(g => g.TimeStamp).ToList();
            Console.WriteLine($"LOKALNI MAPA V CASE ({grids.Count} snapshotu):");
            if (grids.Count == 0) { Console.WriteLine(); return; }
            var known = new Stats("znamych bunek [%]");
            var free = new Stats("Free [%]");
            var blocked = new Stats("Blocked [%]");
            int prevKnown = -1, drops = 0;
            var dropTimes = new List<string>();
            foreach (var g in grids)
            {
                int n = g.Size * g.Size, k = 0, f = 0, b = 0;
                for (int j = 0; j < g.Size; j++)
                    for (int i = 0; i < g.Size; i++)
                    {
                        var st = g.State(i, j);
                        if (st != CellState.Unknown) k++;
                        if (st == CellState.Free) f++;
                        else if (st == CellState.Blocked) b++;
                    }
                known.Add(100.0 * k / n); free.Add(100.0 * f / n); blocked.Add(100.0 * b / n);
                if (prevKnown > 200 && k < prevKnown / 2)
                {
                    drops++;
                    if (dropTimes.Count < 12)
                        dropTimes.Add(string.Format(CultureInfo.InvariantCulture, "{0:F1}s ({1}->{2})",
                                                    (g.TimeStamp - t0).TotalSeconds, prevKnown, k));
                }
                prevKnown = k;
            }
            Console.WriteLine("  " + known.Line("%"));
            Console.WriteLine("  " + free.Line("%"));
            Console.WriteLine("  " + blocked.Line("%"));
            Console.WriteLine($"  propadu znamych bunek pod polovinu mezi snapshoty (reset gridu?): {drops}"
                              + (dropTimes.Count > 0 ? "  v t=" + string.Join(", ", dropTimes) : ""));
            Console.WriteLine();
        }

        /// <summary>Kroky pozy mezi po sobe jdoucimi stavy fuze - jitter GPS pri stani hyba world-kotvenou mapou.</summary>
        private static void PoseSteps(List<RobotStateMsg> states)
        {
            if (states.Count < 2) return;
            var step = new Stats("krok pozy mezi stavy [m]");
            var vs = new Stats("rychlost fuze |V| [m/s]");
            int big = 0;
            for (int i = 1; i < states.Count; i++)
            {
                double dx = states[i].X - states[i - 1].X, dy = states[i].Y - states[i - 1].Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                step.Add(d);
                vs.Add(Math.Abs(states[i].V));
                if (d > 0.3) big++;
            }
            Console.WriteLine("POZA Z FUZE (po sobe jdouci RobotStateMsg):");
            Console.WriteLine("  " + step.Line("m"));
            Console.WriteLine("  " + vs.Line("m/s"));
            Console.WriteLine($"  kroku > 0,3 m (skok pozy; PoseJumpDetector pri nem maze grid): {big}");
            Console.WriteLine();
        }

        /// <summary>
        /// CISTY posun pozy od prvniho k poslednimu radku okna [m]. Zamerne ne soucet kroku: sum |krok|
        /// integruje sum (9 mm jitteru na 10 Hz da 1,6 m za 10 s, i kdyz robot stoji).
        /// </summary>
        private static double Traveled(List<Row> w)
        {
            var a = w.FirstOrDefault(r => !double.IsNaN(r.X));
            var b = w.LastOrDefault(r => !double.IsNaN(r.X));
            if (a == null || b == null) return double.NaN;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string Med(List<Row> w, Func<Row, double> f)
        {
            var v = w.Select(f).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (v.Count == 0) return "-";
            return v[v.Count / 2].ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
