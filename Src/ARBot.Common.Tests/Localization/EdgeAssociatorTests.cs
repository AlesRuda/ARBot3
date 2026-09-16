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

        var sPodlahou = Cfg();                                        // podlaha 10 stupnu
        var t = EdgeAssociator.Associate(net, o, pose, corridor, sPodlahou, 8.0);
        Assert.That(t.Result, Is.EqualTo(EdgeAssocResult.Ok), "s podlahou spravna hrana projde");
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

        // Veto nad 90 stupnu nema smysl - primka nema orientaci, vetsi rozdil smeru neexistuje.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EdgeAssociationConfig { VetoRad = Conversions.Deg2Rad(120) }.Validate());

        Assert.DoesNotThrow(() => new EdgeAssociationConfig().Validate());
    }
}
