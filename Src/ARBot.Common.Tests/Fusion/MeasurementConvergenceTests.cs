using System;
using ARBot.Common.Common;
using ARBot.Common.Fusion;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    [TestFixture]
    public class MeasurementConvergenceTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 0, 0, 0);

        [Test]
        public void Position_Converges()
        {
            var m = new EKFModel();
            for (int i = 0; i < 100; i++)
            {
                m.Predict(0.1);
                m.Update(new PositionMeasurement(10, 5, 1.0, 1.0, T0.AddSeconds(i * 0.1), "GPS"));
            }
            var s = m.Current(T0);
            Assert.That(s.X, Is.EqualTo(10.0).Within(0.2));
            Assert.That(s.Y, Is.EqualTo(5.0).Within(0.2));
        }

        [Test]
        public void Heading_Converges()
        {
            var m = new EKFModel();
            for (int i = 0; i < 100; i++)
            {
                m.Predict(0.1);
                m.Update(new HeadingMeasurement(1.2, 0.05, T0.AddSeconds(i * 0.1), "Compass"));
            }
            Assert.That(m.Current(T0).Theta, Is.EqualTo(1.2).Within(0.02));
        }

        [Test]
        public void Velocity_Converges()
        {
            var m = new EKFModel();
            for (int i = 0; i < 100; i++)
            {
                m.Predict(0.1);
                m.Update(ScalarStateMeasurement.Velocity(1.5, 0.05, T0.AddSeconds(i * 0.1), "Odo"));
            }
            Assert.That(m.Current(T0).V, Is.EqualTo(1.5).Within(0.05));
        }

        [Test]
        public void AngularRate_Converges()
        {
            var m = new EKFModel();
            for (int i = 0; i < 100; i++)
            {
                m.Predict(0.1);
                m.Update(ScalarStateMeasurement.AngularRate(0.8, 0.05, T0.AddSeconds(i * 0.1), "Gyro"));
            }
            Assert.That(m.Current(T0).Omega, Is.EqualTo(0.8).Within(0.05));
        }

        [Test]
        public void FusionOfTwoRateSources_IsMoreCertainThanOne()
        {
            // dva zdroje omega (gyro + odometrie) -> mensi rozptyl nez jen jeden
            var single = new EKFModel();
            var both = new EKFModel();
            for (int i = 0; i < 30; i++)
            {
                double t = i * 0.1;
                single.Predict(0.1);
                single.Update(ScalarStateMeasurement.AngularRate(0.5, 0.1, T0.AddSeconds(t), "Gyro"));

                both.Predict(0.1);
                both.Update(ScalarStateMeasurement.AngularRate(0.5, 0.1, T0.AddSeconds(t), "Gyro"));
                both.Update(ScalarStateMeasurement.AngularRate(0.5, 0.1, T0.AddSeconds(t), "Odo"));
            }
            Assert.That(both.Current(T0).Omega, Is.EqualTo(0.5).Within(0.03));
            double varSingle = single.P[EKFModel.IW, EKFModel.IW];
            double varBoth = both.P[EKFModel.IW, EKFModel.IW];
            Assert.That(varBoth, Is.LessThan(varSingle), "fuze dvou zdroju ma mit mensi rozptyl");
        }

        [Test]
        public void HeadingResidual_WrapsAcrossPi()
        {
            // stav ~ -pi, merenie ~ +pi -> male reziduum
            var meas = new HeadingMeasurement(3.0, 0.05, T0, "Compass");
            var hx = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(1, -3.0);
            var res = meas.Residual(meas.Value, hx);
            Assert.That(res[0], Is.EqualTo(Conversions.NormalizeOrientation(6.0)).Within(1e-9));
            Assert.That(Math.Abs(res[0]), Is.LessThan(0.3));   // ne ~6 rad
        }

        [Test]
        public void PositionUpdate_ShrinksCovariance()
        {
            var m = new EKFModel();
            double before = m.P[EKFModel.IX, EKFModel.IX];
            m.Update(new PositionMeasurement(0, 0, 0.5, 0.5, T0, "GPS"));
            double after = m.P[EKFModel.IX, EKFModel.IX];
            Assert.That(after, Is.LessThan(before));
        }
    }
}
