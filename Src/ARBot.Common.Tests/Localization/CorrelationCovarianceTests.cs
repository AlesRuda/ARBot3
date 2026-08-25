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

    /// <summary>
    /// <b>Honestni sigma: mene informativniho dukazu = vetsi sigma.</b>
    ///
    /// <para>Skore je normovany PODIL souhlasicich bunek, takze o velikosti vzorku za sebou nevi nic
    /// — a sigma z jeho zakriveni (× konstantni <c>Alpha</c>) to nevi taky. Nameřeno 20. 8. 2026:
    /// oblak s 2 214 bunkami hlasil sigma 0,1412 m, oblak s 18 465 bunkami 0,2737 m — tedy VETSI
    /// jistotu tam, kde je podkladu nejmin. Je to jako s anketou: tri dotazani se stoprocentni
    /// shodou vypadaji lip nez tri tisice s 94 %.</para>
    ///
    /// <para><see cref="MapCorrelatorConfig.ReferenceInformativeWeight"/> to opravuje tim, ze
    /// <c>Alpha</c> skaluje podle vahy dukazu, ktery skutecne ROZLISUJE mezi kandidaty. Test hlida
    /// smer: pri poloviční referenci (tedy jako by bylo dvakrat MIN dukazu, nez kolik ho je) musi
    /// sigma klesnout, pri dvojnasobne stoupnout — a to jako odmocnina, protoze
    /// <c>sigma ~ sqrt(alpha)</c>.</para>
    /// </summary>
    [Test]
    public void HonestniSigma_ReferenceSkalujeSigmuOdmocninou()
    {
        var origin = CorrelationTestScenes.Origin();
        var network = CorrelationTestScenes.StraightEastRoad(origin);

        CorrelationCovariance WithReference(double reference)
        {
            var scene = new RoadScene(network, origin);
            var cfg = CorrelationTestScenes.TestConfig();
            cfg.ReferenceInformativeWeight = reference;

            var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0);
            var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
            var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);
            var scorer = new CorrelationScorer(cloud, raster, 0, 0);
            return CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);
        }

        var off = WithReference(0);                 // puvodni chovani, konstantni Alpha
        Assert.That(off.HasPeak, Is.True, "predpoklad testu");
        Assert.That(off.InformativeWeight, Is.GreaterThan(0),
                    "informativni vaha se pocita vzdy, i pri vypnutem skalovani (je to diagnostika)");

        double w = off.InformativeWeight;
        var atRef = WithReference(w);               // reference = skutecna vaha -> nic se nemeni
        var half = WithReference(w / 2);            // jako by bylo dukazu 2x vic -> mensi sigma
        var twice = WithReference(w * 2);           // jako by bylo dukazu 2x min -> vetsi sigma

        TestContext.Out.WriteLine($"informativni vaha {w:F1}; sigma: vypnuto {off.SigmaTight:F4}, "
                                  + $"ref=w {atRef.SigmaTight:F4}, ref=w/2 {half.SigmaTight:F4}, "
                                  + $"ref=2w {twice.SigmaTight:F4}");

        Assert.Multiple(() =>
        {
            Assert.That(atRef.SigmaTight, Is.EqualTo(off.SigmaTight).Within(1e-9),
                        "pri referenci rovne skutecne vaze musi vyjit TATAZ sigma jako bez skalovani");
            Assert.That(half.SigmaTight, Is.LessThan(off.SigmaTight), "vic dukazu = mensi sigma");
            Assert.That(twice.SigmaTight, Is.GreaterThan(off.SigmaTight), "min dukazu = vetsi sigma");
            // sigma ~ sqrt(alpha), takze dvojnasobna reference da sqrt(2)x vetsi sigma.
            Assert.That(twice.SigmaTight / off.SigmaTight, Is.EqualTo(Math.Sqrt(2)).Within(0.02));
        });
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
