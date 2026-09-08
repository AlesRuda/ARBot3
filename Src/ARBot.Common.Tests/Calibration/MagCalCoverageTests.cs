using System;
using ARBot.Common.Calibration;
using NUnit.Framework;

using Vector3 = System.Numerics.Vector3;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// Kose pokryti: co jeste chybi, aby slo poctive prolozit — a hlavne jako POKYN pro obsluhu.
    /// Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalCoverageTests
    {
        /// <summary>
        /// Gravitace pri naklonu <paramref name="naklon"/> [rad] okolo osy dane
        /// <paramref name="smerRad"/> — akcelerometr v klidu meri −g.
        /// </summary>
        private static Vector3 Acc(double naklon, double smerRad = 0)
        {
            double vodorovne = -9.81 * Math.Sin(naklon);
            return new Vector3((float)(vodorovne * Math.Cos(smerRad)),
                               (float)(vodorovne * Math.Sin(smerRad)),
                               (float)(-9.81 * Math.Cos(naklon)));
        }

        /// <summary>Jeden plny obrat pri danem naklonu.</summary>
        private static void Obrat(MagCalCoverage c, double naklon, double smerRad = 0, int n = 600)
        {
            for (int i = 0; i < n; i++)
                c.Add(2 * Math.PI * i / n, new Vector3(0.4f, 0.1f, -0.3f), Acc(naklon, smerRad));
        }

        [Test]
        public void PrazdnePokryti_NeniHotove_AHlasiChybejiciAzimuty()
        {
            var c = new MagCalCoverage();

            Assert.That(c.Complete, Is.False);
            Assert.That(c.MissingText(), Does.Contain("azimut"));
        }

        [Test]
        public void JedenObratNaRovine_MaAzimuty_AleNeNaklony()
        {
            var c = new MagCalCoverage();
            Obrat(c, 0.0);

            Assert.That(c.FilledAzimuthBins, Is.EqualTo(MagCalThresholds.AzimuthBins));
            Assert.That(c.TiltedGroups, Is.EqualTo(0));
            Assert.That(c.Complete, Is.False, "bez naklonu je z vymyslene - nesmi byt hotovo");
            Assert.That(c.MissingText(), Does.Contain("naklon"));
        }

        [Test]
        public void TriObratySProtilehlymiNaklony_JeHotovo()
        {
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            Obrat(c, 0.40);              // ~23° na jednu stranu
            Obrat(c, 0.40, Math.PI);     // ~23° na druhou stranu

            Assert.That(c.TiltGroups, Is.GreaterThanOrEqualTo(MagCalThresholds.MinTiltGroups));
            Assert.That(c.TiltedGroups, Is.GreaterThanOrEqualTo(MagCalThresholds.MinTiltedGroups));
            Assert.That(c.HasOppositeTilts, Is.True);
            Assert.That(c.Complete, Is.True);
            Assert.That(c.MissingText(), Is.Empty);
        }

        [Test]
        public void DvaNaklonyNaTUTEZStranu_NESTACI()
        {
            // ZMERENO v MagCalFitTests: dva naklony na jednu stranu maji podminenost 4,7e7,
            // tedy skoro jako rovina (2,0e8), kdezto par +/- da 434. Kose to musi chytit,
            // jinak by obsluha "dokoncila" mereni, ze ktereho se poctive prolozit neda.
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            Obrat(c, 0.35);              // ~20°, tedy kos 2 -> pocita se jako naklon
            Obrat(c, 0.60);              // ~34°, kos 3 -> taky, ale TENTYZ smer

            Assert.That(c.TiltedGroups, Is.GreaterThanOrEqualTo(2), "dva naklonene kose tam jsou");
            Assert.That(c.HasOppositeTilts, Is.False, "ale oba na tutéz stranu");
            Assert.That(c.Complete, Is.False);
            Assert.That(c.MissingText(), Does.Contain("DRUHOU stranu"),
                "pokyn musi rict, co udelat, ne jen ze to nestaci");
        }

        [Test]
        public void MalyNaklon_SeNepocitaJakoNaklon()
        {
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            Obrat(c, 0.10);              // ~5,7°, pod MinTiltDeg
            Obrat(c, 0.10, Math.PI);

            Assert.That(c.TiltedGroups, Is.EqualTo(0));
            Assert.That(c.Complete, Is.False);
        }

        [Test]
        public void ChybejiciAzimuty_SeHlasiJakoROZSAH_NeVypisCisel()
        {
            var c = new MagCalCoverage();
            // Tri ctvrtiny obratu (450 z 600 vzorku): chybi souvisly usek 270-360°.
            for (int i = 0; i < 450; i++)
                c.Add(2 * Math.PI * i / 600, new Vector3(0.4f, 0.1f, -0.3f), Acc(0));

            string t = c.MissingText();

            Assert.That(t, Does.Contain("270-360°"),
                "souvisly usek jako jeden rozsah - vypis 18 cisel by clovek necetl");
        }

        [Test]
        public void MagIAcc_MajiStejnePoradiIDelku()
        {
            // Fit je paruje po indexu, takze rozejit se nesmi.
            var c = new MagCalCoverage();
            Obrat(c, 0.0, n: 30);

            Assert.That(c.Mag.Count, Is.EqualTo(30));
            Assert.That(c.Acc.Count, Is.EqualTo(c.Mag.Count));
        }
    }
}
