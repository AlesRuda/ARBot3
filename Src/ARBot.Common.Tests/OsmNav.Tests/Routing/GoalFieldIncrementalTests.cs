using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

public class GoalFieldIncrementalTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Dvě paralelní cesty ke cíli: horní levná, dolní dražší (diamant).
    private static (RoadNetwork net, Edge sTop, Edge sLo, LLA goal) Diamond()
    {
        var b = new RoadNetwork.Builder();
        var s = N(1, 50.000, 14.000); var up = N(2, 50.001, 14.001);
        var lo = N(3, 49.999, 14.001); var t = N(4, 50.000, 14.002);
        Edge E(Node x, Node y, double c) => b.AddEdge(x, y, c, 1, c);
        var sUp = E(s, up, 10); var upT = E(up, t, 10);      // horní 20
        var sLo = E(s, lo, 30); var loT = E(lo, t, 30);      // dolní 60
        b.AddTurn(sUp, upT, 0); b.AddTurn(sLo, loT, 0);
        return (b.Build(), sUp, sLo, t.Location);
    }

    [Test]
    public void SetTraversalCost_Blocking_MatchesFreshField()
    {
        var (net, sUp, _, goal) = Diamond();
        var field = new GoalField(net, goal);
        field.EnsureSettled(sUp);

        field.SetTraversalCost(sUp, double.PositiveInfinity); // zablokuj horní vjezd
        // ověř proti čerstvému poli se stejným overlay
        var fresh = new GoalField(net, goal);
        fresh.SetTraversalCost(sUp, double.PositiveInfinity);
        foreach (var e in field.Nodes) { field.EnsureSettled(e); fresh.EnsureSettled(e); }
        foreach (var e in field.Nodes)
            Assert.That(field.CostToGoal(e), Is.EqualTo(fresh.CostToGoal(e)).Within(1e-6));
    }

    [Test]
    public void SetTurnCost_MatchesFreshField()
    {
        var (net, sUp, sLo, goal) = Diamond();
        var upT = net.Successors(sUp).Single();
        var field = new GoalField(net, goal);
        field.EnsureSettled(sUp);

        field.SetTurnCost(sUp, upT, double.PositiveInfinity);   // zakaž horní odbočení
        var fresh = new GoalField(net, goal);
        fresh.SetTurnCost(sUp, upT, double.PositiveInfinity);
        foreach (var e in field.Nodes) { field.EnsureSettled(e); fresh.EnsureSettled(e); }
        foreach (var e in field.Nodes)
            Assert.That(field.CostToGoal(e), Is.EqualTo(fresh.CostToGoal(e)).Within(1e-6));
    }
}
