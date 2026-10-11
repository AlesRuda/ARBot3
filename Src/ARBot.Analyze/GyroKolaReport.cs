using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Runtime;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Vaha gyra a kol v uhlovem kanalu fuze</b> (prikaz <c>gyrokola</c>, tema
    /// <c>lok-fuze-rucni-otoceni</c>). Prehraje fuzi ze zaznamu (IMU, GPS, motory, depo mise;
    /// koridor NE) a meri, kolik z rozporu kol a gyra projde do fuzni <c>ω</c> a do jejiho integralu
    /// (odometricky kurz <c>OdomTheta</c>, ten pouziva <c>localframe=odom</c>):
    /// <list type="number">
    /// <item><b>Shoda prehrani se zaznamem</b> (<c>ω</c>, <c>θ</c>, <c>OdomTheta</c>).</item>
    /// <item><b>Mechanismus</b>: dvoustavovy filtr (θ, ω) jen s mericimi rychlosti, se stejnym
    /// <c>Q</c> a <c>R</c> a s casy mereni ze zaznamu - zisky po <c>Odo/rate</c> a <c>IMU/gyro</c>
    /// a casove vazeny podil kol na <c>ω</c>.</item>
    /// <item><b>Podil z protifaktu</b>: kolum (resp. gyru) se podstrci stály posun <c>--delta</c>
    /// a meri se, kolik z nej skonci v integralu <c>ω</c> celeho enginu (vcetne kompasu a GPS).</item>
    /// <item><b>Epizody otaceni</b> ze zaznamu: ∫gyro, ∫kola, ∫fuze a z toho primo zmereny podil kol
    /// <c>λ = (∫fuze − ∫gyro)/(∫kola − ∫gyro)</c>; u jizdy i pomer kola/gyro (smyk podvozku).</item>
    /// <item><b>Varianty</b> <c>SigmaAngAccel</c> a <c>OdoRateStd</c>: podil, sledovani gyra, zpozdeni
    /// kurzu v zadanych oknech.</item>
    /// </list>
    /// </summary>
    public static class GyroKolaReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private const double Deg = 180.0 / Math.PI;

        /// <summary>Stav fuze v case RobotStateMsg (tam, kde se na nej ptal runtime).</summary>
        private sealed class Bod
        {
            public DateTime T;
            public double W, Th, OTh, V;
        }

        private sealed class Beh
        {
            public double Sigma, OdoStd, DOdo, DGyro;
            public List<Bod> Body = new List<Bod>();
            public List<(DateTime T, double Z)> Gyro = new List<(DateTime, double)>();
            public List<(DateTime T, double Z)> Odo = new List<(DateTime, double)>();
            public List<(DateTime T, double Z)> OdoV = new List<(DateTime, double)>();
            public Dictionary<string, int> Zdroje = new Dictionary<string, int>();
        }

        private sealed class Okno
        {
            public DateTime Od, Do;
            public bool Zadane;
        }

        public static void Run(RecordFile rec, double delta, string okna, string setText,
                               string sigmy, string odostdy, int vypis)
        {
            var c = FusionReplayReport.ReadConfig(rec, out string version);
            if (c.Count == 0)
            {
                Console.WriteLine("gyrokola: v logu zaznamu neni blok ucinne konfigurace - neni podle ceho skladat fuzi.");
                return;
            }
            Console.WriteLine(version ?? "(verze binarky v logu neni)");
            if (!string.IsNullOrWhiteSpace(setText))
            {
                foreach (var kv in setText.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) { Console.WriteLine($"gyrokola: --set '{kv}' neni klic=hodnota."); return; }
                    c[kv.Substring(0, eq).Trim()] = (kv.Substring(eq + 1).Trim(), "--set");
                }
                Console.WriteLine($"POZOR: konfigurace prepsana --set ({setText}).");
            }

            // Pocatek z MapMsg (jako fusionreplay); bez ni si ho mapper vezme z prvniho fixu.
            MapMsg mapMsg = null;
            foreach (var e in rec.Index) if (e.MsgName == "Map") { mapMsg = rec.Read(e) as MapMsg; break; }
            GeoReference origin = mapMsg?.BuildOrigin();

            var wanted = new HashSet<string> { "IMUState", "GPSState", "MotorStateBase", "RobotStateMsg", "MissionMsg" };
            var msgs = new List<Message>();
            foreach (var e in rec.Index)
            {
                if (!wanted.Contains(e.MsgName)) continue;
                var m = rec.Read(e);
                if (m != null) msgs.Add(m);
            }
            var zaznam = msgs.OfType<RobotStateMsg>().ToList();
            if (zaznam.Count < 10) { Console.WriteLine("gyrokola: v zaznamu skoro neni RobotStateMsg."); return; }

            var f0 = FusionReplayReport.BuildFusionConfig(c, origin);
            Console.WriteLine(string.Format(Ci,
                "zprav {0}, RobotStateMsg {1}; SigmaAngAccel {2} rad/s^2, GyroRateStd {3} rad/s, OdoRateStd {4} rad/s, " +
                "rozchod {5} m, OdoOmegaSign {6}; posun --delta {7} rad/s ({8:F2} deg/s)",
                msgs.Count, zaznam.Count, f0.SigmaAngAccel, f0.GyroRateStd, f0.OdoRateStd, f0.WheelBase,
                f0.OdoOmegaSign, delta, delta * Deg));
            Console.WriteLine("Koridor se NEPREHRAVA (jeho korekce kurzu w skoro nemeni, th ano).");
            Console.WriteLine();

            // Varianty: zakladni + osa SigmaAngAccel + osa OdoRateStd.
            var varianty = new List<(double S, double O)> { (f0.SigmaAngAccel, f0.OdoRateStd) };
            foreach (var s in Cisla(sigmy, "2,1,0.5,0.25")) if (!varianty.Contains((s, f0.OdoRateStd))) varianty.Add((s, f0.OdoRateStd));
            foreach (var o in Cisla(odostdy, "0.1,0.3,1")) if (!varianty.Contains((f0.SigmaAngAccel, o))) varianty.Add((f0.SigmaAngAccel, o));

            var ulohy = new List<(int Var, int Druh)>();   // druh 0 = beze zmeny, 1 = +delta kola, 2 = +delta gyro
            for (int i = 0; i < varianty.Count; i++) for (int d = 0; d < 3; d++) ulohy.Add((i, d));
            var behy = new Beh[varianty.Count, 3];
            Parallel.ForEach(ulohy, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, u =>
            {
                var (s, o) = varianty[u.Var];
                behy[u.Var, u.Druh] = Prehraj(msgs, c, origin, s, o,
                                             u.Druh == 1 ? delta : 0, u.Druh == 2 ? delta : 0,
                                             sbirat: u.Var == 0 && u.Druh == 0);
            });
            var zakl = behy[0, 0];
            zakl.Gyro.Sort((a, b) => a.T.CompareTo(b.T));
            zakl.Odo.Sort((a, b) => a.T.CompareTo(b.T));
            zakl.OdoV.Sort((a, b) => a.T.CompareTo(b.T));

            Console.WriteLine("zdroje mereni: " + string.Join(", ", zakl.Zdroje.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            Console.WriteLine();

            Shoda(zaznam, zakl);
            Mechanismus(zakl, varianty, f0.GyroRateStd);
            Rezidua(zakl);
            var epizody = Epizody(zakl, okna);
            Podil(behy, varianty, delta, zakl);
            Otaceni(epizody, zaznam, behy, varianty, zakl, vypis);
        }

        // ------------------------------------------------------------------ prehrani

        private static Beh Prehraj(List<Message> msgs, Dictionary<string, (string Value, string Origin)> c,
                                   GeoReference origin, double sigma, double odoStd, double dOdo, double dGyro, bool sbirat)
        {
            var f = FusionReplayReport.BuildFusionConfig(c, origin);
            f.SigmaAngAccel = sigma;
            f.OdoRateStd = odoStd;
            var engine = new AsyncFusionEngine(new EKFModel(f));
            var mapper = new DefaultMeasurementMapper(f, engine);
            var b = new Beh { Sigma = sigma, OdoStd = odoStd, DOdo = dOdo, DGyro = dGyro };
            bool depotSeen = false;
            foreach (var m in msgs)
            {
                switch (m)
                {
                    case SensorStateBase s:
                        foreach (var meas in mapper.ToMeasurements(s))
                        {
                            var x = meas;
                            if (meas.Source == "Odo/rate")
                            {
                                if (sbirat) b.Odo.Add((meas.TimeStamp, meas.Value[0]));
                                if (dOdo != 0) x = Posun(meas, dOdo);
                            }
                            else if (meas.Source == "IMU/gyro")
                            {
                                if (sbirat) b.Gyro.Add((meas.TimeStamp, meas.Value[0]));
                                if (dGyro != 0) x = Posun(meas, dGyro);
                            }
                            else if (meas.Source == "Odo/speed" && sbirat)
                                b.OdoV.Add((meas.TimeStamp, meas.Value[0]));
                            if (sbirat) { b.Zdroje.TryGetValue(meas.Source, out int n); b.Zdroje[meas.Source] = n + 1; }
                            engine.Enqueue(x);
                        }
                        break;
                    case RobotStateMsg r:
                        var st = engine.GetStateAt(r.TimeStamp);
                        if (st != null)
                            b.Body.Add(new Bod { T = r.TimeStamp, W = st.Omega, Th = st.Theta, OTh = st.OdomTheta, V = st.V });
                        break;
                    case MissionMsg mm when origin != null:
                        // Jako fusionreplay: RobotourMission.ArmingAtDepot inicializuje polohu v depu.
                        if (mm.HasDepot && !depotSeen)
                        {
                            depotSeen = true;
                            var p = origin.ToLocal(LLA.FromDegrees(mm.DepotLatDeg, mm.DepotLonDeg));
                            engine.InitializePosition(p.X, p.Y, Math.Max(mm.HasFixInfo ? mm.FixSpreadM : 0, 0.3), mm.TimeStamp);
                        }
                        else if (!mm.HasDepot) depotSeen = false;
                        break;
                }
            }
            return b;
        }

        private static IMeasurement Posun(IMeasurement m, double d)
            => ScalarStateMeasurement.AngularRate(m.Value[0] + d, Math.Sqrt(m.NoiseCovariance[0, 0]), m.TimeStamp, m.Source);

        // ------------------------------------------------------------------ 1) shoda

        private static void Shoda(List<RobotStateMsg> zaznam, Beh zakl)
        {
            Console.WriteLine("1) SHODA PREHRANI SE ZAZNAMEM (v case RobotStateMsg)");
            var podleT = new Dictionary<DateTime, Bod>();
            foreach (var p in zakl.Body) podleT[p.T] = p;
            var dW = new Stats("|w zaznam - prehrani| [deg/s]");
            var dTh = new Stats("|th zaznam - prehrani| [deg]");
            bool maOdom = zaznam.Any(r => r.OdomTheta != 0);
            var dOTh = new Stats("|d OdomTheta za 10 s, zaznam - prehrani| [deg]");
            RobotStateMsg r0 = null; Bod p0 = null;
            foreach (var r in zaznam)
            {
                if (!podleT.TryGetValue(r.TimeStamp, out var p)) continue;
                dW.Add(Math.Abs(r.Omega - p.W) * Deg);
                dTh.Add(Math.Abs(Conversions.NormalizeOrientation(r.Theta - p.Th)) * Deg);
                if (maOdom)
                {
                    if (r0 == null) { r0 = r; p0 = p; continue; }
                    if ((r.TimeStamp - r0.TimeStamp).TotalSeconds >= 10)
                    {
                        double a = Conversions.NormalizeOrientation(r.OdomTheta - r0.OdomTheta);
                        double b = Conversions.NormalizeOrientation(p.OTh - p0.OTh);
                        dOTh.Add(Math.Abs(Conversions.NormalizeOrientation(a - b)) * Deg);
                        r0 = r; p0 = p;
                    }
                }
            }
            Console.WriteLine("   " + dW.Line(" deg/s"));
            Console.WriteLine("   " + dTh.Line(" deg") + "   (koridor se neprehrava - u jizd s corridorsend=true se th lisit smi)");
            if (maOdom) Console.WriteLine("   " + dOTh.Line(" deg"));
            else Console.WriteLine("   OdomTheta v zaznamu neni (RobotStateMsg pred verzi 2) - Sw ze zaznamu jen z w po 0,1 s");
            Console.WriteLine();
        }

        // ------------------------------------------------------------------ 2) mechanismus

        /// <summary>
        /// Dvoustavovy filtr (θ, ω) jen s <c>Odo/rate</c> a <c>IMU/gyro</c>, casy ze zaznamu, Q a R jako
        /// <see cref="EKFModel"/>. Sleduje, jakou vahou je v posteriorni ω merenie kol (a) a gyra (b):
        /// predikce je nemeni (ω' = ω), update je zprumeruje se ziskem K. Podil kol na ∫ω je casove
        /// vazene a - presne to, co integruje <see cref="OdomPose"/> (posteriorni ω drzena do dalsiho uzlu).
        /// </summary>
        private static (double Podil, double Kodo, double Kgyro, double PosledniKola) Mini(List<(DateTime T, double Z)> gyro,
            List<(DateTime T, double Z)> odo, double sigma, double gyroStd, double odoStd)
        {
            var vse = new List<(DateTime T, bool Kola)>(gyro.Count + odo.Count);
            vse.AddRange(gyro.Select(g => (g.T, false)));
            vse.AddRange(odo.Select(o => (o.T, true)));
            vse.Sort((x, y) => x.T.CompareTo(y.T));
            double s2 = sigma * sigma, rG = gyroStd * gyroStd, rO = odoStd * odoStd;
            double p00 = 1, p01 = 0, p11 = 1;
            double a = 0, b = 0, sumA = 0, sumT = 0, sumKola = 0;
            bool posledniKola = false;
            var kO = new Stats("K kola"); var kG = new Stats("K gyro");
            DateTime tPrev = default;
            foreach (var (t, kola) in vse)
            {
                if (tPrev != default)
                {
                    double dt = (t - tPrev).TotalSeconds;
                    if (dt > 0 && dt < 0.5)
                    {
                        sumA += a * dt; sumT += dt;
                        if (posledniKola) sumKola += dt;
                    }
                    if (dt > 0)
                    {
                        // P = F P Fᵀ + Q, F = [[1, dt], [0, 1]]
                        double n00 = p00 + 2 * dt * p01 + dt * dt * p11;
                        double n01 = p01 + dt * p11;
                        double n11 = p11;
                        p00 = n00 + s2 * dt * dt * dt / 3; p01 = n01 + s2 * dt * dt / 2; p11 = n11 + s2 * dt;
                    }
                }
                tPrev = t;
                double r = kola ? rO : rG;
                double s = p11 + r;
                double k0 = p01 / s, k1 = p11 / s;
                // P = (I − K H) P, H = [0, 1]
                double q00 = p00 - k0 * p01, q01 = p01 - k0 * p11, q11 = p11 - k1 * p11;
                p00 = q00; p01 = q01; p11 = q11;
                a = (1 - k1) * a + (kola ? k1 : 0);
                b = (1 - k1) * b + (kola ? 0 : k1);
                (kola ? kO : kG).Add(k1);
                posledniKola = kola;
            }
            return (sumT > 0 ? sumA / sumT : double.NaN, kO.Median, kG.Median, sumT > 0 ? sumKola / sumT : double.NaN);
        }

        private static void Mechanismus(Beh zakl, List<(double S, double O)> varianty, double gyroStd)
        {
            Console.WriteLine("2) MECHANISMUS: filtr (th, w) jen s merenimi rychlosti, casy ze zaznamu, Q a R jako EKFModel");
            // Kadence a prolozeni.
            double Hz(List<(DateTime T, double Z)> l) => l.Count < 2 ? 0 : (l.Count - 1) / (l[l.Count - 1].T - l[0].T).TotalSeconds;
            var poOdo = new Stats("od Odo/rate k dalsimu IMU/gyro [ms]");
            int gi = 0;
            foreach (var o in zakl.Odo)
            {
                while (gi < zakl.Gyro.Count && zakl.Gyro[gi].T <= o.T) gi++;
                if (gi < zakl.Gyro.Count) poOdo.Add((zakl.Gyro[gi].T - o.T).TotalMilliseconds);
            }
            Console.WriteLine(string.Format(Ci, "   IMU/gyro {0:F1} Hz, Odo/rate {1:F1} Hz; {2}", Hz(zakl.Gyro), Hz(zakl.Odo), poOdo.Line("ms")));
            Console.WriteLine("   SigmaAngAccel  OdoRateStd   K po kolech  K po gyru  cas s posl. merenim kol  podil kol na Sw   (informacni podil)");
            foreach (var (s, o) in varianty)
            {
                var m = Mini(zakl.Gyro, zakl.Odo, s, gyroStd, o);
                // Informacni podil = kdyby ω byla konstanta (Q = 0): vahy umerne frekvenci / R.
                double iO = Hz(zakl.Odo) / (o * o), iG = Hz(zakl.Gyro) / (gyroStd * gyroStd);
                Console.WriteLine(string.Format(Ci, "   {0,13:F2}  {1,10:F2}   {2,11:F3}  {3,9:F3}  {4,23:P1}  {5,15:P1}   ({6:P1})",
                    s, o, m.Kodo, m.Kgyro, m.PosledniKola, m.Podil, iO / (iO + iG)));
            }
            Console.WriteLine("   (podil kol na Sw = casove vazena vaha mereni kol v posteriorni w; tu integruje OdomTheta i predikce th)");
            Console.WriteLine();
        }

        // ------------------------------------------------------------------ 2b) rezidua a skutecne Q

        /// <summary>
        /// Podklad pro poctive <c>OdoRateStd</c> a <c>SigmaAngAccel</c>: rozdil kola − gyro v case
        /// mereni kol (gyro drzene od posledniho vzorku) po tridach pohybu, vypadky gyra a rozptyl
        /// prirustku gyra <c>Var(ω(t+τ) − ω(t))</c> - u nahodne prochazky s hustotou q je to q·τ
        /// (+ 2σ² sumu senzoru, ktery se odhadne ze stani).
        /// </summary>
        private static void Rezidua(Beh zakl)
        {
            Console.WriteLine("2b) PODKLAD PRO SIGMY: kola - gyro v case mereni kol, vypadky gyra, prirustky gyra");
            var g = zakl.Gyro;
            // Otaceni na miste (i rukou) je samostatna trida: reziduum je tam ~ -gyro, ve "stani" by
            // prehlasilo sum kol v klidu (nalez kontroly 10. 10. 2026).
            var tridy = new[] { "stani (|v|<0,05, |gyro|<=0,2)", "otaceni na miste (|v|<0,05)", "jizda rovne", "zatacky (|gyro| > 0,2)", "vse" };
            var r = tridy.Select(_ => new List<double>()).ToArray();
            int gi = 0, vi = 0;
            foreach (var o in zakl.Odo)
            {
                while (gi + 1 < g.Count && g[gi + 1].T <= o.T) gi++;
                if (gi >= g.Count || g[gi].T > o.T || (o.T - g[gi].T).TotalSeconds > 0.02) continue;
                while (vi + 1 < zakl.OdoV.Count && zakl.OdoV[vi + 1].T <= o.T) vi++;
                double v = vi < zakl.OdoV.Count ? zakl.OdoV[vi].Z : 0;
                double d = o.Z - g[gi].Z;
                bool toci = Math.Abs(g[gi].Z) > 0.2;
                int tr = Math.Abs(v) < 0.05 ? (toci ? 1 : 0) : toci ? 3 : 2;
                r[tr].Add(d); r[4].Add(d);
            }
            Console.WriteLine("   trida                          n        sd      1,4826*MAD   p1        p99     [deg/s]");
            for (int k = 0; k < tridy.Length; k++)
            {
                var l = r[k];
                if (l.Count < 2) continue;
                var srt = l.OrderBy(x => x).ToList();
                double med = srt[srt.Count / 2];
                var mad = l.Select(x => Math.Abs(x - med)).OrderBy(x => x).ToList();
                Console.WriteLine(string.Format(Ci, "   {0,-28} {1,7}  {2,8:F2}  {3,10:F2}  {4,8:F2}  {5,8:F2}",
                    tridy[k], l.Count, Sd(l) * Deg, 1.4826 * mad[mad.Count / 2] * Deg,
                    srt[(int)(0.01 * (srt.Count - 1))] * Deg, srt[(int)(0.99 * (srt.Count - 1))] * Deg));
            }

            // Vypadky gyra.
            int nad50 = 0, nad200 = 0; double maxMez = 0;
            for (int k = 1; k < g.Count; k++)
            {
                double dt = (g[k].T - g[k - 1].T).TotalSeconds;
                if (dt > 0.05) nad50++;
                if (dt > 0.2) nad200++;
                maxMez = Math.Max(maxMez, dt);
            }
            Console.WriteLine(string.Format(Ci, "   mezery v IMU/gyro: > 50 ms {0}x, > 200 ms {1}x, nejdelsi {2:F3} s", nad50, nad200, maxMez));

            // Prirustky gyra: ve stani sum senzoru, v jizde q. Stani i jizda se ptaji na OBA konce
            // intervalu (jinak se do stani dostanou rozjezdy a rozbehy otoceni) a deli se SKUTECNYM
            // dt paru (razitka VN100 jsou casy prichodu). Jizda = |v| >= 0,05 na obou koncich, tedy
            // bez otaceni rukou na miste.
            double VAt(DateTime t)
            {
                int k = Dolni(zakl.OdoV, t);
                if (k >= zakl.OdoV.Count) k = zakl.OdoV.Count - 1;
                return k < 0 ? 0 : zakl.OdoV[k].Z;
            }
            Console.WriteLine("   prirustky gyra (w(t+tau) - w(t))^2: stani = sum senzoru, jizda -> q = prumer((d^2 - stani)/dt)  ->  sqrt(q) [rad/s^2]");
            foreach (double tau in new[] { 0.01, 0.05, 0.1, 0.5 })
            {
                var st = new List<double>(); var jz = new List<(double D2, double Dt)>();
                int j = 0;
                for (int k = 0; k < g.Count; k++)
                {
                    var cil = g[k].T.AddSeconds(tau);
                    if (j < k) j = k;
                    while (j < g.Count && g[j].T < cil) j++;
                    if (j >= g.Count) break;
                    double dt = (g[j].T - g[k].T).TotalSeconds;
                    if (dt <= 0 || dt > tau * 1.6 + 0.006) continue;
                    double v0 = VAt(g[k].T), v1 = VAt(g[j].T);
                    double d = g[j].Z - g[k].Z;
                    if (Math.Abs(v0) < 0.02 && Math.Abs(v1) < 0.02 && Math.Abs(g[k].Z) < 0.05 && Math.Abs(g[j].Z) < 0.05) st.Add(d * d);
                    else if (Math.Abs(v0) >= 0.05 && Math.Abs(v1) >= 0.05) jz.Add((d * d, dt));
                }
                double vS = st.Count > 1 ? st.Average() : 0;
                double q = jz.Count > 1 ? jz.Average(x => (x.D2 - vS) / x.Dt) : double.NaN;
                double sdJ = jz.Count > 1 ? Math.Sqrt(jz.Average(x => x.D2)) : double.NaN;
                Console.WriteLine(string.Format(Ci, "     tau {0,5:F2} s: stani sd {1,6:F3} deg/s (n {2}), jizda sd {3,7:F2} deg/s (n {4})  ->  q {5,8:F3}, sqrt(q) {6,6:F2}",
                    tau, Math.Sqrt(vS) * Deg, st.Count, sdJ * Deg, jz.Count, q, Math.Sqrt(Math.Max(0, q))));
            }
            Console.WriteLine("     (q roste-li s kratsim tau, je w spis zakmit nez nahodna prochazka; zda je rychla slozka pohyb tela (Q),");
            Console.WriteLine("      nebo sum gyra za jizdy (R gyra), tahle data nerozlisi - obe cteni ale ubiraji vahu kolum)");
            Console.WriteLine();
        }

        // ------------------------------------------------------------------ 3) podil z protifaktu

        private static void Podil(Beh[,] behy, List<(double S, double O)> varianty, double delta, Beh zakl)
        {
            Console.WriteLine(string.Format(Ci, "3) PODIL Z PROTIFAKTU: kolum / gyru se podstrci +{0} rad/s, kolik skonci v Sw celeho enginu", delta));
            Console.WriteLine("   SigmaAngAccel  OdoRateStd  | kola: vse   stani  zatacky  rovne | gyro: vse | dth fuze p50/p90 pri posunu kol | RMS(w - gyro) | sd w ve stani");
            for (int i = 0; i < varianty.Count; i++)
            {
                var b0 = behy[i, 0];
                var (vse, stani, zat, rov) = Rozpad(b0, behy[i, 1], delta, zakl);
                var (gVse, _, _, _) = Rozpad(b0, behy[i, 2], delta, zakl);
                var dTh = new Stats("dth");
                var pp = new Dictionary<DateTime, Bod>();
                foreach (var p in behy[i, 1].Body) pp[p.T] = p;
                foreach (var p in b0.Body)
                    if (pp.TryGetValue(p.T, out var q)) dTh.Add(Math.Abs(Conversions.NormalizeOrientation(q.Th - p.Th)) * Deg);
                var rms = new List<double>();
                var sdStani = new List<double>();
                int gi = 0;
                foreach (var p in b0.Body)
                {
                    while (gi + 1 < zakl.Gyro.Count && zakl.Gyro[gi + 1].T <= p.T) gi++;
                    if (gi >= zakl.Gyro.Count || zakl.Gyro[gi].T > p.T || (p.T - zakl.Gyro[gi].T).TotalSeconds > 0.05) continue;
                    rms.Add((p.W - zakl.Gyro[gi].Z) * Deg);
                    if (Math.Abs(p.V) < 0.02 && Math.Abs(zakl.Gyro[gi].Z) < 0.02) sdStani.Add(p.W * Deg);
                }
                Console.WriteLine(string.Format(Ci, "   {0,13:F2}  {1,10:F2}  | {2,9:P1} {3,7:P1} {4,8:P1} {5,6:P1} | {6,9:P1} | {7,14:F2} deg / {8:F2} deg            | {9,9:F2} deg/s | {10:F2} deg/s",
                    varianty[i].S, varianty[i].O, vse, stani, zat, rov, gVse, dTh.Median, dTh.Percentile(90),
                    Rms(rms), Sd(sdStani)));
            }
            Console.WriteLine("   (podil = prirustek Sw (OdomTheta) / (posun x cas); zatacky |gyro| > 0,2 rad/s (i na miste), stani |v| < 0,05 m/s;");
            Console.WriteLine("    dth = o kolik se pri trvalem posunu kol odchyli kurz fuze, ktery drzi kompas; RMS a sd proti gyru v case RobotStateMsg)");
            Console.WriteLine();
        }

        private static (double Vse, double Stani, double Zat, double Rov) Rozpad(Beh b0, Beh b1, double delta, Beh zakl)
        {
            var pp = new Dictionary<DateTime, Bod>();
            foreach (var p in b1.Body) pp[p.T] = p;
            double[] sd = new double[4], st = new double[4];
            Bod a0 = null, a1 = null;
            int gi = 0;
            foreach (var p in b0.Body)
            {
                if (!pp.TryGetValue(p.T, out var q)) { a0 = null; continue; }
                if (a0 != null)
                {
                    double dt = (p.T - a0.T).TotalSeconds;
                    if (dt > 0 && dt < 1)
                    {
                        double d = Conversions.NormalizeOrientation((q.OTh - a1.OTh) - (p.OTh - a0.OTh));
                        while (gi + 1 < zakl.Gyro.Count && zakl.Gyro[gi + 1].T <= p.T) gi++;
                        double g = gi < zakl.Gyro.Count ? zakl.Gyro[gi].Z : 0;
                        int tr = Math.Abs(g) > 0.2 ? 2 : Math.Abs(p.V) < 0.05 ? 1 : 3;
                        sd[0] += d; st[0] += delta * dt;
                        sd[tr] += d; st[tr] += delta * dt;
                    }
                }
                a0 = p; a1 = q;
            }
            double R(int k) => st[k] > 0 ? sd[k] / st[k] : double.NaN;
            return (R(0), R(1), R(2), R(3));
        }

        // ------------------------------------------------------------------ 4) epizody otaceni

        private static List<Okno> Epizody(Beh zakl, string okna)
        {
            var vysl = new List<Okno>();
            // Zadana okna (cas dne, "HH:mm:ss.f-HH:mm:ss.f", oddelena carkou).
            if (!string.IsNullOrWhiteSpace(okna) && zakl.Body.Count > 0)
            {
                DateTime den = zakl.Body[0].T.Date;
                foreach (var s in okna.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var casti = s.Split('-');
                    if (casti.Length == 2 && TimeSpan.TryParse(casti[0], Ci, out var a) && TimeSpan.TryParse(casti[1], Ci, out var b))
                        vysl.Add(new Okno { Od = den + a, Do = den + b, Zadane = true });
                    else Console.WriteLine($"gyrokola: okno '{s}' neni HH:mm:ss.f-HH:mm:ss.f");
                }
            }
            // Automaticke: |gyro| > 20 °/s, mezery do 0,5 s se slouci, ∫gyro aspon 10°, okraj 0,3 s.
            const double Prah = 20 / Deg, Mezera = 0.5, Okraj = 0.3, MinUhel = 10 / Deg;
            var g = zakl.Gyro;
            int i = 0;
            while (i < g.Count)
            {
                if (Math.Abs(g[i].Z) <= Prah) { i++; continue; }
                // j = posledni vzorek nad prahem; pokracuje se, dokud dalsi nad prahem prijde do Mezera s.
                int j = i;
                for (int k = i + 1; k < g.Count && (g[k].T - g[j].T).TotalSeconds <= Mezera; k++)
                    if (Math.Abs(g[k].Z) > Prah) j = k;
                double uhel = Integral(g, g[i].T, g[j].T);
                if (Math.Abs(uhel) >= MinUhel)
                    vysl.Add(new Okno { Od = g[i].T.AddSeconds(-Okraj), Do = g[j].T.AddSeconds(Okraj) });
                i = j + 1;
            }
            return vysl;
        }

        /// <summary>∫ z dt nad [t0, t1], hodnota drzena do dalsiho vzorku (tak ji drzi i fuze).</summary>
        private static double Integral(List<(DateTime T, double Z)> l, DateTime t0, DateTime t1)
        {
            int k = Dolni(l, t0);
            if (k > 0) k--;
            double s = 0;
            for (; k < l.Count && l[k].T < t1; k++)
            {
                DateTime a = l[k].T < t0 ? t0 : l[k].T;
                DateTime b = k + 1 < l.Count && l[k + 1].T < t1 ? l[k + 1].T : t1;
                if (b > a) s += l[k].Z * (b - a).TotalSeconds;
            }
            return s;
        }

        private static int Dolni(List<(DateTime T, double Z)> l, DateTime t)
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (l[m].T < t) lo = m + 1; else hi = m; }
            return lo;
        }

        /// <summary>∫ω fuze = prirustek (rozbaleneho) OdomTheta mezi dvema body.</summary>
        private static double IntegralFuze(List<Bod> body, int i0, int j0)
        {
            double s = 0;
            for (int k = i0 + 1; k <= j0; k++) s += Conversions.NormalizeOrientation(body[k].OTh - body[k - 1].OTh);
            return s;
        }

        private static void Otaceni(List<Okno> okna, List<RobotStateMsg> zaznam, Beh[,] behy, List<(double S, double O)> varianty,
                                    Beh zakl, int vypis)
        {
            Console.WriteLine("4) EPIZODY OTACENI (|gyro| > 20 deg/s, Sgyro >= 10 deg) a zadana okna: Sgyro, Skola, Sfuze [deg]");
            Console.WriteLine("   lambda = (Sfuze - Sgyro) / (Skola - Sgyro) = podil kol primo z udalosti (jen pri rozporu >= 3 deg)");
            bool maOdom = zaznam.Any(r => r.OdomTheta != 0);
            var body = zakl.Body;
            var radky = new List<(Okno O, string Typ, double G, double K, double F, double Fz, double L, double V, double[] Fv)>();
            foreach (var o in okna)
            {
                int i0 = Math.Max(0, Index(body, o.Od) - 1);
                int j0 = Math.Min(body.Count - 1, Index(body, o.Do));
                if (j0 <= i0) continue;
                DateTime t0 = body[i0].T, t1 = body[j0].T;
                double G = Integral(zakl.Gyro, t0, t1), K = Integral(zakl.Odo, t0, t1);
                double F = IntegralFuze(body, i0, j0);
                // Totez ze ZAZNAMU (co videl runtime); bez OdomTheta jen ω po 0,1 s.
                double Fz = double.NaN;
                int zi0 = IndexZ(zaznam, t0), zj0 = IndexZ(zaznam, t1);
                if (zj0 > zi0)
                {
                    Fz = 0;
                    for (int k = zi0 + 1; k <= zj0; k++)
                        Fz += maOdom ? Conversions.NormalizeOrientation(zaznam[k].OdomTheta - zaznam[k - 1].OdomTheta)
                                     : zaznam[k - 1].Omega * (zaznam[k].TimeStamp - zaznam[k - 1].TimeStamp).TotalSeconds;
                }
                double v = Integral(zakl.OdoV, t0, t1) / Math.Max(1e-6, (t1 - t0).TotalSeconds);
                double L = Math.Abs(K - G) >= 3 / Deg ? (F - G) / (K - G) : double.NaN;
                // Kola stala = integral i kazdy vzorek kol u nuly; mezera v merenich kol > 0,1 s se
                // oznaci "*" (Integral drzi posledni hodnotu, fuze tam merenie kol nemela).
                double maxKola = 0, maxMezera = 0;
                for (int k = Math.Max(0, Dolni(zakl.Odo, t0) - 1); k < zakl.Odo.Count && zakl.Odo[k].T <= t1; k++)
                {
                    if (zakl.Odo[k].T >= t0) maxKola = Math.Max(maxKola, Math.Abs(zakl.Odo[k].Z));
                    if (k > 0 && zakl.Odo[k].T > t0) maxMezera = Math.Max(maxMezera, (zakl.Odo[k].T - zakl.Odo[k - 1].T).TotalSeconds);
                }
                string typ = o.Zadane ? "zadane"
                           : Math.Abs(K) < 1 / Deg && maxKola < 0.05 ? "kola stala"
                           : (Math.Abs(K) < 0.5 * Math.Abs(G) || Math.Sign(K) != Math.Sign(G)) ? "smyk/proti"
                           : "jizda";
                if (maxMezera > 0.1) typ += "*";
                var fv = new double[varianty.Count];
                for (int vi = 0; vi < varianty.Count; vi++)
                {
                    var bv = behy[vi, 0].Body;
                    int a = Math.Max(0, Index(bv, t0)), b = Math.Min(bv.Count - 1, Index(bv, t1));
                    fv[vi] = b > a ? IntegralFuze(bv, a, b) - G : double.NaN;
                }
                radky.Add((o, typ, G, K, F, Fz, L, v, fv));
            }

            Console.WriteLine($"   epizod {radky.Count(r => !r.O.Zadane)} (jizda {radky.Count(r => r.Typ.StartsWith("jizda"))}, kola stala {radky.Count(r => r.Typ.StartsWith("kola stala"))}, smyk/proti {radky.Count(r => r.Typ.StartsWith("smyk"))}; * = mezera v merenich kol > 0,1 s: {radky.Count(r => r.Typ.EndsWith("*"))}), zadanych oken {radky.Count(r => r.O.Zadane)}");
            Console.WriteLine("   cas           delka  typ         v [m/s]  Sgyro   Skola   Sfuze  Szaznam" + (maOdom ? "" : "~") + "  fuze-gyro      lambda");
            var vyber = radky.Where(r => r.O.Zadane)
                             .Concat(radky.Where(r => !r.O.Zadane).OrderByDescending(r => Math.Abs(r.K - r.G)).Take(vypis))
                             .OrderBy(r => r.O.Od);
            foreach (var r in vyber)
                Console.WriteLine(string.Format(Ci, "   {0:HH:mm:ss.f}  {1,5:F1}  {2,-10}  {3,7:F2}  {4,6:F1}  {5,6:F1}  {6,6:F1}  {7,7:F1}  {8,9:F1}  {9,6:F2}",
                    r.O.Od, (r.O.Do - r.O.Od).TotalSeconds, r.Typ, r.V, r.G * Deg, r.K * Deg, r.F * Deg, r.Fz * Deg, (r.F - r.G) * Deg, r.L));

            // Souhrny.
            var jizda = radky.Where(r => r.Typ == "jizda").ToList();
            var ruka = radky.Where(r => r.Typ == "kola stala" || r.Typ == "smyk/proti").ToList();
            var stala = radky.Where(r => r.Typ == "kola stala").ToList();
            var lS = new Stats("lambda kola stala"); foreach (var r in stala) lS.Add(r.L);
            var pomer = new Stats("Skola / Sgyro v jizde");
            foreach (var r in jizda) pomer.Add(r.K / r.G);
            var lJ = new Stats("lambda jizda"); foreach (var r in jizda) lJ.Add(r.L);
            var lR = new Stats("lambda ruka/smyk"); foreach (var r in ruka) lR.Add(r.L);
            var zR = new Stats("|Sfuze - Sgyro| ruka/smyk [deg]"); foreach (var r in ruka) zR.Add(Math.Abs(r.F - r.G) * Deg);
            Console.WriteLine();
            Console.WriteLine(string.Format(Ci, "   jizda: Skola/Sgyro p10 {0:F3}  p50 {1:F3}  p90 {2:F3} (n {3}) - pomer > 1 = kola hlasi vic otoceni nez telo (smyk podvozku / rozchod)",
                pomer.Percentile(10), pomer.Median, pomer.Percentile(90), pomer.Count));
            Console.WriteLine(string.Format(Ci, "   lambda jizda p50 {0:F3} (p10 {1:F3}, p90 {2:F3}, n {3});  lambda kola stala+smyk p50 {4:F3} (n {5});  |fuze - gyro| tamtez p50 {6:F1} deg, max {7:F1} deg",
                lJ.Median, lJ.Percentile(10), lJ.Percentile(90), lJ.Count, lR.Median, lR.Count, zR.Median, zR.Max));
            Console.WriteLine(string.Format(Ci, "   lambda kola stala p50 {0:F3} (p10 {1:F3}, p90 {2:F3}, n {3})  - cisty podil kol: kolo hlasi nulu, telo se toci",
                lS.Median, lS.Percentile(10), lS.Percentile(90), lS.Count));

            // Varianty v zadanych oknech a v epizodach ruka/smyk.
            Console.WriteLine();
            Console.WriteLine("5) VARIANTY: Sfuze - Sgyro [deg] v zadanych oknech a souhrn pres epizody");
            Console.WriteLine("   SigmaAngAccel  OdoRateStd  " + string.Join("  ", radky.Where(r => r.O.Zadane).Select(r => $"{r.O.Od:HH:mm:ss}")) +
                              "   ruka/smyk sum|.|   jizda sum|.|");
            for (int vi = 0; vi < varianty.Count; vi++)
            {
                var zad = radky.Where(r => r.O.Zadane).Select(r => string.Format(Ci, "{0,8:F1}", r.Fv[vi] * Deg));
                double sR = ruka.Sum(r => double.IsNaN(r.Fv[vi]) ? 0 : Math.Abs(r.Fv[vi])) * Deg;
                double sJ = jizda.Sum(r => double.IsNaN(r.Fv[vi]) ? 0 : Math.Abs(r.Fv[vi])) * Deg;
                Console.WriteLine(string.Format(Ci, "   {0,13:F2}  {1,10:F2}  {2}   {3,14:F1}   {4,10:F1}",
                    varianty[vi].S, varianty[vi].O, string.Join("  ", zad), sR, sJ));
            }
        }

        private static int Index(List<Bod> l, DateTime t)
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (l[m].T < t) lo = m + 1; else hi = m; }
            return lo;
        }

        private static int IndexZ(List<RobotStateMsg> l, DateTime t)
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (l[m].TimeStamp < t) lo = m + 1; else hi = m; }
            return Math.Min(lo, l.Count - 1);
        }

        // ------------------------------------------------------------------ pomocne

        private static IEnumerable<double> Cisla(string text, string vychozi)
        {
            foreach (var s in (string.IsNullOrWhiteSpace(text) ? vychozi : text).Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(s, NumberStyles.Float, Ci, out double d)) yield return d;
        }

        private static double Rms(List<double> s) => s.Count == 0 ? double.NaN : Math.Sqrt(s.Average(x => x * x));

        private static double Sd(List<double> s)
        {
            if (s.Count < 2) return double.NaN;
            double m = s.Average();
            return Math.Sqrt(s.Sum(x => (x - m) * (x - m)) / (s.Count - 1));
        }
    }
}
