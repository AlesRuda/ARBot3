using System.IO;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Precedence default -&gt; soubor -&gt; prikazova radka a chovani pri vadne konfiguraci.
    /// Viz doc/configuration.md.
    /// </summary>
    public class ParamStoreTests
    {
        private static string TempProfil(string obsah)
        {
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".cfg");
            File.WriteAllText(path, obsah);
            return path;
        }

        [Test]
        public void BezZadaniPlatiDefaultZRegistru()
        {
            var s = ParamStore.Build(new string[0]);
            Assert.That(s.GetBool("mapcorr", false), Is.False);
            Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.Default));
        }

        [Test]
        public void SouborPrebijeDefault()
        {
            string p = TempProfil("mapcorr=true\n");
            try
            {
                var s = ParamStore.Build(new[] { "config=" + p });
                Assert.That(s.GetBool("mapcorr", false), Is.True);
                Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.File));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void PrikazovaRadkaPrebijeSoubor()
        {
            string p = TempProfil("mapcorr=true\n");
            try
            {
                var s = ParamStore.Build(new[] { "config=" + p, "mapcorr=false" });
                Assert.That(s.GetBool("mapcorr", false), Is.False);
                Assert.That(s.OriginOf("mapcorr"), Is.EqualTo(ParamOrigin.CommandLine));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void NeznamyKlicVSouboruJeChyba()
        {
            string p = TempProfil("mapcor=true\n");   // preklep
            try
            {
                var ex = Assert.Throws<ParamFileException>(
                    () => ParamStore.Build(new[] { "config=" + p }));
                Assert.That(ex.Message, Does.Contain("mapcor"));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void NeplatnaHodnotaVSouboruJeChyba()
        {
            string p = TempProfil("mapcorr=ano\n");
            try
            {
                var ex = Assert.Throws<ParamFileException>(
                    () => ParamStore.Build(new[] { "config=" + p }));
                Assert.That(ex.Message, Does.Contain("ano"));
            }
            finally { File.Delete(p); }
        }

        [Test]
        public void ChybejiciSouborJeChyba()
        {
            Assert.Throws<ParamFileException>(
                () => ParamStore.Build(new[] { "config=" + Path.Combine(Path.GetTempPath(), "neni.cfg") }));
        }

        [Test]
        public void NeznamyKlicNaPrikazoveRadceJeJenVarovani()
        {
            // Mezi args jsou i cizi argumenty Avalonie a cesta k exe - tvrda chyba by aplikaci
            // znemoznila spustit.
            var s = ParamStore.Build(new[] { "C:\\app\\ARBot.exe", "--prepinac", "mapcor=true" });
            Assert.That(s.Warnings, Has.Some.Contains("mapcor"));
            Assert.That(s.GetBool("mapcorr", false), Is.False);
        }

        [Test]
        public void NeplatnaHodnotaNaPrikazoveRadceJeChyba()
        {
            var ex = Assert.Throws<ParamFileException>(
                () => ParamStore.Build(new[] { "mapcorr=ano" }));
            Assert.That(ex.Message, Does.Contain("ano"));
        }

        [Test]
        public void GetDouble_CteInvariantCulture()
        {
            var s = ParamStore.Build(new[] { "roadwidth=2.5" });
            Assert.That(s.GetDouble("roadwidth", 3.0), Is.EqualTo(2.5).Within(1e-9));
        }

        [Test]
        public void GetPath_ResiRelativniProtiKoreniRepa()
        {
            var s = ParamStore.Build(new[] { "map=OSM/x.osm" });
            Assert.That(s.GetPath("map", null), Is.EqualTo(RepoPaths.Resolve("OSM/x.osm")));
        }

        [Test]
        public void BoolZPrikazoveRadky_MaHodnotuIPuvod()
        {
            // Diagnostika k nalezu z 31. 8. 2026: v panelu byla u 'virtualhw' PRAZDNA hodnota,
            // pritom sloupec Puvod spravne hlasil „prikazova radka". Tenhle test oddeluje data
            // od zobrazeni - kdyz projde, chyba neni ve ParamStore, ale v UI.
            var s = ParamStore.Build(new[] { "virtualhw=true" });

            Assert.That(s.OriginOf("virtualhw"), Is.EqualTo(ParamOrigin.CommandLine));
            Assert.That(s.Get("virtualhw"), Is.EqualTo("true"),
                        "Get musi vratit surovou hodnotu z prikazove radky");
            Assert.That(s.GetBool("virtualhw", false), Is.True);
        }
    }
}
