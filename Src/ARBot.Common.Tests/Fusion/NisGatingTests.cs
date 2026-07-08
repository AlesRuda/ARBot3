using System;
using ARBot.Common.Fusion;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    [TestFixture]
    public class NisGatingTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 0, 0, 0);

        [Test]
        public void ChiSquareThreshold_KnownValues()
        {
            Assert.That(Gating.ChiSquareThreshold(1, 0.95), Is.EqualTo(3.841).Within(0.01));
            Assert.That(Gating.ChiSquareThreshold(2, 0.95), Is.EqualTo(5.991).Within(0.01));
        }

        [Test]
        public void Nis_ZeroWhenMeasurementMatchesPrediction()
        {
            var m = new EKFModel();          // stav 0, P = I
            m.Update(ScalarStateMeasurement.Velocity(0.0, 0.1, T0, "Odo"));
            Assert.That(m.LastNis, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(m.LastAccepted, Is.True);
        }

        [Test]
        public void Nis_MatchesClosedFormForScalar()
        {
            var m = new EKFModel();          // P[v,v] = 1 (jednotkova)
            double zv = 2.0, std = 0.5;
            double r = std * std;
            m.Update(ScalarStateMeasurement.Velocity(zv, std, T0, "Odo"));
            // S = P_vv + r = 1 + r ; NIS = zv^2 / S
            double expected = zv * zv / (1.0 + r);
            Assert.That(m.LastNis, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void Gating_RejectsOutlier_StateUnchanged()
        {
            var m = new EKFModel();
            double vBefore = m.Current(T0).V;   // 0
            var outlier = ScalarStateMeasurement.Velocity(10.0, 0.1, T0, "Odo");
            outlier.GateThreshold = Gating.ChiSquareThreshold(1, 0.99);   // ~6.63
            m.Update(outlier);
            Assert.That(m.LastAccepted, Is.False);
            Assert.That(m.LastNis, Is.GreaterThan(6.63));
            Assert.That(m.Current(T0).V, Is.EqualTo(vBefore).Within(1e-12));   // stav se nezmenil
        }

        [Test]
        public void Gating_AcceptsInlier_StateChanges()
        {
            var m = new EKFModel();
            var ok = ScalarStateMeasurement.Velocity(0.5, 0.5, T0, "Odo");
            ok.GateThreshold = Gating.ChiSquareThreshold(1, 0.99);
            m.Update(ok);
            Assert.That(m.LastAccepted, Is.True);
            Assert.That(m.Current(T0).V, Is.GreaterThan(0.0));
        }

        [Test]
        public void Engine_Diagnostics_ReportsPerMeasurementNis()
        {
            var e = new AsyncFusionEngine(new EKFModel());
            e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.1, T0.AddSeconds(0.0), "Odo"));
            e.Enqueue(new HeadingMeasurement(0.0, 0.05, T0.AddSeconds(0.1), "Compass"));
            var diag = e.Diagnostics();
            Assert.That(diag.Count, Is.EqualTo(2));
            foreach (var d in diag)
            {
                Assert.That(d.Accepted, Is.True);
                Assert.That(double.IsFinite(d.Nis), Is.True);
            }
        }

        [Test]
        public void SoftGate_NeverRejects_ButDownweights()
        {
            var ungated = new EKFModel();
            var soft = new EKFModel();

            var mU = new PositionMeasurement(10, 0, 0.5, 0.5, T0, "GPS");
            var mS = new PositionMeasurement(10, 0, 0.5, 0.5, T0, "GPS")
            {
                GateThreshold = Gating.ChiSquareThreshold(2, 0.99),
                GateMode = GateMode.Soft
            };
            ungated.Update(mU);
            soft.Update(mS);

            Assert.That(soft.LastAccepted, Is.True);                          // nikdy nezamitne
            Assert.That(soft.Current(T0).X, Is.GreaterThan(0));               // ale pohne se
            Assert.That(soft.Current(T0).X, Is.LessThan(ungated.Current(T0).X)); // min nez plne duverovane
        }

        [Test]
        public void SoftGate_RecoversFromLockout_WhereRejectStaysStuck()
        {
            // robot "zabloudil" na X=30 s prilis sebejistym P; GPS spravne ukazuje 0.
            double gate = Gating.ChiSquareThreshold(2, 0.99);

            var reject = new EKFModel();
            reject.SetPose(30, 0, 0);
            var soft = new EKFModel();
            soft.SetPose(30, 0, 0);

            for (int i = 0; i < 100; i++)
            {
                var t = T0.AddSeconds(i * 0.1);
                var gr = new PositionMeasurement(0, 0, 0.5, 0.5, t, "GPS") { GateThreshold = gate, GateMode = GateMode.Reject };
                var gs = new PositionMeasurement(0, 0, 0.5, 0.5, t, "GPS") { GateThreshold = gate, GateMode = GateMode.Soft };
                reject.Update(gr);
                soft.Update(gs);
            }

            // tvrdy reject: NIS trvale nad prahem, nic se neprijme -> zustane zaseknuty u 30
            Assert.That(reject.Current(T0).X, Is.GreaterThan(29.0));
            // mekky gating: postupne se pritahne ke spravne poloze
            Assert.That(soft.Current(T0).X, Is.LessThan(1.0));
        }

        [Test]
        public void Engine_GatedOutlier_DoesNotCorruptEstimate()
        {
            var e = new AsyncFusionEngine(new EKFModel());
            double gate1 = Gating.ChiSquareThreshold(2, 0.99);
            for (int i = 0; i < 60; i++)
            {
                var t = T0.AddSeconds(i * 0.1);
                e.Enqueue(ScalarStateMeasurement.Velocity(0.0, 0.05, t, "Odo"));
                var pos = new PositionMeasurement(0, 0, 0.5, 0.5, t, "GPS") { GateThreshold = gate1 };
                e.Enqueue(pos);
            }
            // jednorazovy divoky skok GPS s gatingem -> ma se zahodit
            var jump = new PositionMeasurement(1000, 1000, 0.5, 0.5, T0.AddSeconds(6.0), "GPS")
            {
                GateThreshold = gate1
            };
            e.Enqueue(jump);
            var s = e.GetStateAt(T0.AddSeconds(6.0));
            Assert.That(Math.Abs(s.X), Is.LessThan(1.0));
            Assert.That(Math.Abs(s.Y), Is.LessThan(1.0));
        }
    }
}
