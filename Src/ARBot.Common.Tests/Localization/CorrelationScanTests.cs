using System;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy hrube-jemneho skenovani (viz doc/map-correlation-localization.md).
/// Grid se generuje se ZNAMOU chybou pozy a sken ji musi najit.
/// </summary>
public class CorrelationScanTests
{
    /// <summary>Sken nad danou siti a danou skutecnou chybou pozy.</summary>
    private static ScanResult Run(RoadNetwork network, double robotX, double robotY,
                                  double dx0, double dy0, double phi0)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(network, origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, robotX, robotY, dx0, dy0, phi0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        return new CorrelationScorer(cloud, raster, robotX, robotY).Scan(cfg);
    }

    [Test]
    public void Scan_PricnaChybaNaPrimeCeste_SeNajde()
    {
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, 0.7, 0.0);

        Assert.That(result.Dy, Is.EqualTo(0.7).Within(0.15));
        Assert.That(result.Score, Is.GreaterThan(0.9));
    }

    [Test]
    public void Scan_ChybaKurzu_SeNajde()
    {
        var origin = CorrelationTestScenes.Origin();
        double phi0 = 4.0 * Math.PI / 180.0;
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, 0.0, phi0);

        Assert.That(result.Phi, Is.EqualTo(phi0).Within(1.5 * Math.PI / 180.0));
    }

    [Test]
    public void Scan_UOdbocky_NajdeIPodelnouChybu()
    {
        // Robot stoji 3 m zapadne od krizovatky, aby ji mel v zaberu gridu.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.TJunction(origin), -3.0, 0.0, 0.8, 0.0, 0.0);

        Assert.That(result.Dx, Is.EqualTo(0.8).Within(0.25),
                    "Odbocka lame podelnou symetrii, takze dx musi byt najitelne.");
    }

    [Test]
    public void Scan_NaOhybu_NajdePricnouChybu()
    {
        // Robot stoji na ceste v miste ohybu.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.CurvedRoad(origin), 0.0, 2.0, 0.0, 0.6, 0.0);

        Assert.That(result.Dy, Is.EqualTo(0.6).Within(0.25));
        Assert.That(result.Score, Is.GreaterThan(0.85));
    }

    [Test]
    public void Scan_NaPrimeCeste_PodelnaSlozkaNeniUrcena()
    {
        // Skutecna chyba je jen pricna; podelna slozka vyjde libovolne (skore je podel plche).
        // Test tvrdi jen to, ze se tim NEROZBIJE pricny odhad.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, -0.6, 0.0);

        Assert.That(result.Dy, Is.EqualTo(-0.6).Within(0.15));
    }

    [Test]
    public void Scan_RemizaNaPrimeCeste_VyhravaSTRED()
    {
        // REGRESNI TEST dokumentovaneho kontraktu (zapis v doc/decisions.md). Podel prime cesty maji
        // desitky kandidatu skore PRESNE stejne - posun podel cesty nemeni nic, co robot vidi.
        // Naivni "prvni vyhrava" pak vratilo OKRAJ okna (namereno dx = -2,4 m) a korelator sam sebe
        // zamitl jako OffsetTooLarge, takze se zahodila i spravne nalezena PRICNA korekce. Spravna
        // odpoved je nejmensi korekce, tedy stred okna: kdyz data nedavaji duvod jednu z remizovych
        // moznosti preferovat, "neopravuj" je jediny obhajitelny vyber.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, 0.7, 0.0);

        TestContext.Out.WriteLine($"Dx={result.Dx:F3} Dy={result.Dy:F3} skore={result.Score:F4} "
                                  + $"hrube={result.CoarsePeakScore:F4} "
                                  + $"vMaximu={result.CoarseStrideScoreAtPeak:F4}");

        Assert.That(result.Dx, Is.EqualTo(0.0).Within(0.11),
                    "Podelna slozka musi na prime ceste zustat u nuly (max. jeden nejjemnejsi krok), "
                    + "ne se prilepit na okraj okna.");
        Assert.That(result.Dy, Is.EqualTo(0.7).Within(0.15), "Pricna slozka musi zustat nalezena.");
    }

    /// <summary>Sken i konkurent podel zadane osy nad jednou scenou.</summary>
    private static (ScanResult Scan, double Rival) RunWithRival(RoadNetwork network,
                                                               double robotX, double robotY,
                                                               double axisAngle)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(network, origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, robotX, robotY, 0, 0, 0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        var scorer = new CorrelationScorer(cloud, raster, robotX, robotY);
        var scan = scorer.Scan(cfg);
        return (scan, scorer.BestRivalAlongAxis(scan, axisAngle, cfg));
    }

    [Test]
    public void Rival_SoubezneCesty_JeBlizkoMaxima()
    {
        // Osa 90 stupnu = NAPRIC cestami, tedy ten smer, ve kterem se soubezne cesty pletou.
        // Vzor se opakuje s periodou 2 m, takze konkurent musi byt skore blizko maxima - jinak
        // by test nejednoznacnosti v MapCorrelationResult nemel co merit.
        var origin = CorrelationTestScenes.Origin();
        var r = RunWithRival(CorrelationTestScenes.ParallelRoads(origin), 0, 0, Math.PI / 2);

        Assert.That(r.Rival, Is.GreaterThan(r.Scan.CoarsePeakScore - 0.5));
    }

    [Test]
    public void Rival_JednaCesta_NapricJeVyrazneHorsi()
    {
        // TOHLE je smysl mereni konkurenta PODEL OSY. Osa 90 stupnu = napric jedinou cestou:
        // posun napric cestu opusti, takze konkurent MUSI byt vyrazne horsi. (Kdyby se konkurent
        // meril ve 2D, nasel by se kandidat PODEL cesty se skore PRESNE stejnym - a kazda prima
        // cesta by se hlasila jako nejednoznacna, cimz by se potlacila i pricna korekce.)
        var origin = CorrelationTestScenes.Origin();
        var r = RunWithRival(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, Math.PI / 2);

        Assert.That(r.Rival, Is.LessThan(r.Scan.CoarsePeakScore - 0.3));
    }

    [Test]
    public void Rival_JednaCesta_PODELJeStejneDobry()
    {
        // Doplnek predchoziho testu: podel cesty je konkurent opravdu stejne dobry. Prave proto se
        // konkurent NESMI merit ve 2D - tohle cislo neni nejednoznacnost, ale znama neurcenost,
        // kterou uz vyjadruje nekonecna sigma volne osy.
        var origin = CorrelationTestScenes.Origin();
        var r = RunWithRival(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0);

        Assert.That(r.Rival, Is.EqualTo(r.Scan.CoarsePeakScore).Within(0.02));
    }

    [Test]
    public void Scan_PocetKandidatuOdpovidaUrovnim()
    {
        var origin = CorrelationTestScenes.Origin();
        var cfg = CorrelationTestScenes.TestConfig();

        int expected = 0;
        foreach (var l in cfg.Levels)
        {
            int nT = (int)Math.Round(l.HalfRangeM / l.StepM);
            int nH = (int)Math.Round(l.HalfRangeHeadingRad / l.StepHeadingRad);
            expected += (2 * nT + 1) * (2 * nT + 1) * (2 * nH + 1);
        }

        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0, 0, 0);

        Assert.That(result.Candidates, Is.EqualTo(expected));
    }
}
