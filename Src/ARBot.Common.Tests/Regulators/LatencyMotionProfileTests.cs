using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Fusion;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Regulators
{
    /// <summary>
    /// <see cref="LatencyMotionProfile"/>: zákon <c>v = max(v_e, −a·L + √((a·L)² + 2a·x + v_e²))</c>
    /// a jeho chování v uzavřené smyčce proti <see cref="TrapezoidMotionProfile"/>.
    /// Viz doc/path-following.md, „Profil se zpožděním smyčky", a registr
    /// <c>lp-regulator-kmitani-rotace</c>.
    /// </summary>
    public class LatencyMotionProfileTests
    {
        private const double VMax = 1.7;
        private const double WMax = Math.PI / 6;
        private const double A = 0.5;
        private const double Rozchod = 0.41;
        private const double L = 0.4;

        private static LatencyMotionProfile Novy(double latency = L) => new LatencyMotionProfile(VMax, WMax, A, Rozchod, latency);
        private static TrapezoidMotionProfile Stary() => new TrapezoidMotionProfile(VMax, WMax, A, Rozchod);

        // ---------------- zakon ----------------

        [Test]
        public void Dist2Speed_JeUzavrenyTvarZakona()
        {
            var p = Novy();
            foreach (double x in new[] { 0.01, 0.1, 0.5, 1.4, 3.0 })
                foreach (double ve in new[] { 0.0, 0.3 })
                {
                    double exp = Math.Min(VMax, Math.Max(ve, -A * L + Math.Sqrt(A * L * A * L + 2 * A * x + ve * ve)));
                    Assert.That(p.Dist2Speed(x, 0, ve).Speed, Is.EqualTo(exp).Within(1e-12), $"x={x} ve={ve}");
                    Assert.That(p.Dist2Speed(-x, 0, ve).Speed, Is.EqualTo(-exp).Within(1e-12), $"x=-{x} ve={ve}");
                }
        }

        /// <summary>
        /// Podmínka, ze které zákon vznikl: po <c>L</c> jízdy příkazem a následném brzdění <c>a</c> robot
        /// dojede přesně na konec dráhy. Tohle je jeho význam, ne jen vzorec.
        /// </summary>
        [Test]
        public void Dist2Speed_PoZpozdeniABrzdeniDojedePresneNaKonec()
        {
            var p = Novy();
            foreach (double x in new[] { 0.3, 1.0, 1.4, 2.5 })
            {
                double v = p.Dist2Speed(x, 0, 0).Speed;
                Assert.That(v * L + v * v / (2 * A), Is.EqualTo(x).Within(1e-9), $"x={x}");
            }
        }

        /// <summary>U cíle je zesílení konečné, <c>1/L</c> — to je celý rozdíl proti časově optimálnímu
        /// zákonu bez zpoždění, jehož zesílení jde u nuly do nekonečna.</summary>
        [Test]
        public void UCile_JeZesileniJednaLomenoL()
        {
            foreach (double lat in new[] { 0.2, 0.4 })
            {
                var p = Novy(lat);
                double x = 1e-5;
                Assert.That(p.Dist2Speed(x, 0, 0).Speed / x, Is.EqualTo(1 / lat).Within(0.01 / lat), $"L={lat}");
                double b = 1e-6;
                Assert.That(p.Rot2RotSpeed(b, 0, 0).RotationSpeed / b, Is.EqualTo(1 / lat).Within(0.01 / lat), $"rotace L={lat}");
            }
        }

        /// <summary>Robot jedoucí nejvýš <c>v_e</c> brzdit nemusí, takže průjezd uzlem se nezpomalí;
        /// obě větve zákona se potkají v <c>x = v_e·L</c> (spojitě).</summary>
        [Test]
        public void PrujezdUzlem_NezpomalujePodEndSpeed_ASpojiteNavazuje()
        {
            var p = Novy();
            double ve = 0.8;
            Assert.That(p.Dist2Speed(0, 0.8, ve).Speed, Is.EqualTo(ve).Within(1e-12));
            Assert.That(p.Dist2Speed(ve * L * 0.5, 0.8, ve).Speed, Is.EqualTo(ve).Within(1e-12));
            double x0 = ve * L;
            Assert.That(p.Dist2Speed(x0 + 1e-7, 0, ve).Speed, Is.EqualTo(ve).Within(1e-6), "napojeni vetvi");
            Assert.That(p.Dist2Speed(x0 + 0.1, 0, ve).Speed, Is.GreaterThan(ve));
        }

        /// <summary>Žádné schody: dnešní profil skáče po <c>0,9·a·tSam</c> = 0,045 m/s, tenhle roste
        /// ryze monotonně i po milimetrech.</summary>
        [Test]
        public void Prikaz_NeniKvantovany()
        {
            var p = Novy();
            double prev = -1;
            for (double x = 0.001; x < 3; x += 0.001)
            {
                double v = p.Dist2Speed(x, 0, 0).Speed;
                if (v >= VMax) break;
                Assert.That(v, Is.GreaterThan(prev), $"x={x}");
                prev = v;
            }
        }

        /// <summary>
        /// <b>Pevný bod při stálé vzdálenosti mrkve</b> — kvůli němu FreeRun 25. 9. jel 0,855 m/s:
        /// starý profil plánuje trojúhelník z aktuální rychlosti, takže opakované volání se stejnou
        /// vzdáleností konverguje hluboko pod bezpečnou rychlost. Nový na aktuální rychlosti nezávisí.
        /// </summary>
        [Test]
        public void PevnyBod_PriStaleVzdalenostiMrkve()
        {
            double Pevny(IMotionProfile p, double x)
            {
                double v = 0;
                for (int i = 0; i < 300; i++) v = p.Dist2Speed(x, v, 0).Speed;
                return v;
            }
            Assert.That(Pevny(Stary(), 1.4), Is.EqualTo(0.855).Within(1e-9), "naměřený medián FreeRun 25. 9.");
            Assert.That(Pevny(Stary(), 3.0), Is.EqualTo(1.305).Within(1e-9));
            Assert.That(Pevny(Novy(), 1.4), Is.EqualTo(-0.2 + Math.Sqrt(0.04 + 1.4)).Within(1e-9));   // 0,9900
            Assert.That(Pevny(Novy(), 3.0), Is.EqualTo(-0.2 + Math.Sqrt(0.04 + 3.0)).Within(1e-9));   // 1,5436
        }

        /// <summary>
        /// Když robot už zastavit nestihne, příkaz leží POD jeho rychlostí o celou rezervu — motorová
        /// jednotka tedy brzdí naplno. Starý profil v té situaci vracel ~0,6–0,9 aktuální rychlosti
        /// (rotace 1° při 0,5 rad/s k cíli: 0,204 rad/s), takže přestřelení bylo dané.
        /// </summary>
        [Test]
        public void NestihneZastavit_PrikazNeniVazanyNaAktualniRychlost()
        {
            var p = Novy();
            double b = Math.PI / 180;
            double w0 = p.Rot2RotSpeed(b, 0, 0).RotationSpeed;
            Assert.That(p.Rot2RotSpeed(b, 0.5, 0).RotationSpeed, Is.EqualTo(w0).Within(1e-12));
            Assert.That(w0, Is.LessThan(0.05), "1° u cíle = ~0,04 rad/s, ne 0,2");
            Assert.That(Stary().Rot2RotSpeed(b, 0.5, 0).RotationSpeed, Is.GreaterThan(0.2),
                        "charakterizace puvodniho profilu - duvod zmeny");
        }

        /// <summary><c>T_rot</c> nezávisí na měřené ω a u nuly jde k nule — zákmity rotace tak nesrážejí
        /// dopřednou rychlost přes <see cref="IMotionProfile.SpeedLimit"/>.</summary>
        [Test]
        public void DobaRotace_NezavisiNaMereneOmega_AVNuleJeNula()
        {
            var p = Novy();
            Assert.That(p.Rot2RotSpeed(0, 0.3, 0).RegulationTime, Is.EqualTo(0));
            double b = 0.05;
            double t = p.Rot2RotSpeed(b, 0, 0).RegulationTime;
            Assert.That(p.Rot2RotSpeed(b, -0.4, 0).RegulationTime, Is.EqualTo(t));
            Assert.That(p.Rot2RotSpeed(b, 0.4, 0).RegulationTime, Is.EqualTo(t));
            double alpha = A / (Rozchod / 2);
            Assert.That(t, Is.EqualTo(2 * Math.Sqrt(b / alpha)).Within(1e-12), "trojuhelnik z klidu");
        }

        [Test]
        public void Duration_OdpovidaKinematice()
        {
            // trojuhelnik z klidu do klidu: t = 2*sqrt(x/a)
            Assert.That(LatencyMotionProfile.Duration(1.0, 0, 0, 10, 0.5), Is.EqualTo(2 * Math.Sqrt(2.0)).Within(1e-12));
            // lichobeznik: 0 -> 1 m/s (1 m), jizda, 1 -> 0 (1 m) pri a=0,5; x=5 -> 2+2+3 = 7 s
            Assert.That(LatencyMotionProfile.Duration(5.0, 0, 0, 1.0, 0.5), Is.EqualTo(7.0).Within(1e-12));
            // nestihne zpomalit: rovnomerne z 1 na 0 na 0,5 m -> 1 s
            Assert.That(LatencyMotionProfile.Duration(0.5, 1.0, 0, 2, 0.5), Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void NulovaNeboZapornaLatence_JeChyba()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Novy(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Novy(-0.1));
        }

        // ---------------- uzavrena smycka ----------------
        //
        // Model akcniho clenu je NAMERENY (ARBot.Analyze drive, FreeRun 20260925-144658.rec): prikaz
        // rotace -> mrtva doba 0,05 s + 1. rad T = 0,08 s, zesileni 1,1 (gyro i kola shodne);
        // dopredna rychlost: mrtva doba 0,05 s + rampa ridici jednotky 0,5 m/s^2. Takt 10 Hz, regulator
        // je SKUTECNY PathPlanner + PathResult. Simulace s dnesnim profilem reprodukuje namerene
        // kmitani (2,6-3,1 zmeny znamenka omega za sekundu pri mrkvi 1,4 m), coz je jediny duvod, proc
        // ji jde verit i pro novy profil.

        /// <summary>
        /// FreeRun na rovince s mrkví 3 m, s poruchou terénu, šumem kurzu a rozptylem mrkve 5 cm při
        /// přeplánování. Nový profil má kmitat výrazně méně (simulace 25. 9.: 1,83 → 0,20 změny
        /// znaménka ω za sekundu) a jet rychleji (1,305 → 1,54 m/s).
        /// </summary>
        [Test]
        public void FreeRun_Mrkev3m_KmitaMeneAJedeRychleji()
        {
            var stary = FreeRun(Stary(), 3.0);
            var novy = FreeRun(Novy(), 3.0);
            TestContext.WriteLine($"stary: v p50 {stary.v:F3}, zmen znamenka {stary.flips:F2}/s, rms y {stary.y:F3} m");
            TestContext.WriteLine($"novy:  v p50 {novy.v:F3}, zmen znamenka {novy.flips:F2}/s, rms y {novy.y:F3} m");
            Assert.That(stary.flips, Is.GreaterThan(1.0), "simulace musi dnesni kmitani reprodukovat, jinak nic nedokazuje");
            Assert.That(novy.flips, Is.LessThan(0.5));
            Assert.That(novy.flips, Is.LessThan(stary.flips / 4));
            Assert.That(novy.v, Is.GreaterThan(1.45));
            Assert.That(stary.v, Is.LessThan(1.35));
            Assert.That(novy.y, Is.LessThan(0.06), "cena: prictna odchylka smi vzrust, ale v toleranci ~10 cm");
        }

        /// <summary>Dojezd na cíl 5 m z klidu: bez přejetí (s <c>L</c> = 0,1 by přejel o 8 cm).</summary>
        [Test]
        public void Dojezd_NaCil_BezPrejeti()
        {
            var (tFin, overshoot, finalErr) = Stop(Novy());
            Assert.That(tFin, Is.GreaterThan(0), "dojel");
            Assert.That(overshoot, Is.LessThan(0.005));
            Assert.That(finalErr, Is.LessThan(0.1));
            var (_, overshootKratke, _) = Stop(Novy(0.1));
            Assert.That(overshootKratke, Is.GreaterThan(0.03), "charakterizace: L pod skutecnym zpozdenim prejizdi");
        }

        /// <summary>Zatáčka 90° se zastavením: bez kmitu rotace a s menší odchylkou než původní profil.</summary>
        [Test]
        public void Zatacka90_BezKmituAPresneji()
        {
            var stary = Corner(Stary());
            var novy = Corner(Novy());
            TestContext.WriteLine($"stary: odchylka {stary.dev:F3} m, zmen znamenka {stary.flips}");
            TestContext.WriteLine($"novy:  odchylka {novy.dev:F3} m, zmen znamenka {novy.flips}");
            Assert.That(novy.flips, Is.EqualTo(0));
            Assert.That(novy.dev, Is.LessThan(stary.dev));
            Assert.That(novy.tFin, Is.GreaterThan(0));
        }

        private static (double v, double flips, double y) FreeRun(IMotionProfile p, double lc)
        {
            double sv = 0, sf = 0, sy = 0;
            const int runs = 5;
            for (int seed = 1; seed <= runs; seed++)
            {
                var rnd = new Random(seed);
                var planner = new PathPlanner(p, 0.01, 0.3, 0.15);
                var s = new Plant { Y = 0.3 };
                double dist = 0, lastW = 0, sumY2 = 0;
                int flips = 0, n = 0;
                IRegulator reg = null;
                var vs = new List<double>();
                for (double t = 0; t < 70; t += Plant.Dt)
                {
                    dist += (-dist / 0.3 + 0.1 * Math.Sqrt(2 / 0.3) * Gauss(rnd) / Math.Sqrt(Plant.Dt)) * Plant.Dt;
                    if (s.Tick(t))
                    {
                        var st = s.State(0.3 * Math.PI / 180 * Gauss(rnd));
                        if (reg == null || rnd.NextDouble() < 0.85)
                        {
                            double vcap = Math.Min(VMax, Math.Sqrt(2 * A * lc));
                            reg = planner.Plan(new[]
                            {
                                new RegulatorWayPoint { X = st.X, Y = st.Y, Speed = vcap },
                                new RegulatorWayPoint { X = st.X + lc, Y = 0.05 * Gauss(rnd), Speed = 0 },
                            });
                        }
                        var r = reg.Control(st);
                        s.Command(t, r.Speed, r.RotationSpeed);
                        if (t > 10)
                        {
                            n++;
                            if (Math.Sign(r.RotationSpeed) != Math.Sign(lastW) && Math.Abs(r.RotationSpeed) > 0.05 && Math.Abs(lastW) > 0.05) flips++;
                            sumY2 += s.Y * s.Y;
                            vs.Add(s.V);
                        }
                        lastW = r.RotationSpeed;
                    }
                    s.Step(t, dist);
                }
                vs.Sort();
                sv += vs[vs.Count / 2];
                sf += flips / 60.0;
                sy += Math.Sqrt(sumY2 / n);
            }
            return (sv / runs, sf / runs, sy / runs);
        }

        private static (double tFin, double overshoot, double finalErr) Stop(IMotionProfile p)
        {
            var reg = new PathPlanner(p, 0.01, 0.3, 0.15).Plan(new[]
            {
                new RegulatorWayPoint { X = 0, Y = 0 },
                new RegulatorWayPoint { X = 5, Y = 0, Speed = 0 },
            });
            var s = new Plant();
            double tFin = -1, xmax = 0;
            for (double t = 0; t < 40; t += Plant.Dt)
            {
                if (s.Tick(t))
                {
                    var r = reg.Control(s.State(0));
                    s.Command(t, r.Speed, r.RotationSpeed);
                    if (reg.IsFinished && tFin < 0) tFin = t;
                }
                s.Step(t, 0);
                xmax = Math.Max(xmax, s.X);
            }
            return (tFin, Math.Max(0, xmax - 5), Math.Abs(5 - s.X));
        }

        private static (double tFin, double dev, int flips) Corner(IMotionProfile p)
        {
            var reg = new PathPlanner(p, 0.01, 0.3, 0.15).Plan(new[]
            {
                new RegulatorWayPoint { X = 0, Y = 0 },
                new RegulatorWayPoint { X = 4, Y = 0, MaxPositionError = 0.1 },
                new RegulatorWayPoint { X = 4, Y = 4, Speed = 0 },
            });
            var s = new Plant();
            double tFin = -1, dev = 0, lastW = 0;
            int flips = 0;
            for (double t = 0; t < 40; t += Plant.Dt)
            {
                if (s.Tick(t))
                {
                    var r = reg.Control(s.State(0));
                    s.Command(t, r.Speed, r.RotationSpeed);
                    if (Math.Sign(r.RotationSpeed) != Math.Sign(lastW) && Math.Abs(r.RotationSpeed) > 0.05 && Math.Abs(lastW) > 0.05) flips++;
                    if (Math.Abs(r.RotationSpeed) > 0.05) lastW = r.RotationSpeed;
                    if (reg.IsFinished && tFin < 0) tFin = t;
                }
                s.Step(t, 0);
                // vzdalenost od lomene cary (0,0)-(4,0)-(4,4)
                double d1 = s.X <= 4 ? Math.Abs(s.Y) : Math.Sqrt((s.X - 4) * (s.X - 4) + s.Y * s.Y);
                double d2 = s.Y >= 0 ? Math.Abs(s.X - 4) : Math.Sqrt((s.X - 4) * (s.X - 4) + s.Y * s.Y);
                dev = Math.Max(dev, Math.Min(d1, d2));
            }
            return (tFin, dev, flips);
        }

        private static double Gauss(Random r)
        {
            double u1 = 1 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        /// <summary>Naměřený akční člen (viz komentář nad uzavřenou smyčkou).</summary>
        private sealed class Plant
        {
            public const double Dt = 0.001;
            private const double Dead = 0.05, Tw = 0.08, Kw = 1.1, Ramp = 0.5;
            public double X, Y, Th, V, W;
            private double next;
            private double uv, uw;
            private readonly Queue<(double t, double v, double w)> q = new Queue<(double, double, double)>();

            public bool Tick(double t)
            {
                if (t + 1e-9 < next) return false;
                next += 0.1;
                return true;
            }

            public RobotState State(double thNoise) => new RobotState { X = X, Y = Y, Theta = Th + thNoise, V = V, Omega = W };

            public void Command(double t, double v, double w) => q.Enqueue((t + Dead, v, w));

            public void Step(double t, double disturbance)
            {
                while (q.Count > 0 && q.Peek().t <= t) { var c = q.Dequeue(); uv = c.v; uw = c.w; }
                double m = Ramp * Dt;
                V += Math.Clamp(uv - V, -m, m);
                W += (Kw * uw - W) * (1 - Math.Exp(-Dt / Tw));
                Th += (W + disturbance) * Dt;
                X += V * Math.Cos(Th) * Dt;
                Y += V * Math.Sin(Th) * Dt;
            }
        }
    }
}
