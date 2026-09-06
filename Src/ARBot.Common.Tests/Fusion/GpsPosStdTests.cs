using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// <b>Sigma polohy z GPS jako parametr</b> (<c>gpsposstd</c>) — a hlavně proč není jedno,
    /// jakou hodnotu má.
    ///
    /// <para><b>Nač to je (6. 9. 2026).</b> Výchozích 1,5 m je přesnost jednoho fixu, jenže filtr
    /// jich bere 10 za sekundu a počítá je za <b>nezávislé</b>. Změřeno na stojícím robotu
    /// (390 s, <c>records/20260902-222601.rec</c>), že chyba GPS má dekorelační čas ~40 s
    /// a <b>průměr ze 100 fixů je stejně přesný jako jeden</b>. Filtr si tak nasadí ~400× víc
    /// informace, než v datech je: hlásí sigmu polohy 0,074 m a přitom odhad stojícího robota
    /// ujíždí 5,5 m/min — a to rozhýbe world-kotvený occupancy grid.</para>
    ///
    /// <para>Prozatímní léčba je nafouknout sigmu o odmocninu z počtu korelovaných vzorků
    /// (<c>gpsposstd=30</c> v profilu robota). Aby to šlo, musí ta hodnota <b>být parametr</b> —
    /// do 6. 9. 2026 to bylo natvrdo pole ve <see cref="FusionConfig"/>, ačkoli na klíč
    /// <c>gpsposstd</c> odkazoval popis <c>gpsdopsigma</c>. Viz doc/ekf-fusion.md.</para>
    /// </summary>
    public class GpsPosStdTests
    {
        private static GPSState Fix(double dop) => new GPSState
        {
            Quality = GPSState.FixQuality.DgpsFix,
            NumberOfSatellites = 8,
            Hdop = dop,
        };

        [Test]
        public void Parametr_gpsposstd_JeVRegistru_AMaDefaultZFusionConfig()
        {
            Assert.That(ParamRegistry.GpsPosStd, Is.Not.Null);
            Assert.That(ParamRegistry.GpsPosStd.Name, Is.EqualTo("gpsposstd"));
            Assert.That(ParamRegistry.GpsPosStd.Value,
                        Is.EqualTo(new FusionConfig().GpsPosStd).Within(1e-9),
                        "default parametru se musi brat z FusionConfig, ne opisovat");
        }

        [Test]
        public void SigmaPolohy_RosteSGpsPosStd()
        {
            var maly = new FusionConfig { GpsPosStd = 1.5, GpsScaleStdByDop = false };
            var velky = new FusionConfig { GpsPosStd = 30.0, GpsScaleStdByDop = false };

            Assert.That(DefaultMeasurementMapper.PositionStd(Fix(1.0), maly), Is.EqualTo(1.5).Within(1e-9));
            Assert.That(DefaultMeasurementMapper.PositionStd(Fix(1.0), velky), Is.EqualTo(30.0).Within(1e-9));
        }

        [Test]
        public void SigmaPolohy_SeSkalovanimDOP_NasobiObojiCleny()
        {
            // sigma = gpsposstd * max(1, DOP) - obe casti musi zustat nezavisle nastavitelne:
            // gpsposstd resi CASOVOU KORELACI (kolik informace se z fixu smi vzit),
            // DOP resi KVALITU toho konkretniho fixu. Splest je dohromady by znamenalo,
            // ze se korelace "opravi" jen kdyz je zrovna spatna geometrie druzic.
            var cfg = new FusionConfig { GpsPosStd = 30.0, GpsScaleStdByDop = true };
            Assert.That(DefaultMeasurementMapper.PositionStd(Fix(3.7), cfg),
                        Is.EqualTo(30.0 * 3.7).Within(1e-6));

            // DOP pod jednickou sigmu NEZMENSUJE - fix nemuze byt lepsi nez zaklad.
            Assert.That(DefaultMeasurementMapper.PositionStd(Fix(0.5), cfg), Is.EqualTo(30.0).Within(1e-9));
        }

        [Test]
        public void NafouknutiSigmy_OdpovidaZmerenemuPoctuKorelovanychVzorku()
        {
            // Dokumentacni test: 30 m neni cislo od oka. Je to sigma jednoho fixu (1,5 m)
            // nasobena odmocninou z poctu vzorku v jednom dekorelacnim case (10 Hz * 40 s = 400).
            const double sigmaJednohoFixu = 1.5;
            const double kadenceHz = 10.0;
            const double dekorelacniCasS = 40.0;

            double korelovanychVzorku = kadenceHz * dekorelacniCasS;
            double poctivaSigma = sigmaJednohoFixu * System.Math.Sqrt(korelovanychVzorku);

            Assert.That(korelovanychVzorku, Is.EqualTo(400).Within(1e-9));
            Assert.That(poctivaSigma, Is.EqualTo(30.0).Within(0.01),
                        "hodnota v config/pi-provoz.cfg ma odpovidat zmerene korelaci");
        }
    }
}
