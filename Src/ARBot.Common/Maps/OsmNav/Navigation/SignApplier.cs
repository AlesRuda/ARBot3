using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Maps.OsmNav.Navigation;

/// <summary>
/// Promítá runtime dopravní značky do sdíleného <see cref="GoalField"/> (globální overlay).
/// Pozn.: sign na hraně, kterou <see cref="GoalField.InsertGoal"/> právě rozdělila/zastínila
/// (tj. cílová hrana samotná, ne její dočasné půlky), je no-op by design - zastíněná hrana
/// už není součástí Nodes a GoalField.SetTraversalCost/SetTurnCost navíc ignoruje i dočasné
/// půlky splitu (Index &gt;= permanentní hranice), viz komentář tamtéž.
/// </summary>
public sealed class SignApplier
{
    private readonly GoalField _field;
    public SignApplier(GoalField field) => _field = field;

    public void SpeedLimit(Edge edge, double metersPerSecond) =>
        _field.SetTraversalCost(edge, edge.LengthMeters / metersPerSecond);

    public void CloseRoad(Edge edge) => _field.SetTraversalCost(edge, double.PositiveInfinity);

    public void NoTurn(Edge from, Edge to) => _field.SetTurnCost(from, to, double.PositiveInfinity);

    public void OnlyTurn(Edge from, Edge onlyTo)
    {
        foreach (var s in _field.Successors(from))
            if (s.Index != onlyTo.Index) _field.SetTurnCost(from, s, double.PositiveInfinity);
    }
}
