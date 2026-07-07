using System;
using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    [TestFixture]
    public class EKFModelTests
    {
        [Test]
        public void PredictState_StraightLine()
        {
            var m = new TestableModel();
            var x = TestableModel.State(0, 0, 0, 2.0, 0);   // v=2, omega=0
            var n = m.PublicPredict(x, 0.5);
            Assert.That(n[EKFModel.IX], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(n[EKFModel.IY], Is.EqualTo(0.0).Within(1e-9));
            Assert.That(n[EKFModel.ITh], Is.EqualTo(0.0).Within(1e-9));
            Assert.That(n[EKFModel.IV], Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void PredictState_PureRotation()
        {
            var m = new TestableModel();
            var x = TestableModel.State(0, 0, 0, 0, 1.0);   // v=0, omega=1
            var n = m.PublicPredict(x, 0.5);
            Assert.That(n[EKFModel.IX], Is.EqualTo(0.0).Within(1e-9));
            Assert.That(n[EKFModel.IY], Is.EqualTo(0.0).Within(1e-9));
            Assert.That(n[EKFModel.ITh], Is.EqualTo(0.5).Within(1e-9));
            Assert.That(n[EKFModel.IW], Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void PredictState_Arc_UsesMidpointHeading()
        {
            var m = new TestableModel();
            double v = 1.0, w = 1.0, dt = 0.2;
            var x = TestableModel.State(0, 0, 0, v, w);
            var n = m.PublicPredict(x, dt);
            double b = 0 + w * dt / 2.0;
            Assert.That(n[EKFModel.IX], Is.EqualTo(v * Math.Cos(b) * dt).Within(1e-9));
            Assert.That(n[EKFModel.IY], Is.EqualTo(v * Math.Sin(b) * dt).Within(1e-9));
            Assert.That(n[EKFModel.ITh], Is.EqualTo(w * dt).Within(1e-9));
        }

        [Test]
        public void JacobianF_MatchesNumericalDifference()
        {
            var m = new TestableModel();
            var x = TestableModel.State(1.0, -2.0, 0.5, 1.2, 0.3);
            double dt = 0.1;
            var Fa = m.PublicJacobianF(x, dt);
            var Fn = NumericalJacobian(m, x, dt);
            for (int i = 0; i < EKFModel.N; i++)
                for (int j = 0; j < EKFModel.N; j++)
                    Assert.That(Fa[i, j], Is.EqualTo(Fn[i, j]).Within(1e-5),
                        $"F[{i},{j}] analyticky != numericky");
        }

        private static Matrix<double> NumericalJacobian(TestableModel m, Vector<double> x, double dt)
        {
            int n = x.Count;
            var F = Matrix<double>.Build.Dense(n, n);
            double eps = 1e-7;
            var f0 = m.PublicPredict(x, dt);
            for (int j = 0; j < n; j++)
            {
                var xp = x.Clone();
                xp[j] += eps;
                var fp = m.PublicPredict(xp, dt);
                for (int i = 0; i < n; i++)
                    F[i, j] = (fp[i] - f0[i]) / eps;
            }
            return F;
        }

        [Test]
        public void ProcessNoise_ScalesWithDt_ClosedForm()
        {
            var cfg = new FusionConfig { SigmaAccel = 1.3, SigmaAngAccel = 0.7, PositionNoiseFloor = 0 };
            var m = new TestableModel(cfg);
            double dt = 0.25;
            var x = TestableModel.State(0, 0, 0, 0, 0);   // theta=0 -> podelny smer = osa X
            var Q = m.PublicProcessNoise(x, dt);

            double sa2 = 1.3 * 1.3, sal2 = 0.7 * 0.7;
            Assert.That(Q[EKFModel.IV, EKFModel.IV], Is.EqualTo(sa2 * dt).Within(1e-12));
            Assert.That(Q[EKFModel.IW, EKFModel.IW], Is.EqualTo(sal2 * dt).Within(1e-12));
            Assert.That(Q[EKFModel.IX, EKFModel.IX], Is.EqualTo(sa2 * dt * dt * dt / 3.0).Within(1e-12));
            // theta=0 -> zadny sum polohy v ose Y
            Assert.That(Q[EKFModel.IY, EKFModel.IY], Is.EqualTo(0.0).Within(1e-12));
            Assert.That(Q[EKFModel.ITh, EKFModel.ITh], Is.EqualTo(sal2 * dt * dt * dt / 3.0).Within(1e-12));
            Assert.That(Q[EKFModel.IX, EKFModel.IV], Is.EqualTo(sa2 * dt * dt / 2.0).Within(1e-12));
        }

        [Test]
        public void ProcessNoise_QvvLinearInDt()
        {
            var cfg = new FusionConfig { SigmaAccel = 1.0 };
            var m = new TestableModel(cfg);
            var x = TestableModel.State(0, 0, 0, 0, 0);
            var q1 = m.PublicProcessNoise(x, 0.1)[EKFModel.IV, EKFModel.IV];
            var q2 = m.PublicProcessNoise(x, 0.2)[EKFModel.IV, EKFModel.IV];
            Assert.That(q2 / q1, Is.EqualTo(2.0).Within(1e-9));   // linearni v dt
        }
    }
}
