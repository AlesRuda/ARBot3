using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Maps.OsmNav.Navigation;

/// <summary>
/// Tenký sledovač gradientu nad sdíleným <see cref="GoalField"/>: mapmatchne polohu,
/// vybere orientaci s nižší cenou do cíle (field-aware: zohledňuje zbývající traversal
/// aktuálního segmentu) a vrátí (hrana, cílový uzel).
/// Off-route se neřeší explicitně — jiná poloha jen přečte pole jinde.
/// </summary>
public sealed class Navigator
{
    private static readonly GreatCircle _greatCircle = new();
    private readonly GoalField _field;
    private readonly NavigatorOptions _opts;

    public Navigator(GoalField field, NavigatorOptions? options = null)
    {
        _field = field;
        _opts = options ?? new NavigatorOptions();
    }

    public NavigationFix Update(LLA position)
    {
        var edge = _field.NearestNode(position, out double t, out _, out double dist);
        if (edge is null)
            return new NavigationFix(null, null, double.PositiveInfinity, false, true);

        // Field-aware direction choice (same logic as Router):
        // costFwd = remaining traversal on matched edge + CostToGoal(edge)
        // costRev = elapsed traversal on reverse edge + CostToGoal(rev)
        // field.NearestNode/FindReverse/BaseTraversalCost also work for the goal split's
        // temporary halves, so a robot on the goal segment maps onto a regular edge with
        // no special-casing needed here.
        _field.EnsureSettled(edge);
        var rev = _field.FindReverse(edge);
        if (rev is not null) _field.EnsureSettled(rev);

        double costFwd = (1.0 - t) * _field.BaseTraversalCost(edge) + _field.CostToGoal(edge);
        double costRev = rev is not null
            ? t * _field.BaseTraversalCost(rev) + _field.CostToGoal(rev)
            : double.PositiveInfinity;

        var chosen = (costRev < costFwd) ? rev! : edge;

        // Check arrived first: if the robot is physically within arrival radius it has reached the goal
        // regardless of routing state (e.g. last-mile edge may have no successors in the field).
        bool arrived = _greatCircle.Distance(position, _field.GoalPoint) <= _opts.ArrivalRadiusMeters;
        if (arrived)
            return new NavigationFix(chosen, chosen.To, dist, true, false);

        if (double.IsPositiveInfinity(_field.CostToGoal(chosen)))
            return new NavigationFix(chosen, chosen.To, dist, false, true);

        return new NavigationFix(chosen, chosen.To, dist, false, false);
    }
}
