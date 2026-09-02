using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav.Osm;

public class TravelProfileTests
{
    private static OsmWayRaw Way(params (string k, string v)[] tags) =>
        new(1, new long[] { 1, 2 }, tags.ToDictionary(t => t.k, t => t.v));

    [Test]
    public void Car_AcceptsResidential_RejectsFootway()
    {
        var car = TravelProfile.Car();
        Assert.That(car.AcceptsWay(Way(("highway", "residential"))));
        Assert.That(car.AcceptsWay(Way(("highway", "footway"))), Is.False);
    }

    [Test]
    public void Pedestrian_AcceptsFootway_IgnoresOneway()
    {
        var walk = TravelProfile.Pedestrian();
        Assert.That(walk.AcceptsWay(Way(("highway", "footway"))));
        Assert.That(walk.IsOneway(Way(("highway", "residential"), ("oneway", "yes"))), Is.False);
    }

    [Test]
    public void Car_RespectsOneway()
    {
        var car = TravelProfile.Car();
        Assert.That(car.IsOneway(Way(("highway", "residential"), ("oneway", "yes"))));
    }

    [Test]
    public void Car_RejectsPrivateAccess()
    {
        var car = TravelProfile.Car();
        Assert.That(car.AcceptsWay(Way(("highway", "service"), ("access", "private"))), Is.False);
    }

    [Test]
    public void Car_BlockedByBollard_PedestrianNot()
    {
        var node = new OsmNodeRaw(3, 50, 14, new Dictionary<string, string> { ["barrier"] = "bollard" });
        Assert.That(TravelProfile.Car().BlocksNode(node));
        Assert.That(TravelProfile.Pedestrian().BlocksNode(node), Is.False);
    }

    [Test]
    public void EdgeCost_DefaultsToTimeByMaxSpeed()
    {
        var car = TravelProfile.Car(); // 13.9 m/s
        double cost = car.EdgeCost(Way(("highway", "residential")), 139.0);
        Assert.That(cost, Is.InRange(9.5, 10.5)); // ~10 s
    }

    // --- Profil Robot -----------------------------------------------------------------------
    // Nas robot neni chodec: po cyklostezce jet muze, po schodech ne. Obojí byla nalezena vada
    // profilu Pedestrian, kterym se mapa nacitala. Viz doc/osm-nav.md.

    private static OsmNodeRaw Node(string barrier) =>
        new(3, 50, 14, new Dictionary<string, string> { ["barrier"] = barrier });

    private static OsmNodeRaw Node(string barrier, params (string k, string v)[] tags)
    {
        var d = new Dictionary<string, string> { ["barrier"] = barrier };
        foreach (var t in tags) d[t.k] = t.v;
        return new OsmNodeRaw(3, 50, 14, d);
    }

    [Test]
    public void Robot_PrijmeCyklostezku_ChodecJiZahazuje()
    {
        // Nalezeno 1. 9. 2026 na haje.osm: z 387 cest se zahazovalo 9 cyklostezek, tedy jedina
        // systematicka ztrata. V OSM se kresli modre carkovane - bylo videt na podkladu a chybelo
        // v siti.
        var way = Way(("highway", "cycleway"));
        Assert.That(TravelProfile.Robot().AcceptsWay(way), "robot na cyklostezku patri");
        Assert.That(TravelProfile.Pedestrian().AcceptsWay(way), Is.False, "puvodni chovani chodce");
    }

    [Test]
    public void Robot_ZahodiSchody_ChodecJePrijima()
    {
        // Opacny smer teze vady: kolovy robot schody nevyjede, ale chodec ano - a plansovac by po
        // nich trasu vedl (9 cest v haje.osm, 37 v HajeRovne.osm).
        var way = Way(("highway", "steps"));
        Assert.That(TravelProfile.Robot().AcceptsWay(way), Is.False, "po schodech robot nejede");
        Assert.That(TravelProfile.Pedestrian().AcceptsWay(way), "puvodni chovani chodce");
    }

    [Test]
    public void Robot_PrijmeBezneCestyStejneJakoChodec()
    {
        var robot = TravelProfile.Robot();
        foreach (string hw in new[] { "footway", "path", "track", "pedestrian", "residential",
                                      "living_street", "service", "unclassified", "tertiary" })
            Assert.That(robot.AcceptsWay(Way(("highway", hw))), $"{hw} ma zustat prujezdny");
    }

    [Test]
    public void Robot_JeBlokovanNeprekonatelnymiBarierami()
    {
        var robot = TravelProfile.Robot();
        foreach (string b in new[] { "stile", "turnstile", "kissing_gate", "cycle_barrier" })
            Assert.That(robot.BlocksNode(Node(b)), $"{b} robot neprojede");
    }

    [Test]
    public void Robot_NENIblokovanZavorouAniSloupkem()
    {
        // Rozchod robota je 0,41 m, takze mezerou u sloupku nebo vedle zavory projede; a co
        // neprojede, zastavi lokalni vyhybani (occupancy grid). Blokovat je globalne by v parku
        // plnem bran rozpojilo sit. Viz doc/osm-nav.md.
        var robot = TravelProfile.Robot();
        Assert.That(robot.BlocksNode(Node("gate")), Is.False);
        Assert.That(robot.BlocksNode(Node("lift_gate")), Is.False);
        Assert.That(robot.BlocksNode(Node("bollard")), Is.False);
        Assert.That(robot.BlocksNode(Node("entrance")), Is.False, "entrance je OTVOR, ne prekazka");
    }

    [Test]
    public void Robot_RespektujeSoukromyPristup()
    {
        Assert.That(TravelProfile.Robot().AcceptsWay(
            Way(("highway", "service"), ("access", "private"))), Is.False);
    }

    // --- Pristup a zamceni NA UZLU ----------------------------------------------------------
    // Do 2. 9. 2026 se BlockedAccess uplatnoval jen na CESTY (AcceptsWay), takze zamcena nebo
    // soukroma branka byla prujezdna. V datech to neni teoreticke: haje.osm 1x, Piestany.osm 2x,
    // Bratislava.osm 10x brana s access=private nebo customers. Viz doc/osm-nav.md.

    [Test]
    public void Zamcena_Brana_Blokuje()
    {
        // locked=yes podle OSM znamena "obvykle zamceno, potreba klic". Robot klic nema.
        Assert.That(TravelProfile.Robot().BlocksNode(Node("gate", ("locked", "yes"))));
    }

    [Test]
    public void Soukroma_Brana_Blokuje()
    {
        var robot = TravelProfile.Robot();
        Assert.That(robot.BlocksNode(Node("gate", ("access", "private"))));
        Assert.That(robot.BlocksNode(Node("gate", ("access", "no"))));
    }

    [Test]
    public void OtevrenaBrana_NEBLOKUJE()
    {
        // Zustava puvodni chovani: brana bez omezeni je pruchod, ne prekazka - na krizeni plotu
        // s cestou je to PRAVE ta cesta skrz.
        var robot = TravelProfile.Robot();
        Assert.That(robot.BlocksNode(Node("gate")), Is.False);
        Assert.That(robot.BlocksNode(Node("gate", ("access", "yes"))), Is.False);
        Assert.That(robot.BlocksNode(Node("gate", ("locked", "no"))), Is.False);
    }

    [Test]
    public void PristupSeResiJEN_naUzluSBarierou()
    {
        // Uzel bez bariery se neposuzuje - pristup na ceste uz resi AcceptsWay a blokovat kazdy
        // uzel s access= by rozpojilo sit na mistech, kde zadna prekazka neni.
        var robot = TravelProfile.Robot();
        var bezBariery = new OsmNodeRaw(3, 50, 14,
            new Dictionary<string, string> { ["access"] = "private" });
        Assert.That(robot.BlocksNode(bezBariery), Is.False);
    }

    [Test]
    public void ZamceniIPristup_PlatiIProOstatniProfily()
    {
        // Neni to zvlastnost robota - zamcena brana neni prujezdna pro nikoho.
        foreach (var p in new[] { TravelProfile.Robot(), TravelProfile.Pedestrian(),
                                  TravelProfile.Car(), TravelProfile.Bicycle() })
        {
            Assert.That(p.BlocksNode(Node("gate", ("locked", "yes"))), $"{p.Name}: zamcena brana");
            Assert.That(p.BlocksNode(Node("gate", ("access", "private"))), $"{p.Name}: soukroma brana");
        }
    }
}
