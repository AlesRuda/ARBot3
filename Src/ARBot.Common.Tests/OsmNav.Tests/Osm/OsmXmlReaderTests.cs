using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav.Osm;

public class OsmXmlReaderTests
{
    private const string Xml = """
    <osm version="0.6">
      <node id="1" lat="50.0000" lon="14.0000"/>
      <node id="2" lat="50.0000" lon="14.0010"/>
      <node id="3" lat="50.0010" lon="14.0010">
        <tag k="barrier" v="bollard"/>
      </node>
      <way id="100">
        <nd ref="1"/><nd ref="2"/>
        <tag k="highway" v="residential"/>
        <tag k="oneway" v="yes"/>
      </way>
      <way id="101">
        <nd ref="2"/><nd ref="3"/>
        <tag k="highway" v="residential"/>
      </way>
      <relation id="500">
        <member type="way" ref="100" role="from"/>
        <member type="node" ref="2" role="via"/>
        <member type="way" ref="101" role="to"/>
        <tag k="type" v="restriction"/>
        <tag k="restriction" v="no_left_turn"/>
      </relation>
    </osm>
    """;

    [Test]
    public void Read_ParsesNodesWaysTags()
    {
        var data = OsmXmlReader.ReadString(Xml);
        Assert.That(data.Nodes.Count, Is.EqualTo(3));
        Assert.That(data.Ways.Count, Is.EqualTo(2));

        var node3 = data.Nodes.Single(n => n.Id == 3);
        Assert.That(node3.Tags["barrier"], Is.EqualTo("bollard"));

        var way100 = data.Ways.Single(w => w.Id == 100);
        Assert.That(way100.NodeRefs, Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(way100.Tags["oneway"], Is.EqualTo("yes"));
    }

    [Test]
    public void Read_ParsesViaNodeRestriction()
    {
        var data = OsmXmlReader.ReadString(Xml);
        Assert.That(data.Restrictions, Has.Exactly(1).Items);
        var r = data.Restrictions.Single();
        Assert.That(r.FromWay, Is.EqualTo(100));
        Assert.That(r.ViaNode, Is.EqualTo(2));
        Assert.That(r.ToWay, Is.EqualTo(101));
        Assert.That(r.Restriction, Is.EqualTo("no_left_turn"));
    }
}
