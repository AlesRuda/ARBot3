using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Common.Tests.OsmNav.Routing;

/// <summary>Referenční cost-to-goal: Dijkstra po REVERZNÍCH hranách z cíle přes GoalField.</summary>
public static class GoalDijkstraOracle
{
    public static double[] CostToGoal(GoalField f)
    {
        // Edge.Index je hustý přes CELOU síť + temp hrany (0.._p+tempCount-1); f.Nodes je
        // filtrované (bez zastíněných originálů cílového splitu), takže Nodes.Count může být
        // menší než max Index+1 — pole musí velikostí odpovídat indexům, ne počtu uzlů.
        var dist = new double[f.Nodes.Max(e => e.Index) + 1];
        Array.Fill(dist, double.PositiveInfinity);
        dist[f.Goal.Index] = 0;
        var pq = new PriorityQueue<Edge, double>();
        pq.Enqueue(f.Goal, 0);
        while (pq.TryDequeue(out var u, out double du))
        {
            if (du > dist[u.Index]) continue;
            foreach (var p in f.Predecessors(u)) // kdo může vstoupit do u
            {
                double c = f.EdgeCost(p, u);
                if (double.IsPositiveInfinity(c)) continue;
                double nd = du + c;
                if (nd < dist[p.Index]) { dist[p.Index] = nd; pq.Enqueue(p, nd); }
            }
        }
        return dist;
    }
}
