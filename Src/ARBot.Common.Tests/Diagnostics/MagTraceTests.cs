using System;
using System.Numerics;
using ARBot.Common.Diagnostics;
using NUnit.Framework;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Testy stopy magnetického pole (<see cref="MagTrace"/>) — měřidla pro test „ovlivňují
    /// magnetometr kabely?". Podstatné jsou dvě věci: že rozdíl proti nule odpovídá <b>skutečně
    /// vloženému</b> rušení, a že měřidlo <b>samo řekne</b>, když robot mezitím pootočil (jinak by
    /// vydávalo zemskou složku za rušení — past, na které se to při rozboru záznamu už jednou
    /// zlomilo, viz doc/imu-and-frames.md).
    /// </summary>
    [TestFixture]
    public class MagTraceTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Naplní stopu konstantním polem; <paramref name="omegaDegS"/> simuluje klid/pohyb.</summary>
        private static void Napln(MagTrace t, Vector3 pole, double odS, double doS,
                                  double omegaDegS = 0, double yawRad = 0, double hz = 100)
        {
            for (double s = odS; s < doS; s += 1.0 / hz)
                t.Pridej(T0.AddSeconds(s), pole,
                         new Vector3(0, 0, (float)(omegaDegS * Math.PI / 180.0)), yawRad);
        }

        [Test]
        public void PrazdnaStopa_NemaPosledni_ANepadne()
        {
            var snap = new MagTrace().Snimek();
            Assert.That(snap.Vzorky, Is.Empty);
            Assert.That(snap.Posledni, Is.Null);
            Assert.That(snap.Klid, Is.False, "prazdna stopa nesmi tvrdit, ze odecet plati");
        }

        [Test]
        public void RozdilProtiNule_OdpovidaVlozenemuRuseni()
        {
            // Tohle je ta vlastnost, kvuli ktere meridlo vzniklo: vlozim znamy offset a musi
            // vyjit zpatky. Cisla jsou z realneho nalezu - kabely ke kameram delaly 6,4 mG.
            var t = new MagTrace();
            var zaklad = new Vector3(0.199f, 0.010f, -0.447f);
            var ruseni = new Vector3(0.0048f, -0.0036f, -0.0023f);

            Napln(t, zaklad, 0, 2);
            Assert.That(t.Vynuluj(), Is.True);
            Napln(t, zaklad + ruseni, 2, 4);

            var snap = t.Snimek();
            Assert.That(snap.Rozdil.X * 1000, Is.EqualTo(4.8).Within(0.05));
            Assert.That(snap.Rozdil.Y * 1000, Is.EqualTo(-3.6).Within(0.05));
            Assert.That(snap.Rozdil.Z * 1000, Is.EqualTo(-2.3).Within(0.05));
            Assert.That(snap.Klid, Is.True, "robot stal, odecet ma platit");
        }

        [Test]
        public void BezNuly_SePocitaProtiPrumeruOkna()
        {
            var t = new MagTrace();
            Napln(t, new Vector3(0.2f, 0, -0.4f), 0, 1);
            Napln(t, new Vector3(0.3f, 0, -0.4f), 1, 2);

            var snap = t.Snimek();
            Assert.That(snap.Nula, Is.Null);
            Assert.That(snap.Reference.X, Is.EqualTo(0.25f).Within(0.005),
                        "bez nuly je referenci prumer okna, aby byl graf vycentrovany");
            Assert.That(snap.Rozdil.X, Is.EqualTo(0.05f).Within(0.005));
        }

        [Test]
        public void OtoceniRobotu_ShodiVerdiktKlid()
        {
            // ⚠️ Jadro meridla: pootoceni o 1 stupen udela ve vodorovne slozce ~3,5 mG, tedy vic
            // nez cely hledany efekt. Kdyz se robot otoci, odecet NESMI platit.
            var t = new MagTrace();
            Napln(t, new Vector3(0.2f, 0, -0.4f), 0, 2, omegaDegS: 0, yawRad: 0);
            Assert.That(t.Vynuluj(), Is.True);
            Napln(t, new Vector3(0.25f, 0, -0.4f), 2, 4, omegaDegS: 0,
                  yawRad: 10 * Math.PI / 180.0);

            var snap = t.Snimek();
            Assert.That(snap.YawOdNulyDeg, Is.Not.Null);
            Assert.That(snap.YawOdNulyDeg.Value, Is.EqualTo(10).Within(0.1));
            Assert.That(snap.Klid, Is.False,
                        "robot se od nuly otocil o 10 stupnu - odecet neplati");
        }

        [Test]
        public void TociciSeRobot_ShodiVerdiktIBezNuly()
        {
            var t = new MagTrace();
            Napln(t, new Vector3(0.2f, 0, -0.4f), 0, 2, omegaDegS: 20);
            Assert.That(t.Snimek().Klid, Is.False, "|w| nad prahem = odecet neplati");
        }

        [Test]
        public void ChybejiciGyro_SeChovaJakoNEPLATI()
        {
            // "Nevim" musi byt stejne pristne jako "hybe se" - jinak by meridlo u senzoru bez
            // gyra tise vydavalo otoceni za ruseni.
            var t = new MagTrace();
            for (double s = 0; s < 2; s += 0.01)
                t.Pridej(T0.AddSeconds(s), new Vector3(0.2f, 0, -0.4f), null, null);

            var snap = t.Snimek();
            Assert.That(snap.Vzorky, Is.Not.Empty);
            Assert.That(snap.Klid, Is.False);
        }

        [Test]
        public void Okno_ZahazujeStarsiVzorky()
        {
            var t = new MagTrace(TimeSpan.FromSeconds(2));
            Napln(t, new Vector3(0.2f, 0, -0.4f), 0, 5);

            var snap = t.Snimek();
            Assert.That(snap.TDo - snap.TOd, Is.LessThanOrEqualTo(2.01));
            Assert.That(snap.Vzorky.Count, Is.LessThanOrEqualTo(210));
        }

        [Test]
        public void RozpetiVelikosti_MeriTvrdeZelezo()
        {
            // Pres pomalou otocku je rozpeti |B| primo mira tvrdeho zeleza (12. 9. 2026: 0,019 G
            // po kalibraci, 14. 9.: 0,177 G). Tady se simuluje otocka s vlozenym hard-iron
            // offsetem ve vodorovne rovine.
            var t = new MagTrace();
            const float bh = 0.199f, bz = -0.447f, hard = 0.05f;
            double s = 0;
            for (int i = 0; i <= 360; i++, s += 0.05)
            {
                double a = i * Math.PI / 180.0;
                // Zemska vodorovna slozka se v telese otaci, hard iron je v telese konstantni.
                var pole = new Vector3((float)(bh * Math.Cos(a)) + hard,
                                       (float)(bh * Math.Sin(a)), bz);
                t.Pridej(T0.AddSeconds(s), pole, new Vector3(0, 0, 0.3f), a);
            }

            var snap = t.Snimek();
            double ocekavaneMax = Math.Sqrt(Math.Pow(bh + hard, 2) + bz * bz);
            double ocekavaneMin = Math.Sqrt(Math.Pow(bh - hard, 2) + bz * bz);
            Assert.That(snap.MaxG, Is.EqualTo(ocekavaneMax).Within(0.002));
            Assert.That(snap.MinG, Is.EqualTo(ocekavaneMin).Within(0.002));
            Assert.That(snap.RozpetiG, Is.EqualTo(ocekavaneMax - ocekavaneMin).Within(0.003));
        }

        [Test]
        public void SkokCasuZpet_ZahodiHistorii()
        {
            // Prepnuti na jiny zaznam / restart senzoru: dva nesouvisejici useky se nesmi
            // potkat v jednom grafu.
            var t = new MagTrace();
            Napln(t, new Vector3(0.2f, 0, -0.4f), 0, 2);
            t.Vynuluj();
            t.Pridej(T0.AddSeconds(-100), new Vector3(0.3f, 0, -0.4f), Vector3.Zero, 0);

            var snap = t.Snimek();
            Assert.That(snap.Vzorky.Count, Is.EqualTo(1));
            Assert.That(snap.Nula, Is.Null, "nula ze stareho useku by neplatila");
        }

        [Test]
        public void VynulovatBezDat_Nepadne_AVratiFalse()
        {
            var t = new MagTrace();
            Assert.That(t.Vynuluj(), Is.False);
            t.Pridej(T0, new Vector3(0.2f, 0, -0.4f), Vector3.Zero, 0);
            Assert.That(t.Vynuluj(), Is.False, "nula z jednoho vzorku by nesla jeho sum");
        }

        [Test]
        public void ZrusNulu_VratiReferenciNaPrumerOkna()
        {
            var t = new MagTrace();
            Napln(t, new Vector3(0.2f, 0, -0.4f), 0, 1);
            Assert.That(t.Vynuluj(), Is.True);
            Assert.That(t.Snimek().Nula, Is.Not.Null);

            t.ZrusNulu();
            var snap = t.Snimek();
            Assert.That(snap.Nula, Is.Null);
            Assert.That(snap.Rozdil.Length(), Is.LessThan(1e-4),
                        "proti prumeru konstantniho useku je rozdil nula");
        }
    }
}
