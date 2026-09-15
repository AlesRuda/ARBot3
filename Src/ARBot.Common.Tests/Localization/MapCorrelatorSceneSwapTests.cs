using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Scena korelatoru jde vymenit za behu — naucena sirka cesty
/// (doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 5).
///
/// <para><see cref="RoadScene"/> je NEMENNA, takze jde o atomickou zamenu reference: vlakno
/// stupne uvidi bud starou, nebo novou, nikdy rozpracovanou.</para>
/// </summary>
public class MapCorrelatorSceneSwapTests
{
    private static MapCorrelator Korelator(RoadNetwork net, ARBot.Common.Coordinates.GeoReference o)
        => new MapCorrelator(new AsyncFusionEngine(new EKFModel()),
                             new RoadScene(net, o),
                             CorrelationTestScenes.TestConfig());

    [Test]
    public void ScenaJdeVymenit()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var korelator = Korelator(net, o);

        var nova = new RoadScene(net, o, RoadWidthOverrides.Build(net, _ => 6.0));
        korelator.Scene = nova;

        Assert.That(korelator.Scene, Is.SameAs(nova));
        Assert.That(korelator.Scene.IsRoad(0, 2.5), Is.True, "nova scena uz ma naucenou sirku");
    }

    [Test]
    public void ScenaNesmiBytNull()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var korelator = Korelator(net, o);

        Assert.That(() => korelator.Scene = null, Throws.ArgumentNullException);
    }
}
