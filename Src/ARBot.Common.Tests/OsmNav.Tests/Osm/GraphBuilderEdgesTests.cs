using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav.Osm;

public class GraphBuilderEdgesTests
{
    private const string TwoWays = """
    <osm version="0.6">
      <node id="1" lat="50.0000" lon="14.0000"/>
      <node id="2" lat="50.0000" lon="14.0010"/>
      <node id="3" lat="50.0000" lon="14.0020"/>
      <way id="100"><nd ref="1"/><nd ref="2"/><nd ref="3"/>
        <tag k="highway" v="residential"/></way>
    </osm>
    """;

    private const string OneWay = """
    <osm version="0.6">
      <node id="1" lat="50.0000" lon="14.0000"/>
      <node id="2" lat="50.0000" lon="14.0010"/>
      <way id="100"><nd ref="1"/><nd ref="2"/>
        <tag k="highway" v="residential"/><tag k="oneway" v="yes"/></way>
    </osm>
    """;

    [Test]
    public void Build_TwoWay_CreatesBothDirectionsPerSegment()
    {
        var data = OsmXmlReader.ReadString(TwoWays);
        var net = GraphBuilder.BuildNetwork(data, TravelProfile.Car());
        // 2 segmenty × 2 směry = 4 orientované hrany
        Assert.That(net.Edges.Count, Is.EqualTo(4));
    }

    [Test]
    public void Build_OneWay_CreatesForwardOnly()
    {
        var data = OsmXmlReader.ReadString(OneWay);
        var net = GraphBuilder.BuildNetwork(data, TravelProfile.Car());
        Assert.That(net.Edges, Has.Exactly(1).Items);
        Assert.That(net.Edges[0].From.Id, Is.EqualTo(1));
        Assert.That(net.Edges[0].To.Id, Is.EqualTo(2));
    }

    [Test]
    public void Build_Pedestrian_IgnoresOneway_CreatesBothDirections()
    {
        var data = OsmXmlReader.ReadString(OneWay);
        var net = GraphBuilder.BuildNetwork(data, TravelProfile.Pedestrian());
        Assert.That(net.Edges.Count, Is.EqualTo(2));
    }
}
