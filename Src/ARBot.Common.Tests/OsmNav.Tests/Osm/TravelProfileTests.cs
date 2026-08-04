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
}
