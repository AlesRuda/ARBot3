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

    public IReadOnlyList<Edge> Plan(LLA from) => Plan(from, out _);

    /// <summary>
    /// Trasa jako <see cref="Plan(LLA)"/> a k ní <b>zbývající délka od robotu</b> [m].
    /// <para>Trasa je seznam <b>celých</b> hran, takže prostý součet <c>LengthMeters</c> započítá
    /// první hranu i tou částí, která je už za robotem (na startu nadhodnocení až o jednu hranu).
    /// Tady se z první hrany bere jen zbytek před robotem: <c>1 − t</c>, když se jede po hraně,
    /// kterou vrátil mapmatch, a <c>t</c>, když se jede po její <b>obrácené</b> orientaci (stejně
    /// jako cena <c>costRev</c> níže). Do 29. 9. 2026 se délka počítala jen součtem.</para>
    /// </summary>
    public IReadOnlyList<Edge> Plan(LLA from, out double remainingM)
    {
        remainingM = 0;
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

        // Podíl první hrany, který je ještě PŘED robotem (t je parametr na hraně z mapmatche).
        double ahead = 1.0 - t;
        if (rev is not null && costRev < costFwd) { start = rev; ahead = t; }

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

        for (int i = 0; i < path.Count; i++)
            remainingM += path[i].LengthMeters;
        // Oříznout jen tehdy, když první hrana trasy JE hrana, na které robot stojí (virtuální
        // smyčka cíle se do trasy nepřidává, pak začíná trasa až další hranou).
        if (path.Count > 0 && ReferenceEquals(path[0], start))
            remainingM -= (1.0 - Math.Clamp(ahead, 0.0, 1.0)) * start.LengthMeters;
        return path;
    }
}
