using System.IO;
using ARBot;
using ARBot.Robot;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// <see cref="BuildInfo"/>: rozbor <c>AssemblyInformationalVersion</c>, který skládá
    /// <c>Src/Directory.Build.props</c>. Verze jde do hlavičky crash logu a do záznamu, takže se
    /// hlídá obojí — že se z razítkovaného tvaru vytáhne všechno, a hlavně že <b>nesmyslný nebo
    /// chybějící atribut nevyhodí výjimku</b>: rozbor běží i uvnitř zápisu pádu, kde by druhá
    /// výjimka smazala jedinou stopu po té první.
    /// </summary>
    public class BuildInfoTests
    {
        [Test]
        public void RazitkovanaVerze_RozebereVsechnySlozky()
        {
            var b = BuildInfo.Parse("1.0.247.11448+2316de12 (2026-09-05 06:21 UTC)");

            Assert.Multiple(() =>
            {
                Assert.That(b.Version, Is.EqualTo("1.0.247.11448"));
                Assert.That(b.GitHash, Is.EqualTo("2316de12"));
                Assert.That(b.IsDirty, Is.False);
                Assert.That(b.IsDev, Is.False);
                Assert.That(b.BuildTimeUtc, Is.EqualTo(new DateTime(2026, 9, 5, 6, 21, 0, DateTimeKind.Utc)));
            });
        }

        [Test]
        public void RozpracovanaKopie_JeVidetJakoDirty()
        {
            var b = BuildInfo.Parse("1.0.247.11448+2316de12-dirty (2026-09-05 06:21 UTC)");

            Assert.Multiple(() =>
            {
                Assert.That(b.IsDirty, Is.True);
                // Hash se pozna bez pripony, jinak by neslo dohledat commit.
                Assert.That(b.GitHash, Is.EqualTo("2316de12"));
                Assert.That(b.Popis(), Does.Contain("2316de12-dirty"));
            });
        }

        [Test]
        public void BezGitu_ZustaneVerzeACas()
        {
            // Stavelo se tam, kde neni .git (nasazeny strom, archiv) - hash proste neni.
            var b = BuildInfo.Parse("1.0.247.11448 (2026-09-05 06:21 UTC)");

            Assert.Multiple(() =>
            {
                Assert.That(b.Version, Is.EqualTo("1.0.247.11448"));
                Assert.That(b.GitHash, Is.Empty);
                Assert.That(b.IsDirty, Is.False);
                Assert.That(b.BuildTimeUtc, Is.Not.Null);
                Assert.That(b.Popis(), Does.Contain("build 2026-09-05 06:21 UTC"));
            });
        }

        [Test]
        public void VyvojovyBuild_HlasiSeJakoNerazitkovany()
        {
            var b = BuildInfo.Parse("0.0.0.0-dev");

            Assert.Multiple(() =>
            {
                Assert.That(b.IsDev, Is.True);
                Assert.That(b.Version, Is.EqualTo("0.0.0.0"));
                Assert.That(b.BuildTimeUtc, Is.Null);
                Assert.That(b.Popis(), Does.Contain("nerazítkovaný"));
            });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ChybejiciAtribut_JeNeznamaVerzeANePad(string? info)
        {
            var b = BuildInfo.Parse(info);

            Assert.Multiple(() =>
            {
                Assert.That(b.Version, Is.EqualTo("neznámá"));
                Assert.That(b.Popis(), Is.EqualTo("neznámá"));
            });
        }

        [TestCase("nesmysl")]
        [TestCase("1.0.247.11448+")]
        [TestCase("1.0.247.11448+hash (co to je")]
        [TestCase("+ ()")]
        public void NesmyslnyAtribut_Nevyhodi(string info)
        {
            BuildInfo b = null!;

            Assert.DoesNotThrow(() => b = BuildInfo.Parse(info));
            Assert.That(b.Popis(), Is.Not.Null);
        }

        [Test]
        public void NecitelnyCasBuildu_SeZahodi_VerzeZustane()
        {
            // Cas je doplnkovy udaj; kdyz se neprecte, verze a hash musi platit dal.
            var b = BuildInfo.Parse("1.0.247.11448+2316de12 (vcera)");

            Assert.Multiple(() =>
            {
                Assert.That(b.BuildTimeUtc, Is.Null);
                Assert.That(b.Version, Is.EqualTo("1.0.247.11448"));
                Assert.That(b.GitHash, Is.EqualTo("2316de12"));
            });
        }

        [Test]
        public void Raw_NeseCelyAtribut()
        {
            const string info = "1.0.247.11448+2316de12-dirty (2026-09-05 06:21 UTC)";

            Assert.That(BuildInfo.Parse(info).Raw, Is.EqualTo(info));
        }

        [Test]
        public void CrashLog_MaVHlavicceVerzi()
        {
            // Verze v crash logu je hlavni duvod, proc BuildInfo vzniklo: po padu na zarizeni je to
            // jedina stopa, ktera rekne, KTERA binarka spadla. Zapis nesmi spadnout ani zmenit
            // navratovou hodnotu, kdyz je verze neznama - proto se hlida cely radek, ne jen cislo.
            string? cesta = CrashLog.Write("test BuildInfoTests", new InvalidOperationException("zkouska"), terminating: false);

            Assert.That(cesta, Is.Not.Null, "crash log se nezapsal");
            string obsah = File.ReadAllText(cesta!);
            Assert.Multiple(() =>
            {
                Assert.That(obsah, Does.Contain("verze:"));
                Assert.That(obsah, Does.Contain(BuildInfo.Current.Version));
            });

            try { File.Delete(cesta!); } catch { }
        }

        [Test]
        public void Current_NecteNic_AlePopisVzdyNeco()
        {
            // Vstupni assembly je pri testech testhost, takze na hodnotu se spolehnout nejde -
            // hlida se jen, ze cteni atributu z bezici assembly nepadne a neco vrati.
            Assert.That(BuildInfo.Current.Popis(), Is.Not.Empty);
        }
    }
}
