using System;
using System.Linq;
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
            Obrat(c, 0.35);              // ~20°
            Obrat(c, 0.60);              // ~34°, jina velikost, ale TENTYZ smer

            // ⚠️ Od 10. 9. 2026 padnou obe velikosti do JEDNOHO radku - velikost uz neni
            // soucasti klice (viz MagCalCoverage.Radek). Brana tim NESLABNE, naopak:
            // driv daly dve velikosti na tutez stranu tri skupiny a Complete blokoval az
            // HasOppositeTilts, dnes se na tri skupiny jednostrannym naklanenim nedostane.
            Assert.That(c.TiltedGroups, Is.EqualTo(1), "tataz strana je jeden radek, ne dva");
            Assert.That(c.TiltGroups, Is.EqualTo(2), "rovina + jedna strana");
            Assert.That(c.HasOppositeTilts, Is.False, "oba naklony jsou na tutéz stranu");
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

        /// <summary>
        /// Obrat, pri kterem VELIKOST naklonu kolisa — tak, jak to vyjde, kdyz obsluha drzi
        /// robota v rukou. Smer naklonu zustava, protoze ten drzi podlozka nebo ruka.
        /// </summary>
        private static void ObratRukou(MagCalCoverage c, double stred, double rozkyv,
                                       double smerRad = 0, int n = 600)
        {
            for (int i = 0; i < n; i++)
            {
                double f = 2 * Math.PI * i / n;
                c.Add(f, new Vector3(0.4f, 0.1f, -0.3f),
                      Acc(stred + rozkyv * Math.Sin(3 * f), smerRad));
            }
        }

        [Test]
        public void NaklonRukou_SKolisajiciVelikosti_SePOCITA()
        {
            // ⚠️ Tohle je vada z pole 10. 9. 2026: obsluha robota naklonila a otacela, hlaska
            // o naklonech zmizela, a presto to nikam nevedlo. Kdyz se kose klicuji VELIKOSTI
            // odklonu po 10°, rozpadne se rucni naklon do vic skupin a zadna nedosahne poloviny
            // azimutu. Velikost proto v klici NENI - jen smer a to, jestli je odklon nad prahem.
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            ObratRukou(c, 0.44, 0.17);              // ~25° ± 10°, tedy pres tri desetistupnove kose
            ObratRukou(c, 0.44, 0.17, Math.PI);

            Assert.That(c.TiltedGroups, Is.EqualTo(2),
                "kolisajici rucni naklon musi dat JEDNU skupinu na stranu, ne tri poloprazdne");
            Assert.That(c.HasOppositeTilts, Is.True);
            Assert.That(c.Complete, Is.True, "a cele pokryti ma byt hotove");
        }

        [Test]
        public void Mrizka_MaPevnePetRadku_ASouhlasiSKriteriem()
        {
            // Mrizka je to, co vidi obsluha na strance. Musi tedy rikat TOTEZ co kriterium,
            // jinak by vznikly dva seznamy, ktere se rozejdou.
            var c = new MagCalCoverage();
            Obrat(c, 0.0);
            Obrat(c, 0.40);
            Obrat(c, 0.40, Math.PI);

            var m = c.Grid();

            Assert.That(m.Count, Is.EqualTo(MagCalCoverage.TiltRows),
                "radku je pevny pocet - rovina a ctyri smery podlozeni");
            foreach (var r in m)
                Assert.That(r.Counts.Length, Is.EqualTo(MagCalThresholds.AzimuthBins));

            Assert.That(m.Count(r => r.Sufficient), Is.EqualTo(c.TiltGroups),
                "pocet dostatecnych radku MUSI souhlasit s TiltGroups");
            Assert.That(m.Count(r => r.Sufficient && r.Tilted), Is.EqualTo(c.TiltedGroups),
                "a odklonene radky s TiltedGroups");
            Assert.That(m.Sum(r => r.Counts.Sum()), Is.EqualTo(c.Mag.Count),
                "kazdy vzorek lezi prave v jedne bunce");
        }

        [Test]
        public void Mrizka_UkazujeAktualniBunku()
        {
            // Bez toho obsluha nevi, KAM robota natocit - a presne to byl duvod cele zmeny.
            var c = new MagCalCoverage();
            c.Add(0.0, new Vector3(0.4f, 0.1f, -0.3f), Acc(0.0));
            Assert.That(c.CurrentRow, Is.EqualTo(0), "na rovine je to prvni radek");
            Assert.That(c.CurrentAzimuthBin, Is.EqualTo(0));

            c.Add(Math.PI, new Vector3(0.4f, 0.1f, -0.3f), Acc(0.40, Math.PI / 2));
            Assert.That(c.CurrentRow, Is.Not.EqualTo(0), "po naklonu uz to rovina neni");
            Assert.That(c.CurrentAzimuthBin, Is.EqualTo(MagCalThresholds.AzimuthBins / 2));
            Assert.That(c.CurrentTiltDeg, Is.EqualTo(0.40 * 180 / Math.PI).Within(0.5),
                "a odklon ve stupnich, protoze slouceni velikosti ho z radku odstranilo");
        }
    }
}
