using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

/// <summary>
/// Task 8: ověřuje klíčovou vlastnost v2 — JEDNO sdílené <see cref="GoalField"/> (jedna
/// mapa v paměti) obsluhuje více pozičních hypotéz (MCL částic) bez jakéhokoli
/// per-hypotézového stavu. Žádná produkční změna — jde čistě o test vlastnosti.
/// </summary>
public class SharedFieldTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Obousměrná linka n1-n2-n3-n4, cíl u n4 (na dedikovaném koncovém úseku).
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
    public void OneField_ServesMultipleHypotheses_Consistently()
    {
        var (net, goal) = Line();
        var field = new GoalField(net, goal); // JEDNO pole (jedna mapa v paměti)

        // tři "hypotézy" (částice) na různých místech podél linky, v rostoucí
        // vzdálenosti od cíle (n4 je na konci s rostoucím lon) — každá jen ČTE
        // sdílené pole přes svůj vlastní (lehký) Navigator.
        var n1 = new Navigator(field);
        var n2 = new Navigator(field);
        var n3 = new Navigator(field);

        var hFar = n1.Update(LLA.FromDegrees(50.00001, 14.0005));   // mezi n1-n2, nejdál od cíle
        var hMid = n2.Update(LLA.FromDegrees(50.00001, 14.0015));   // mezi n2-n3
        var hNear = n3.Update(LLA.FromDegrees(50.00001, 14.0025));  // mezi n3-n4, nejblíž cíli

        // Potvrzení: jde skutečně o JEDNO pole — všichni tři navigátoři čtou stejnou instanci.
        Assert.That(GetField(n1), Is.SameAs(field));
        Assert.That(GetField(n2), Is.SameAs(field));
        Assert.That(GetField(n3), Is.SameAs(field));

        // žádná hypotéza není NoRoute
        Assert.That(hFar.NoRoute, Is.False);
        Assert.That(hMid.NoRoute, Is.False);
        Assert.That(hNear.NoRoute, Is.False);

        // všechny míří k cíli: CostToGoal aktuální hrany klesá s blízkostí k cíli
        // (field.CostToGoal je jediný zdroj pravdy, sdílený mezi všemi hypotézami).
        double costFar = field.CostToGoal(hFar.CurrentEdge!);
        double costMid = field.CostToGoal(hMid.CurrentEdge!);
        double costNear = field.CostToGoal(hNear.CurrentEdge!);

        Assert.That(costFar > costMid, $"costFar={costFar} musí být > costMid={costMid}");
        Assert.That(costMid > costNear, $"costMid={costMid} musí být > costNear={costNear}");

        // a lokálně: cílový uzel každé hypotézy je blíž cíli (nižší lon-vzdálenost k n4)
        // než uzel, ze kterého vychází — tj. postupuje směrem k cíli, ne od něj.
        foreach (var h in new[] { hFar, hMid, hNear })
        {
            double fromLon = h.CurrentEdge!.From.Location.Longitude;
            double toLon = h.TargetNode!.Location.Longitude;
            Assert.That(toLon >= fromLon - 1e-9, $"TargetNode.Lon={toLon} musí postupovat >= From.Lon={fromLon}");
        }
    }

    [Test]
    public void FrontierExpandedByOneHypothesis_IsReusedByAnother()
    {
        var (net, goal) = Line();
        var field = new GoalField(net, goal);

        // Pozn. k adaptaci na reálný split (oproti plánu): cíl je dead-end přesně u n4, takže
        // GoalField.InsertGoal ROZDĚLÍ a ZASTÍNÍ původní hranu n3->n4 (i její reverzní n4->n3) —
        // ty se stávají permanentně nedosažitelnými "mrtvými" uzly v edge-based grafu (nahrazeny
        // dočasnými půlkami n3->T, T->n4 atd., viz GoalFieldSplitTests). Použití n3->n4 jako
        // "near" by tedy testovalo mrtvý uzel s věčně ∞ cenou — ne vlastnost sdíleného pole.
        // Místo toho použijeme dvojici hran, které OBĚ zůstávají v reálném grafu (nezastíněné):
        // far = n1->n2 (nejdál od cíle), near = n2->n3 (blíž cíli). Vzdálená hypotéza usadí "far",
        // což donutí LPA* frontier expandovat směrem ven od cíle přes celou linku — a protože
        // "near" leží na cestě MEZI cílem a "far", musí být (stejně jako u Dijkstry se
        // stoupajícími klíči) usazen jako vedlejší produkt TÉTO JEDNÉ expanze, bez further explicit
        // EnsureSettled(near) volání. To je právě sdílený/lazy frontier v akci.
        var far = net.Edges.Single(e => e.From.Id == 1 && e.To.Id == 2);
        var near = net.Edges.Single(e => e.From.Id == 2 && e.To.Id == 3);

        field.EnsureSettled(far);
        double costFar = field.CostToGoal(far);
        Assert.That(double.IsPositiveInfinity(costFar), Is.False);

        // bližší hypotéza čte hodnotu z TÉHOŽ pole, aniž by explicitně vyvolala vlastní expanzi —
        // frontier rozšířený vzdálenou hypotézou už "near" usadil (je konzistentní se sdílením:
        // hodnota nezávisí na tom, KTERÁ hypotéza expanzi vyvolala).
        double costNear = field.CostToGoal(near);

        Assert.That(double.IsPositiveInfinity(costNear), Is.False);
        Assert.That(costFar > costNear, $"costFar={costFar} musí být > costNear={costNear} (dál od cíle = větší cena)");

        // re-čtení stejných hran (jako by to dělala jiná/třetí hypotéza) musí vrátit
        // BEZE ZMĚNY stejné hodnoty — sdílený frontier se dál nemění, jen se čte.
        Assert.That(field.CostToGoal(far), Is.EqualTo(costFar).Within(1e-9));
        Assert.That(field.CostToGoal(near), Is.EqualTo(costNear).Within(1e-9));
    }

    // Reflection helper: potvrzuje, že Navigator interně drží referenci na PŘESNĚ tu
    // samou GoalField instanci, kterou mu předal konstruktor — žádné klonování/kopie pole.
    private static GoalField GetField(Navigator navigator)
    {
        var f = typeof(Navigator).GetField("_field", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (GoalField)f!.GetValue(navigator)!;
    }
}
