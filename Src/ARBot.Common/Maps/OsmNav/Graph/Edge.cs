using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Graph;

/// <summary>
/// Orientovaná hrana původní mapy = uzel edge-based grafu.
/// <see cref="Index"/> je hustý index do polí plánovače.
/// </summary>
public sealed class Edge
{
    public int Index { get; }
    public Node From { get; }
    public Node To { get; }
    public double LengthMeters { get; }
    public long WayId { get; }

    public Edge(int index, Node from, Node to, double lengthMeters, long wayId)
    {
        Index = index;
        From = from;
        To = to;
        LengthMeters = lengthMeters;
        WayId = wayId;
    }
}
