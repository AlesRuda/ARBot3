using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;
using ARBot.Common.Vision;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// Testy ridici smycky <see cref="ControlLoop"/>: na taktu vzorkuje fuzi, vola
    /// <c>motor.Drive</c> (dif = RotationSpeed * Rozchod) a emituje RobotStateMsg + DriveCommandMsg.
    /// </summary>
    public class ControlLoopTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Motory zaznamenavajici volani Drive (test double).</summary>
        private sealed class SpyMotors : IMotorControl
        {
            public int DriveCount;
            public double LastForvard, LastDif;
            public string Name => "Spy";
            public bool IsError => false;
            public void Drive(double forvard, double dif)
            {
                DriveCount++;
                LastForvard = forvard;
                LastDif = dif;
            }
            public void SetAcceleration(double a) { }
            public IMotorState GetLastMeasurement() => new MotorStateBase(false, 0, 0, 0, 0, 0);
            public event EventHandler<IMotorState> MeasurementArived { add { } remove { } }
        }

        [Test]
        public void OnTick_CallsDrive_AndEmitsDerivedMessages()
        {
            var mapper = new DefaultMeasurementMapper();
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var ts = TimeSpan.FromMilliseconds(20);

            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler, period: ts);
            // Naplanovana draha (0,0)->(3,2): smycka jede path controller.
            var profile = new TrapezoidMotionProfile(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed,
                                                     Profile.MaxAcceleration, Profile.Rozchod);
            loop.Regulator = new PathPlanner(profile).Plan(new[]
            {
                new RegulatorWayPoint { X = 0, Y = 0 },
                new RegulatorWayPoint { X = 3, Y = 2 },
            });

            var msgs = new List<Message>();
            var collector = new DelegateTarget(m => { lock (msgs) msgs.Add(m); });
            collector.Start();

            using (loop.Output.Connect(collector))
            {
                // "feed IMU": mereni z nekolika IMU do fuze + takty na mrizce jejich casu
                for (int i = 0; i < 5; i++)
                {
                    var imu = TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: i * 0.02, omega: 0.1);
                    foreach (var m in mapper.ToMeasurements(imu))
                        engine.Enqueue(m);
                    scheduler.PumpDue(imu.TimeStamp);
                }
            }
            loop.Stop();
            collector.Stop();

            // Drive byl volan (jednou na kazdy takt).
            Assert.That(motor.DriveCount, Is.GreaterThan(0), "Drive nebyl volan");

            List<RobotStateMsg> states;
            List<DriveCommandMsg> cmds;
            lock (msgs)
            {
                states = msgs.FindAll(m => m is RobotStateMsg).ConvertAll(m => (RobotStateMsg)m);
                cmds = msgs.FindAll(m => m is DriveCommandMsg).ConvertAll(m => (DriveCommandMsg)m);
            }

            // Emituje oba typy, stejny pocet jako pocet taktu.
            Assert.That(states.Count, Is.EqualTo(motor.DriveCount), "pocet RobotStateMsg != pocet taktu");
            Assert.That(cmds.Count, Is.EqualTo(motor.DriveCount), "pocet DriveCommandMsg != pocet taktu");

            // Posledni prikaz: dif = RotationSpeed * Rozchod; Forvard = Speed.
            var last = cmds[^1];
            Assert.That(last.Dif, Is.EqualTo(last.RotationSpeed * Profile.Rozchod).Within(1e-12));
            Assert.That(last.Forvard, Is.EqualTo(last.Speed).Within(1e-12));

            // Argumenty poslani do motoru odpovidaji poslednimu prikazu.
            Assert.That(motor.LastDif, Is.EqualTo(last.Dif).Within(1e-12));
            Assert.That(motor.LastForvard, Is.EqualTo(last.Forvard).Within(1e-12));
        }

        /// <summary>Pull kamer (test double): vrati na kazdem pullu predpripravene snimky.</summary>
        private sealed class FakeCameraPull : ICameraPullSource
        {
            public readonly List<CameraFrame> ToReturn = new List<CameraFrame>();
            public int PullCount;
            public IReadOnlyList<CameraFrame> PullLatest()
            {
                PullCount++;
                // Snimky se pri pullu "vyzvednou" (jako GetLastMeasurement): dalsi pull uz vraci prazdno.
                var snap = new List<CameraFrame>(ToReturn);
                ToReturn.Clear();
                return snap;
            }
        }

        [Test]
        public void OnTick_PullsCameras_AndForwardsFrameToOutput()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var ts = TimeSpan.FromMilliseconds(20);

            var pull = new FakeCameraPull();
            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler, period: ts, cameras: pull);

            var frames = new List<CameraFrame>();
            var collector = new DelegateTarget(m => { if (m is CameraFrame f) lock (frames) frames.Add(f); });
            collector.Start();

            using (loop.Output.Connect(collector))
            {
                // Pripravime jeden snimek s gridem a napumpujeme jeden takt -> snimek se forwardne.
                var cam = new CameraFrame
                {
                    Name = "Left",
                    TimeStamp = T0,
                    Grid = new PolarTraversabilityGrid
                    {
                        AzimuthCount = 1,
                        ColumnsPerCell = 1,
                        RadialEdges = new[] { new RadialEdge(0f, 0), new RadialEdge(1f, 1) },
                        Cells = new[] { new PolarCell { Count = 1, Class = TraversabilityClass.Free } },
                    }
                };
                pull.ToReturn.Add(cam);
                scheduler.PumpDue(T0);           // takt s dostupnym snimkem
                scheduler.PumpDue(T0.AddMilliseconds(20));   // dalsi takt, uz bez noveho snimku
            }
            loop.Stop();
            collector.Stop();

            Assert.That(pull.PullCount, Is.GreaterThanOrEqualTo(2), "PullLatest nebyl volan na kazdem tiku");

            List<CameraFrame> got;
            lock (frames) got = new List<CameraFrame>(frames);
            Assert.That(got.Count, Is.EqualTo(1), "forwardnut ma byt prave jeden snimek");
            Assert.That(got[0].Name, Is.EqualTo("Left"));
            Assert.That(got[0].Grid, Is.Not.Null, "forwardnut ma byt cely ramec vcetne gridu");
        }

        [Test]
        public void NoPath_StandsStill()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var ts = TimeSpan.FromMilliseconds(20);
            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler, period: ts);
            // Path zamerne NEnastaven -> bezpecny stav (stani).

            for (int i = 0; i < 5; i++)
                scheduler.PumpDue(T0.AddMilliseconds(i * 20));
            loop.Stop();

            Assert.That(motor.DriveCount, Is.GreaterThan(0), "Drive se vola i bez drahy");
            Assert.That(motor.LastForvard, Is.EqualTo(0.0), "bez drahy stoji (forvard=0)");
            Assert.That(motor.LastDif, Is.EqualTo(0.0), "bez drahy stoji (dif=0)");
        }

        [Test]
        public void Watchdog_StalePath_RampsForwardDown()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var ts = TimeSpan.FromMilliseconds(100);
            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler,
                                       period: ts, pathTimeout: TimeSpan.FromMilliseconds(250));
            var profile = new TrapezoidMotionProfile(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed,
                                                     Profile.MaxAcceleration, Profile.Rozchod);
            loop.Regulator = new PathPlanner(profile).Plan(new[]
            {
                new RegulatorWayPoint { X = 0, Y = 0 },
                new RegulatorWayPoint { X = 5, Y = 0 },
            });

            var cmds = new List<DriveCommandMsg>();
            var collector = new DelegateTarget(m => { if (m is DriveCommandMsg c) lock (cmds) cmds.Add(c); });
            collector.Start();
            using (loop.Output.Connect(collector))
            {
                // t=0..600ms; draha se neobnovuje -> po 250ms zastarala -> dobrzdeni.
                for (int i = 0; i <= 6; i++)
                    scheduler.PumpDue(T0.AddMilliseconds(i * 100));
            }
            loop.Stop();
            collector.Stop();

            List<DriveCommandMsg> got;
            lock (cmds) got = new List<DriveCommandMsg>(cmds);
            Assert.That(got.Count, Is.GreaterThan(3));
            double peak = 0;
            foreach (var c in got) peak = Math.Max(peak, c.Forvard);
            Assert.That(peak, Is.GreaterThan(0), "pred zastaranim robot jel");
            Assert.That(got[^1].Forvard, Is.LessThan(peak), "po zastarani drahy dobrzduje");
        }
    }
}
