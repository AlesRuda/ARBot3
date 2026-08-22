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
            public IMotorState GetLastMeasurement() => new MotorStateBase(false, 0, 0, 0, 0, 0, 0, 0);
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

            // Posledni prikaz: dif = RotationSpeed * Rozchod / 2 (dif je offset na kolo); Forvard = Speed.
            var last = cmds[^1];
            Assert.That(last.Dif, Is.EqualTo(last.RotationSpeed * Profile.Rozchod / 2.0).Within(1e-12));
            Assert.That(last.Forvard, Is.EqualTo(last.Speed).Within(1e-12));

            // Argumenty poslani do motoru odpovidaji poslednimu prikazu.
            Assert.That(motor.LastDif, Is.EqualTo(last.Dif).Within(1e-12));
            Assert.That(motor.LastForvard, Is.EqualTo(last.Forvard).Within(1e-12));
        }

        /// <summary>
        /// Ground truth (22. 8. 2026): kdyz je zdroj nastaveny, smycka emituje
        /// <see cref="GroundTruthMsg"/> ke KAZDEMU <see cref="RobotStateMsg"/> a se STEJNYM casem.
        /// Bez shody casu by rozdil obou zprav nebyl chyba odhadu, ale chyba odhadu plus posun
        /// v case - a cele mereni konvergence by bylo k nicemu. Viz doc/virtual-hw.md.
        /// </summary>
        [Test]
        public void OnTick_WithGroundTruthSource_EmitsPairedTruthMessages()
        {
            var mapper = new DefaultMeasurementMapper();
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();

            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler,
                                       period: TimeSpan.FromMilliseconds(20));

            // Zdroj skutecnosti: staci cokoli, co vrati zpravu s pozadovanym casem.
            int truthCalls = 0;
            loop.GroundTruthAt = t =>
            {
                truthCalls++;
                return new GroundTruthMsg { X = 1.0, Y = 2.0, Theta = 0.5, TimeStamp = t };
            };

            var msgs = new List<Message>();
            var collector = new DelegateTarget(m => { lock (msgs) msgs.Add(m); });
            collector.Start();

            using (loop.Output.Connect(collector))
            {
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

            List<RobotStateMsg> states;
            List<GroundTruthMsg> truths;
            lock (msgs)
            {
                states = msgs.FindAll(m => m is RobotStateMsg).ConvertAll(m => (RobotStateMsg)m);
                truths = msgs.FindAll(m => m is GroundTruthMsg).ConvertAll(m => (GroundTruthMsg)m);
            }

            Assert.That(states, Is.Not.Empty, "predpoklad testu: smycka tikala");
            Assert.That(truthCalls, Is.GreaterThan(0), "zdroj skutecnosti nebyl dotazan");
            Assert.That(truths.Count, Is.EqualTo(states.Count), "ke kazdemu odhadu patri jedna skutecnost");

            for (int i = 0; i < states.Count; i++)
                Assert.That(truths[i].TimeStamp, Is.EqualTo(states[i].TimeStamp),
                            $"takt {i}: skutecnost a odhad musi mit tentyz cas");
        }

        /// <summary>Bez zdroje skutecnosti (realny HW) se zadna zprava navic emitovat nesmi.</summary>
        [Test]
        public void OnTick_WithoutGroundTruthSource_EmitsNothingExtra()
        {
            var mapper = new DefaultMeasurementMapper();
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();

            var loop = new ControlLoop(engine, new SpyMotors(), new VirtualClock(), scheduler,
                                       period: TimeSpan.FromMilliseconds(20));

            var msgs = new List<Message>();
            var collector = new DelegateTarget(m => { lock (msgs) msgs.Add(m); });
            collector.Start();

            using (loop.Output.Connect(collector))
            {
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

            lock (msgs)
            {
                Assert.That(msgs.Exists(m => m is RobotStateMsg), Is.True, "predpoklad testu: smycka tikala");
                Assert.That(msgs.Exists(m => m is GroundTruthMsg), Is.False);
            }
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

        /// <summary>
        /// Prevod uhlove rychlosti na argument <c>Drive</c>: <c>dif = omega * rozchod / 2</c>, protoze
        /// dif je OFFSET NA KOLO (driver ho k jednomu kolu prictе a od druheho odecte), tedy
        /// <c>vR - vL = omega*rozchod = 2*dif</c>. Bez pulky by robot zatacel dvakrat rychleji, nez
        /// regulator chce. Test je tu proto, ze presne tuhle pulku kod driv nemel.
        /// </summary>
        [Test]
        public void RotationSpeed_ToDif_IsHalfWheelBase()
        {
            var (loop, scheduler, motor, cmds, collector, conn) = MakeDrivingLoop();

            var tk = T0.AddMilliseconds(100);
            scheduler.PumpDue(tk);
            var last = CmdAt(cmds, tk);
            conn.Dispose(); loop.Stop(); collector.Stop();

            Assert.That(last.RotationSpeed, Is.EqualTo(0.3).Within(1e-12));
            Assert.That(last.Dif, Is.EqualTo(0.3 * Profile.Rozchod / 2.0).Within(1e-12));
            Assert.That(motor.LastDif, Is.EqualTo(last.Dif).Within(1e-12));
        }

        // ---------------- Nouzove zastaveni (doc/robotour-mission.md) ----------------

        /// <summary>
        /// Stav motoru pro testy nouzoveho zastaveni. Rychlost kol je od verze 2 vlastni pole
        /// zpravy (uz se nedopocitava z prirustku enkoderu a doby od vyzvednuti).
        /// </summary>
        private static MotorStateBase Motor(bool estop, double wheelSpeed)
            => new MotorStateBase(estop, wheelSpeed * 0.1, wheelSpeed * 0.1, 24, 0, 0,
                                  wheelSpeed, wheelSpeed)
            {
                TimeStamp = T0,
            };

        [Test]
        public void EmergencyStop_WhileRolling_ZerosForward_KeepsRotation()
        {
            var (loop, scheduler, motor, cmds, collector, conn) = MakeDrivingLoop();

            // Robot jede (nenulova rychlost kol) a je zmacknute nouzove zastaveni.
            var tk = T0.AddMilliseconds(100);
            Feed(loop, Motor(estop: true, wheelSpeed: 0.5));
            scheduler.PumpDue(tk);

            var last = CmdAt(cmds, tk);
            conn.Dispose(); loop.Stop(); collector.Stop();

            Assert.That(last.EmergencyStop, Is.True, "priznak nouzoveho zastaveni ma byt v zaznamu");
            Assert.That(last.Forvard, Is.EqualTo(0.0), "dopredna rychlost musi byt nulova");
            Assert.That(motor.LastForvard, Is.EqualTo(0.0), "do motoru se posila nula");
            // Dokud se kola toci, zatoceni podle regulatoru zustava - dobrzdeni je rizene.
            Assert.That(last.RotationSpeed, Is.EqualTo(0.3).Within(1e-12), "pri dotaceni rotace zustava");
            Assert.That(motor.LastDif, Is.EqualTo(0.3 * Profile.Rozchod / 2.0).Within(1e-12));
        }

        [Test]
        public void EmergencyStop_WhenStanding_ZerosRotationToo()
        {
            var (loop, scheduler, motor, cmds, collector, conn) = MakeDrivingLoop();

            // Nouzove zastaveni a kola uz stoji (nulovy prirustek enkoderu).
            var tk = T0.AddMilliseconds(100);
            Feed(loop, Motor(estop: true, wheelSpeed: 0));
            scheduler.PumpDue(tk);

            var last = CmdAt(cmds, tk);
            conn.Dispose(); loop.Stop(); collector.Stop();

            Assert.That(last.EmergencyStop, Is.True);
            Assert.That(last.Forvard, Is.EqualTo(0.0));
            Assert.That(last.RotationSpeed, Is.EqualTo(0.0), "ve stoje se rotace nuluje - zadne otaceni na miste");
            Assert.That(motor.LastDif, Is.EqualTo(0.0), "posledni odeslany prikaz je (0,0)");
        }

        [Test]
        public void EmergencyStop_Released_ResumesControl()
        {
            var (loop, scheduler, motor, cmds, collector, conn) = MakeDrivingLoop();

            var tkStop = T0.AddMilliseconds(100);
            Feed(loop, Motor(estop: true, wheelSpeed: 0));
            scheduler.PumpDue(tkStop);
            Assert.That(CmdAt(cmds, tkStop).Forvard, Is.EqualTo(0.0), "pod stopem stoji");

            // Uvolneni: regulator zas generuje zasahy (draha se mezitim NEzastarala - smycka bezi dal).
            var tkGo = T0.AddMilliseconds(200);
            Feed(loop, Motor(estop: false, wheelSpeed: 0));
            scheduler.PumpDue(tkGo);

            var last = CmdAt(cmds, tkGo);
            conn.Dispose(); loop.Stop(); collector.Stop();

            Assert.That(last.EmergencyStop, Is.False);
            Assert.That(last.Forvard, Is.GreaterThan(0.0), "po uvolneni se zas jede");
        }

        [Test]
        public void NoMotorState_DoesNotStop()
        {
            var (loop, scheduler, motor, cmds, collector, conn) = MakeDrivingLoop();

            // Stav motoru vubec nedosel (napr. DummyMotors bez zdroje zprav) - jizda se nesmi zastavit.
            var tk = T0.AddMilliseconds(100);
            scheduler.PumpDue(tk);

            var last = CmdAt(cmds, tk);
            conn.Dispose(); loop.Stop(); collector.Stop();

            Assert.That(last.EmergencyStop, Is.False);
            Assert.That(last.Forvard, Is.GreaterThan(0.0), "bez stavu motoru robot normalne jede");
        }

        /// <summary>
        /// Regulator s pevnym zasahem - chovani nouzoveho zastaveni se ma testovat na smycce,
        /// ne na geometrii <see cref="PathPlanner"/>u (jinak by test zavisel na tom, jakou rotaci
        /// zrovna vyjde zatacka).
        /// </summary>
        private sealed class ConstRegulator : IRegulator
        {
            public double Speed = 0.8, Rotation = 0.3;
            public bool IsFinished => false;
            public RegulatorResult Control(IModelState state)
                => new RegulatorResult { Speed = Speed, RotationSpeed = Rotation };
        }

        /// <summary>
        /// Smycka s konstantnim regulatorem (0,8 m/s, 0,3 rad/s), spustena a s pripojenym sberacem
        /// <see cref="DriveCommandMsg"/>. Fuze je prazdna - <c>GetStateAt</c> pak vraci bazovy stav
        /// (nikoli null), takze smycka regularne ridi.
        /// </summary>
        private static (ControlLoop loop, Scheduler scheduler, SpyMotors motor,
                        List<DriveCommandMsg> cmds, DelegateTarget collector, IDisposable conn) MakeDrivingLoop()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler,
                                       period: TimeSpan.FromMilliseconds(100));
            loop.Start();
            loop.Regulator = new ConstRegulator();

            var cmds = new List<DriveCommandMsg>();
            var collector = new DelegateTarget(m => { if (m is DriveCommandMsg c) lock (cmds) cmds.Add(c); });
            collector.Start();
            var conn = loop.Output.Connect(collector);
            return (loop, scheduler, motor, cmds, collector, conn);
        }

        /// <summary>
        /// Posle stav motoru do smycky a POCKA, nez ho jeji vlakno prevezme - jinak by takt mohl
        /// probehnout jeste se starym stavem a test by prochazel naprazdno.
        /// </summary>
        private static void Feed(ControlLoop loop, MotorStateBase state)
        {
            loop.Post(state);
            var until = DateTime.UtcNow.AddSeconds(2);
            while (loop.LastMotorState != state && DateTime.UtcNow < until)
                System.Threading.Thread.Sleep(1);
            Assert.That(loop.LastMotorState, Is.SameAs(state), "smycka neprevzala stav motoru");
        }

        /// <summary>
        /// Pocka na prikaz z taktu v case <paramref name="tk"/> a vrati ho. Ceka se na KONKRETNI
        /// takt (podle casu), ne jen "na posledni prikaz" - sberac je na vlastnim vlakne, takze
        /// "posledni" by mohl byt jeste ten z predchoziho taktu a test by prosel naprazdno.
        /// </summary>
        private static DriveCommandMsg CmdAt(List<DriveCommandMsg> cmds, DateTime tk)
        {
            var until = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < until)
            {
                lock (cmds)
                    for (int i = cmds.Count - 1; i >= 0; i--)
                        if (cmds[i].TimeStamp == tk) return cmds[i];
                System.Threading.Thread.Sleep(1);
            }
            Assert.Fail($"prikaz z taktu {tk:HH:mm:ss.fff} nedosel");
            return null;
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
