using ARBot.Common.Configuration;
using ARBot.Robot;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// <see cref="RuntimeBootstrap.TryConfigure"/>: společný začátek UI i headless aplikace.
    /// Hlídá, že vadná konfigurace skončí hláškou (ne výjimkou), že se <c>maxspeed=</c> propíše do
    /// <see cref="Profile"/> a že bez parametru se <see cref="Profile"/> nesahá.
    /// </summary>
    /// <remarks><see cref="ParamStore.Build"/> přepisuje statické <c>ParamStore.Current</c> a
    /// <see cref="Profile"/> je statický, proto testy neběží paralelně a po sobě hodnoty vrací.</remarks>
    [NonParallelizable]
    public class RuntimeBootstrapTests
    {
        private double maxSpeed, safeDist, prefDist;

        [SetUp]
        public void Zapamatuj()
        {
            maxSpeed = Profile.MaxAllowedSpeed;
            safeDist = Profile.SafeDist;
            prefDist = Profile.PrefDist;
        }

        [TearDown]
        public void Vrat()
        {
            Profile.MaxAllowedSpeed = maxSpeed;
            Profile.SafeDist = safeDist;
            Profile.PrefDist = prefDist;
            ParamStore.Build(new string[0]);
        }

        [Test]
        public void VadnyProfil_VratiHlaskuANevyhodi()
        {
            var log = new List<string>();
            string? chyba = null;

            Assert.DoesNotThrow(() =>
                chyba = RuntimeBootstrap.TryConfigure(
                    new[] { "ARBot.exe", "config=tenhle-profil-neexistuje.cfg" }, log.Add));

            Assert.Multiple(() =>
            {
                Assert.That(chyba, Does.StartWith("Chyba konfigurace:"));
                Assert.That(chyba, Does.Contain("tenhle-profil-neexistuje.cfg"));
                Assert.That(log, Is.Empty, "pri chybe se konfigurace nevypisuje - nic platneho neni");
            });
        }

        [Test]
        public void MaxSpeed_SePropiseDoProfile()
        {
            double v = Math.Min(0.5, Profile.MaxTheoreticalSpeed);
            var log = new List<string>();

            string? chyba = RuntimeBootstrap.TryConfigure(
                new[] { "ARBot.exe", "maxspeed=" + v.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                log.Add);

            Assert.Multiple(() =>
            {
                Assert.That(chyba, Is.Null);
                Assert.That(Profile.MaxAllowedSpeed, Is.EqualTo(v));
                Assert.That(log, Has.Some.Contains("maxspeed="), "ucinna konfigurace se vypisuje pres log");
            });
        }

        [Test]
        public void BezParametru_SeProfileNemeni()
        {
            var log = new List<string>();

            string? chyba = RuntimeBootstrap.TryConfigure(new[] { "ARBot.exe" }, log.Add);

            Assert.Multiple(() =>
            {
                Assert.That(chyba, Is.Null);
                Assert.That(Profile.MaxAllowedSpeed, Is.EqualTo(maxSpeed));
                Assert.That(Profile.SafeDist, Is.EqualTo(safeDist));
                Assert.That(Profile.PrefDist, Is.EqualTo(prefDist));
                Assert.That(log.Count, Is.EqualTo(ParamRegistry.All.Count + 1), "hlavicka + radek na parametr");
            });
        }

        [Test]
        public void CiziArgumentBezRovnitka_NeniChyba()
        {
            // Environment.GetCommandLineArgs() zacina cestou k exe; Avalonia i dotnet pridavaji
            // dalsi prepinace. Nic z toho nesmi start shodit.
            string? chyba = RuntimeBootstrap.TryConfigure(
                new[] { @"C:\app\ARBot.Headless.dll", "--verbose" }, _ => { });

            Assert.That(chyba, Is.Null);
        }
    }
}
