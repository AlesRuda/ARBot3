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
        public void CiziArgumentNaPrikazoveRadceJeJenVarovani()
        {
            // Mezi args jsou i cizi argumenty Avalonie a cesta k exe - tvrda chyba by aplikaci
            // znemoznila spustit. Klic, ktery se nepodoba nicemu z registru, tedy jen varuje.
            var s = ParamStore.Build(new[] { "C:\\app\\ARBot.exe", "--prepinac", "--prepinac=1" });
            Assert.That(s.Warnings, Has.Some.Contains("--prepinac"));
        }

        /// <summary>
        /// Klic, ktery se PODOBA znamemu parametru, start zastavi - a hlaska rekne, co se myslelo.
        ///
        /// <para>Nalezeno 12. 9. 2026: <c>cfg=track_hv.cfg</c> misto <c>config=</c> se tise
        /// zahodilo, takze se nenacetl profil, nebyla mapa, nezalozil se virtualni HW a stranka
        /// nahledu hlasila, ze motory nemaji nouzove zastaveni - pricina pet kroku daleko.</para>
        /// </summary>
        [TestCase("mapcor=true", "mapcorr")]          // preklep: jedna uprava
        [TestCase("cfg=profil.cfg", "config")]        // zkratka: podposloupnost (vzdalenost je 3)
        [TestCase("missin=freerun", "mission")]
        public void PodobnyKlicNaPrikazoveRadceJeChyba(string argument, string ocekavanyNavrh)
        {
            var ex = Assert.Throws<ParamFileException>(() => ParamStore.Build(new[] { argument }));
            Assert.That(ex.Message, Does.Contain(ocekavanyNavrh));
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
