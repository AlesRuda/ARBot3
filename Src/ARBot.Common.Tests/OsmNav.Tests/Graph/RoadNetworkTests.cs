using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav.Graph;

public class RoadNetworkTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    [Test]
    public void Builder_BuildsReadOnlyTopologyAndCosts()
    {
        var b = new RoadNetwork.Builder();
        var a = N(1, 50.0, 14.000); var c = N(2, 50.0, 14.001); var d = N(3, 50.0, 14.002);
        var e0 = b.AddEdge(a, c, 71, 10, 5);
        var e1 = b.AddEdge(c, d, 71, 10, 9);
        b.AddTurn(e0, e1, 2);
        var net = b.Build();

        Assert.That(net.Count, Is.EqualTo(2));
        Assert.That(net.BaseTraversalCost(e0), Is.EqualTo(5));
        Assert.That(net.Successors(e0), Does.Contain(e1));
        Assert.That(net.Predecessors(e1), Does.Contain(e0));
        Assert.That(net.BaseTurnCost(e0, e1), Is.EqualTo(2));
        Assert.That(net.BaseEdgeCost(e0, e1), Is.EqualTo(11)); // turn 2 + traversal(e1) 9
        Assert.That(double.IsPositiveInfinity(net.BaseTurnCost(e1, e0)));
    }

    [Test]
    public void FindReverse_And_NearestEdge()
    {
        var b = new RoadNetwork.Builder();
        var a = N(1, 50.0000, 14.0000); var c = N(2, 50.0000, 14.0010);
        var ac = b.AddEdge(a, c, 71, 10, 5);
        var ca = b.AddEdge(c, a, 71, 10, 5);
        var net = b.Build();

        Assert.That(net.FindReverse(ac), Is.SameAs(ca));
        var near = net.NearestEdge(LLA.FromDegrees(50.0001, 14.0005), out double t, out _, out double dist);
        Assert.That(near, Is.Not.Null);
        Assert.That(t, Is.InRange(0.4, 0.6));
        Assert.That(dist, Is.InRange(0, 30));
    }

    [Test]
    public void BuildNetwork_FromOsm_RespectsOneway()
    {
        const string oneWay = """
        <osm version="0.6">
          <node id="1" lat="50.0000" lon="14.0000"/>
          <node id="2" lat="50.0000" lon="14.0010"/>
          <way id="100"><nd ref="1"/><nd ref="2"/>
            <tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
        </osm>
        """;
        var net = GraphBuilder.BuildNetwork(OsmXmlReader.ReadString(oneWay), TravelProfile.Car());
        Assert.That(net.Edges, Has.Exactly(1).Items);
        Assert.That(net.Edges[0].From.Id, Is.EqualTo(1));
    }
}
