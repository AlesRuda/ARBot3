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
            var regulator = new Regulator(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed,
                                          Profile.MaxAcceleration, Profile.Rozchod);
            var ts = TimeSpan.FromMilliseconds(20);

            var loop = new ControlLoop(engine, regulator, motor, new VirtualClock(), scheduler,
                                       targetX: 3.0, targetY: 2.0, period: ts);

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
    }
}
