using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Rozbor slozenych hodnot parametru. Tentyz kod pouziva registr pri validaci i runtime pri
    /// cteni - viz doc/configuration.md.
    /// </summary>
    public class ParamParsersTests
    {
        [TestCase("1.5,2", true, 1.5, 2.0)]
        [TestCase(" 1.5 , 2 ", true, 1.5, 2.0)]
        [TestCase("1,2,3", true, 1.0, 2.0)]      // dalsi casti se ignoruji (dosavadni chovani)
        [TestCase("-1,0", true, -1.0, 0.0)]
        [TestCase("asd", false, 0.0, 0.0)]
        [TestCase("1", false, 0.0, 0.0)]
        [TestCase("1;2", false, 0.0, 0.0)]
        [TestCase("1,5;2", false, 0.0, 0.0)]
        [TestCase("", false, 0.0, 0.0)]
        public void TryPair(string vstup, bool ceka, double a, double b)
        {
            Assert.That(ParamParsers.TryPair(vstup, out double va, out double vb), Is.EqualTo(ceka));
            if (!ceka) return;
            Assert.That(va, Is.EqualTo(a).Within(1e-9));
            Assert.That(vb, Is.EqualTo(b).Within(1e-9));
        }

        [Test]
        public void TryPair_NullNeprojde()
        {
            Assert.That(ParamParsers.TryPair(null, out _, out _), Is.False);
        }

        [Test]
        public void TryPair_DesetinnaCarkaNeprojde()
        {
            // "1,5" je pro nas DVOJICE 1 a 5, ne cislo jedna a pul - proto je v profilu vsude
            // desetinna tecka. Bez toho by se hodnota tise rozpadla na neco jineho.
            Assert.That(ParamParsers.TryPair("1,5", out double a, out double b), Is.True);
            Assert.That(a, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(b, Is.EqualTo(5.0).Within(1e-9));
        }

        [TestCase("50.1,14.5", true, null)]
        [TestCase("50.1,14.5,90", true, 90.0)]
        [TestCase("asd", false, null)]
        [TestCase("50.1", false, null)]
        public void TryLatLonHeading(string vstup, bool ceka, double? kurz)
        {
            Assert.That(ParamParsers.TryLatLonHeading(vstup, out _, out _, out double? h),
                        Is.EqualTo(ceka));
            if (ceka) Assert.That(h, Is.EqualTo(kurz));
        }

        [Test]
        public void Pair_HlidaMeze_ADuvodRekneCoSeCekalo()
        {
            var validator = ParamParsers.Pair("vlevo,vpravo", minA: 0, minB: 0,
                                              aStrict: true, bStrict: true);

            Assert.That(validator("1,1").Ok, Is.True);
            Assert.That(validator("0,1").Ok, Is.False);       // musi byt KLADNE
            Assert.That(validator("1,-1").Ok, Is.False);
            Assert.That(validator("asd").Error, Does.Contain("vlevo,vpravo"));
        }

        [Test]
        public void LatLonOrGps_BereIKlicoveSlovo()
        {
            Assert.That(ParamParsers.LatLonOrGps("gps").Ok, Is.True);
            Assert.That(ParamParsers.LatLonOrGps("GPS").Ok, Is.True);
            Assert.That(ParamParsers.LatLonOrGps("50.1,14.5").Ok, Is.True);
            Assert.That(ParamParsers.LatLonOrGps("asd").Ok, Is.False);
        }

        [Test]
        public void PoseError_JedeZTehozParseruJakoRuntime()
        {
            Assert.That(ParamParsers.PoseError("0.6,-0.4").Ok, Is.True);
            Assert.That(ParamParsers.PoseError("0.6,-0.4,3").Ok, Is.True);
            Assert.That(ParamParsers.PoseError("asd").Ok, Is.False);
        }
    }
}
