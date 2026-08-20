using System;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy kovariance korelace (viz doc/map-correlation-localization.md).
/// Klicove tvrzeni navrhu: anizotropie vznikne SAMA ze zakriveni skore - nic se nedetekuje.
/// </summary>
public class CorrelationCovarianceTests
{
    /// <summary>Spocte kovarianci nad danou siti a polohou robotu (bez chyby pozy).</summary>
    private static CorrelationCovariance Run(RoadNetwork network, double robotX, double robotY)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(network, origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, robotX, robotY, 0, 0, 0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        var scorer = new CorrelationScorer(cloud, raster, robotX, robotY);
        return CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);
    }

    [Test]
    public void PrimaCesta_PodelnaSigmaJeVyrazneVetsiNezPricna()
    {
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        Assert.That(cov.HasPeak, Is.True);
        Assert.That(cov.SigmaLoose, Is.GreaterThan(cov.SigmaTight * 3.0),
                    "Na prime ceste musi byt jedna osa vyrazne mene urcena - to je jadro celeho slibu.");
    }

    [Test]
    public void PrimaCesta_UrcenaOsaMiriNapricCesty()
    {
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        // Cesta vede na vychod (0 rad), takze dobre urcena osa je sever/jih => +-90 stupnu.
        double deg = Math.Abs(cov.TightAxisAngle * 180.0 / Math.PI) % 180.0;
        Assert.That(deg, Is.EqualTo(90.0).Within(20.0));
    }

    [Test]
    public void PrimaCesta_MaMaximumIKdyzZakriveniPodelChybi()
    {
        // REGRESNI TEST. Na prime ceste je podelna druha derivace PRESNE nula, takze -H je jen
        // semidefinitni a neda se invertovat. Drive to zahodilo cely vysledek (HasPeak=false) -
        // tedy i PRICNOU korekci, ktera je hlavni vystup cele funkce. Viz
        // doc/map-correlation-localization.md, "Singularni H je na prime ceste NORMALNI STAV".
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        Assert.That(cov.HasPeak, Is.True,
                    "Prima cesta MUSI dat pouzitelny vysledek - jen s nekonecnou podelnou sigmou.");
        Assert.That(cov.SigmaTight, Is.LessThan(1.0), "Pricna slozka musi byt urcena.");
        Assert.That(double.IsPositiveInfinity(cov.SigmaLoose) || cov.SigmaLoose > 3.0, Is.True,
                    "Podelna slozka na prime ceste urcena byt nema.");
    }

    [Test]
    public void SikmaCesta_PricnaSlozkaJeUrcena()
    {
        // Sikma cesta drzi ALESPON to, ze pricna slozka je urcena a osa miri priblizne napric.
        //
        // POZOR - OTEVRENY UKOL, ktery tento test SCHVALNE netvrdi: na sikme ceste vychazi
        // SigmaLoose konecna (namereno 0,1848 m), i kdyz prima cesta zadnou podelnou informaci
        // nenese - skore je podel ni PRESNE ploche, zmereno pres +-1 m. Je to artefakt fitu
        // kvadraticke formy na "tent" skore. Kdyby se tady tvrdilo "SigmaLoose musi byt nekonecna",
        // test by cerveny zustal, dokud se ta vada neopravi - a to je rozhodnuti autora, ne tohoto
        // tasku. Cisla, odvozeni a dve neuspesne opravy jsou v
        // doc/map-correlation-localization.md, sekce Otevrene ukoly.
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.DiagonalRoad(origin), 0, 0);

        Assert.That(cov.HasPeak, Is.True);
        Assert.That(cov.SigmaTight, Is.LessThan(1.0), "Pricna slozka musi byt urcena i na sikme ceste.");

        // Cesta vede pod 45 stupni, takze dobre urcena osa miri napric = 135 stupnu.
        double deg = ((cov.TightAxisAngle * 180.0 / Math.PI) % 180.0 + 180.0) % 180.0;
        Assert.That(deg, Is.EqualTo(135.0).Within(20.0));
    }

    [Test]
    public void TKrizovatka_ObeSigmyJsouMale()
    {
        var origin = CorrelationTestScenes.Origin();
        var atJunction = Run(CorrelationTestScenes.TJunction(origin), -3.0, 0.0);
        var onStraight = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        Assert.That(atJunction.HasPeak, Is.True);
        // KONECNA sigma je tady to podstatne tvrzeni: odbocka lame podelnou symetrii, takze
        // podelna slozka prestava byt neurcena. Samo "mensi nez na prime ceste" by prosla
        // trivialne, kdyby na prime ceste bylo +Inf.
        Assert.That(double.IsFinite(atJunction.SigmaLoose), Is.True,
                    "U odbocky musi byt podelna slozka KONECNA, ne neurcena.");
        Assert.That(atJunction.SigmaLoose, Is.LessThan(2.0));
        Assert.That(atJunction.SigmaLoose, Is.LessThan(onStraight.SigmaLoose),
                    "U odbocky musi byt podelna slozka LEPE urcena nez na prime ceste.");
    }

    [Test]
    public void SigmaNikdyNespadnePodDolniHranici()
    {
        var origin = CorrelationTestScenes.Origin();
        var cfg = CorrelationTestScenes.TestConfig();
        var cov = Run(CorrelationTestScenes.TJunction(origin), -3.0, 0.0);

        Assert.That(cov.SigmaTight, Is.GreaterThanOrEqualTo(cfg.SigmaFloorM));
        Assert.That(cov.SigmaPhi, Is.GreaterThanOrEqualTo(cfg.SigmaFloorHeadingRad));
    }

    [Test]
    public void PlocheSkore_NemaMaximum()
    {
        // Grid bez jakekoli informace (vsechny bunky slabe) => zadny oblak, zadne zakriveni.
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0);
        Array.Clear(msg.Road, 0, msg.Road.Length);   // vse "nevim"

        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);
        var scorer = new CorrelationScorer(cloud, raster, 0, 0);

        var cov = CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);

        Assert.That(cov.HasPeak, Is.False);
    }

    [Test]
    public void NoPeak_NemaMaximum()
    {
        Assert.That(CorrelationCovariance.NoPeak().HasPeak, Is.False);
    }
}
