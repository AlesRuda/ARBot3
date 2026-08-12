using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision.Synthetic;

/// <summary>
/// Testy geometrie sceny pro virtualni kameru (viz doc/virtual-hw.md).
/// Vozovka je sjednoceni kapsli kolem os hran; sirka se interpoluje mezi <see cref="Node.Width"/>.
/// </summary>
public class RoadSceneTests
{
    /// <summary>Pocatek lokalni ENU roviny pouzity ve vsech testech.</summary>
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>
    /// Sit s jedinou hranou vedouci z pocatku 10 m na vychod, konstantni sirky.
    /// </summary>
    private static RoadNetwork StraightEastRoad(GeoReference origin, double widthMeters)
    {
        var a = new Node(1, origin.ToLLA(0, 0), widthMeters);
        var b = new Node(2, origin.ToLLA(10, 0), widthMeters);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 10.0, wayId: 1, traversalCost: 10.0);
        return builder.Build();
    }

    [Test]
    public void IsRoad_OnCenterline_IsTrue()
    {
        var origin = Origin();
        var scene = new RoadScene(StraightEastRoad(origin, 4.0), origin);

        Assert.That(scene.IsRoad(5.0, 0.0), Is.True);
    }

    [Test]
    public void IsRoad_BeyondHalfWidth_IsFalse()
    {
        var origin = Origin();
        var scene = new RoadScene(StraightEastRoad(origin, 4.0), origin);   // polosirka 2 m

        Assert.That(scene.IsRoad(5.0, 2.5), Is.False);
    }

    /// <summary>
    /// Sirka se ma interpolovat mezi uzly: u siroke A je bod jeste na vozovce, u uzkeho B uz ne.
    /// </summary>
    [Test]
    public void IsRoad_WidthIsInterpolatedBetweenNodes()
    {
        var origin = Origin();
        var a = new Node(1, origin.ToLLA(0, 0), 6.0);    // polosirka 3 m
        var b = new Node(2, origin.ToLLA(10, 0), 2.0);   // polosirka 1 m

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 10.0, wayId: 1, traversalCost: 10.0);
        var scene = new RoadScene(builder.Build(), origin);

        Assert.Multiple(() =>
        {
            Assert.That(scene.IsRoad(0.5, 1.5), Is.True, "u sirokeho konce A ma bod lezet na vozovce");
            Assert.That(scene.IsRoad(9.5, 1.5), Is.False, "u uzkeho konce B uz ne");
        });
    }

    /// <summary>
    /// Lomena cesta pres mnoho useku a stovky metru - hlida, ze prostorovy index nevynecha
    /// useky ve vzdalenych bunkach (dotaz musi dat totez co linearni prohledani).
    /// </summary>
    [Test]
    public void IsRoad_LongPolyline_HitsEverySegment()
    {
        var origin = Origin();
        var builder = new RoadNetwork.Builder();

        // Schodovita cesta: 20 useku po 25 m, stridave na vychod a na sever.
        var pts = new List<(double e, double n)> { (0, 0) };
        for (int i = 0; i < 20; i++)
        {
            var (e, n) = pts[^1];
            pts.Add(i % 2 == 0 ? (e + 25, n) : (e, n + 25));
        }

        var nodes = new List<Node>();
        for (int i = 0; i < pts.Count; i++)
            nodes.Add(new Node(i + 1, origin.ToLLA(pts[i].e, pts[i].n), 4.0));
        for (int i = 0; i + 1 < nodes.Count; i++)
            builder.AddEdge(nodes[i], nodes[i + 1], 25.0, wayId: 1, traversalCost: 25.0);

        var scene = new RoadScene(builder.Build(), origin);

        Assert.Multiple(() =>
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double mx = (pts[i].e + pts[i + 1].e) / 2.0;
                double my = (pts[i].n + pts[i + 1].n) / 2.0;
                Assert.That(scene.IsRoad(mx, my), Is.True, $"stred useku {i} ma byt na vozovce");
            }
            Assert.That(scene.IsRoad(-50, -50), Is.False, "daleko mimo sit");
            Assert.That(scene.IsRoad(12.5, 10.0), Is.False, "vedle prvniho useku");
        });
    }
}
