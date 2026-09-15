using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Runtime.Tests;

/// <summary>
/// Minimalni sit pro testy runtime.
///
/// <para>⚠️ <b>Zamerne se NEPOUZIVA <c>CorrelationTestScenes</c></b> z <c>ARBot.Common.Tests</c>:
/// tenhle projekt ten testovaci projekt <b>nereferencuje</b> (jedina reference je
/// <c>ARBot.Runtime</c>) a pridavat referenci mezi dvema testovacimi projekty by zatahlo celou
/// cizi sadu. Viz doc/plan-naucena-sirka-do-mapy-kroky.md.</para>
/// </summary>
internal static class TestRoadNetwork
{
    public static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Jedna prima cesta podel osy X (na vychod), delka 60 m, stred v y = 0.</summary>
    public static RoadNetwork StraightEastRoad(GeoReference o, double width = 3.0)
    {
        var a = new Node(1, o.ToLLA(-30, 0), width);
        var b = new Node(2, o.ToLLA(30, 0), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 60.0, wayId: 1, traversalCost: 60.0);
        return builder.Build();
    }
}
