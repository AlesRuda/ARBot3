#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Maps.OsmNav.Osm;

/// <summary>Sestaví <see cref="RoadNetwork"/> z OSM dat filtrovaných profilem.</summary>
public static class GraphBuilder
{
    private static readonly GreatCircle _greatCircle = new();

    public static RoadNetwork BuildNetwork(OsmData data, TravelProfile profile)
    {
        var builder = new RoadNetwork.Builder();
        var nodeIndex = data.Nodes.ToDictionary(n => n.Id);
        var mapNodes = new Dictionary<long, Node>();

        Node GetNode(long id)
        {
            if (!mapNodes.TryGetValue(id, out Node? n))
            {
                var raw = nodeIndex[id];
                n = new Node(id, LLA.FromDegrees(raw.Lat, raw.Lon));
                mapNodes[id] = n;
            }
            return n;
        }

        var built = new List<Edge>();
        foreach (var way in data.Ways)
        {
            if (!profile.AcceptsWay(way)) continue;
            bool oneway = profile.IsOneway(way);
            for (int i = 0; i + 1 < way.NodeRefs.Count; i++)
            {
                long aId = way.NodeRefs[i], bId = way.NodeRefs[i + 1];
                if (!nodeIndex.ContainsKey(aId) || !nodeIndex.ContainsKey(bId)) continue;
                Node a = GetNode(aId), b = GetNode(bId);
                double len = _greatCircle.Distance(a.Location, b.Location);
                double cost = profile.EdgeCost(way, len);
                built.Add(builder.AddEdge(a, b, len, way.Id, cost));
                if (!oneway) built.Add(builder.AddEdge(b, a, len, way.Id, cost));
            }
        }
        BuildTurnsInto(builder, built, data, profile);
        return builder.Build();
    }

    private static void BuildTurnsInto(RoadNetwork.Builder builder, List<Edge> edges, OsmData data, TravelProfile profile)
    {
        var blockedNodes = data.Nodes.Where(profile.BlocksNode).Select(n => n.Id).ToHashSet();
        var noTurns = new HashSet<(long From, long Via, long To)>();
        var onlyTurns = new Dictionary<(long From, long Via), long>();
        foreach (var r in data.Restrictions)
        {
            if (r.Restriction.StartsWith("no_")) noTurns.Add((r.FromWay, r.ViaNode, r.ToWay));
            else if (r.Restriction.StartsWith("only_")) onlyTurns[(r.FromWay, r.ViaNode)] = r.ToWay;
        }
        var incoming = new Dictionary<long, List<Edge>>();
        var outgoing = new Dictionary<long, List<Edge>>();
        foreach (var e in edges)
        {
            (incoming.TryGetValue(e.To.Id, out var inL) ? inL : incoming[e.To.Id] = new()).Add(e);
            (outgoing.TryGetValue(e.From.Id, out var outL) ? outL : outgoing[e.From.Id] = new()).Add(e);
        }
        foreach (long via in incoming.Keys)
        {
            if (blockedNodes.Contains(via)) continue;
            if (!outgoing.TryGetValue(via, out var outs)) continue;
            foreach (var inEdge in incoming[via])
            {
                bool onlyRule = onlyTurns.TryGetValue((inEdge.WayId, via), out long onlyToWay);
                foreach (var outEdge in outs)
                {
                    if (outEdge.To.Id == inEdge.From.Id && outEdge.WayId == inEdge.WayId) continue;
                    if (noTurns.Contains((inEdge.WayId, via, outEdge.WayId))) continue;
                    if (onlyRule && outEdge.WayId != onlyToWay) continue;
                    builder.AddTurn(inEdge, outEdge, 0.0);
                }
            }
        }
    }
}
