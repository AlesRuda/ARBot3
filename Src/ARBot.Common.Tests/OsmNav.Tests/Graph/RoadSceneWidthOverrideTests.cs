using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Tests.Localization;

namespace ARBot.Common.Tests.OsmNav.Graph;

/// <summary>
/// Prekryv sirek v <see cref="RoadScene"/> a v <c>MapMsg</c>. <b>BEZ prekryvu se nesmi zmenit
/// NIC</b> — to je pojistka, ze vychozi stav zustal presne takovy, jaky byl.
/// Viz doc/plan-naucena-sirka-do-mapy.md.
/// </summary>
public class RoadSceneWidthOverrideTests
{
    [Test]
    public void SPrekryvem_seCestaRozsiri()
    {
        // Cesta vede na vychod, mapa rika 3 m (polosirka 1,5). Bod 2,5 m stranou je MIMO;
        // po naucenych 6 m (polosirka 3) uz je uvnitr.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var prekryv = RoadWidthOverrides.Build(net, _ => 6.0);

        var bez = new RoadScene(net, o);
        var s = new RoadScene(net, o, prekryv);

        Assert.That(bez.IsRoad(0, 2.5), Is.False, "pri mapovych 3 m je 2,5 m stranou mimo cestu");
        Assert.That(s.IsRoad(0, 2.5), Is.True, "po naucenych 6 m uz je uvnitr");
    }

    [Test]
    public void BezPrekryvu_seChovaStejneJakoDriv()
    {
        // Pojistka proti tomu, aby se zmenilo VYCHOZI chovani: prazdny prekryv musi dat TOTEZ
        // co zadny, bod po bodu.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);

        var bez = new RoadScene(net, o);
        var prazdny = new RoadScene(net, o, RoadWidthOverrides.Prazdny);

        for (double y = -3; y <= 3.0001; y += 0.25)
            Assert.That(prazdny.IsRoad(0, y), Is.EqualTo(bez.IsRoad(0, y)), $"y={y}");
    }

    [Test]
    public void MapMsg_neseSirkuZPrekryvu()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var prekryv = RoadWidthOverrides.Build(net, _ => 6.0);

        var msg = net.ToLogMessage("test", prekryv);

        Assert.That(msg.Nodes, Is.Not.Empty);
        foreach (var n in msg.Nodes)
            Assert.That(n.WidthMeters, Is.EqualTo(6.0).Within(1e-9));
    }

    [Test]
    public void MapMsgBezPrekryvu_neseMapovouSirku()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);

        var msg = net.ToLogMessage("test");

        foreach (var n in msg.Nodes)
            Assert.That(n.WidthMeters, Is.EqualTo(3.0).Within(1e-9));
    }
}
