using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Regulators;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Proč robot jede takhle rychle a proč cuká</b> — rekonstrukce regulátoru dráhy
    /// (<see cref="PathResult.Control"/>) takt po taktu. Viz doc/path-following.md.
    ///
    /// <para>Motivace (25. 9. 2026, FreeRun v Modřanech): robot jel výrazně pomaleji, než je
    /// <c>maxspeed=</c>, a nejel plynule. <c>envelope</c> umí říct, jaký strop dal PLÁNOVAČ, ale
    /// ne, co z něj udělal regulátor: jeho mezivýsledky (strop z obálky, vazba na dobu rotace,
    /// úhel na cíl) jdou jen do <c>Debug</c>, tedy v Release na robotu nikam.</para>
    ///
    /// <para><b>Jak:</b> zprávy se berou v pořadí zápisu. Každý <see cref="LocalPlanMsg"/> s drahou
    /// postaví nový regulátor (<see cref="PathPlanner.Plan"/> se stejným profilem jako
    /// <c>ARBotRuntime</c>), každý takt řídicí smyčky (<see cref="RobotStateMsg"/> +
    /// <see cref="DriveCommandMsg"/> se stejným razítkem) na něm zavolá <c>Control</c> nad
    /// zaznamenaným stavem. <b>Shoda s příkazem v záznamu</b> je první řádek výstupu — bez ní by
    /// rozpad popisoval jiný regulátor.</para>
    ///
    /// <para><b>Protifaktický řádek „bez brzdění na konci plánu":</b> tentýž plán, ale strop uzlů
    /// jen z odstupu (<c>EnvVClearance</c>), tedy jako by potvrzeně sjízdný terén pokračoval za
    /// posledním uzlem. Je to horní mez toho, co by dalo prodloužení hranice potvrzeného za mrkev
    /// — ne předpověď.</para>
    /// </summary>
    public static class DriveReport
    {
        private sealed class Tick
        {
            public double T;
            public double Cmd, CmdRot;
            public double Rep, RepRot, RepVCmd, Beta, RotTime, LimitDist;
            public int Target, Nodes;
            public double Cf;          // protifakticky prikaz bez brzdeni na konci planu
            public double VFuse, WFuse;
            public bool NewPlan, Stop;
        }

        public static void Run(RecordFile rec, double maxSpeedArg, double from, double to)
        {
            double maxSpeed = !double.IsNaN(maxSpeedArg) ? maxSpeedArg : ConfigValue(rec, "maxspeed=") ?? Profile.MaxAllowedSpeed;
            var profile = new TrapezoidMotionProfile(maxSpeed, Profile.MaxAllowedRotationSpeed,
                                                     Profile.MaxAcceleration, Profile.Rozchod);
            var planner = new PathPlanner(profile, Profile.PathEpsilonMargin, Profile.LookaheadTime, Profile.LookaheadMin);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PROFIL: maxspeed={0:F2} m/s, a={1:F2} m/s2, omega_max={2:F3} rad/s, rozchod={3:F2} m, lookahead {4:F2} s / min {5:F2} m",
                maxSpeed, Profile.MaxAcceleration, Profile.MaxAllowedRotationSpeed, Profile.Rozchod,
                Profile.LookaheadTime, Profile.LookaheadMin));

            IRegulator reg = null, regCf = null;
            bool newPlan = false;
            RobotStateMsg pendingState = null;
            var ticks = new List<Tick>();
            var wheel = new List<(double t, double v)>();
            var wheelW = new List<(double t, double w)>();   // (R - L) / rozchod - znamenko a meritko overi fit
            var gyro = new List<(double t, double w)>();
            DateTime? t0 = null;

            foreach (var e in rec.Index)
            {
                string n = e.MsgName;
                if (n != "LocalPlanMsg" && n != "RobotStateMsg" && n != "DriveCommandMsg" && n != "MotorStateBase" && n != "IMUState") continue;
                var m = rec.Read(e);
                switch (m)
                {
                    case LocalPlanMsg p:
                        t0 ??= p.TimeStamp;
                        if (p.WayPoints != null && p.WayPoints.Length >= 2)
                        {
                            try { reg = planner.Plan(p.WayPoints); } catch { reg = null; }
                            regCf = null;
                            if (p.HasEnvelope && p.EnvVClearance.Length == p.WayPoints.Length)
                            {
                                // KOPIE uzlu - RegulatorWayPoint je trida, prepsat Speed na miste by
                                // zmenilo i plan, ze ktereho uz bezi rekonstrukce.
                                var wps = p.WayPoints.Select((w, k) => new RegulatorWayPoint
                                {
                                    X = w.X, Y = w.Y, MaxPositionError = w.MaxPositionError,
                                    MaxSpeedError = w.MaxSpeedError, Orientation = w.Orientation,
                                    MaxOrientationError = w.MaxOrientationError,
                                    Speed = Math.Max(0.05, Math.Min(maxSpeed, p.EnvVClearance[k])),
                                }).ToArray();
                                try { regCf = planner.Plan(wps); } catch { regCf = null; }
                            }
                            newPlan = true;
                        }
                        break;
                    case RobotStateMsg s:
                        pendingState = s;
                        break;
                    case MotorStateBase mot when mot.HasMeasurement && t0.HasValue:
                        wheel.Add(((mot.TimeStamp - t0.Value).TotalSeconds,
                                   0.5 * (mot.LeftWheelSpeed + mot.RightWheelSpeed)));
                        wheelW.Add(((mot.TimeStamp - t0.Value).TotalSeconds,
                                    (mot.RightWheelSpeed - mot.LeftWheelSpeed) / Profile.Rozchod));
                        break;
                    case IMUState imu when imu.AngularVelocity.HasValue && t0.HasValue:
                        gyro.Add(((imu.TimeStamp - t0.Value).TotalSeconds, imu.AngularVelocity.Value.Z));
                        break;
                    case DriveCommandMsg d:
                        if (!t0.HasValue || pendingState == null || pendingState.TimeStamp != d.TimeStamp) break;
                        var tk = new Tick
                        {
                            T = (d.TimeStamp - t0.Value).TotalSeconds,
                            Cmd = d.Speed, CmdRot = d.RotationSpeed,
                            VFuse = pendingState.V, WFuse = pendingState.Omega,
                            Stop = d.EmergencyStop || d.Held,
                            NewPlan = newPlan,
                            Rep = double.NaN, Cf = double.NaN,
                        };
                        newPlan = false;
                        if (reg != null)
                        {
                            var st = pendingState.ToRobotState();
                            var r = reg.Control(st);
                            tk.Rep = r.Speed; tk.RepRot = r.RotationSpeed;
                            if (reg is PathResult pr)
                            {
                                tk.RepVCmd = pr.LastVCmd; tk.Beta = pr.LastBeta; tk.RotTime = pr.LastRotTime;
                                tk.LimitDist = pr.LastLimitDist; tk.Target = pr.LastTargetIndex;
                                tk.Nodes = pr.WayPoints.Length;
                            }
                            if (regCf != null) tk.Cf = regCf.Control(pendingState.ToRobotState()).Speed;
                        }
                        ticks.Add(tk);
                        break;
                }
            }

            var sel = ticks.Where(t => t.T >= from && t.T <= to).ToList();
            Console.WriteLine($"taktu ridici smycky: {ticks.Count}, ve vyberu {sel.Count}, s regulatorem {sel.Count(t => !double.IsNaN(t.Rep))}");
            Console.WriteLine();
            var drive = sel.Where(t => !t.Stop && !double.IsNaN(t.Rep) && t.Cmd > 0.02).ToList();

            // (1) Shoda rekonstrukce
            var diff = new Stats("|rekonstrukce - zaznam| v");
            var diffW = new Stats("|rekonstrukce - zaznam| w");
            foreach (var t in drive) { diff.Add(Math.Abs(t.Rep - t.Cmd)); diffW.Add(Math.Abs(t.RepRot - t.CmdRot)); }
            Console.WriteLine("SHODA REKONSTRUKCE S PRIKAZEM (takty za jizdy, bez stopu):");
            Console.WriteLine("  " + diff.Line("m/s"));
            Console.WriteLine("  " + diffW.Line("rad/s"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  do 0,02 m/s: {0:F1} %  (neshoda = regulator dostal jiny plan/stav, nez se tu priradil)",
                100.0 * drive.Count(t => Math.Abs(t.Rep - t.Cmd) <= 0.02) / Math.Max(1, drive.Count)));
            Console.WriteLine();

            // (2) Rozpad rychlosti
            Console.WriteLine("ROZPAD RYCHLOSTI (takty za jizdy):");
            var sVCmd = new Stats("strop z obalky drahy vCmd");
            var sOut = new Stats("po vazbe na rotaci v");
            var sCmd = new Stats("prikaz v zaznamu");
            var sCf = new Stats("bez brzdeni na konci planu");
            var sFuse = new Stats("rychlost fuze |V|");
            var sBeta = new Stats("|beta| na cilovy uzel [deg]");
            var sRot = new Stats("doba dorovnani rotace [s]");
            var sDist = new Stats("vzdalenost ciloveho uzlu [m]");
            int rotBinds = 0, lastTarget = 0;
            var rotLoss = new Stats("ztrata vazbou na rotaci");
            foreach (var t in drive)
            {
                sVCmd.Add(t.RepVCmd); sOut.Add(t.Rep); sCmd.Add(t.Cmd); sCf.Add(t.Cf); sFuse.Add(Math.Abs(t.VFuse));
                sBeta.Add(Math.Abs(t.Beta) * 180 / Math.PI); sRot.Add(t.RotTime); sDist.Add(t.LimitDist);
                if (t.Rep < t.RepVCmd - 0.01) { rotBinds++; rotLoss.Add(t.RepVCmd - t.Rep); }
                if (t.Target == t.Nodes - 1) lastTarget++;
            }
            foreach (var s in new[] { sVCmd, sOut, sCmd, sCf, sFuse }) Console.WriteLine("  " + s.Line("m/s"));
            foreach (var s in new[] { sBeta, sRot, sDist }) Console.WriteLine("  " + s.Line());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  vazba na rotaci srazila rychlost v {0} z {1} taktu ({2:F1} %)", rotBinds, drive.Count,
                100.0 * rotBinds / Math.Max(1, drive.Count)));
            if (rotLoss.Count > 0) Console.WriteLine("    " + rotLoss.Line("m/s"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  cilovy uzel = POSLEDNI uzel planu (mrkev): {0:F1} % taktu", 100.0 * lastTarget / Math.Max(1, drive.Count)));
            Console.WriteLine("  vCmd = min pres uzly pred robotem Dist2Speed(vzdalenost, v, VLimit); posledni uzel ma VLimit 0,");
            Console.WriteLine("  takze pri mrkvi ve stale stejne vzdalenosti je vCmd strop 'zastav v mrkvi'.");
            Console.WriteLine();

            // (3) Cukani
            Console.WriteLine("CUKANI - zmena prikazu mezi po sobe jdoucimi takty (za jizdy):");
            var dv = new Stats("|dv| prikazu [m/s]");
            var dvNew = new Stats("|dv| v taktu s NOVYM planem");
            var dvOld = new Stats("|dv| v taktu se STARYM planem");
            var dw = new Stats("|dw| prikazu [rad/s]");
            int flips = 0, big = 0, pairs = 0, bigEnv = 0, bigRot = 0;
            for (int i = 1; i < sel.Count; i++)
            {
                var a = sel[i - 1]; var b = sel[i];
                if (a.Stop || b.Stop || a.Cmd <= 0.02 || b.Cmd <= 0.02 || b.T - a.T > 0.15) continue;
                pairs++;
                double d = Math.Abs(b.Cmd - a.Cmd);
                dv.Add(d); (b.NewPlan ? dvNew : dvOld).Add(d);
                dw.Add(Math.Abs(b.CmdRot - a.CmdRot));
                if (d > 0.1)
                {
                    big++;
                    // Kdo skok udelal: zmena stropu z obalky, nebo zmena srazky vazbou na rotaci?
                    double dEnv = Math.Abs(b.RepVCmd - a.RepVCmd);
                    double dRot = Math.Abs((b.RepVCmd - b.Rep) - (a.RepVCmd - a.Rep));
                    if (dEnv >= dRot) bigEnv++; else bigRot++;
                }
                if (Math.Sign(a.CmdRot) != Math.Sign(b.CmdRot) && Math.Abs(a.CmdRot) > 0.05 && Math.Abs(b.CmdRot) > 0.05) flips++;
            }
            foreach (var s in new[] { dv, dvNew, dvOld }) Console.WriteLine("  " + s.Line("m/s"));
            // Buzeni: o kolik se mezi takty zmeni uhel na cilovy uzel - s novym planem a se starym.
            // Se starym planem ho meni jen pohyb robotu (a sum pozy), s novym i to, kam plan cil polozil.
            var dbNew = new Stats("|d beta| s NOVYM planem [deg]");
            var dbOld = new Stats("|d beta| se STARYM planem [deg]");
            var dbRot = new Stats("  z toho vlastni rotace w*dt [deg]");
            for (int i = 1; i < sel.Count; i++)
            {
                var a = sel[i - 1]; var b = sel[i];
                if (a.Stop || b.Stop || a.Cmd <= 0.02 || b.Cmd <= 0.02 || b.T - a.T > 0.15 || double.IsNaN(b.Beta)) continue;
                double d = Math.Abs(b.Beta - a.Beta) * 180 / Math.PI;
                (b.NewPlan ? dbNew : dbOld).Add(d);
                dbRot.Add(Math.Abs(a.WFuse) * (b.T - a.T) * 180 / Math.PI);
            }
            foreach (var s in new[] { dbNew, dbOld, dbRot }) Console.WriteLine("  " + s.Line());
            Console.WriteLine("  " + dw.Line("rad/s"));
            double dur = pairs * 0.1;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  skoku |dv| > 0,1 m/s: {0} z {1} ({2:F1} %, {3:F2} za sekundu jizdy)", big, pairs,
                100.0 * big / Math.Max(1, pairs), big / Math.Max(0.1, dur)));
            Console.WriteLine($"    z toho hlavne zmenou stropu z obalky {bigEnv}, zmenou vazby na rotaci {bigRot}");
            var wf = new Stats("|omega| fuze za jizdy [rad/s]");
            foreach (var t in drive) wf.Add(Math.Abs(t.WFuse));
            Console.WriteLine("  " + wf.Line());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  zmen znamenka rotace (|w| > 0,05 rad/s): {0} ({1:F2} za sekundu jizdy)", flips, flips / Math.Max(0.1, dur)));

            // Kvantovani: diskretni profil (TrapezoidMotionProfile.Compute) vraci (ve + (ne-1)*a*tSam)*0,9
            // se zaokrouhlenym ne, takze prikaz skace po schodech. Nejcastejsi hodnoty to ukazou.
            Console.WriteLine("  nejcastejsi hodnoty prikazu (zaokrouhleno na 0,005):");
            Console.WriteLine("    v [m/s]:   " + string.Join("  ", drive.GroupBy(t => Math.Round(t.Cmd / 0.005) * 0.005)
                .OrderByDescending(g => g.Count()).Take(8)
                .Select(g => string.Format(CultureInfo.InvariantCulture, "{0:F3} ({1:F1} %)", g.Key, 100.0 * g.Count() / drive.Count))));
            Console.WriteLine("    |w| [rad/s]: " + string.Join("  ", drive.GroupBy(t => Math.Round(Math.Abs(t.CmdRot) / 0.005) * 0.005)
                .OrderByDescending(g => g.Count()).Take(8)
                .Select(g => string.Format(CultureInfo.InvariantCulture, "{0:F3} ({1:F1} %)", g.Key, 100.0 * g.Count() / drive.Count))));

            // Kolisani skutecne rychlosti kol v 1s oknech
            var wsel = wheel.Where(w => w.t >= from && w.t <= to).ToList();
            if (wsel.Count > 10)
            {
                var sdWheel = new Stats("sd rychlosti kol v 1s okne");
                var sdCmd = new Stats("sd prikazu v 1s okne");
                foreach (var g in wsel.GroupBy(w => Math.Floor(w.t)))
                {
                    var vs = g.Select(x => x.v).ToList();
                    if (vs.Count < 5 || vs.Average() < 0.2) continue;
                    sdWheel.Add(Sd(vs));
                    var cs = sel.Where(t => Math.Floor(t.T) == g.Key && !t.Stop).Select(t => t.Cmd).ToList();
                    if (cs.Count >= 5) sdCmd.Add(Sd(cs));
                }
                Console.WriteLine("  " + sdCmd.Line("m/s"));
                Console.WriteLine("  " + sdWheel.Line("m/s") + $"  (MotorStateBase {wsel.Count / Math.Max(1.0, (wsel[^1].t - wsel[0].t)):F0} Hz)");
            }
            Console.WriteLine();

            // (4) Casova osa
            Console.WriteLine("CASOVA OSA (okno 20 s, mediany; takty za jizdy):");
            Console.WriteLine("    t[s]    n   vCmd  vOut   cmd    cf  vFuze  |beta|  Trot  dCil  rot%  dv>0.1");
            foreach (var g in drive.GroupBy(t => Math.Floor(t.T / 20) * 20))
            {
                var L = g.ToList();
                int nb = 0;
                for (int i = 1; i < L.Count; i++) if (Math.Abs(L[i].Cmd - L[i - 1].Cmd) > 0.1 && L[i].T - L[i - 1].T < 0.15) nb++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0} {1,4} {2,6:F2} {3,5:F2} {4,5:F2} {5,5:F2} {6,6:F2} {7,7:F1} {8,5:F2} {9,5:F2} {10,5:F0} {11,6}",
                    g.Key, L.Count, Med(L.Select(t => t.RepVCmd)), Med(L.Select(t => t.Rep)), Med(L.Select(t => t.Cmd)),
                    Med(L.Select(t => t.Cf)), Med(L.Select(t => Math.Abs(t.VFuse))),
                    Med(L.Select(t => Math.Abs(t.Beta) * 180 / Math.PI)), Med(L.Select(t => t.RotTime)),
                    Med(L.Select(t => t.LimitDist)), 100.0 * L.Count(t => t.Rep < t.RepVCmd - 0.01) / L.Count, nb));
            }
            Console.WriteLine("  vCmd = strop z obalky drahy, vOut = po vazbe na rotaci (rekonstrukce), cmd = zaznam,");
            Console.WriteLine("  cf = protifakticky bez brzdeni na konci planu, dCil = vzdalenost ciloveho uzlu,");
            Console.WriteLine("  rot% = podil taktu, kde rychlost srazila vazba na rotaci.");
            Console.WriteLine();

            RotationDynamics(sel, gyro, wheelW, from, to);
        }

        /// <summary>
        /// <b>Jak rychle robot na prikaz rotace odpovi.</b> Casove optimalni profil (<c>Rot2RotSpeed</c>)
        /// predpoklada, ze prikazana omega plati hned; zpozdeni v akcnim clenu z nej udela rele se
        /// zpozdenim, tedy mezni cyklus. Tady se ze zaznamu proklada model „mrtva doba tau +
        /// setrvacnost prvniho radu T + zesileni K": <c>y' = (K·u(t − tau) − y)/T</c>, u = prikazana
        /// omega (drzena mezi takty), y = gyro (VN100) a rotace z kol (enkodery). Jen useky za jizdy.
        /// </summary>
        private static void RotationDynamics(List<Tick> ticks, List<(double t, double w)> gyro,
                                             List<(double t, double w)> wheelW, double from, double to)
        {
            Console.WriteLine("DYNAMIKA ROTACE - odezva na prikaz omega (model: mrtva doba tau + 1. rad T + zesileni K):");
            var cmd = ticks.Where(t => !t.Stop).OrderBy(t => t.T).ToList();
            if (cmd.Count < 50) { Console.WriteLine("  malo taktu"); return; }
            double[] ct = cmd.Select(t => t.T).ToArray();
            double[] cw = cmd.Select(t => t.CmdRot).ToArray();
            double[] cv = cmd.Select(t => t.Cmd).ToArray();

            foreach (var (name, series) in new[] { ("gyro VN100", gyro), ("kola (R-L)/rozchod", wheelW), })
            {
                var ser = series.Where(x => x.t >= Math.Max(from, ct[0] + 1) && x.t <= Math.Min(to, ct[^1])).ToList();
                if (ser.Count < 200) { Console.WriteLine($"  {name}: malo vzorku"); continue; }
                // Useky: jen tam, kde se jede (prikaz v > 0,2) a mezi takty neni dira.
                var ok = new bool[ser.Count];
                for (int i = 0; i < ser.Count; i++)
                {
                    int k = Hold(ct, ser[i].t);
                    ok[i] = k >= 0 && cv[k] > 0.2 && ser[i].t - ct[k] < 0.15;
                }

                double best = double.MaxValue, bTau = 0, bT = 0, bK = 0, r2 = 0;
                double vy = 0; int ny = 0; double my = 0;
                for (int i = 0; i < ser.Count; i++) if (ok[i]) { my += ser[i].w; ny++; }
                my /= Math.Max(1, ny);
                for (int i = 0; i < ser.Count; i++) if (ok[i]) vy += (ser[i].w - my) * (ser[i].w - my);

                var f = new double[ser.Count];
                for (double tau = 0; tau <= 0.5001; tau += 0.01)
                    for (double T = 0.01; T <= 0.6001; T += 0.01)
                    {
                        // odezva jednotkoveho zesileni
                        double y = 0;
                        for (int i = 0; i < ser.Count; i++)
                        {
                            double dt = i == 0 ? 0 : Math.Min(0.1, ser[i].t - ser[i - 1].t);
                            int k = Hold(ct, ser[i].t - tau);
                            double u = k >= 0 ? cw[k] : 0;
                            y += (u - y) * (1 - Math.Exp(-dt / T));
                            f[i] = y;
                        }
                        double fy = 0, ff = 0;
                        for (int i = 0; i < ser.Count; i++) if (ok[i]) { fy += f[i] * ser[i].w; ff += f[i] * f[i]; }
                        if (ff <= 0) continue;
                        double K = fy / ff, e = 0;
                        for (int i = 0; i < ser.Count; i++) if (ok[i]) { double d = ser[i].w - K * f[i]; e += d * d; }
                        if (e < best) { best = e; bTau = tau; bT = T; bK = K; r2 = 1 - e / vy; }
                    }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-20} n={1,6}  tau={2:F2} s  T={3:F2} s  K={4:F2}  R2={5:F2}", name, ny, bTau, bT, bK, r2));
            }
            Console.WriteLine("  tau = mrtva doba (prikaz -> zacatek odezvy), T = casova konstanta, K = skutecna/prikazana omega.");
            Console.WriteLine("  Rele se zpozdenim kmita s periodou ~4*(tau+T); zmena znamenka 2x za periodu.");
        }

        /// <summary>Index posledniho taktu s casem &lt;= t (prikaz se drzi do dalsiho taktu); -1 pred prvnim.</summary>
        private static int Hold(double[] ct, double t)
        {
            int i = Array.BinarySearch(ct, t);
            if (i >= 0) return i;
            return ~i - 1;
        }

        private static double Sd(List<double> v)
        {
            double m = v.Average();
            return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / Math.Max(1, v.Count - 1));
        }

        private static double Med(IEnumerable<double> v)
        {
            var s = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToArray();
            return s.Length == 0 ? double.NaN : s[s.Length / 2];
        }

        private static double? ConfigValue(RecordFile rec, string prefix)
        {
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "Info") continue;
                if (!(rec.Read(e) is Info info)) continue;
                string s = (info.Message ?? string.Empty).Trim();
                if (!s.StartsWith(prefix)) continue;
                string rest = s.Substring(prefix.Length).Trim();
                int sp = rest.IndexOf(' ');
                if (sp > 0) rest = rest.Substring(0, sp);
                if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return v;
            }
            return null;
        }
    }
}
