using System;
using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Testy skalarniho merenia polohy podel osy (viz doc/map-correlation-localization.md).
    /// Slouzi korelaci s mapou: dve merenia po vlastnich osach kovariance misto jedne
    /// PositionMeasurement s diagonalni R.
    /// </summary>
    [TestFixture]
    public class AxisOffsetMeasurementTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0);

        private static Vector<double> State(double x, double y, double theta = 0)
            => Vector<double>.Build.DenseOfArray(new[] { x, y, theta, 0.0, 0.0 });

        [Test]
        public void Predict_JeSkalarniProjekceNaOsu()
        {
            // Osa mirici na severovychod, normovana.
            var m = new AxisOffsetMeasurement(1, 1, value: 0, std: 0.1, T0, "MapCorr");

            var hx = m.Predict(State(3.0, 4.0));

            Assert.That(hx.Count, Is.EqualTo(1));
            Assert.That(hx[0], Is.EqualTo((3.0 + 4.0) / Math.Sqrt(2.0)).Within(1e-9));
        }

        [Test]
        public void Jacobian_MaJenSlozkyPolohy()
        {
            var m = new AxisOffsetMeasurement(0, 1, value: 0, std: 0.1, T0, "MapCorr");

            var h = m.Jacobian(State(0, 0));

            Assert.That(h.RowCount, Is.EqualTo(1));
            Assert.That(h[0, EKFModel.IX], Is.EqualTo(0.0).Within(1e-12));
            Assert.That(h[0, EKFModel.IY], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(h[0, EKFModel.ITh], Is.EqualTo(0.0));
            Assert.That(h[0, EKFModel.IV], Is.EqualTo(0.0));
            Assert.That(h[0, EKFModel.IW], Is.EqualTo(0.0));
        }

        [Test]
        public void OsaSeNormuje()
        {
            // Nenormovana osa (dlouha 5) musi dat tentyz jakobian jako normovana.
            var m = new AxisOffsetMeasurement(3, 4, value: 0, std: 0.1, T0, "MapCorr");

            var h = m.Jacobian(State(0, 0));

            Assert.That(h[0, EKFModel.IX], Is.EqualTo(0.6).Within(1e-12));
            Assert.That(h[0, EKFModel.IY], Is.EqualTo(0.8).Within(1e-12));
        }

        [Test]
        public void NulovaOsa_Vyhodi()
        {
            Assert.That(() => new AxisOffsetMeasurement(0, 0, 0, 0.1, T0, "MapCorr"),
                        Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void NoiseCovariance_JeCtverecSigmy()
        {
            var m = new AxisOffsetMeasurement(1, 0, value: 0, std: 0.25, T0, "MapCorr");

            Assert.That(m.NoiseCovariance[0, 0], Is.EqualTo(0.0625).Within(1e-12));
        }

        [Test]
        public void MereniPodelX_KorigujeXaNechaY()
        {
            // Klicove tvrzeni: merenie podel jedne osy nesmi hybat kolmou slozkou.
            var model = new EKFModel();
            model.Update(new PositionMeasurement(0, 0, 0.5, 0.5, T0, "GPS"));

            for (int i = 0; i < 100; i++)
            {
                model.Predict(0.1);
                // "Skutecna X je 4" - rikame to jen podel osy X.
                model.Update(new AxisOffsetMeasurement(1, 0, value: 4.0, std: 0.1,
                                                      T0.AddSeconds(i * 0.1), "MapCorr"));
            }

            var s = model.Current(T0.AddSeconds(10));
            Assert.That(s.X, Is.EqualTo(4.0).Within(0.2));
            Assert.That(s.Y, Is.EqualTo(0.0).Within(0.2), "Merenie podel X nesmi tahnout Y.");
        }

        [Test]
        public void MereniPodelOtoceneOsy_NechaKolmouSlozku()
        {
            // TOHLE je vlastni test tvrzeni "merenie podel osy nehybe kolmou slozkou".
            // MereniPodelX_KorigujeXaNechaY ho NEOVERUJE: osa (1,0) je zarovnana se svetem a pri
            // theta = 0 zustava P_xy presne nulove, takze Y nehne ani spatne spocteny jakobian -
            // ten test by prosel i s vadnou implementaci.
            // Osa (3,4)/5 neni ani zarovnana, ani symetricka, takze spatne normovany NEBO prohozeny
            // jakobian kolmou slozku posune a test to pozna.
            var model = new EKFModel();
            const double ax = 0.6, ay = 0.8;      // (3,4)/5
            const double target = 4.0;            // 4 m podel osy, kolmo nic

            // Schvalne BEZ Predict: pohybovy model pri theta = 0 pridava sum jen do X (Q[IX,IV]),
            // cimz by translacni blok P prestal byt izotropni a kolma slozka by se pohnula
            // LEGITIMNE (skrz korelaci v P). Bez predikce zustava P izotropni a tvrzeni je ciste.
            for (int i = 0; i < 200; i++)
                model.Update(new AxisOffsetMeasurement(3, 4, target, 0.05,
                                                      T0.AddSeconds(i * 0.01), "MapCorr"));

            var s = model.Current(T0.AddSeconds(2));
            double along = ax * s.X + ay * s.Y;
            double across = -ay * s.X + ax * s.Y;

            Assert.That(along, Is.EqualTo(target).Within(0.05), "Slozka PODEL osy se ma zkorigovat.");
            Assert.That(across, Is.EqualTo(0.0).Within(1e-9), "Kolma slozka se hybat nesmi.");
        }

        [Test]
        public void Residual_JeRozdilBezZabaleni()
        {
            // Poloha neni uhel, takze se nic nezabaluje - jen rozdil.
            var m = new AxisOffsetMeasurement(1, 0, value: 5.0, std: 0.1, T0, "MapCorr");

            var res = m.Residual(m.Value, m.Predict(State(3.0, 0.0)));

            Assert.That(res[0], Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void VychoziGateMode_JeReject()
        {
            var m = new AxisOffsetMeasurement(1, 0, 0, 0.1, T0, "MapCorr");

            Assert.That(m.GateMode, Is.EqualTo(GateMode.Reject));
            Assert.That(m.GateThreshold, Is.Null, "Bez explicitniho prahu se negatuje.");
        }
    }
}
