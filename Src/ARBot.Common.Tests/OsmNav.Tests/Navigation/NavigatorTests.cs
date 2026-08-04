using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Navigation;

public class NavigatorTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    private static (RoadNetwork net, LLA goal) Line()
    {
        var b = new RoadNetwork.Builder();
        var n1 = N(1, 50.0, 14.0000); var n2 = N(2, 50.0, 14.0010);
        var n3 = N(3, 50.0, 14.0020); var n4 = N(4, 50.0, 14.0030);
        Edge E(Node x, Node y) { double l = new GreatCircle().Distance(x.Location, y.Location); return b.AddEdge(x, y, l, 1, l); }
        var all = new[] { E(n1, n2), E(n2, n1), E(n2, n3), E(n3, n2), E(n3, n4), E(n4, n3) };
        foreach (var i in all) foreach (var o in all)
            if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId)) b.AddTurn(i, o, 0);
        return (b.Build(), n4.Location);
    }

    [Test]
    public void Update_ReportsEdgeTowardGoal()
    {
        var (net, goal) = Line();
        var nav = new Navigator(new GoalField(net, goal));
        var fix = nav.Update(LLA.FromDegrees(50.0, 14.0005)); // mezi 1 a 2, cíl u 4 (vpravo)
        Assert.That(fix.NoRoute, Is.False);
        Assert.That(fix.Arrived, Is.False);
        Assert.That(fix.TargetNode!.Id, Is.EqualTo(2)); // směřuje k uzlu 2 (dál k cíli), ne zpět k 1
    }

    [Test]
    public void Update_NearGoal_Arrived()
    {
        var (net, goal) = Line();
        var nav = new Navigator(new GoalField(net, goal));
        var fix = nav.Update(LLA.FromDegrees(50.0, 14.00300)); // na uzlu 4
        Assert.That(fix.Arrived);
    }

    [Test]
    public void Update_DifferentPositions_NoExplicitRerouteNeeded()
    {
        var (net, goal) = Line();
        var field = new GoalField(net, goal);
        var nav = new Navigator(field);
        // „přeskočíme" na jinou hranu — pole to zvládne bez reroute.
        // Pozice 14.0025 leží přímo na cílovém segmentu n3-n4 (cíl u n4). Díky reálnému
        // splitu cílové hrany (Task 3-REV) už to není ∞ — robot se namapuje na regulérní
        // půlku A->T a směřuje k dočasnému uzlu T (== cíl), ne na zastíněnou n3->n4.
        var f1 = nav.Update(LLA.FromDegrees(50.0, 14.0005));
        var f2 = nav.Update(LLA.FromDegrees(50.0, 14.0025));
        Assert.That(f1.NoRoute, Is.False); Assert.That(f2.NoRoute, Is.False);
        long tId = field.Goal.From.Id; // G = T->T, takže G.From je T
        Assert.That(f2.TargetNode!.Id, Is.EqualTo(tId)); // směřuje k T (== cíl), ne pryč od něj
    }
}
