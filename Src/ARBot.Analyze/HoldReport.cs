using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Drzene zastaveni (<c>StopHold</c>) ze zaznamu</b> — jak robot pod holdem brzdil a jak se
    /// po jeho uvolneni rozjel. Tema <c>lp-drzene-zastaveni-stophold</c>, doc/plan-drive-hold.md.
    ///
    /// <para><b>Co se overuje.</b> Ridici smycka pod holdem srazi doprednou rychlost <b>rampou</b>
    /// <c>Profile.MaxAcceleration · Ts</c> za takt (ne tvrdou nulou) a smer z regulatoru drzi dal;
    /// po uvolneni prikaz vraci regulator a rozjezd ma byt plynuly — rampu ma delat profil pohybu.
    /// Do zaznamu jde <c>DriveCommandMsg.Held</c> (verze 3) v kazdem taktu, takze epizody jsou presne
    /// prechody toho priznaku; duvod drzeni je v logu (<c>StopHold:</c>).</para>
    ///
    /// <para><b>Tiskne</b> pro kazdou epizodu: rychlost pred holdem (prikaz, fuze, kola), brzdeni
    /// (krok prikazu za takt proti rampe, cas a draha do stani prikazu i kol, zpomaleni kol),
    /// a rozjezd po uvolneni (nejvetsi krok prikazu za takt, zrychleni kol, cas na puvodni
    /// rychlost) a casovou osu takt po taktu.</para>
    /// </summary>
    public static class HoldReport
    {
        private struct Tick
        {
            public DateTime T;
            public double Cmd, Rot, V;
            public bool Held, Estop;
        }

        /// <param name="estop">Epizody NOUZOVEHO zastaveni (<c>DriveCommandMsg.EmergencyStop</c>)
        /// misto drzeneho — pro srovnani rozjezdu po uvolneni stopu (obsluha na stanovisti).</param>
        public static void Run(RecordFile rec, double beforeSec = 1.0, double afterSec = 3.0, bool estop = false)
        {
            var ticks = new List<Tick>();
            var wheels = new List<(DateTime T, double V)>();
            var reasons = new List<(DateTime T, string Text)>();
            var plans = new List<DateTime>();                       // casy LocalPlanMsg s drahou
            var gyro = new List<(DateTime T, double V)>();          // IMU omega Z [rad/s]
            RobotStateMsg st = null;

            foreach (var e in rec.Index)
            {
                string n = e.MsgName;
                if (n != "DriveCommandMsg" && n != "RobotStateMsg" && n != "MotorStateBase" && n != "Info"
                    && n != "LocalPlanMsg" && n != "IMUState") continue;
                switch (rec.Read(e))
                {
                    case RobotStateMsg s: st = s; break;
                    case DriveCommandMsg d:
                        ticks.Add(new Tick
                        {
                            T = d.TimeStamp, Cmd = d.Speed, Rot = d.RotationSpeed, Held = d.Held, Estop = d.EmergencyStop,
                            V = st != null && st.TimeStamp == d.TimeStamp ? st.V : double.NaN,
                        });
                        break;
                    case MotorStateBase m when m.HasMeasurement:
                        wheels.Add((m.TimeStamp, 0.5 * (m.LeftWheelSpeed + m.RightWheelSpeed)));
                        break;
                    case LocalPlanMsg lp when lp.WayPoints != null && lp.WayPoints.Length >= 2:
                        plans.Add(lp.TimeStamp);
                        break;
                    case IMUState imu when imu.AngularVelocity.HasValue:
                        gyro.Add((imu.TimeStamp, imu.AngularVelocity.Value.Z));
                        break;
                    case Info i when (i.Message ?? "").Contains("StopHold:"):
                        reasons.Add((i.TimeStamp, i.Message.Trim()));
                        break;
                }
            }
            ticks.Sort((a, b) => a.T.CompareTo(b.T));
            wheels.Sort((a, b) => a.T.CompareTo(b.T));
            plans.Sort();
            gyro.Sort((a, b) => a.T.CompareTo(b.T));

            double ts = Profile.Ts / 1000.0;
            double ramp = Profile.MaxAcceleration * ts;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "DriveCommandMsg {0}, MotorStateBase {1}, hlasek StopHold {2}; rampa brzdeni pod holdem "
                + "MaxAcceleration·Ts = {3:F2}·{4:F2} = {5:F3} m/s za takt",
                ticks.Count, wheels.Count, reasons.Count, Profile.MaxAcceleration, ts, ramp));
            // Konstanty jsou z DNESNIHO kodu, ne z binarky, ktera jela: do 27. 9. 2026 (8ebd41f) byla
            // MaxDecceleration i MaxAcceleration 0,50, tedy rampa 0,05 m/s za takt; od 6. 10. 2026 je jen MaxAcceleration.
            Console.WriteLine("  (konstanty z dnesniho kodu; binarky do 27. 9. 2026 mely 0,50 m/s2, tedy rampu 0,05 m/s za takt)");
            foreach (var r in reasons) Console.WriteLine($"  log {r.T:HH:mm:ss.fff}  {r.Text}");

            // Epizody = souvisle useky Held == true (resp. EmergencyStop == true s --estop).
            Func<Tick, bool> on = estop ? (x => x.Estop) : (x => x.Held);
            var eps = new List<(int From, int To)>();
            for (int i = 0; i < ticks.Count; i++)
            {
                if (!on(ticks[i]) || (i > 0 && on(ticks[i - 1]))) continue;
                int j = i;
                while (j + 1 < ticks.Count && on(ticks[j + 1])) j++;
                eps.Add((i, j));
            }
            Console.WriteLine(estop
                ? $"epizod NOUZOVEHO zastaveni (EmergencyStop v DriveCommandMsg): {eps.Count}"
                : $"epizod drzeneho zastaveni (Held v DriveCommandMsg): {eps.Count}");
            if (ticks.Count > 0 && eps.Count == 0)
                Console.WriteLine("  (zaznam pred verzi 3 DriveCommandMsg priznak nema - pak je Held vzdy false)");

            int k = 0;
            foreach (var (from, to) in eps)
            {
                k++;
                var t0 = ticks[from].T;
                var t1 = ticks[to].T;
                var pre = from > 0 ? ticks[from - 1] : ticks[from];
                double wPre = WheelAt(wheels, pre.T);
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "=== EPIZODA {0}: {1:HH:mm:ss.fff} - {2:HH:mm:ss.fff} ({3:F1} s, {4} taktu){5}",
                    k, t0, t1, (t1 - t0).TotalSeconds, to - from + 1,
                    estop ? ", NOUZOVE ZASTAVENI"
                          : ticks.Skip(from).Take(to - from + 1).Any(x => x.Estop) ? ", pri tom i NOUZOVE ZASTAVENI" : ""));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  pred zastavenim: prikaz {0:F2} m/s, fuze {1:F2} m/s, kola {2:F2} m/s  -> {3}",
                    pre.Cmd, pre.V, wPre, Math.Abs(wPre) > 0.05 || pre.Cmd > 0.05 ? "ROBOT JEL" : "robot stal"));

                // --- Brzdeni: prikaz pod holdem
                double maxStep = 0; int rampTicks = 0, jumps = 0;
                DateTime? cmdZero = null;
                for (int i = from; i <= to; i++)
                {
                    double prev = i > 0 ? ticks[i - 1].Cmd : ticks[i].Cmd;
                    double drop = prev - ticks[i].Cmd;
                    if (drop > maxStep) maxStep = drop;
                    if (drop > 1e-6) rampTicks++;
                    if (drop > ramp * 1.5 + 1e-6) jumps++;
                    if (cmdZero == null && ticks[i].Cmd <= 1e-6) cmdZero = ticks[i].T;
                }
                // Kola: derivace z jednotlivych vzorku (~91 Hz) je zasumena, proto se zpomaleni
                // pocita PRUMERNE - rychlost pred holdem / doba do stani.
                DateTime? wheelStop = null;
                double dist = 0;
                for (int i = 0; i < wheels.Count; i++)
                {
                    if (wheels[i].T < t0) continue;
                    if (wheels[i].T > t1.AddSeconds(2)) break;
                    if (i > 0 && wheels[i - 1].T >= t0 && wheelStop == null)
                    {
                        double dt = (wheels[i].T - wheels[i - 1].T).TotalSeconds;
                        if (dt > 0 && dt < 0.2) dist += Math.Abs(wheels[i].V) * dt;
                    }
                    if (wheelStop == null && Math.Abs(wheels[i].V) < 0.02) wheelStop = wheels[i].T;
                }
                double meanDecel = wheelStop.HasValue && (wheelStop.Value - t0).TotalSeconds > 0
                    ? Math.Abs(wPre) / (wheelStop.Value - t0).TotalSeconds : double.NaN;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  brzdeni prikazu (pod nouzovym stopem je nula hned, ne rampa): nejvetsi pokles za takt {0:F3} m/s (rampa {1:F3}), taktu s poklesem {2}, "
                    + "skoku nad 1,5x rampu {3}; prikaz na nule za {4}",
                    maxStep, ramp, rampTicks, jumps, Fmt(cmdZero, t0)));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  kola: stoji (|v| < 0,02) za {0}, draha od zacatku holdu do stani {1:F2} m, prumerne zpomaleni {2:F2} m/s2",
                    Fmt(wheelStop, t0), dist, meanDecel));
                if (pre.Cmd > 0.05)
                {
                    double ideal = pre.Cmd / ramp * ts;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  ocekavano z rampy: prikaz z {0:F2} m/s na nulu za {1:F2} s, brzdna draha ~{2:F2} m",
                        pre.Cmd, ideal, pre.Cmd * pre.Cmd / (2 * Profile.MaxAcceleration)));
                }

                // --- Rozjezd po uvolneni
                if (to + 1 < ticks.Count)
                {
                    double maxRise = 0; int n = 0;
                    DateTime limit = t1.AddSeconds(afterSec);
                    double target = pre.Cmd;
                    DateTime? reached = null;
                    for (int i = to + 1; i < ticks.Count && ticks[i].T <= limit; i++)
                    {
                        double rise = ticks[i].Cmd - ticks[i - 1].Cmd;
                        if (rise > maxRise) maxRise = rise;
                        n++;
                        if (reached == null && target > 0.05 && ticks[i].Cmd >= 0.9 * target) reached = ticks[i].T;
                    }
                    // Kola: kdy se rozjela (|v| > 0,02) a kdy dosahla 90 % rychlosti pred holdem;
                    // prumerne zrychleni mezi tim.
                    DateTime? wMove = null, w90 = null;
                    foreach (var w in wheels)
                    {
                        if (w.T <= t1 || w.T > limit) continue;
                        if (wMove == null && Math.Abs(w.V) > 0.02) wMove = w.T;
                        if (w90 == null && Math.Abs(wPre) > 0.05 && Math.Abs(w.V) >= 0.9 * Math.Abs(wPre)) w90 = w.T;
                    }
                    double meanAcc = wMove.HasValue && w90.HasValue && w90 > wMove
                        ? 0.9 * Math.Abs(wPre) / (w90.Value - wMove.Value).TotalSeconds : double.NaN;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  rozjezd ({0:F0} s po uvolneni, {1} taktu): nejvetsi narust prikazu za takt {2:F3} m/s "
                        + "(MaxAcceleration·Ts = {3:F3}), 90 % puvodniho prikazu za {4}; kola se rozjela za {5}, "
                        + "90 % puvodni rychlosti za {6}, prumerne zrychleni {7:F2} m/s2",
                        afterSec, n, maxRise, Profile.MaxAcceleration * ts,
                        target > 0.05 ? Fmt(reached, t1) : "- (pred holdem stal)",
                        Fmt(wMove, t1), Math.Abs(wPre) > 0.05 ? Fmt(w90, t1) : "-", meanAcc));
                }

                // --- Casova osa
                // Prvni plan po uvolneni: bez noveho planu je regulator zastaraly (ControlLoop:
                // pathTimeout) a dopredny prikaz jde rampou z NULY, tedy zustava nula.
                int pi = plans.BinarySearch(t1);
                pi = pi < 0 ? ~pi : pi;
                Console.WriteLine(pi < plans.Count
                    ? string.Format(CultureInfo.InvariantCulture, "  prvni plan po uvolneni za {0:F2} s; posledni plan pred holdem {1:F2} s pred jeho zacatkem",
                        (plans[pi] - t1).TotalSeconds,
                        PlanAge(plans, t0))
                    : "  po uvolneni uz zadny plan neprisel");
                Console.WriteLine("    cas[s]  held  prikaz  rot     fuze   kola   gyro  plan[s]");
                for (int i = 0; i < ticks.Count; i++)
                {
                    double rel = (ticks[i].T - t0).TotalSeconds;
                    if (ticks[i].T < t0.AddSeconds(-beforeSec) || ticks[i].T > t1.AddSeconds(afterSec)) continue;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,7:F2}  {1,4}  {2,6:F3} {3,6:F3}  {4,6:F3} {5,6:F3} {6,6:F3} {7,6:F2}",
                        rel, ticks[i].Held ? "H" : (ticks[i].Estop ? "E" : "."), ticks[i].Cmd, ticks[i].Rot,
                        ticks[i].V, WheelAt(wheels, ticks[i].T), WheelAt(gyro, ticks[i].T), PlanAge(plans, ticks[i].T)));
                }
            }
        }

        /// <summary>Stari posledniho planu s drahou v case t [s]; NaN, kdyz zadny nebyl.</summary>
        private static double PlanAge(List<DateTime> plans, DateTime t)
        {
            int i = plans.BinarySearch(t);
            i = i < 0 ? ~i - 1 : i;
            return i >= 0 ? (t - plans[i]).TotalSeconds : double.NaN;
        }

        private static string Fmt(DateTime? t, DateTime from)
            => t.HasValue ? ((t.Value - from).TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s") : "NIKDY (v okne)";

        /// <summary>Rychlost kol nejblize pred casem t (do 0,2 s), jinak NaN.</summary>
        private static double WheelAt(List<(DateTime T, double V)> w, DateTime t)
        {
            int lo = 0, hi = w.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (w[mid].T <= t) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return best >= 0 && (t - w[best].T).TotalSeconds <= 0.2 ? w[best].V : double.NaN;
        }
    }
}
