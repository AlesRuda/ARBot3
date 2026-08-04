using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

/// <summary>
/// Regrese pro V2 review Fix 1: sign-overlay (<see cref="GoalField.SetTraversalCost"/> /
/// <see cref="GoalField.SetTurnCost"/>) nesmí ukládat hodnoty pod DOČASNÝM indexem (split
/// half cíle, Index &gt;= počet permanentních hran sítě). Dočasné indexy se po
/// <see cref="GoalField.ClearGoal"/> + <see cref="GoalField.InsertGoal"/> RECYKLUJÍ
/// (_p, _p+1, ...) - bez guardu by stará hodnota "prosákla" do nového splitu a potichu
/// zkorumpovala CostToGoal, přestože sign byl myšlen jen pro starý (už neexistující) split.
/// </summary>
public class GoalFieldReplanOverlayTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Obousměrná linka n1-n2-n3-n4, hrany 100 m, plné odbočky (bez U-turn).
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

    [Test]
    public void StaleOverride_OnReusedTempIndex_DoesNotBleedIntoNewSplit()
    {
        var (net, n1, n2, _, n4) = Line100();
        var goalA = n2.Location;   // cíl A: uprostřed hrany n1<->n2
        var goalB = n4.Location;   // cíl B: jiné místo (dead-end u n4)

        var field = new GoalField(net, goalA);

        // Dočasná půlka splitu blízko cíle A (Index >= net.Count == permanentní hranice).
        var pointNearGoalA = LLA.FromDegrees(50.0, 14.0002);
        var tempHalf = field.NearestNode(pointNearGoalA, out _, out _, out _);
        Assert.That(tempHalf, Is.Not.Null);
        Assert.That(tempHalf!.Index >= net.Count, "NearestNode musí vrátit dočasnou půlku splitu (temp index)");

        // Simuluje značku na AKTUÁLNÍM segmentu (jako by SignApplier.SpeedLimit/CloseRoad dostal
        // NavigationFix.CurrentEdge, což může být právě dočasná půlka splitu).
        field.SetTraversalCost(tempHalf, 999999);

        // Replan na úplně jiné místo - dočasné indexy (_p, _p+1, ...) se recyklují.
        field.ClearGoal();
        field.InsertGoal(goalB);
        foreach (var e in field.Nodes) field.EnsureSettled(e);

        // Referenční pole úplně BEZ předchozího signu.
        var fresh = new GoalField(net, goalB);
        foreach (var e in fresh.Nodes) fresh.EnsureSettled(e);

        Assert.That(field.Nodes.Count, Is.EqualTo(fresh.Nodes.Count));
        foreach (var e in field.Nodes)
            Assert.That(field.CostToGoal(e), Is.EqualTo(fresh.CostToGoal(e)).Within(1e-6));
    }
}
