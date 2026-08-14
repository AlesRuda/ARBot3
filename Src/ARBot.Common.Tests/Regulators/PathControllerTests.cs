using System;
using System.Collections.Generic;
using ARBot.Common.Fusion;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Regulators
{
    /// <summary>
    /// Simulační testy sledování dráhy (<see cref="PathResult.Control"/>): robot se integruje
    /// z akčních zásahů (rate-limit = zpoždění aktuace) a ověřuje se průjezd waypointy v toleranci,
    /// dojezd/zastavení a absence kmitání. Viz <c>doc/path-following.md</c>.
    /// </summary>
    public class PathControllerTests
    {
        private const double VMax = 0.8;
        private const double WMax = Math.PI / 6;
        private const double Accel = 0.20;
        private const double Rozchod = 0.41;
        private const double Ts = 0.1;              // perioda řízení [s]
        private const int Substeps = 5;             // jemná integrace pózy v rámci tiku

        private static PathPlanner MakePlanner() => new PathPlanner(new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod));

        private static RegulatorWayPoint Wp(double x, double y, double speed = 0, double eps = 0.1)
            => new RegulatorWayPoint { X = x, Y = y, Speed = speed, MaxPositionError = eps };

        private sealed class SimResult
        {
            public bool Finished;
            public double FinalSpeed;
            public double MaxSpeed;
            public double MaxCrossTrack;
            public int OmegaSignChanges;
            public double[] MinDistToWaypoint;
            public int Ticks;
        }

        /// <summary>
        /// Odsimuluje robota řízeného <paramref name="path"/> ze startovní pózy. Zásahy jsou
        /// rate-limitované (accel-limit dopredné i rotační rychlosti), póza se integruje po substepech.
        /// </summary>
        private static SimResult Simulate(PathResult path, RegulatorWayPoint[] wps,
                                          double startX, double startY, double startOrient, int maxTicks = 2000)
        {
            double angAccel = Accel / (Rozchod / 2.0);
            var state = new RobotState { X = startX, Y = startY, Orientation = startOrient };
            double v = 0, w = 0;

            var res = new SimResult { MinDistToWaypoint = new double[wps.Length], MaxCrossTrack = 0 };
            for (int i = 0; i < wps.Length; i++) res.MinDistToWaypoint[i] = double.PositiveInfinity;

            double prevW = 0;
            int tick = 0;
            for (; tick < maxTicks; tick++)
            {
                var r = path.Control(state);

                // Rate-limit zásahů (zpoždění aktuace).
                v += Clamp(r.Speed - v, -Accel * Ts, Accel * Ts);
                w += Clamp(r.RotationSpeed - w, -angAccel * Ts, angAccel * Ts);
                if (v < 0) v = 0;
                v = Math.Min(v, VMax);
                w = Clamp(w, -WMax, WMax);

                // Zápis rychlostí do stavu.
                state.V = v;
                state.Omega = w;

                // Jemná integrace pózy.
                double dt = Ts / Substeps;
                for (int sub = 0; sub < Substeps; sub++)
                {
                    double o = state.Orientation;
                    state.X += v * Math.Cos(o) * dt;
                    state.Y += v * Math.Sin(o) * dt;
                    state.Orientation = o + w * dt;
                }

                // Metriky.
                res.MaxSpeed = Math.Max(res.MaxSpeed, v);
                res.MaxCrossTrack = Math.Max(res.MaxCrossTrack, DistToPath(path, state.X, state.Y));
                for (int i = 0; i < wps.Length; i++)
                {
                    double dx = wps[i].X - state.X, dy = wps[i].Y - state.Y;
                    res.MinDistToWaypoint[i] = Math.Min(res.MinDistToWaypoint[i], Math.Sqrt(dx * dx + dy * dy));
                }
                if (Math.Sign(r.RotationSpeed) != Math.Sign(prevW) && Math.Abs(r.RotationSpeed) > 1e-3)
                    res.OmegaSignChanges++;
                prevW = r.RotationSpeed;

                if (path.IsFinished && v < 0.02) { tick++; break; }
            }

            res.Finished = path.IsFinished;
            res.FinalSpeed = v;
            res.Ticks = tick;
            return res;
        }

        private static double DistToPath(PathResult path, double px, double py)
        {
            double best = double.PositiveInfinity;
            foreach (var sg in path.Segments)
            {
                double t = (px - sg.StartX) * sg.DirX + (py - sg.StartY) * sg.DirY;
                if (t < 0) t = 0; else if (t > sg.Length) t = sg.Length;
                double cx = px - (sg.StartX + t * sg.DirX);
                double cy = py - (sg.StartY + t * sg.DirY);
                best = Math.Min(best, Math.Sqrt(cx * cx + cy * cy));
            }
            return best;
        }

        private static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);

        [Test]
        public void Straight_ReachesEnd_Stops_NoOscillation()
        {
            var wps = new[] { Wp(0, 0), Wp(2, 0, speed: 0) };
            var path = (PathResult)MakePlanner().Plan(wps);
            var r = Simulate(path, wps, 0, 0, 0);

            Assert.That(r.Finished, Is.True, "dojel na konec");
            Assert.That(r.FinalSpeed, Is.LessThan(0.05), "zastavil");
            Assert.That(r.MinDistToWaypoint[1], Is.LessThanOrEqualTo(0.1), "prošel v toleranci cíle");
            Assert.That(r.MaxCrossTrack, Is.LessThan(0.02), "drží přímku");
            Assert.That(r.OmegaSignChanges, Is.LessThanOrEqualTo(2), "nekmitá");
        }

        /// <summary>
        /// Na dlouhém rovném úseku musí robot rozjet na strop, i když startuje s malou odchylkou
        /// kurzu. Hlídá regresi „rychlost uzamčená vazbou na dobu rotace" (viz doc/path-following.md):
        /// dokud se do <c>SpeedLimit</c> posílala vzdálenost k LOOKAHEAD bodu, závisel omezovač na
        /// vlastním výstupu — nízká rychlost → lookahead na podlaze 0,15 m → nízký strop → nízká
        /// rychlost. Robot pak trvale lezl ~0,1 m/s, i když plán povoloval plnou rychlost.
        /// </summary>
        [TestCase(0.0, TestName = "Rovinka_RozjedeSeNaStrop_BezOdchylky")]
        [TestCase(12.0, TestName = "Rovinka_RozjedeSeNaStrop_SOdchylkouKurzu")]
        public void Straight_ReachesFullSpeed(double startHeadingDeg)
        {
            // 20 m rovinky: dost dlouhá, aby se stihlo rozjet i s accelerací 0,2 m/s^2 a zase zastavit.
            var wps = new[] { Wp(0, 0), Wp(20, 0, speed: 0) };
            var path = (PathResult)MakePlanner().Plan(wps);
            var r = Simulate(path, wps, 0, 0, startHeadingDeg * Math.PI / 180.0);

            Assert.Multiple(() =>
            {
                Assert.That(r.MaxSpeed, Is.GreaterThan(0.9 * VMax),
                            $"robot se nerozjel (max {r.MaxSpeed:F2} m/s z povolených {VMax:F2}) - "
                            + "rychlost nejspis znovu drzi vazba na dobu rotace");
                Assert.That(r.Finished, Is.True, "dojel na konec");
                Assert.That(r.FinalSpeed, Is.LessThan(0.05), "zastavil");
                Assert.That(r.MaxCrossTrack, Is.LessThan(0.35), "nevybocil z drahy");
            });
        }

        /// <summary>
        /// Hustá řada kolineárních uzlů (typický výstup A* po string-pullingu) nesmí robota brzdit.
        /// Cíl řízení je uzel dráhy, takže vzdálenost k němu jde při průjezdu k nule — kdyby se
        /// uzel "pod robotem" nepřeskakoval, spadl by strop z <c>SpeedLimit</c> na nulu u KAŽDÉHO
        /// uzlu a robot by popojížděl. Viz doc/path-following.md.
        /// </summary>
        [Test]
        public void ManyCollinearWaypoints_DoesNotStallAtEach()
        {
            var wps = new List<RegulatorWayPoint>();
            for (int i = 0; i <= 20; i++) wps.Add(Wp(i, 0));            // uzel po 1 m
            wps[wps.Count - 1] = Wp(20, 0, speed: 0);                   // na konci zastavit
            var arr = wps.ToArray();

            var path = (PathResult)MakePlanner().Plan(arr);
            var r = Simulate(path, arr, 0, 0, 0);

            Assert.Multiple(() =>
            {
                Assert.That(r.MaxSpeed, Is.GreaterThan(0.9 * VMax),
                            $"robot se nerozjel (max {r.MaxSpeed:F2} m/s) - brzdi nejspis na kazdem uzlu");
                Assert.That(r.Finished, Is.True, "dojel na konec");
                Assert.That(r.MaxCrossTrack, Is.LessThan(0.05), "drzi primku");
            });
        }

        [Test]
        public void Corner90_PassesWaypointsInTolerance_NoOscillation()
        {
            var wps = new[] { Wp(0, 0), Wp(1, 0), Wp(1, 1, speed: 0) };
            var path = (PathResult)MakePlanner().Plan(wps);
            var r = Simulate(path, wps, 0, 0, 0);

            Assert.That(r.Finished, Is.True, "dojel na konec");
            Assert.That(r.FinalSpeed, Is.LessThan(0.05), "zastavil");
            Assert.That(r.MinDistToWaypoint[1], Is.LessThanOrEqualTo(0.1), "prošel v toleranci rohu P1");
            Assert.That(r.MinDistToWaypoint[2], Is.LessThanOrEqualTo(0.1), "prošel v toleranci cíle P2");
            Assert.That(r.OmegaSignChanges, Is.LessThanOrEqualTo(6), "nekmitá");
        }

        [Test]
        public void StartFacingAway_RecoversToPath()
        {
            // Robot startuje natočený o 150° od směru trasy -> musí se otočit a dojet.
            var wps = new[] { Wp(0, 0), Wp(2, 0, speed: 0) };
            var path = (PathResult)MakePlanner().Plan(wps);
            var r = Simulate(path, wps, 0, 0, Math.PI * 5.0 / 6.0);

            Assert.That(r.Finished, Is.True, "dojel na konec i z opačného natočení");
            Assert.That(r.MinDistToWaypoint[1], Is.LessThanOrEqualTo(0.1));
        }

        [Test]
        public void SPath_MultipleCorners_PassesAllWaypoints()
        {
            var wps = new[] { Wp(0, 0), Wp(2, 0), Wp(2, 1), Wp(4, 1), Wp(4, 2, speed: 0) };
            var path = (PathResult)MakePlanner().Plan(wps);
            var r = Simulate(path, wps, 0, 0, 0);

            Assert.That(r.Finished, Is.True);
            Assert.That(r.FinalSpeed, Is.LessThan(0.05));
            for (int i = 1; i < wps.Length; i++)
                Assert.That(r.MinDistToWaypoint[i], Is.LessThanOrEqualTo(0.1), $"waypoint {i} v toleranci");
        }
    }
}
