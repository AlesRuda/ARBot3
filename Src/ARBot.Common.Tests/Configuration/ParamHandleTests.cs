using ARBot.Common.Configuration;
using ARBot.Common.Missions;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Typovane odkazy na parametry (<c>ParamRegistry.X.Value</c>): default je definovany presne
    /// jednou v registru a u parametru s pravdou v kodu (Profile, konfiguracni tridy) je z ni
    /// odvozeny, ne opsany. Viz doc/configuration.md.
    /// </summary>
    /// <remarks><see cref="ParamStore.Build"/> prepisuje staticke <c>ParamStore.Current</c>, proto
    /// se na konci vraci prazdny store a testy nebezi paralelne.</remarks>
    [NonParallelizable]
    public class ParamHandleTests
    {
        [Test]
        public void BezZadaniPlatiDefaultZRegistru()
        {
            ParamStore.Build(new string[0]);
            Assert.Multiple(() =>
            {
                Assert.That(ParamRegistry.NoUart.Value, Is.False);
                Assert.That(ParamRegistry.StRobot.Value, Is.True);
                Assert.That(ParamRegistry.RoadWidth.Value, Is.EqualTo(3.0));
                Assert.That(ParamRegistry.Mission.Value, Is.EqualTo("none"));
                Assert.That(ParamRegistry.Mission.Is("NONE"), Is.True, "vycet se porovnava bez ohledu na velikost pismen");
                Assert.That(ParamRegistry.Map.Value, Is.Null, "cesta bez defaultu a bez zadani je null");
                Assert.That(ParamRegistry.QrCamera.IsEmpty, Is.True);
                Assert.That(ParamRegistry.NoUart.IsSet, Is.False);
                Assert.That(ParamRegistry.NoUart.Origin, Is.EqualTo(ParamOrigin.Default));
            });
        }

        [Test]
        public void DefaultZKoduJeOdvozeny_NeOpsany()
        {
            // Kdyz nekdo zmeni hodnotu v Profile nebo v konfiguracni tride, registr ji ma hned -
            // presne to, co se 3. 9. 2026 u freerunlook (3 -> 1,5) delalo rucne na dvou mistech.
            ParamStore.Build(new string[0]);
            Assert.Multiple(() =>
            {
                Assert.That(ParamRegistry.MaxSpeed.Value, Is.EqualTo(Profile.MaxAllowedSpeed));
                Assert.That(ParamRegistry.SafeDist.Value, Is.EqualTo(Profile.SafeDist));
                Assert.That(ParamRegistry.UartAHRS.Value, Is.EqualTo(Profile.PortAHRS));
                Assert.That(ParamRegistry.UartMotor.Value, Is.EqualTo(Profile.PortMotor));
                Assert.That(ParamRegistry.UartGPS.Value, Is.EqualTo(Profile.PortGPS));
                Assert.That(ParamRegistry.FreeRunLook.Value, Is.EqualTo(new FreeRunConfig().LookaheadM));
                Assert.That(ParamRegistry.DepotFix.Value, Is.EqualTo(new RobotourConfig().DepotFixSec));
                Assert.That(ParamRegistry.DepthNoise.Value, Is.EqualTo(new SyntheticSceneOptions().DepthNoiseM));
                Assert.That(ParamRegistry.GrassRough.Value, Is.EqualTo(new SyntheticSceneOptions().GrassRoughnessM));
                Assert.That(ParamRegistry.GrassHeight.Value, Is.EqualTo(new SyntheticSceneOptions().GrassHeightM));
            });
        }

        [Test]
        public void ZadaniPrebijeDefault_AJeVidetPuvod()
        {
            try
            {
                ParamStore.Build(new[] { "roadwidth=2.5", "mission=FreeRun", "map=OSM/SyntetickyRovny.osm", "no_uart=true" });
                Assert.Multiple(() =>
                {
                    Assert.That(ParamRegistry.RoadWidth.Value, Is.EqualTo(2.5));
                    Assert.That(ParamRegistry.Mission.Is("freerun"), Is.True);
                    Assert.That(ParamRegistry.NoUart.Value, Is.True);
                    Assert.That(ParamRegistry.NoUart.IsSet, Is.True);
                    Assert.That(ParamRegistry.NoUart.Origin, Is.EqualTo(ParamOrigin.CommandLine));
                    Assert.That(ParamRegistry.Map.Value, Does.EndWith("SyntetickyRovny.osm"));
                    Assert.That(System.IO.Path.IsPathRooted(ParamRegistry.Map.Value), Is.True,
                                "relativni cesta se resi proti koreni repa");
                    Assert.That(ParamRegistry.StRobot.IsSet, Is.False, "nezadane zustava default");
                });
            }
            finally
            {
                ParamStore.Build(new string[0]);
            }
        }

        [Test]
        public void VypisKonfigurace_MaRadekProKazdyParametrSPuvodem()
        {
            try
            {
                var s = ParamStore.Build(new[] { "no_uart=true" });
                var radky = System.Linq.Enumerable.ToList(s.DescribeAll());

                Assert.Multiple(() =>
                {
                    Assert.That(radky.Count, Is.EqualTo(ParamRegistry.All.Count + 1), "hlavicka + radek na parametr");
                    Assert.That(radky, Has.Some.Contains("no_uart=true"));
                    Assert.That(radky, Has.Some.Contains("prikazova radka"));
                    Assert.That(radky, Has.Some.Contains("roadwidth=3"));
                });
            }
            finally
            {
                ParamStore.Build(new string[0]);
            }
        }
    }
}
