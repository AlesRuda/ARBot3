using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

public class RouterTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Rovnoramenný trojúhelník: základna b1-b2 (obousměrná), strany do vrcholu.
    private static RoadNetwork Triangle()
    {
        var b = new RoadNetwork.Builder();
        var b1 = N(1, 50.0000, 14.0000); var b2 = N(2, 50.0000, 14.0020); var apex = N(3, 50.0020, 14.0010);
        var edges = new List<Edge>();
        void TW(Node x, Node y, long way)
        {
            double l = new GreatCircle().Distance(x.Location, y.Location);
            edges.Add(b.AddEdge(x, y, l, way, l));
            edges.Add(b.AddEdge(y, x, l, way, l));
        }
        TW(b1, b2, 100); TW(b1, apex, 101); TW(b2, apex, 102);
        foreach (var i in edges) foreach (var o in edges)
            if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId)) b.AddTurn(i, o, 0);
        return b.Build();
    }

    [Test]
    public void Plan_ReturnsRouteWithoutVirtualEdges()
    {
        var net = Triangle();
        var field = new GoalField(net, LLA.FromDegrees(50.0019, 14.0010)); // cíl u vrcholu
        var path = new Router(field).Plan(LLA.FromDegrees(50.0000, 14.0002)); // start u b1
        Assert.That(path, Is.Not.Empty);
        Assert.That(path.Any(e => e.From.Id == e.To.Id), Is.False);
    }

    [Test]
    public void Plan_ChoosesDirectionTowardGoal()
    {
        var net = Triangle();
        var field = new GoalField(net, LLA.FromDegrees(50.0019, 14.0010));
        var router = new Router(field);
        var nearB1 = router.Plan(LLA.FromDegrees(50.0000, 14.0001));
        var nearB2 = router.Plan(LLA.FromDegrees(50.0000, 14.0019));
        Assert.That(nearB2[0].To.Id, Is.Not.EqualTo(nearB1[0].To.Id)); // jiný první směr
    }

    // Regresní test: bod blízko n1 konce hrany n1->n2, cíl u n3.
    // t ≈ 0.05 (blízko n1), tj. geometricky blíže n1 (away from goal).
    // Správný první krok: n1->n2 (To.Id==2), ne zpět.
    // Druhý scénář: bod blízko n3 konce hrany n2->n3, cíl u n1.
    // t ≈ 0.95, tj. geometricky blíže n3. Stará pravidlo t>0.5 by PONECHALO n2->n3
    // (chybně, pryč od cíle). Správný první krok: n3->n2 (To.Id==2).
    [Test]
    public void Plan_PicksReachableDirection_WhenNearEndpointIsDeadEnd()
    {
        // Síť: n1 - n2 - n3, obousměrné (n1<->n2 a n2<->n3), plné odbočky.
        var b = new RoadNetwork.Builder();
        var n1 = N(1, 50.0, 14.000);
        var n2 = N(2, 50.0, 14.001);
        var n3 = N(3, 50.0, 14.002);
        var edges = new List<Edge>();
        void TW(Node x, Node y, long way)
        {
            double l = new GreatCircle().Distance(x.Location, y.Location);
            edges.Add(b.AddEdge(x, y, l, way, l));
            edges.Add(b.AddEdge(y, x, l, way, l));
        }
        TW(n1, n2, 10); TW(n2, n3, 11);
        // Přidej všechny povolené odbočky (bez U-turnů na stejné way).
        foreach (var i in edges)
            foreach (var o in edges)
                if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId))
                    b.AddTurn(i, o, 0);
        var net = b.Build();

        // --- Scénář 1: start blízko n1 (t≈0.05 na n1->n2), cíl u n3 ---
        // Správný směr: n1->n2 (To.Id==2). Stará i nová pravidla shodně.
        {
            var field = new GoalField(net, LLA.FromDegrees(50.0, 14.00195)); // blízko n3
            var path = new Router(field).Plan(LLA.FromDegrees(50.0, 14.00005)); // blízko n1
            Assert.That(path, Is.Not.Empty);
            Assert.That(path[0].To.Id, Is.EqualTo(2)); // první krok k n2, směr ke goal
        }

        // --- Scénář 2: start blízko n3 (t≈0.95 na n2->n3), cíl u n1 ---
        // t>0.5 by PONECHALO n2->n3 (WRONGly away from goal).
        // Field-aware pravidlo vybere reverzní n3->n2 (To.Id==2, směr ke goal).
        {
            var field = new GoalField(net, LLA.FromDegrees(50.0, 14.00005)); // blízko n1
            var path = new Router(field).Plan(LLA.FromDegrees(50.0, 14.00195)); // blízko n3
            Assert.That(path, Is.Not.Empty);
            Assert.That(path[0].To.Id, Is.EqualTo(2)); // první krok k n2, směr ke goal
        }
    }
}
