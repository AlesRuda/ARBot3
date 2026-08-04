using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Maps.OsmNav.Routing;

/// <summary>Bezstavová extrakce trasy z <see cref="GoalField"/> sestupem gradientu.</summary>
public sealed class Router
{
    private readonly GoalField _field;
    public Router(GoalField field) => _field = field;

    public IReadOnlyList<Edge> Plan(LLA from)
    {
        var start = _field.NearestNode(from, out double t, out _, out _);
        if (start is null) return System.Array.Empty<Edge>();

        // Vyber směr jízdy podle skutečných nákladů k cíli, ne jen geometrie.
        // costFwd = zbývající traversal start hrany + cost-to-goal start hrany
        // costRev = ujitá část jako traversal rev hrany + cost-to-goal rev hrany
        // Pokud rev neexistuje (jednosměrná), použij start.
        // field.NearestNode/FindReverse/BaseTraversalCost fungují i pro dočasné půlky
        // cílového splitu, takže robot na cílovém segmentu se namapuje na regulérní
        // hranu bez speciálního případu.
        var rev = _field.FindReverse(start);
        _field.EnsureSettled(start);
        if (rev is not null) _field.EnsureSettled(rev);

        double costFwd = (1.0 - t) * _field.BaseTraversalCost(start) + _field.CostToGoal(start);
        double costRev = rev is not null
            ? t * _field.BaseTraversalCost(rev) + _field.CostToGoal(rev)
            : double.PositiveInfinity;

        if (rev is not null && costRev < costFwd) start = rev;

        if (double.IsPositiveInfinity(_field.CostToGoal(start))) return System.Array.Empty<Edge>();

        var path = new List<Edge>();
        if (start.From.Id != start.To.Id) path.Add(start);
        var cur = start;
        int guard = _field.Nodes.Count + 1;
        while (cur.Index != _field.Goal.Index && guard-- > 0)
        {
            var next = _field.NextEdge(cur);
            if (next is null) return System.Array.Empty<Edge>();
            _field.EnsureSettled(next);
            if (next.From.Id != next.To.Id) path.Add(next); // odfiltruj virtuální G
            cur = next;
        }
        return path;
    }
}
