using System;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Prirazeni koridoru k hrane site: <b>po ktere ceste robot jede</b>.
///
/// <para>Do 16. 9. 2026 se brala prosta nejblizsi hrana a kurz do vyberu nevstupoval — nad
/// <c>records/test/20260916-164926.rec</c> se pak POLOVINA cyklu parovala na pricnou ulici.
/// Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class EdgeAssociatorTests
{
    private static GeoReference Origin() => CorrelationTestScenes.Origin();

    /// <summary>Poza s dodanou kovarianci (sigma pricne i kurzu zadava test).</summary>
    private static RobotState Pose(double x, double y, double theta,
                                   double sigmaPosM = 1.0, double sigmaThetaRad = 0.02)
    {
        var p = Matrix<double>.Build.Dense(EKFModel.N, EKFModel.N);
        p[EKFModel.IX, EKFModel.IX] = sigmaPosM * sigmaPosM;
        p[EKFModel.IY, EKFModel.IY] = sigmaPosM * sigmaPosM;
        p[EKFModel.ITh, EKFModel.ITh] = sigmaThetaRad * sigmaThetaRad;
        return new RobotState { X = x, Y = y, Theta = theta, Covariance = p };
    }

    /// <summary>Koridor videny kamerou: sirka, pricna poloha robotu, sklon v ramci robotu.</summary>
    private static RoadCorridor Corridor(double width, double lateral, double dirRad)
        => new RoadCorridor
        {
            Reason = CorridorReason.Ok,
            Width = width,
            Lateral = lateral,
            DirectionRad = dirRad,
            SigmaLateral = 0.05,
            SigmaDirectionRad = Conversions.Deg2Rad(0.5),
        };

    private static EdgeAssociationConfig Cfg() => new EdgeAssociationConfig();

    // ---------------------------------------------------------------------------------------
    // RoadNetwork.NearestEdges
    // ---------------------------------------------------------------------------------------

    [Test]
    public void NearestEdges_vraciKandidatySerazeneOdNejblizsiho()
    {
        var o = Origin();
        var net = CorrelationTestScenes.TJunction(o);

        // Robot kousek na sever od krizovatky: nejblizsi je odbocka na sever, pak cesta na vychod.
        var list = net.NearestEdges(o.ToLLA(0.2, 3.0), k: 4);

        Assert.That(list.Count, Is.GreaterThanOrEqualTo(2));
        for (int i = 1; i < list.Count; i++)
            Assert.That(list[i].DistanceM, Is.GreaterThanOrEqualTo(list[i - 1].DistanceM),
                        "kandidati maji chodit od nejblizsiho");
    }

    [Test]
    public void NearestEdges_obeStranyObousmerneCesty_jsouJEDENkandidat()
    {
        // Obousmerna cesta je v siti dve hrany se SHODNOU geometrii. Kdyby sly do vysledku obe,
        // byl by druhy kandidat tyz kus asfaltu a kazde prirazeni by vyslo jako nejednoznacne.
        var o = Origin();
        var a = new Node(1, o.ToLLA(-30, 0), 4.0);
        var b = new Node(2, o.ToLLA(30, 0), 4.0);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 60.0, wayId: 1, traversalCost: 60.0);
        builder.AddEdge(b, a, 60.0, wayId: 1, traversalCost: 60.0);
        var net = builder.Build();

        var list = net.NearestEdges(o.ToLLA(0, 0.5), k: 4);

        Assert.That(list.Count, Is.EqualTo(1), "obe strany teze cesty se pocitaji za jednu hranu");
    }

    [Test]
    public void NearestEdges_respektujeStropVzdalenosti()
    {
        var o = Origin();
        var net = CorrelationTestScenes.TJunction(o);

        var list = net.NearestEdges(o.ToLLA(0, 40), k: 4, maxDistanceM: 5.0);

        Assert.That(list.Count, Is.EqualTo(0), "daleko od vseho nema byt zadny kandidat");
    }

    // ---------------------------------------------------------------------------------------
    // Vyber spravne hrany
    // ---------------------------------------------------------------------------------------

    [Test]
    public void UKrizovatky_vybereCestuPODELjizdy_iKdyzJeBlizPRICNA()
    {
        // Jadro cele zmeny. Robot jede na VYCHOD po ceste podel osy X, ale poza je posunuta na
        // sever tak, ze nejblizsi hranou je odbocka na SEVER. Podle vzdalenosti by vyhrala ona;
        // podle azimutu vyhraje ta spravna.
        var o = Origin();
        var net = CorrelationTestScenes.TJunction(o);
        var pose = Pose(x: 0.3, y: 2.5, theta: 0);
        var corridor = Corridor(width: 4.0, lateral: 0, dirRad: 0);

        // Kontrola premisy: nejblizsi hrana je opravdu ta pricna.
        var nearest = net.NearestEdge(o.ToLLA(pose.X, pose.Y), out _, out _, out _);
        Assert.That(nearest!.WayId, Is.EqualTo(2), "premisa testu: nejblizsi je odbocka na sever");

        var assoc = EdgeAssociator.Associate(net, o, pose, corridor, Cfg(), maxEdgeDistanceM: 8.0);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.Ok));
        Assert.That(assoc.Axis.WayId, Is.EqualTo(1), "vybrat se ma cesta POdel jizdy, ne pricna");
    }

    [Test]
    public void PricnaUlice_padneNaVETOazimutu_aNeposuzujeSe()
    {
        // Sit ma JEN pricnou cestu (na sever), robot jede na vychod. Zadny kandidat nezbyde.
        var o = Origin();
        var builder = new RoadNetwork.Builder();
        var c = new Node(3, o.ToLLA(0, -10), 4.0);
        var d = new Node(4, o.ToLLA(0, 20), 4.0);
        builder.AddEdge(c, d, 30.0, wayId: 2, traversalCost: 30.0);
        var net = builder.Build();

        var assoc = EdgeAssociator.Associate(net, o, Pose(0.5, 0, 0), Corridor(4.0, 0, 0),
                                             Cfg(), maxEdgeDistanceM: 8.0);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.NoCandidate));
        Assert.That(assoc.Candidates, Is.EqualTo(0), "kolma cesta se nema ani posuzovat");
    }

    // ---------------------------------------------------------------------------------------
    // Past: OSM cesta je rozdelena na segmenty
    // ---------------------------------------------------------------------------------------

    [Test]
    public void KolinearniSousedniSegment_NENIdruhyKandidat()
    {
        // TJunction ma cestu na vychod rozdelenou na DVA kolinearni useky (a-c, c-b, tyz wayId).
        // Oba lezi na teze primce, takze RoadAxis.Relate z nich spocita TOTEZ - kdyby soutezily,
        // byla by to remiza sama se sebou a test nejednoznacnosti by zamitl kazdou rovnou cestu.
        var o = Origin();
        var net = CorrelationTestScenes.TJunction(o);

        // Robot kus od krizovatky, aby pricna odbocka padla na veto a zbyly jen ty dva useky.
        var assoc = EdgeAssociator.Associate(net, o, Pose(10, 0.3, 0), Corridor(4.0, 0.3, 0),
                                             Cfg(), maxEdgeDistanceM: 8.0);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.Ok),
                    "kolinearni sousedni segment nesmi delat nejednoznacnost");
        Assert.That(assoc.Candidates, Is.EqualTo(1), "oba useky teze primky jsou JEDNA hypoteza");
    }

    [Test]
    public void DveSKUTECNErovnobezneCesty_jsouNejednoznacne_aNeposleSeNic()
    {
        // Dve rovnobezne cesty 2 m od sebe, robot presne mezi nimi: obe sedi stejne dobre.
        // Vybrat tu o chlup lepsi by znamenalo hadat - spravna odpoved je "nevim".
        var o = Origin();
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(new Node(1, o.ToLLA(-30, 1.0), 4.0), new Node(2, o.ToLLA(30, 1.0), 4.0),
                        60.0, wayId: 1, traversalCost: 60.0);
        builder.AddEdge(new Node(3, o.ToLLA(-30, -1.0), 4.0), new Node(4, o.ToLLA(30, -1.0), 4.0),
                        60.0, wayId: 2, traversalCost: 60.0);
        var net = builder.Build();

        var assoc = EdgeAssociator.Associate(net, o, Pose(0, 0, 0), Corridor(4.0, 0, 0),
                                             Cfg(), maxEdgeDistanceM: 8.0);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.Ambiguous));
        Assert.That(assoc.Candidates, Is.EqualTo(2));
    }

    // ---------------------------------------------------------------------------------------
    // Podelny presah (assocfloorlong, od 29. 9. 2026)
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Rovinka z jedne OSM cesty zalomena o 2° v uzlu (0, 0): usek A→B do (−100, 0), usek B→C
    /// 150 m dal pod 2°. Presne takhle vypada cyklostezka v Modranech (lomena cara po 20–180 m).
    /// </summary>
    private static RoadNetwork LomenaRovinka(GeoReference o)
    {
        double bend = Conversions.Deg2Rad(2.0);
        var a = new Node(1, o.ToLLA(-100, 0), 4.0);
        var b = new Node(2, o.ToLLA(0, 0), 4.0);
        var c = new Node(3, o.ToLLA(150 * Math.Cos(bend), 150 * Math.Sin(bend)), 4.0);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 100.0, wayId: 7, traversalCost: 100.0);
        builder.AddEdge(b, c, 150.0, wayId: 7, traversalCost: 150.0);
        return builder.Build();
    }

    [Test]
    public void LomenaRovinka_vzdalenySousedniUsek_NENIdruhyKandidat()
    {
        // Robot 50 m pred zlomem, vedle useku A→B. Primka useku B→C (2°) ma v tom miste osu
        // o 50·tan 2° = 1,75 m vedle - vic nez SameHypothesisLateralM, takze je to samostatna
        // hypoteza, a pri podlaze pricne sigmy 3 m je rozdil chi2 jen ~0,3. Robot vedle nej ale
        // nestoji (presah 50 m), tak nema soutezit.
        var o = Origin();
        var net = LomenaRovinka(o);
        var pose = Pose(x: -50, y: 0.3, theta: 0);
        var corridor = Corridor(width: 4.0, lateral: 0.3, dirRad: 0);

        var bezPresahu = Cfg();
        bezPresahu.SigmaLongitudinalFloorM = 0;
        var stare = EdgeAssociator.Associate(net, o, pose, corridor, bezPresahu, double.PositiveInfinity);
        Assert.That(stare.Result, Is.EqualTo(EdgeAssocResult.Ambiguous),
                    "premisa testu: bez presahu je to remiza se sousednim usekem (29. 9. 2026)");

        var assoc = EdgeAssociator.Associate(net, o, pose, corridor, Cfg(), double.PositiveInfinity);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.Ok),
                    "usek 50 m daleko se hlasi jen extrapolaci sve primky");
        Assert.That(assoc.Axis.OverhangM, Is.EqualTo(0).Within(1e-9), "vitez je usek, vedle ktereho robot stoji");
        Assert.That(assoc.Axis.Lateral, Is.EqualTo(0.3).Within(0.01), "osa z useku vedle robotu, ne extrapolovana");
    }

    [Test]
    public void DveSKUTECNErovnobezneCesty_zustanouNejednoznacne_iSPresahem()
    {
        // Presah nesmi rozseknout skutecnou nejednoznacnost: robot stoji vedle OBOU cest, takze
        // obe maji presah 0 a prirazka je nula.
        var o = Origin();
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(new Node(1, o.ToLLA(-30, 1.0), 4.0), new Node(2, o.ToLLA(30, 1.0), 4.0),
                        60.0, wayId: 1, traversalCost: 60.0);
        builder.AddEdge(new Node(3, o.ToLLA(-30, -1.0), 4.0), new Node(4, o.ToLLA(30, -1.0), 4.0),
                        60.0, wayId: 2, traversalCost: 60.0);
        var net = builder.Build();
        var cfg = Cfg();
        Assert.That(cfg.SigmaLongitudinalFloorM, Is.GreaterThan(0), "test ma bezet s presahem");

        var assoc = EdgeAssociator.Associate(net, o, Pose(0, 0, 0), Corridor(4.0, 0, 0),
                                             cfg, double.PositiveInfinity);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.Ambiguous));
    }

    [Test]
    public void ZaKoncemSite_malyPresahProjde_velkyNe()
    {
        // Jedina cesta konci v (0, 0). Poza 3 m za koncem (podelna chyba pozy) = prirazka 1,
        // porad prijato. Poza 15 m za koncem = prirazka 25 nad stropem 9,21 - robot u te cesty
        // podle mapy neni.
        var o = Origin();
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(new Node(1, o.ToLLA(-60, 0), 4.0), new Node(2, o.ToLLA(0, 0), 4.0),
                        60.0, wayId: 1, traversalCost: 60.0);
        var net = builder.Build();
        var corridor = Corridor(4.0, 0, 0);

        var blizko = EdgeAssociator.Associate(net, o, Pose(3, 0, 0), corridor, Cfg(), double.PositiveInfinity);
        Assert.That(blizko.Result, Is.EqualTo(EdgeAssocResult.Ok));
        Assert.That(blizko.Chi2, Is.EqualTo(1.0).Within(0.05), "presah 3 m pri podlaze 3 m = prirazka 1");

        var daleko = EdgeAssociator.Associate(net, o, Pose(15, 0, 0), corridor, Cfg(), double.PositiveInfinity);
        Assert.That(daleko.Result, Is.EqualTo(EdgeAssocResult.NoCandidate));
    }

    [Test]
    public void Relate_podelnyPresah_nulaVedleUsecky_kladnyZaObemaKonci()
    {
        var o = Origin();
        var a = new Node(1, o.ToLLA(0, 0), 4.0);
        var b = new Node(2, o.ToLLA(20, 0), 4.0);
        var edge = new RoadNetwork.Builder().AddEdge(a, b, 20.0, wayId: 1, traversalCost: 20.0);

        Assert.That(RoadAxis.Relate(o, edge, 0.5, 1, 10, 1, 0).OverhangM, Is.EqualTo(0).Within(1e-6));
        Assert.That(RoadAxis.Relate(o, edge, 0, 5, -5, 1, 0).OverhangM, Is.EqualTo(5).Within(0.01));
        Assert.That(RoadAxis.Relate(o, edge, 1, 7, 27, 1, 0).OverhangM, Is.EqualTo(7).Within(0.01));
        // Na smeru jizdy nezalezi (Relate hranu otoci podle kurzu).
        Assert.That(RoadAxis.Relate(o, edge, 1, 7, 27, 1, Math.PI).OverhangM, Is.EqualTo(7).Within(0.01));
    }

    // ---------------------------------------------------------------------------------------
    // Podlahy sigem
    // ---------------------------------------------------------------------------------------

    [Test]
    public void BezPODLAHYsigmyKurzu_bySpravnaHranaNEPROSLA()
    {
        // Presne to, co se namerilo nad 20260916-164926.rec: fuze hlasi sigmu kurzu ~1 stupen,
        // skutecna chyba kurzu je ale 15-20 (nezkalibrovany magnetometr). Bez podlahy vyjde
        // chi-kvadrat v radu stovek i na SPRAVNE hrane a zamitne se uplne vsechno.
        var o = Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o);
        var pose = Pose(0, 0, 0, sigmaPosM: 1.0, sigmaThetaRad: Conversions.Deg2Rad(1.1));
        var corridor = Corridor(4.0, 0, Conversions.Deg2Rad(16));   // kurz je o 16 stupnu vedle

        var bezPodlahy = Cfg();
        bezPodlahy.SigmaHeadingFloorRad = 0;
        bezPodlahy.SigmaLateralFloorM = 0;
        var s = EdgeAssociator.Associate(net, o, pose, corridor, bezPodlahy, 8.0);
        Assert.That(s.Result, Is.EqualTo(EdgeAssocResult.NoCandidate),
                    "bez podlahy zamitne i spravnou hranu");
        Assert.That(s.Chi2, Is.GreaterThan(100), "chi-kvadrat radu stovek - presne jak v zaznamu");

        // Podlaha 10 stupnu = vychozi hodnota do 27. 9. 2026, zvolena prave pro tuhle chybu kurzu.
        var sPodlahou = Cfg();
        sPodlahou.SigmaHeadingFloorRad = Conversions.Deg2Rad(10);
        var t = EdgeAssociator.Associate(net, o, pose, corridor, sPodlahou, 8.0);
        Assert.That(t.Result, Is.EqualTo(EdgeAssocResult.Ok), "s podlahou 10 stupnu spravna hrana projde");

        // CENA dnesniho defaultu 5 stupnu (od 27. 9. 2026, po kalibraci magnetometru): pri chybe
        // kurzu 16 stupnu uz zamitne i spravnou hranu. Tenhle radek to drzi jako vedomou volbu -
        // kdyby kurz zase ujel (14. 9. 2026: kabely ke kameram), je treba podlahu zvednout.
        var dnes = EdgeAssociator.Associate(net, o, pose, corridor, new EdgeAssociationConfig(), 8.0);
        Assert.That(dnes.Result, Is.EqualTo(EdgeAssocResult.NoCandidate),
                    "podlaha 5 stupnu pri chybe kurzu 16 stupnu spravnou hranu zamitne");
    }

    [Test]
    public void PODLAHAsigmyPricne_pustiPozuVzdalenouOdVozovky()
    {
        // Poza 5 m od osy vozovky - zmereno nad 20260916-164926.rec, kde byl pricny nesouhlas
        // p90 6,6 m a odstup pozy od hrany p50 az 5,1 m. Filtr si pritom mysli, ze zna polohu
        // na 1,4 m, takze bez podlahy je to (5/1,4)^2 = 12,6, tedy nad prahem 9,21.
        var o = Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o);
        var pose = Pose(0, 5.0, 0, sigmaPosM: 1.4);
        var corridor = Corridor(4.0, 0, 0);            // kamera rika "jsem na ose koridoru"

        var bezPodlahy = Cfg();
        bezPodlahy.SigmaLateralFloorM = 0;
        Assert.That(EdgeAssociator.Associate(net, o, pose, corridor, bezPodlahy, 8.0).Result,
                    Is.EqualTo(EdgeAssocResult.NoCandidate));

        Assert.That(EdgeAssociator.Associate(net, o, pose, corridor, Cfg(), 8.0).Result,
                    Is.EqualTo(EdgeAssocResult.Ok));
    }

    [Test]
    public void BezKovariancePozy_seJedeNaPodlahach_aNespadneTo()
    {
        var o = Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o);
        var pose = new RobotState { X = 0, Y = 0.5, Theta = 0 };   // Covariance == null

        var assoc = EdgeAssociator.Associate(net, o, pose, Corridor(4.0, 0.5, 0), Cfg(), 8.0);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.Ok));
    }

    // ---------------------------------------------------------------------------------------
    // Meze a odolnost
    // ---------------------------------------------------------------------------------------

    [Test]
    public void MimoMapu_vraciNoEdge()
    {
        var o = Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o);

        var assoc = EdgeAssociator.Associate(net, o, Pose(0, 500, 0), Corridor(4.0, 0, 0), Cfg(), 8.0);

        Assert.That(assoc.Result, Is.EqualTo(EdgeAssocResult.NoEdge));
    }

    [Test]
    public void ChybejiciVstupy_nespadnou()
    {
        var o = Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o);
        Assert.That(EdgeAssociator.Associate(null, o, Pose(0, 0, 0), Corridor(4, 0, 0), Cfg(), 8).Result,
                    Is.EqualTo(EdgeAssocResult.NoEdge));
        Assert.That(EdgeAssociator.Associate(net, o, null, Corridor(4, 0, 0), Cfg(), 8).Result,
                    Is.EqualTo(EdgeAssocResult.NoEdge));
        Assert.That(EdgeAssociator.Associate(net, o, Pose(0, 0, 0), null, Cfg(), 8).Result,
                    Is.EqualTo(EdgeAssocResult.NoEdge));
    }

    [Test]
    public void Validate_chytiNesmyslneMeze()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeAssociationConfig { Candidates = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeAssociationConfig { Chi2Max = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeAssociationConfig { Chi2Margin = -1 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeAssociationConfig { SigmaLongitudinalFloorM = -1 }.Validate());
        Assert.DoesNotThrow(() => new EdgeAssociationConfig { SigmaLongitudinalFloorM = 0 }.Validate());

        // Veto nad 90 stupnu nema smysl - primka nema orientaci, vetsi rozdil smeru neexistuje.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EdgeAssociationConfig { VetoRad = Conversions.Deg2Rad(120) }.Validate());

        Assert.DoesNotThrow(() => new EdgeAssociationConfig().Validate());
    }
}
