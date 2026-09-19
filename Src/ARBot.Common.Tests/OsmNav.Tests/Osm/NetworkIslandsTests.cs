using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Common.Tests.OsmNav.Osm;

/// <summary>
/// Odriznuti ostrovu site (<see cref="NetworkIslands"/>). Replika souteze 19. 9. 2026: chodnik
/// (dlouhy), namesti (kratke, husty) a mezi nimi JEN schody - pod profilem Robot je namesti
/// ostrov, pod profilem Pedestrian (schody pousti) je sit souvisla.
/// </summary>
public class NetworkIslandsTests
{
    private const string SquareAndSidewalk = """
    <osm version="0.6">
      <node id="1" lat="50.0000" lon="14.0000"/>
      <node id="2" lat="50.0000" lon="14.0020"/>
      <node id="3" lat="50.0000" lon="14.0040"/>
      <node id="10" lat="50.0001" lon="14.0020"/>
      <node id="11" lat="50.0002" lon="14.0020"/>
      <node id="12" lat="50.0002" lon="14.0022"/>
      <node id="13" lat="50.0001" lon="14.0022"/>
      <way id="100"><nd ref="1"/><nd ref="2"/><nd ref="3"/><tag k="highway" v="footway"/></way>
      <way id="200"><nd ref="10"/><nd ref="11"/><nd ref="12"/><nd ref="13"/><nd ref="10"/>
        <tag k="highway" v="pedestrian"/><tag k="area" v="yes"/></way>
      <way id="300"><nd ref="2"/><nd ref="10"/><tag k="highway" v="steps"/></way>
    </osm>
    """;

    [Test]
    public void Robot_SchodyNespojuji_NamestiJeOstrov()
    {
        var data = OsmXmlReader.ReadString(SquareAndSidewalk);
        var r = NetworkIslands.Analyze(data, TravelProfile.Robot());

        Assert.Multiple(() =>
        {
            Assert.That(r.Components.Count, Is.EqualTo(2), "chodnik a namesti, schody je nespoji");
            Assert.That(r.Components[0].WayIds, Is.EquivalentTo(new long[] { 100 }), "nejvetsi podle DELKY je chodnik (~290 m)");
            Assert.That(r.Components[1].WayIds, Is.EquivalentTo(new long[] { 200 }));
            Assert.That(r.Components[1].NodeCount, Is.EqualTo(4), "namesti ma vic uzlu na metr - pocet uzlu by rozhodl spatne");
        });
    }

    [Test]
    public void Pedestrian_SchodyPousti_SitJeSouvisla()
    {
        var data = OsmXmlReader.ReadString(SquareAndSidewalk);
        var r = NetworkIslands.Analyze(data, TravelProfile.Pedestrian());
        Assert.That(r.IsConnected, Is.True);
    }

    [Test]
    public void Prune_ZahodiOstrov_ASitPakNemaHranuNamesti()
    {
        var data = OsmXmlReader.ReadString(SquareAndSidewalk);
        var pruned = NetworkIslands.Prune(data, TravelProfile.Robot(), out var report);
        var net = GraphBuilder.BuildNetwork(pruned, TravelProfile.Robot());

        Assert.Multiple(() =>
        {
            Assert.That(report.Dropped, Is.EqualTo(1));
            Assert.That(net.Edges.Select(e => e.WayId).Distinct(), Is.EquivalentTo(new long[] { 100 }));
            Assert.That(pruned.Ways.Any(w => w.Id == 300), Is.True, "schody profil nepousti, ale z dat se nemazou (jiny profil je muze chtit)");
            Assert.That(pruned.Nodes.Count, Is.EqualTo(data.Nodes.Count), "uzly zustavaji, GraphBuilder si bere jen odkazovane");
        });
    }

    [Test]
    public void Prune_SouvislaSit_VratiTutezInstanci()
    {
        var data = OsmXmlReader.ReadString(SquareAndSidewalk);
        var pruned = NetworkIslands.Prune(data, TravelProfile.Pedestrian(), out var report);
        Assert.Multiple(() =>
        {
            Assert.That(report.IsConnected, Is.True);
            Assert.That(pruned, Is.SameAs(data));
        });
    }

    [Test]
    public void Describe_RikaCoSeZahodilo()
    {
        var data = OsmXmlReader.ReadString(SquareAndSidewalk);
        NetworkIslands.Prune(data, TravelProfile.Robot(), out var report);
        string text = NetworkIslands.Describe(report);
        Assert.That(text, Does.Contain("ZAHOZENO 1").And.Contain("way 200").And.Contain("mapprune=false"));
    }
}
