#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Maps.OsmNav.Graph;

/// <summary>
/// Neměnná (po sestavení jen ke čtení) edge-based síť: uzly = orientované hrany,
/// přechody = odbočení. Bezpečná ke sdílení mezi hypotézami/vlákny.
/// </summary>
public sealed class RoadNetwork
{
    private readonly List<Edge> _edges;
    private readonly double[] _traversal;
    private readonly List<Edge>[] _succ;
    private readonly List<Edge>[] _pred;
    private readonly Dictionary<(int From, int To), double> _turn;
    private readonly int[] _reverse;   // _reverse[e.Index] = index reverzní hrany, nebo -1

    private RoadNetwork(List<Edge> edges, double[] traversal, List<Edge>[] succ,
        List<Edge>[] pred, Dictionary<(int, int), double> turn, int[] reverse)
    {
        _edges = edges; _traversal = traversal; _succ = succ; _pred = pred; _turn = turn; _reverse = reverse;
    }

    public int Count => _edges.Count;
    public IReadOnlyList<Edge> Edges => _edges;
    public IReadOnlyList<Edge> Successors(Edge e) => _succ[e.Index];
    public IReadOnlyList<Edge> Predecessors(Edge e) => _pred[e.Index];
    public double BaseTraversalCost(Edge e) => _traversal[e.Index];

    public double BaseTurnCost(Edge from, Edge to) =>
        _turn.TryGetValue((from.Index, to.Index), out double c) ? c : double.PositiveInfinity;

    public double BaseEdgeCost(Edge from, Edge to)
    {
        double turn = BaseTurnCost(from, to);
        return double.IsPositiveInfinity(turn) ? double.PositiveInfinity : turn + BaseTraversalCost(to);
    }

    /// <summary>O(1): reverzní hrana (opačné From/To, stejná WayId), předpočítaná v <see cref="Builder.Build"/>.</summary>
    public Edge? FindReverse(Edge e)
    {
        int r = _reverse[e.Index];
        return r >= 0 ? _edges[r] : null;
    }

    public Edge? NearestEdge(LLA p, out double t, out LLA proj, out double distance)
    {
        Edge? best = null; distance = double.PositiveInfinity; t = 0; proj = p;
        for (int i = 0; i < _edges.Count; i++)
        {
            if (double.IsPositiveInfinity(_traversal[i])) continue;
            var e = _edges[i];
            var (cp, d, tt) = p.ProjectOntoSegment(e.From.Location, e.To.Location);
            if (d < distance) { distance = d; best = e; t = tt; proj = cp; }
        }
        return best;
    }

    /// <summary>Mutovatelný builder; po <see cref="Build"/> je síť neměnná.</summary>
    public sealed class Builder
    {
        private readonly List<Edge> _edges = new();
        private readonly List<double> _traversal = new();
        private readonly List<List<Edge>> _succ = new();
        private readonly List<List<Edge>> _pred = new();
        private readonly Dictionary<(int, int), double> _turn = new();

        public Edge AddEdge(Node from, Node to, double lengthMeters, long wayId, double traversalCost)
        {
            var e = new Edge(_edges.Count, from, to, lengthMeters, wayId);
            _edges.Add(e); _traversal.Add(traversalCost);
            _succ.Add(new List<Edge>()); _pred.Add(new List<Edge>());
            return e;
        }

        public void AddTurn(Edge from, Edge to, double turnCost = 0.0)
        {
            _succ[from.Index].Add(to);
            _pred[to.Index].Add(from);
            _turn[(from.Index, to.Index)] = turnCost;
        }

        public RoadNetwork Build()
        {
            // Lookup klíčovaný (From.Id, To.Id, WayId) -> index; při duplicitách si drží PRVNÍ
            // výskyt, aby seděl s původní semantikou lineárního hledání (foreach v pořadí _edges).
            var lookup = new Dictionary<(long, long, long), int>();
            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                var key = (e.From.Id, e.To.Id, e.WayId);
                if (!lookup.ContainsKey(key)) lookup[key] = i;
            }

            var reverse = new int[_edges.Count];
            for (int i = 0; i < _edges.Count; i++)
            {
                var e = _edges[i];
                reverse[i] = lookup.TryGetValue((e.To.Id, e.From.Id, e.WayId), out int idx) ? idx : -1;
            }

            return new(_edges, _traversal.ToArray(), _succ.ToArray(), _pred.ToArray(), _turn, reverse);
        }
    }
}
