using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Parser a zapis profilu klic=hodnota. Viz doc/configuration.md.
    /// </summary>
    public class ParamFileTests
    {
        private static List<KeyValuePair<string, string>> Parse(params string[] lines)
            => ParamFile.Parse(lines);

        [Test]
        public void Parse_ZakladniDvojice()
        {
            var v = Parse("mapcorr=true");
            Assert.That(v, Has.Count.EqualTo(1));
            Assert.That(v[0].Key, Is.EqualTo("mapcorr"));
            Assert.That(v[0].Value, Is.EqualTo("true"));
        }

        [Test]
        public void Parse_IgnorujeKomentareAPrazdneRadky()
        {
            var v = Parse("# komentar", "", "   ", "mission=freerun", "# dalsi");
            Assert.That(v.Select(p => p.Key), Is.EqualTo(new[] { "mission" }));
        }

        [Test]
        public void Parse_OrezavaMezeryKolemRovnitka()
        {
            var v = Parse("  mission  =  freerun  ");
            Assert.That(v[0].Key, Is.EqualTo("mission"));
            Assert.That(v[0].Value, Is.EqualTo("freerun"));
        }

        [Test]
        public void Parse_HodnotaSmiObsahovatRovnitko()
        {
            // Deli se na PRVNIM rovnitku - budouci slozena hodnota by na tom nemela ztroskotat.
            var v = Parse("st_out=a=b");
            Assert.That(v[0].Value, Is.EqualTo("a=b"));
        }

        [Test]
        public void Parse_PrazdnaHodnotaJePlatna()
        {
            // qrcamera= (prazdne) ma v ARBotRuntime vlastni vyznam: skenuji se VSECHNY kamery.
            var v = Parse("qrcamera=");
            Assert.That(v, Has.Count.EqualTo(1));
            Assert.That(v[0].Value, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Parse_RadekBezRovnitkaJeChyba()
        {
            var ex = Assert.Throws<ParamFileException>(() => Parse("mapcorr"));
            Assert.That(ex.Message, Does.Contain("mapcorr"));
            Assert.That(ex.Message, Does.Contain("1"));      // cislo radku
        }

        [Test]
        public void Parse_DuplicitniKlicJeChyba()
        {
            var ex = Assert.Throws<ParamFileException>(() => Parse("mapcorr=true", "MAPCORR=false"));
            Assert.That(ex.Message, Does.Contain("MAPCORR").IgnoreCase);
        }

        [Test]
        public void Format_ZapisSeDaPrecistZpatky()
        {
            var hodnoty = new Dictionary<string, string> { { "mapcorr", "true" }, { "mission", "freerun" } };
            string text = ParamFile.Format(hodnoty);
            var zpet = ParamFile.Parse(text.Split('\n'));
            Assert.That(zpet.ToDictionary(p => p.Key, p => p.Value), Is.EquivalentTo(hodnoty));
        }

        [Test]
        public void Format_NeznamyKlicSeZapiseBezKomentare()
        {
            string text = ParamFile.Format(new Dictionary<string, string> { { "nezname_x", "1" } });
            Assert.That(text, Does.Contain("nezname_x=1"));
        }

        [Test]
        public void Format_ProfilSPopisyJdeZpatkyNacistDoStore()
        {
            // End-to-end pro soubor: to, co zapise panel (hodnoty + komentare s popisy a nadpisy
            // kategorii z registru), musi ParamStore.Build precist bez chyby a se stejnymi
            // hodnotami. Chyti to napr. popis, ktery by se omylem zapsal bez '#'.
            var hodnoty = new Dictionary<string, string>
            {
                { "mapcorr", "true" }, { "mission", "freerun" }, { "roadwidth", "2.5" },
            };
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".cfg");
            File.WriteAllText(path, ParamFile.Format(hodnoty));
            try
            {
                var s = ParamStore.Build(new[] { "config=" + path });
                Assert.That(s.GetBool("mapcorr", false), Is.True);
                Assert.That(s.GetString("mission", null), Is.EqualTo("freerun"));
                Assert.That(s.GetDouble("roadwidth", 3.0), Is.EqualTo(2.5).Within(1e-9));
                Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.File));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Format_PiseKomentarSPopisemZRegistru()
        {
            string text = ParamFile.Format(new Dictionary<string, string> { { "mapcorr", "true" } });
            Assert.That(text, Does.Contain("# --- Fuze a lokalizace ---"));
            Assert.That(text, Does.Contain("# Zapina korelaci"));
            Assert.That(text, Does.Contain("mapcorr=true"));
        }
    }
}
