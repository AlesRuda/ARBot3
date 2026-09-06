using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Fusion;

/// <summary>
/// Kvalita GPS fixu ve fúzi (6. 9. 2026): brána na počet družic a DOP + škálování sigmy podle DOP.
///
/// <para><b>Proč to vzniklo:</b> do té změny brala fúze <b>každý</b> fix, u kterého
/// <c>GPSState.IsFixed</c> řekl „ano", a vždy s toutéž sigmou — počet družic a DOP zpráva nese, ale
/// nikdo se na ně nedíval. Na robotu se to projevilo tak, že odhad polohy ujel ~570 m jedním směrem
/// rychlostí ~0,7 m/s, <b>zatímco robot stál</b> a rychlost ve stavu byla nula: polohu tedy netáhla
/// predikce, ale bezvýhradně přijímaná měření.</para>
///
/// <para>Klíčová zásada, kterou testy hlídají: <b>neznámá hodnota není špatná hodnota</b> —
/// přijímač, který počet družic nebo DOP nehlásí (nula), branou projde.</para>
/// </summary>
public class GpsKvalitaTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);

    private static GPSState Fix(GPSState.FixQuality kvalita = GPSState.FixQuality.DgpsFix,
                                int druzic = 9, double dop = 1.0)
        => new GPSState
        {
            Latitude = Conversions.Deg2Rad(49.21),
            Longitude = Conversions.Deg2Rad(16.60),
            Quality = kvalita,
            NumberOfSatellites = druzic,
            Hdop = dop,
            TimeStamp = T0,
        };

    // ---------------- Brána ----------------

    [Test]
    public void DobryFix_Projde()
    {
        Assert.That(DefaultMeasurementMapper.PositionRejectReason(Fix(), new FusionConfig()), Is.Null);
    }

    [Test]
    public void MaloDruzic_SeZahodi()
    {
        var duvod = DefaultMeasurementMapper.PositionRejectReason(Fix(druzic: 3), new FusionConfig());

        Assert.That(duvod, Does.Contain("druzic"), "důvod musí říct, co je špatně");
    }

    [Test]
    public void VysokyDop_SeZahodi()
    {
        var duvod = DefaultMeasurementMapper.PositionRejectReason(Fix(dop: 25), new FusionConfig());

        Assert.That(duvod, Does.Contain("DOP"));
    }

    [Test]
    public void NeplatnyFix_SeZahodi()
    {
        // Estimated = mrtvý odhad bez družic. Právě takové řešení ujíždí jedním směrem, i když
        // robot stojí — a u-blox ho do 6. 9. 2026 hlásil jako platný GpsFix (viz uBloxGps).
        var duvod = DefaultMeasurementMapper.PositionRejectReason(
            Fix(kvalita: GPSState.FixQuality.Estimated), new FusionConfig());

        Assert.That(duvod, Does.Contain("neplatny fix"));
    }

    [Test]
    public void NehlasenyPocetDruzicANeznamyDop_Projdou()
    {
        // Neznámá hodnota není špatná hodnota: přijímač, který je nehlásí, se nesmí umlčet.
        var duvod = DefaultMeasurementMapper.PositionRejectReason(Fix(druzic: 0, dop: 0), new FusionConfig());

        Assert.That(duvod, Is.Null);
    }

    [Test]
    public void VypnutaBrana_PustiCokoliSPlatnymFixem()
    {
        var cfg = new FusionConfig { GpsMinSatellites = 0, GpsMaxDop = 0 };

        Assert.Multiple(() =>
        {
            Assert.That(DefaultMeasurementMapper.PositionRejectReason(Fix(druzic: 1, dop: 99), cfg), Is.Null);
            // Neplatný fix ale neprojde ani s vypnutou bránou - to není kritérium kvality.
            Assert.That(DefaultMeasurementMapper.PositionRejectReason(
                Fix(kvalita: GPSState.FixQuality.Invalid), cfg), Is.Not.Null);
        });
    }

    // ---------------- Sigma podle DOP ----------------

    [Test]
    public void SigmaSeNasobiDop()
    {
        var cfg = new FusionConfig();

        Assert.That(DefaultMeasurementMapper.PositionStd(Fix(dop: 4), cfg),
                    Is.EqualTo(cfg.GpsPosStd * 4).Within(1e-9));
    }

    [Test]
    public void DopPodJednu_SigmuNezmensi()
    {
        // DOP < 1 by jinak snížil sigmu pod deklarovanou přesnost přijímače.
        var cfg = new FusionConfig();

        Assert.That(DefaultMeasurementMapper.PositionStd(Fix(dop: 0.6), cfg), Is.EqualTo(cfg.GpsPosStd));
    }

    [Test]
    public void NeznamyDopNeboVypnuteSkalovani_DaZakladniSigmu()
    {
        var cfg = new FusionConfig();
        var bezSkalovani = new FusionConfig { GpsScaleStdByDop = false };

        Assert.Multiple(() =>
        {
            Assert.That(DefaultMeasurementMapper.PositionStd(Fix(dop: 0), cfg), Is.EqualTo(cfg.GpsPosStd));
            Assert.That(DefaultMeasurementMapper.PositionStd(Fix(dop: 8), bezSkalovani),
                        Is.EqualTo(cfg.GpsPosStd));
        });
    }

    // ---------------- Celá cesta přes mapper ----------------

    [Test]
    public void ZahozenyFix_NevyrobiZadneMereniPolohy()
    {
        var cfg = new FusionConfig { GeoReference = GeoReference.FromDegrees(49.21, 16.60) };
        var mapper = new DefaultMeasurementMapper(cfg);

        var mereni = mapper.ToMeasurements(Fix(druzic: 2)).ToList();

        Assert.That(mereni, Is.Empty, "špatný fix nesmí do fúze pustit ani kurz nebo rychlost");
    }

    [Test]
    public void PrijatyFix_MaSigmuPodleDop()
    {
        var cfg = new FusionConfig { GeoReference = GeoReference.FromDegrees(49.21, 16.60) };
        var mapper = new DefaultMeasurementMapper(cfg);

        var poloha = mapper.ToMeasurements(Fix(dop: 3)).OfType<PositionMeasurement>().Single();

        // Sigma se do měření propíše, jinak by škálování bylo jen ozdoba. V měření je jako
        // kovariance, tedy sigma na druhou.
        double ocekavana = cfg.GpsPosStd * 3;
        Assert.That(poloha.NoiseCovariance[0, 0], Is.EqualTo(ocekavana * ocekavana).Within(1e-9));
    }
}
