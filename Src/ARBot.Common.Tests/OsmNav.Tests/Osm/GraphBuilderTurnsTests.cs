using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav.Osm;

public class GraphBuilderTurnsTests
{
    // Křižovatka X(=node 2): from way 100 (1->2), to ways 101 (2->3) a 102 (2->4).
    private const string Cross = """
    <osm version="0.6">
      <node id="1" lat="50.0000" lon="14.0000"/>
      <node id="2" lat="50.0000" lon="14.0010"/>
      <node id="3" lat="50.0000" lon="14.0020"/>
      <node id="4" lat="50.0010" lon="14.0010"/>
      <way id="100"><nd ref="1"/><nd ref="2"/><tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
      <way id="101"><nd ref="2"/><nd ref="3"/><tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
      <way id="102"><nd ref="2"/><nd ref="4"/><tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
    </osm>
    """;

    private static string WithRestriction(string kind) => """
    <osm version="0.6">
      <node id="1" lat="50.0000" lon="14.0000"/>
      <node id="2" lat="50.0000" lon="14.0010"/>
      <node id="3" lat="50.0000" lon="14.0020"/>
      <node id="4" lat="50.0010" lon="14.0010"/>
      <way id="100"><nd ref="1"/><nd ref="2"/><tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
      <way id="101"><nd ref="2"/><nd ref="3"/><tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
      <way id="102"><nd ref="2"/><nd ref="4"/><tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
      <relation id="500">
        <member type="way" ref="100" role="from"/>
        <member type="node" ref="2" role="via"/>
        <member type="way" ref="102" role="to"/>
        <tag k="type" v="restriction"/><tag k="restriction" v="KIND"/>
      </relation>
    </osm>
    """.Replace("KIND", kind);

    private static Edge Edge(RoadNetwork net, long fromNode, long toNode) =>
        net.Edges.Single(e => e.From.Id == fromNode && e.To.Id == toNode);

    [Test]
    public void Turns_ConnectIncomingToOutgoingAtJunction()
    {
        var net = GraphBuilder.BuildNetwork(OsmXmlReader.ReadString(Cross), TravelProfile.Car());
        var inEdge = Edge(net, 1, 2);
        Assert.That(net.Successors(inEdge), Does.Contain(Edge(net, 2, 3)));
        Assert.That(net.Successors(inEdge), Does.Contain(Edge(net, 2, 4)));
    }

    [Test]
    public void NoTurnRestriction_RemovesForbiddenTurn()
    {
        var net = GraphBuilder.BuildNetwork(OsmXmlReader.ReadString(WithRestriction("no_left_turn")), TravelProfile.Car());
        var inEdge = Edge(net, 1, 2);
        Assert.That(net.Successors(inEdge), Does.Not.Contain(Edge(net, 2, 4))); // 100->102 zakázáno
        Assert.That(net.Successors(inEdge), Does.Contain(Edge(net, 2, 3)));       // 100->101 povoleno
    }

    [Test]
    public void OnlyTurnRestriction_KeepsOnlyAllowedTurn()
    {
        var net = GraphBuilder.BuildNetwork(OsmXmlReader.ReadString(WithRestriction("only_straight_on")), TravelProfile.Car());
        var inEdge = Edge(net, 1, 2);
        Assert.That(net.Successors(inEdge), Does.Contain(Edge(net, 2, 4)));       // jen 100->102
        Assert.That(net.Successors(inEdge), Does.Not.Contain(Edge(net, 2, 3))); // 100->101 potlačeno
    }
}
