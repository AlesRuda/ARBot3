using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Parametr <c>web=</c> (port webovehu nahledu headless). Neplatny port ma byt chyba pri startu,
    /// ne tichy pad na default. Viz doc/plan-headless-web.md a doc/configuration.md.
    /// </summary>
    /// <remarks><see cref="ParamStore.Build"/> prepisuje staticke <c>ParamStore.Current</c>, proto
    /// se na konci vraci prazdny store a testy nebezi paralelne.</remarks>
    [NonParallelizable]
    public class WebPortParamTests
    {
        [TearDown]
        public void Uklid() => ParamStore.Build(new string[0]);

        [Test]
        public void VychoziJeVypnuto()
        {
            ParamStore.Build(new string[0]);
            Assert.That(ParamRegistry.Web.Value, Is.EqualTo(0), "nahled je ve vychozim stavu vypnuty");
        }

        [TestCase("8080")]
        [TestCase("1024")]
        [TestCase("65535")]
        [TestCase("0")]
        public void PlatnyPortProjde(string hodnota)
        {
            Assert.DoesNotThrow(() => ParamStore.Build(new[] { "web=" + hodnota }));
            Assert.That(ParamRegistry.Web.Value,
                        Is.EqualTo(double.Parse(hodnota, System.Globalization.CultureInfo.InvariantCulture)));
        }

        [TestCase("80", "privilegovany port")]
        [TestCase("1023", "privilegovany port")]
        [TestCase("65536", "nad rozsahem")]
        [TestCase("-1", "zaporny")]
        [TestCase("8080.5", "necele cislo")]
        [TestCase("osmdesat", "neni cislo")]
        public void NeplatnyPortJeChybaPriStartu(string hodnota, string proc)
        {
            var ex = Assert.Throws<ParamFileException>(() => ParamStore.Build(new[] { "web=" + hodnota }),
                                                       $"{hodnota}: {proc}");
            Assert.That(ex.Message, Does.Contain("web"));
        }
    }
}
