using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Navigation;

public class SignApplierTests
{
    private static Node N(long id, double lat, double lon) => new(id, LLA.FromDegrees(lat, lon));

    // Diamant: horní levná (s-up-t), dolní dražší (s-lo-t), obě se sbíhají do t a
    // pokračují SAMOSTATNOU výjezdovou hranou t->y. Cíl je umístěn u y, tedy na
    // vyhrazené výjezdové hraně - NE na hranách sUp/upT, které značkujeme. Díky tomu
    // GoalField.InsertGoal rozdělí (a zastíní) jen t->y, a značky na sUp/upT mají
    // reálný efekt (nejsou "pod cílem").
    private static (RoadNetwork net, Edge sUp, Edge upT, Edge sLo, Edge loT, LLA goal) Diamond()
    {
        var b = new RoadNetwork.Builder();
        var s = N(1, 50.0000, 14.0000);
        var up = N(2, 50.0010, 14.0010);
        var lo = N(3, 49.9990, 14.0010);
        var t = N(4, 50.0000, 14.0020);
        var y = N(5, 50.0000, 14.0040);

        Edge E(Node x, Node yy, double len) => b.AddEdge(x, yy, len, 1, len);
        var sUp = E(s, up, 10); var upT = E(up, t, 10);
        var sLo = E(s, lo, 30); var loT = E(lo, t, 30);
        var tY = E(t, y, 20);

        b.AddTurn(sUp, upT, 0);
        b.AddTurn(sLo, loT, 0);
        b.AddTurn(upT, tY, 0);
        b.AddTurn(loT, tY, 0);
        // žádné U-turny (sUp<->upT apod. nejsou obousměrné, U-turn by nedával smysl)

        return (b.Build(), sUp, upT, sLo, loT, y.Location);
    }

    [Test]
    public void CloseRoad_ForcesDetour()
    {
        var (net, sUp, upT, sLo, loT, goal) = Diamond();
        var field = new GoalField(net, goal);

        field.EnsureSettled(sUp);
        field.EnsureSettled(sLo);
        Assert.That(field.CostToGoal(sUp) < field.CostToGoal(sLo)); // horní větev je levnější

        new SignApplier(field).CloseRoad(upT); // uzavři horní výjezd z junction

        field.EnsureSettled(sUp);
        field.EnsureSettled(sLo);
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(sUp)) || field.CostToGoal(sUp) > field.CostToGoal(sLo));
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(sLo)), Is.False); // dolní větev zůstává průchodná
    }

    [Test]
    public void SpeedLimit_SetsProportionalTraversalCost()
    {
        var (net, sUp, upT, _, _, goal) = Diamond();
        var field = new GoalField(net, goal);

        // Rychlostní značka na upT (délka 10 m) při 2 m/s -> traversal cost 5 s.
        // Traversal(upT) se promítne do ceny VSTUPU do upT, tedy do CostToGoal jejího
        // předchůdce sUp (ne do CostToGoal(upT) samotné - to je cena OD upT k cíli).
        new SignApplier(field).SpeedLimit(upT, metersPerSecond: 2.0);
        field.EnsureSettled(sUp);

        Assert.That(field.CostToGoal(sUp), Is.EqualTo(5.0 + field.CostToGoal(upT)).Within(1e-6));
    }

    [Test]
    public void NoTurn_RemovesTurn()
    {
        var (net, sUp, upT, sLo, loT, goal) = Diamond();
        var field = new GoalField(net, goal);

        field.EnsureSettled(sUp);
        field.EnsureSettled(sLo);
        double sLoBefore = field.CostToGoal(sLo);
        Assert.That(field.CostToGoal(sUp) < sLoBefore); // horní odbočka se dřív používala

        new SignApplier(field).NoTurn(sUp, upT); // zakaž jediné odbočení z sUp

        field.EnsureSettled(sUp);
        field.EnsureSettled(sLo);
        Assert.That(double.IsPositiveInfinity(field.CostToGoal(sUp))); // sUp už nikam nevede
        Assert.That(field.CostToGoal(sLo), Is.EqualTo(sLoBefore).Within(1e-6)); // dolní větev je overlayem nedotčená
    }
}
