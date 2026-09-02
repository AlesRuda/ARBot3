using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Smerodatne odchylky stavu EKF z kovariance. Je to jedine, co o vyvoji filtru dosud nebylo
    /// videt: kovariance tekla v RobotStateMsg i do zaznamu, ale nikdo ji nezobrazoval.
    /// Viz doc/ekf-fusion.md a doc/telemetry-view.md.
    /// </summary>
    public class StateSigmaTests
    {
        /// <summary>Diagonalni P s rozptyly podle rozvrzeni EKFModel (IX, IY, ITh, IV, IW).</summary>
        private static Matrix<double> P(double vx, double vy, double vth, double vv, double vw)
        {
            var m = Matrix<double>.Build.Dense(5, 5);
            m[0, 0] = vx; m[1, 1] = vy; m[2, 2] = vth; m[3, 3] = vv; m[4, 4] = vw;
            return m;
        }

        [Test]
        public void Odmocniny_Diagonaly_PodleRozvrzeniStavu()
        {
            var p = P(0.04, 0.09, 0.0025, 0.16, 0.25);

            Assert.That(StateSigma.X(p), Is.EqualTo(0.2).Within(1e-12));
            Assert.That(StateSigma.Y(p), Is.EqualTo(0.3).Within(1e-12));
            Assert.That(StateSigma.Theta(p), Is.EqualTo(0.05).Within(1e-12), "v RADIANECH");
            Assert.That(StateSigma.V(p), Is.EqualTo(0.4).Within(1e-12));
            Assert.That(StateSigma.Omega(p), Is.EqualTo(0.5).Within(1e-12), "v rad/s");
        }

        [Test]
        public void SigmaPolohy_JeOdmocninaSouctuRozptylu()
        {
            // Jedno cislo do grafu na otazku "roste mi nejistota?". ZAMERNE to neni poloosa
            // elipsy - ta by brala v uvahu i korelaci P[0,1] a patri do panelu, ne do tabulky.
            var p = P(0.09, 0.16, 0, 0, 0);
            Assert.That(StateSigma.Position(p), Is.EqualTo(0.5).Within(1e-12));   // sqrt(0,09+0,16)
        }

        [Test]
        public void SigmaPolohy_IgnorujeKorelaci()
        {
            // Doklad k predchozimu testu: nenulova mimodiagonala vysledek nemeni.
            var p = P(0.09, 0.16, 0, 0, 0);
            p[0, 1] = p[1, 0] = 0.11;
            Assert.That(StateSigma.Position(p), Is.EqualTo(0.5).Within(1e-12));
        }

        [Test]
        public void ChybejiciKovariance_JeNull_NeNula()
        {
            // Covariance ve zprave muze byt null. Nula by lhala ("filtr si je jisty"), sloupec ma
            // zustat prazdny - Num<T> bere double?, takze null je spravna odpoved.
            Assert.That(StateSigma.X(null), Is.Null);
            Assert.That(StateSigma.Position(null), Is.Null);
            Assert.That(StateSigma.Omega(null), Is.Null);
        }

        [Test]
        public void MalaMatice_JeNull_NeVyjimka()
        {
            // Kdyby se stavovy vektor zmenil nebo prisla zprava ze stareho zaznamu s jinym P,
            // nesmi to shodit telemetrii.
            var maly = Matrix<double>.Build.Dense(2, 2, 1.0);
            Assert.That(StateSigma.Omega(maly), Is.Null);
            Assert.That(StateSigma.X(maly), Is.EqualTo(1.0).Within(1e-12), "co se vejde, se spocte");
        }

        [Test]
        public void ZapornyRozptyl_JeNull()
        {
            // Numericky rozpadla kovariance: zaporny rozptyl nema odmocninu a NaN by se v grafu
            // tvaril jako platna hodnota.
            var p = P(-1e-9, 0.04, 0, 0, 0);
            Assert.That(StateSigma.X(p), Is.Null);
            Assert.That(StateSigma.Y(p), Is.EqualTo(0.2).Within(1e-12));
        }
    }
}
