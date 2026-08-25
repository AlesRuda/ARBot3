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
    /// <para><see cref="MapCorrelatorConfig.ReferenceInformativeEvidence"/> to opravuje tim, ze
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
            cfg.ReferenceInformativeEvidence = reference;

            var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0);
            var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
            var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);
            var scorer = new CorrelationScorer(cloud, raster, 0, 0);
            return CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);
        }

        var off = WithReference(0);                 // puvodni chovani, konstantni Alpha
        Assert.That(off.HasPeak, Is.True, "predpoklad testu");
        Assert.That(off.InformativeEvidence, Is.GreaterThan(0),
                    "informativni vaha se pocita vzdy, i pri vypnutem skalovani (je to diagnostika)");

        double w = off.InformativeEvidence;
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

    /// <summary>
    /// Kovariance na PRIME CESTE nad gridem daneho rozliseni. <b>Vyrez sveta zustava tentyz</b>
    /// (9,6 m na stranu) - meni se jen hustota mrize, tedy pocet bunek pri STEJNEM mnozstvi
    /// skutecne informace.
    /// </summary>
    private static CorrelationCovariance StraightRoadAt(double resolution, double reference,
                                                        double hessianStepM)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);

        var cfg = CorrelationTestScenes.TestConfig();
        cfg.ReferenceInformativeEvidence = reference;
        cfg.HessianStepM = hessianStepM;

        int size = (int)Math.Round(CorrelationTestScenes.GridSize
                                   * CorrelationTestScenes.Resolution / resolution);
        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0, size, resolution);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);
        var scorer = new CorrelationScorer(cloud, raster, 0, 0);
        return CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);
    }

    /// <summary>
    /// <b>Honestni sigma: reference nesmi byt vazana na ROZLISENI GRIDU.</b>
    ///
    /// <para>Puvodni podoba opravy (25. 8. 2026) skalovala <c>Alpha</c> podle SUROVEHO poctu
    /// informativnich bunek. Ten ale roste jako <c>1/plocha bunky</c>: pri dvojnasobnem rozliseni
    /// je bunek ctyrikrat vic, i kdyz robot nevidi ani o kousek vic sveta. Referencni hodnota
    /// namerena pri 5 cm by pri 10 cm znamenala neco jineho - a to je presne ten duvod, proc se
    /// oprava nemohla stat vychozim stavem.</para>
    ///
    /// <para>Lecba: informativni dukaz se meri v <b>m² · log-odds</b> (soucet <c>|w|</c> × plocha
    /// bunky), tedy fyzikalne. Test drzi to, co z toho ma plynout: <b>TENTYZ vyrez sveta musi dat
    /// tutez sigmu, at je mriz jakkoli husta.</b></para>
    /// </summary>
    [Test]
    public void HonestniSigma_ReferenceNezavisiNaRozliseniGridu()
    {
        // Reference v NOVYCH jednotkach [m² · log-odds]. Konkretni hodnota je jen skala - test
        // porovnava dva bezy MEZI SEBOU, takze na ni nezalezi.
        const double reference = 20.0;

        var coarse = StraightRoadAt(0.10, reference, hessianStepM: 0.40);
        var fine = StraightRoadAt(0.05, reference, hessianStepM: 0.40);

        // SUROVA vaha (dukaz / plocha bunky) je to, cim se skalovalo do 25. 8. vecer. Jeji podil
        // mezi rozlisenimi je prave ta vada, kterou tento test zavira - ctyrikrat vic bunek pri
        // temze mnozstvi informace.
        double rawCoarse = coarse.InformativeEvidence / (0.10 * 0.10);
        double rawFine = fine.InformativeEvidence / (0.05 * 0.05);

        TestContext.Out.WriteLine(
            $"10 cm: dukaz {coarse.InformativeEvidence:F2} m2*lo (surove {rawCoarse:F0}), "
            + $"sigma {coarse.SigmaTight:F4} m\n"
            + $" 5 cm: dukaz {fine.InformativeEvidence:F2} m2*lo (surove {rawFine:F0}), "
            + $"sigma {fine.SigmaTight:F4} m\n"
            + $"       podil dukazu {fine.InformativeEvidence / coarse.InformativeEvidence:F3}, "
            + $"surove vahy {rawFine / rawCoarse:F3}, sigma {fine.SigmaTight / coarse.SigmaTight:F3}");

        Assert.Multiple(() =>
        {
            Assert.That(coarse.HasPeak && fine.HasPeak, Is.True, "predpoklad testu");
            // NAMERENO 25. 8. 2026: dukaz 15,36 m²·lo pri obou rozlisenich, surova vaha 1536 proti
            // 6144 (presne 4x). Se surovou vahou by tedy sigma vysla PULOVA - odtud "reference je
            // vazana na rozliseni gridu". Tolerance je mala schvalne: na teto scene vychazi rovnost
            // PRESNA, takze uz maly posun je regrese, ne kvantizacni sum.
            Assert.That(rawFine / rawCoarse, Is.EqualTo(4.0).Within(0.05),
                        "predpoklad testu: surovy pocet bunek roste jako 1/plocha bunky");
            Assert.That(fine.InformativeEvidence / coarse.InformativeEvidence,
                        Is.EqualTo(1.0).Within(0.02),
                        "informativni dukaz je fyzikalni velicina - hustota mrize ho nesmi menit");
            Assert.That(fine.SigmaTight / coarse.SigmaTight, Is.EqualTo(1.0).Within(0.02),
                        "tentyz vyrez sveta = tataz sigma, at je mriz jakkoli husta");
        });
    }

    /// <summary>
    /// <b>Honestni sigma: reference nesmi byt vazana na KROK DERIVACE.</b>
    ///
    /// <para>Dokumentace kovariance to dosud priznavala jako past: skore je "tent", takze
    /// zakriveni je ~<c>1/h</c> a <c>sigma ~ sqrt(h)</c> - zmena
    /// <see cref="MapCorrelatorConfig.HessianStepM"/> tedy prepocitala vsechny sigmy a musela se
    /// ladit SPOLU s <c>Alpha</c>.</para>
    ///
    /// <para>Skalovani podle informativniho dukazu to <b>vyresi mimochodem</b>: informativnich
    /// bunek je pasmo sirky <c>2h</c> u okraje cesty, takze dukazu je take ~<c>h</c> a
    /// <c>alphaEff ~ 1/h</c>. Obe zavislosti se vykrati. Tenhle test to drzi - a zaroven je
    /// duvod, proc se dukaz normuje plochou bunky, ale <b>ne</b> krokem derivace.</para>
    /// </summary>
    [Test]
    public void HonestniSigma_ReferenceNezavisiNaKrokuDerivace()
    {
        const double reference = 20.0;

        var small = StraightRoadAt(0.10, reference, hessianStepM: 0.30);
        var big = StraightRoadAt(0.10, reference, hessianStepM: 0.60);
        var off = StraightRoadAt(0.10, reference: 0, hessianStepM: 0.30);
        var offBig = StraightRoadAt(0.10, reference: 0, hessianStepM: 0.60);

        TestContext.Out.WriteLine(
            $"h=0,30: dukaz {small.InformativeEvidence:F2} m2*lo, sigma {small.SigmaTight:F4} m\n"
            + $"h=0,60: dukaz {big.InformativeEvidence:F2} m2*lo, sigma {big.SigmaTight:F4} m\n"
            + $"bez skalovani: sigma {off.SigmaTight:F4} -> {offBig.SigmaTight:F4} m "
            + $"(podil {offBig.SigmaTight / off.SigmaTight:F3}, ceka se ~sqrt(2) = 1,414)");

        Assert.Multiple(() =>
        {
            // NAMERENO 25. 8. 2026: dukaz 11,52 -> 23,04 m²·lo (presne 2x), sigma 0,1768 m v obou
            // pripadech. Bez skalovani 0,1342 -> 0,1897 m, tedy presne sqrt(2) - past, kterou
            // dokumentace kovariance dosud priznavala.
            Assert.That(big.InformativeEvidence / small.InformativeEvidence,
                        Is.EqualTo(2.0).Within(0.05),
                        "pasmo informativnich bunek ma sirku 2h, takze dvojnasobny krok = dvojnasobny dukaz");
            Assert.That(big.SigmaTight / small.SigmaTight, Is.EqualTo(1.0).Within(0.02),
                        "krok derivace je numericky detail - sigma na nem zavisi nema");
            Assert.That(offBig.SigmaTight / off.SigmaTight, Is.EqualTo(Math.Sqrt(2)).Within(0.05),
                        "bez skalovani sigma ~ sqrt(h) - to je ta stara past, a zaroven duvod, "
                        + "proc se informativni dukaz krokem derivace NEDELI");
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
