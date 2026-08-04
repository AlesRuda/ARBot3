using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

/// <summary>
/// Regresní testy pro reálný split cílové hrany (nahrazuje starý augment-pahýl
/// z <c>BugProbeTests</c>). Konkrétní číselné hodnoty, nezávislé na interním
/// wiringu pole — to hlídá test 3 (nezávislá Dijkstra oracle).
/// </summary>
public class GoalFieldSplitTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Obousměrná linka n1-n2-n3-n4, každá hrana len=100 (explicitně), plné turny (bez U-turn).
    private static (RoadNetwork net, Node n1, Node n2, Node n3, Node n4) Line100()
    {
        var b = new RoadNetwork.Builder();
        var n1 = N(1, 50.0, 14.0000); var n2 = N(2, 50.0, 14.0010);
        var n3 = N(3, 50.0, 14.0020); var n4 = N(4, 50.0, 14.0030);
        Edge E(Node x, Node y) => b.AddEdge(x, y, 100, 1, 100);
        var all = new[] { E(n1, n2), E(n2, n1), E(n2, n3), E(n3, n2), E(n3, n4), E(n4, n3) };
        foreach (var i in all) foreach (var o in all)
            if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId)) b.AddTurn(i, o, 0);
        return (b.Build(), n1, n2, n3, n4);
    }

    /// <summary>Replikuje field-aware volbu směru z Routeru/Navigatoru (nezávisle na jejich kódu).</summary>
    private static Edge ChooseDirection(GoalField field, LLA p)
    {
        var edge = field.NearestNode(p, out double t, out _, out _)
                   ?? throw new InvalidOperationException("NearestNode nevrátil žádnou hranu.");
        field.EnsureSettled(edge);
        var rev = field.FindReverse(edge);
        if (rev is not null) field.EnsureSettled(rev);

        double costFwd = (1 - t) * field.BaseTraversalCost(edge) + field.CostToGoal(edge);
        double costRev = rev is not null
            ? t * field.BaseTraversalCost(rev) + field.CostToGoal(rev)
            : double.PositiveInfinity;
        return (rev is not null && costRev < costFwd) ? rev : edge;
    }

    [Test]
    public void DeadEndGoal_RobotOnGoalSegment_FiniteCost()
    {
        var (net, _, _, n3, n4) = Line100();
        var field = new GoalField(net, n4.Location); // slepý cíl přesně u n4

        // 10 % po úseku n3->n4 (blízko n3) => NearestNode musí vrátit A->T půlku (From=n3).
        var near = LLA.FromDegrees(50.0, 14.0021);
        var e = field.NearestNode(near, out double t, out _, out _);
        Assert.That(e, Is.Not.Null);
        Assert.That(e!.From.Id, Is.EqualTo(n3.Id));

        field.EnsureSettled(e);
        double cost = field.CostToGoal(e);
        Assert.That(double.IsPositiveInfinity(cost), Is.False,
            $"CostToGoal(A->T) = {cost}  (na staré augment implementaci by bylo ∞)");

        // "zbývající vzdálenost k cíli" = remaining traversal + cost-to-goal; u splitu
        // s cílem přesně v n4 je cost-to-goal(A->T)=0, takže se to redukuje na (1-t)*100.
        double remaining = (1 - t) * field.BaseTraversalCost(e) + cost;
        Assert.That(remaining, Is.EqualTo(90.0).Within(1e-3)); // blízko 100 od n3 (10% už ujeto)

        var farther = LLA.FromDegrees(50.0, 14.0025); // 50 % po úseku
        var e2 = field.NearestNode(farther, out double t2, out _, out _);
        field.EnsureSettled(e2!);
        double remaining2 = (1 - t2) * field.BaseTraversalCost(e2!) + field.CostToGoal(e2!);
        Assert.That(remaining2, Is.EqualTo(50.0).Within(1e-3));
        Assert.That(remaining2 < remaining, "vzdálenost k cíli musí klesat dál po segmentu");
    }

    [Test]
    public void MidSegmentGoal_TwoWay_HeadsTowardGoal()
    {
        var b = new RoadNetwork.Builder();
        var n1 = N(1, 50.0, 14.0000); var n2 = N(2, 50.0, 14.0010);
        var n3 = N(3, 50.0, 14.0030); var n4 = N(4, 50.0, 14.0040);
        Edge E(Node x, Node y, double len) => b.AddEdge(x, y, len, 1, len);
        var all = new[]
        {
            E(n1, n2, 100), E(n2, n1, 100),
            E(n2, n3, 200), E(n3, n2, 200), // dlouhá obousměrná hrana, cíl uprostřed
            E(n3, n4, 100), E(n4, n3, 100),
        };
        foreach (var i in all) foreach (var o in all)
            if (i.To.Id == o.From.Id && !(o.To.Id == i.From.Id && o.WayId == i.WayId)) b.AddTurn(i, o, 0);
        var net = b.Build();

        var goal = LLA.FromDegrees(50.0, 14.0020); // t=0.5 na n2->n3 (200 m)
        var field = new GoalField(net, goal);
        long tId = field.Goal.From.Id; // G = T->T, takže G.From je T

        // Robot těsně PŘED cílem (bližší k n2, t~0.3 originálního úseku).
        var before = LLA.FromDegrees(50.0, 14.0016);
        var chosenBefore = ChooseDirection(field, before);
        Assert.That(chosenBefore.To.Id, Is.EqualTo(tId));
        field.EnsureSettled(chosenBefore);
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(chosenBefore)), Is.False);

        // Robot těsně ZA cílem (bližší k n3, t~0.7 originálního úseku).
        var after = LLA.FromDegrees(50.0, 14.0024);
        var chosenAfter = ChooseDirection(field, after);
        Assert.That(chosenAfter.To.Id, Is.EqualTo(tId));
        field.EnsureSettled(chosenAfter);
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(chosenAfter)), Is.False);
    }

    /// <summary>Prostá Dijkstra "cost do uzlu T" nad RoadNetwork (žádná souvislost s GoalField).</summary>
    private static double[] ManualCostToNode(RoadNetwork net, long nodeId)
    {
        var dist = new double[net.Count];
        Array.Fill(dist, double.PositiveInfinity);
        var pq = new PriorityQueue<Edge, double>();
        foreach (var e in net.Edges)
            if (e.To.Id == nodeId) { dist[e.Index] = 0; pq.Enqueue(e, 0); }

        while (pq.TryDequeue(out var u, out double du))
        {
            if (du > dist[u.Index]) continue;
            foreach (var p in net.Predecessors(u))
            {
                double c = net.BaseEdgeCost(p, u);
                if (double.IsPositiveInfinity(c)) continue;
                double nd = du + c;
                if (nd < dist[p.Index]) { dist[p.Index] = nd; pq.Enqueue(p, nd); }
            }
        }
        return dist;
    }

    [Test]
    public void Oracle_SplitMatchesManualPreSplitDijkstra()
    {
        // Trojúhelníkový cyklus P->A->B->P (jednosměrný), hrana A->B je cílová (split).
        // Nekolineární uzly (L-tvar), aby nevznikaly falešné shody vzdálenosti v NearestEdge.
        var p = N(1, 50.0000, 14.0000);
        var a = N(3, 50.0000, 14.0010);
        var bNode = N(4, 50.0010, 14.0010);

        // --- Síť 1: reálný GoalField split ---
        var b1 = new RoadNetwork.Builder();
        var ePA = b1.AddEdge(p, a, 30, 10, 30);
        var eAB = b1.AddEdge(a, bNode, 100, 20, 100);
        var eBP = b1.AddEdge(bNode, p, 20, 30, 20);
        b1.AddTurn(ePA, eAB, 5);
        b1.AddTurn(eAB, eBP, 3);
        b1.AddTurn(eBP, ePA, 2);
        var net1 = b1.Build();

        var goal = LLA.FromDegrees(50.0004, 14.0010); // t=0.4 na A->B (len=100)
        var field = new GoalField(net1, goal);

        long tId = field.Goal.From.Id;
        var eAT = field.Nodes.Single(x => x.From.Id == a.Id && x.To.Id == tId);
        var eTB = field.Nodes.Single(x => x.From.Id == tId && x.To.Id == bNode.Id);

        field.EnsureSettled(eAT); field.EnsureSettled(eTB);
        field.EnsureSettled(ePA); field.EnsureSettled(eBP);

        // --- Síť 2: RUČNÍ pre-split (T je normální uzel) + prostá Dijkstra, nezávislé na GoalField ---
        var t = new Node(99, goal);
        var b2 = new RoadNetwork.Builder();
        var ePA2 = b2.AddEdge(p, a, 30, 10, 30);
        var eAT2 = b2.AddEdge(a, t, 40, 20, 40);    // t*100=40
        var eTB2 = b2.AddEdge(t, bNode, 60, 20, 60); // (1-t)*100=60
        var eBP2 = b2.AddEdge(bNode, p, 20, 30, 20);
        b2.AddTurn(ePA2, eAT2, 5);
        b2.AddTurn(eTB2, eBP2, 3);
        b2.AddTurn(eBP2, ePA2, 2);
        var net2 = b2.Build();

        var dist = ManualCostToNode(net2, t.Id);

        Assert.That(field.CostToGoal(eAT), Is.EqualTo(dist[eAT2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(eTB), Is.EqualTo(dist[eTB2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(ePA), Is.EqualTo(dist[ePA2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(eBP), Is.EqualTo(dist[eBP2.Index]).Within(1e-6));

        // Sanity: konkrétní očekávané hodnoty (ruční výpočet, viz report).
        Assert.That(field.CostToGoal(eAT), Is.EqualTo(0.0).Within(1e-6));
        Assert.That(field.CostToGoal(ePA), Is.EqualTo(45.0).Within(1e-6));
        Assert.That(field.CostToGoal(eBP), Is.EqualTo(77.0).Within(1e-6));
        Assert.That(field.CostToGoal(eTB), Is.EqualTo(100.0).Within(1e-6));
    }

    /// <summary>
    /// Stejná oracle metoda jako <see cref="Oracle_SplitMatchesManualPreSplitDijkstra"/>, ale
    /// pro OBOUSMĚRNOU cílovou hranu — hlídá zpětnou (B-stranu, eBT/eTA) větev
    /// <c>GoalField.InsertGoal</c>, kterou předchozí test (jednosměrný trojúhelník) nepokrýval.
    /// Čtverec P-A-B-Q-P, všechny hrany obousměrné, cíl na A&lt;-&gt;B (t=0.4). Turn costy c1..c8
    /// jsou všechny RŮZNÉ a NENULOVÉ — kdyby reverzní větev omylem použila succ/pred z "e"
    /// místo "rev" (half-swap bug), dostala by jiné turny (c1/c2 místo c3/c4) i jiné navazující
    /// hrany, takže by výsledná čísla pro eBT/eTA/ePA-strana nesouhlasila s oracle.
    /// </summary>
    [Test]
    public void Oracle_TwoWaySplit_MatchesManualPreSplitDijkstra()
    {
        var p = N(1, 50.0000, 14.0000);
        var a = N(2, 50.0000, 14.0010);
        var bNode = N(3, 50.0010, 14.0010);
        var q = N(4, 50.0010, 14.0000);

        const double c1 = 7, c2 = 11, c3 = 13, c4 = 17, c5 = 19, c6 = 23, c7 = 29, c8 = 31;

        // --- Síť 1: reálný GoalField split (obousměrná cílová hrana A<->B) ---
        var b1 = new RoadNetwork.Builder();
        var ePA = b1.AddEdge(p, a, 30, 10, 30);
        var eAP = b1.AddEdge(a, p, 30, 10, 30);
        var eAB = b1.AddEdge(a, bNode, 100, 20, 100);   // e   (musí být přidána PŘED eBA, aby vyhrála tie v NearestEdge)
        var eBA = b1.AddEdge(bNode, a, 100, 20, 100);   // rev
        var eBQ = b1.AddEdge(bNode, q, 40, 30, 40);
        var eQB = b1.AddEdge(q, bNode, 40, 30, 40);
        var eQP = b1.AddEdge(q, p, 50, 40, 50);
        var ePQ = b1.AddEdge(p, q, 50, 40, 50);

        b1.AddTurn(ePA, eAB, c1);   // -> eAT  (dopředná, A-strana)
        b1.AddTurn(eAB, eBQ, c2);   // eTB ->  (dopředná, A-strana)
        b1.AddTurn(eBA, eAP, c3);   // eTA ->  (ZPĚTNÁ, B-strana)
        b1.AddTurn(eQB, eBA, c4);   // -> eBT  (ZPĚTNÁ, B-strana)
        b1.AddTurn(eBQ, eQP, c5);   // uzavírá dopřednou smyčku
        b1.AddTurn(eQP, ePA, c6);   // ... zpět na eAT (přesměrováno)
        b1.AddTurn(eAP, ePQ, c7);   // uzavírá zpětnou smyčku
        b1.AddTurn(ePQ, eQB, c8);   // ... zpět na eBT (přesměrováno)
        var net1 = b1.Build();

        var goal = LLA.FromDegrees(50.0004, 14.0010); // t=0.4 na A->B (len=100)
        var field = new GoalField(net1, goal);

        long tId = field.Goal.From.Id;
        var eAT = field.Nodes.Single(x => x.From.Id == a.Id && x.To.Id == tId);
        var eTB = field.Nodes.Single(x => x.From.Id == tId && x.To.Id == bNode.Id);
        var eBT = field.Nodes.Single(x => x.From.Id == bNode.Id && x.To.Id == tId);
        var eTA = field.Nodes.Single(x => x.From.Id == tId && x.To.Id == a.Id);

        foreach (var e in new[] { eAT, eTB, eBT, eTA, ePA, eAP, eBQ, eQB })
            field.EnsureSettled(e);

        // --- Síť 2: RUČNÍ pre-split OBOU směrů (T normální uzel) + prostá Dijkstra, nezávislé na GoalField ---
        var t = new Node(99, goal);
        var b2 = new RoadNetwork.Builder();
        var ePA2 = b2.AddEdge(p, a, 30, 10, 30);
        var eAP2 = b2.AddEdge(a, p, 30, 10, 30);
        var eAT2 = b2.AddEdge(a, t, 40, 20, 40);     // t*100        (z e)
        var eTB2 = b2.AddEdge(t, bNode, 60, 20, 60); // (1-t)*100    (z e)
        var eBT2 = b2.AddEdge(bNode, t, 60, 20, 60); // (1-t)*100    (z rev)
        var eTA2 = b2.AddEdge(t, a, 40, 20, 40);     // t*100        (z rev)
        var eBQ2 = b2.AddEdge(bNode, q, 40, 30, 40);
        var eQB2 = b2.AddEdge(q, bNode, 40, 30, 40);
        var eQP2 = b2.AddEdge(q, p, 50, 40, 50);
        var ePQ2 = b2.AddEdge(p, q, 50, 40, 50);

        b2.AddTurn(ePA2, eAT2, c1);
        b2.AddTurn(eTB2, eBQ2, c2);
        b2.AddTurn(eTA2, eAP2, c3);
        b2.AddTurn(eQB2, eBT2, c4);
        b2.AddTurn(eBQ2, eQP2, c5);
        b2.AddTurn(eQP2, ePA2, c6);
        b2.AddTurn(eAP2, ePQ2, c7);
        b2.AddTurn(ePQ2, eQB2, c8);
        var net2 = b2.Build();

        var dist = ManualCostToNode(net2, t.Id);

        // Dopředná strana (eAT/eTB) — už krytá jednosměrným testem výše, zde jen pro úplnost.
        Assert.That(field.CostToGoal(eAT), Is.EqualTo(dist[eAT2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(eTB), Is.EqualTo(dist[eTB2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(ePA), Is.EqualTo(dist[ePA2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(eBQ), Is.EqualTo(dist[eBQ2.Index]).Within(1e-6));

        // ZPĚTNÁ strana (eBT/eTA) — TOHLE je nový oracle guard, který předtím chyběl.
        Assert.That(field.CostToGoal(eBT), Is.EqualTo(dist[eBT2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(eTA), Is.EqualTo(dist[eTA2.Index]).Within(1e-6));
        Assert.That(field.CostToGoal(eQB), Is.EqualTo(dist[eQB2.Index]).Within(1e-6)); // reverzní "approach" hrana
        Assert.That(field.CostToGoal(eAP), Is.EqualTo(dist[eAP2.Index]).Within(1e-6));

        // Sanity: konkrétní ručně dopočítané hodnoty (viz report) — všechny odlišné a nenulové
        // (kromě triviálních eAT/eBT=0, obě jsou přímo v cíli T), takže half-swap bug v reverzní
        // větvi (např. použití Successors(e)/Predecessors(e) místo Successors(rev)/Predecessors(rev))
        // by změnil c3/c4 na c1/c2 a/nebo napojil jiné navazující hrany a test by spadl.
        Assert.That(field.CostToGoal(eAT), Is.EqualTo(0.0).Within(1e-6));
        Assert.That(field.CostToGoal(eBT), Is.EqualTo(0.0).Within(1e-6));
        Assert.That(field.CostToGoal(ePA), Is.EqualTo(47.0).Within(1e-6));
        Assert.That(field.CostToGoal(eQB), Is.EqualTo(77.0).Within(1e-6));
        Assert.That(field.CostToGoal(eBQ), Is.EqualTo(169.0).Within(1e-6));
        Assert.That(field.CostToGoal(eTB), Is.EqualTo(220.0).Within(1e-6));
        Assert.That(field.CostToGoal(eAP), Is.EqualTo(227.0).Within(1e-6));
        Assert.That(field.CostToGoal(eTA), Is.EqualTo(270.0).Within(1e-6));
    }

    [Test]
    public void ClearGoal_RestoresAndReplans()
    {
        var (net, n1, _, _, n4) = Line100();
        int baseCount = net.Count;

        var field = new GoalField(net, n4.Location);
        Assert.That(field.Nodes.Count > baseCount);

        field.ClearGoal();
        Assert.That(field.Nodes.Count, Is.EqualTo(baseCount));

        field.InsertGoal(n1.Location);
        Assert.That(field.Nodes.Count > baseCount);

        var e43 = net.Edges.Single(e => e.From.Id == 4 && e.To.Id == 3); // směr zpět k n1
        field.EnsureSettled(e43);
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(e43)), Is.False);
    }
}
