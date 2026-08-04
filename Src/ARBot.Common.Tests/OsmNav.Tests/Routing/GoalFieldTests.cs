using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

public class GoalFieldTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Obousměrný řetěz 1-2-3-4 podél rovnoběžky (cost = délka).
    private static (RoadNetwork net, Node n1, Node n4) Chain()
    {
        var b = new RoadNetwork.Builder();
        var n1 = N(1, 50.0, 14.0000); var n2 = N(2, 50.0, 14.0010);
        var n3 = N(3, 50.0, 14.0020); var n4 = N(4, 50.0, 14.0030);
        Edge Fwd(Node x, Node y) { double l = new GreatCircle().Distance(x.Location, y.Location); return b.AddEdge(x, y, l, 1, l); }
        var e12 = Fwd(n1, n2); var e21 = Fwd(n2, n1);
        var e23 = Fwd(n2, n3); var e32 = Fwd(n3, n2);
        var e34 = Fwd(n3, n4); var e43 = Fwd(n4, n3);
        // plné propojení navazujících hran (bez U-turn)
        var all = new[] { e12, e21, e23, e32, e34, e43 };
        foreach (var i in all) foreach (var o in all)
            if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId)) b.AddTurn(i, o, 0);
        return (b.Build(), n1, n4);
    }

    [Test]
    public void EnsureSettled_CostToGoal_MatchesDijkstraFromGoal()
    {
        var (net, n1, n4) = Chain();
        var field = new GoalField(net, n4.Location); // cíl u uzlu 4
        // usadíme vše: EnsureSettled pro každý uzel
        foreach (var e in field.Nodes) field.EnsureSettled(e);

        var oracle = GoalDijkstraOracle.CostToGoal(field);
        foreach (var e in field.Nodes)
            Assert.That(field.CostToGoal(e), Is.EqualTo(oracle[e.Index]).Within(1e-6));
    }

    [Test]
    public void NextEdge_DescendsTowardGoal()
    {
        var (net, n1, n4) = Chain();
        var field = new GoalField(net, n4.Location);
        // start u uzlu 1: hrana 1->2
        var start = net.Edges.Single(e => e.From.Id == 1 && e.To.Id == 2);
        field.EnsureSettled(start);
        // sestup: každý krok snižuje CostToGoal
        var cur = start;
        double prev = field.CostToGoal(cur);
        int guard = 10;
        while (field.NextEdge(cur) is Edge nxt && guard-- > 0)
        {
            field.EnsureSettled(nxt);
            Assert.That(field.CostToGoal(nxt) <= prev + 1e-9);
            prev = field.CostToGoal(nxt);
            cur = nxt;
        }
        Assert.That(prev <= 1e-9); // došli jsme k cíli (g==0)
    }

    [Test]
    public void CostToGoal_Unreachable_IsInfinity()
    {
        var b = new RoadNetwork.Builder();
        var iso = b.AddEdge(N(9, 49.0, 13.0), N(10, 49.0, 13.001), 71, 1, 5); // izolovaná, daleko
        var a = b.AddEdge(N(1, 50.0, 14.0), N(2, 50.0, 14.001), 71, 1, 5);
        var net = b.Build();
        var field = new GoalField(net, LLA.FromDegrees(50.0, 14.0005)); // cíl u a
        field.EnsureSettled(iso);
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(iso)));
    }
}
