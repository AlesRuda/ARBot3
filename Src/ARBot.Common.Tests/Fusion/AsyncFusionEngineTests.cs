using System;
using System.Collections.Generic;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    [TestFixture]
    public class AsyncFusionEngineTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 0, 0, 0);

        private static List<IMeasurement> Sequence()
        {
            return new List<IMeasurement>
            {
                ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(0.0), "Odo"),
                ScalarStateMeasurement.AngularRate(0.5, 0.05, T0.AddSeconds(0.1), "Gyro"),
                ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(0.2), "Odo"),
                new PositionMeasurement(0.4, 0.1, 0.5, 0.5, T0.AddSeconds(0.3), "GPS"),
                new HeadingMeasurement(0.15, 0.05, T0.AddSeconds(0.3), "Compass"),
            };
        }

        [Test]
        public void OutOfSequence_ReplayGivesSameResultAsInOrder()
        {
            var seq = Sequence();

            // A: v poradi
            var eA = new AsyncFusionEngine(new EKFModel());
            foreach (var m in seq)
                eA.Enqueue(m);
            var sA = eA.GetStateAt(T0.AddSeconds(0.3));

            // B: merenie @0.1 dorazi opozdene (out-of-sequence) az nakonec
            var eB = new AsyncFusionEngine(new EKFModel());
            eB.Enqueue(seq[0]);   // 0.0
            eB.Enqueue(seq[2]);   // 0.2
            eB.Enqueue(seq[3]);   // 0.3
            eB.Enqueue(seq[4]);   // 0.3
            eB.Enqueue(seq[1]);   // 0.1 - OOSM
            var sB = eB.GetStateAt(T0.AddSeconds(0.3));

            Assert.That(sB.X, Is.EqualTo(sA.X).Within(1e-9));
            Assert.That(sB.Y, Is.EqualTo(sA.Y).Within(1e-9));
            Assert.That(sB.Theta, Is.EqualTo(sA.Theta).Within(1e-9));
            Assert.That(sB.V, Is.EqualTo(sA.V).Within(1e-9));
            Assert.That(sB.Omega, Is.EqualTo(sA.Omega).Within(1e-9));
        }

        [Test]
        public void TooOldMeasurement_IsDiscarded()
        {
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(0.2));
            for (int i = 0; i < 20; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(i * 0.1), "Odo"));

            int before = e.BufferedCount;
            // merenie hluboko v minulosti (mimo okno) se ma zahodit -> buffer beze zmeny
            e.Enqueue(ScalarStateMeasurement.Velocity(9.0, 0.05, T0.AddSeconds(0.0), "Odo"));
            Assert.That(e.BufferedCount, Is.EqualTo(before));
        }

        [Test]
        public void Prune_KeepsBufferBounded()
        {
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(0.2));
            for (int i = 0; i < 50; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(i * 0.1), "Odo"));

            // okno 0.2 s, krok 0.1 s -> ~2-3 merenia
            Assert.That(e.BufferedCount, Is.LessThanOrEqualTo(4));
            var s = e.GetStateAt(T0.AddSeconds(5.0));
            Assert.That(double.IsFinite(s.V), Is.True);
        }

        [Test]
        public void GetStateAt_PredictsForward()
        {
            var e = new AsyncFusionEngine(new EKFModel());
            // ustali rychlost 2 m/s a orientaci 0
            for (int i = 0; i < 50; i++)
            {
                var t = T0.AddSeconds(i * 0.05);
                e.Enqueue(ScalarStateMeasurement.Velocity(2.0, 0.02, t, "Odo"));
                e.Enqueue(new HeadingMeasurement(0.0, 0.02, t, "Compass"));
            }
            var last = e.FilterTime;
            var now = e.GetStateAt(last);
            var future = e.GetStateAt(last.AddSeconds(1.0));   // +1 s dopredu
            Assert.That(future.X - now.X, Is.EqualTo(2.0).Within(0.2));   // v*dt ~ 2 m
        }

        [Test]
        public void GetStateAt_ReconstructsPastWithinWindow()
        {
            var e = new AsyncFusionEngine(new EKFModel());   // okno 1 s
            for (int i = 0; i < 80; i++)
            {
                var t = T0.AddSeconds(i * 0.05);
                e.Enqueue(ScalarStateMeasurement.Velocity(2.0, 0.02, t, "Odo"));
                e.Enqueue(new HeadingMeasurement(0.0, 0.02, t, "Compass"));
            }
            var last = e.FilterTime;
            var now = e.GetStateAt(last);
            var past = e.GetStateAt(last.AddSeconds(-0.5));   // 0.5 s zpet (v okne)
            // pred 0.5 s byl robot o ~v*0.5 = 1 m vzad
            Assert.That(now.X - past.X, Is.EqualTo(1.0).Within(0.2));
            Assert.That(past.X, Is.LessThan(now.X));
        }

        // --- SlipDetector ---

        private class FakeMotor : IMotorState
        {
            public bool IsEmergencyStop => false;
            public double LeftEncoder => 0;
            public double RightEncoder => 0;
            public double LeftWheelSpeed { get; set; }
            public double RightWheelSpeed { get; set; }
            public double Voltage => 0;
            public double LeftMotorCurrent => 0;
            public double RightMotorCurrent => 0;
        }

        [Test]
        public void SlipDetector_FlagsUnphysicalWheelAcceleration()
        {
            var cfg = new FusionConfig { MaxWheelAccel = 5.0, SlipRScale = 100.0 };
            var slip = new SlipDetector(cfg);

            var motor = new FakeMotor { LeftWheelSpeed = 0, RightWheelSpeed = 0 };
            Assert.That(slip.OdometryStdScale(motor, T0), Is.EqualTo(1.0).Within(1e-9));

            // skok o 2 m/s za 0.1 s -> 20 m/s^2 >> 5 -> smyk
            motor = new FakeMotor { LeftWheelSpeed = 2.0, RightWheelSpeed = 2.0 };
            double scale = slip.OdometryStdScale(motor, T0.AddSeconds(0.1));
            Assert.That(slip.IsSlipping, Is.True);
            Assert.That(scale, Is.GreaterThan(1.0));
        }

        [Test]
        public void SlipInflatedOdometry_DoesNotDominateHonestSensors()
        {
            // odometrie "lze" (v=3) s obrovskym R (smyk), GPS speed poctive 0 -> odhad ~0
            var m = new EKFModel();
            for (int i = 0; i < 60; i++)
            {
                var t = T0.AddSeconds(i * 0.1);
                m.Predict(0.1);
                m.Update(ScalarStateMeasurement.Velocity(3.0, 5.0, t, "Odo(slip)"));   // velke std
                m.Update(ScalarStateMeasurement.Velocity(0.0, 0.1, t, "GPS"));          // male std
            }
            Assert.That(m.Current(T0).V, Is.LessThan(0.5));
        }
    }
}
