#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;

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

    /// <summary>
    /// Nejblizsi hrana k bodu (+ parametr na ni, prumet a vzdalenost).
    /// <para><b>Pri shodne vzdalenosti vyhrava drive pridana hrana</b> (ostre <c>&lt;</c>) - na tom
    /// zavisi vyber cilove hrany pri splitu, viz <c>GoalFieldSplitTests</c>. Bod lezici PRESNE na
    /// obousmerne hrane je stejne daleko od obou jejich smeru, takze tohle poradi neni detail.</para>
    /// </summary>
    /// <summary>
    /// Kandidat na „hranu, po ktere jedeme" — vysledek <see cref="NearestEdges"/>.
    /// </summary>
    public readonly struct EdgeCandidate
    {
        public EdgeCandidate(Edge edge, double t, LLA projection, double distanceM)
        {
            Edge = edge; T = t; Projection = projection; DistanceM = distanceM;
        }

        /// <summary>Hrana site.</summary>
        public Edge Edge { get; }

        /// <summary>Parametr kolmeho prumetu na hranu (0 = From, 1 = To).</summary>
        public double T { get; }

        /// <summary>Kolmy prumet bodu na hranu.</summary>
        public LLA Projection { get; }

        /// <summary>Vzdalenost bodu od hrany [m].</summary>
        public double DistanceM { get; }
    }

    /// <summary>
    /// <b>K nejblizsich hran</b> k bodu, serazenych od nejblizsi — kandidati pro prirazeni
    /// „po ktere ceste jedu".
    ///
    /// <para><b>Proc ne jen <see cref="NearestEdge"/>.</b> Nejblizsi hrana nemusi byt ta spravna:
    /// pri chybe polohy nekolika metru vyhraje u krizovatky <b>pricna ulice</b>. Zmereno
    /// 16. 9. 2026, ze se to tyka <b>poloviny</b> cyklu hranove lokalizace. Rozhodnout to jde az
    /// dalsim udajem (azimut, sirka), a k tomu je potreba vic nez jeden kandidat. Viz
    /// doc/map-correlation-localization.md.</para>
    ///
    /// <para>⚠️ <b>Obousmerna cesta je v siti DVE hrany</b> se shodnou geometrii a nulovym
    /// rozdilem vzdalenosti. Do vysledku jde jen <b>jedna z nich</b> (ta drive pridana, stejne
    /// jako u <see cref="NearestEdge"/>) — jinak by kazde prirazeni vyslo jako nejednoznacne,
    /// protoze druhy kandidat by byl tyz kus asfaltu.</para>
    /// </summary>
    /// <param name="p">Bod (poloha robotu).</param>
    /// <param name="k">Nejvyse kolik kandidatu vratit (pod 1 se bere 1).</param>
    /// <param name="maxDistanceM">Kandidaty dal nez tohle se zahodi.</param>
    public IReadOnlyList<EdgeCandidate> NearestEdges(LLA p, int k,
                                                    double maxDistanceM = double.PositiveInfinity)
    {
        if (k < 1) k = 1;
        var best = new List<EdgeCandidate>(k + 1);
        var seen = new List<(long From, long To, long Way)>(k + 1);

        for (int i = 0; i < _edges.Count; i++)
        {
            if (double.IsPositiveInfinity(_traversal[i])) continue;
            var e = _edges[i];
            var (cp, d, tt) = p.ProjectOntoSegment(e.From.Location, e.To.Location);
            if (d > maxDistanceM) continue;
            if (best.Count == k && d >= best[best.Count - 1].DistanceM) continue;

            // Neorientovany klic: obe hrany obousmerne cesty jsou tyz kus asfaltu.
            var key = e.From.Id <= e.To.Id ? (e.From.Id, e.To.Id, e.WayId) : (e.To.Id, e.From.Id, e.WayId);
            int dup = seen.IndexOf(key);
            if (dup >= 0)
            {
                // Drive pridana hrana vyhrava (ostre <), stejne jako v NearestEdge.
                if (d >= best[dup].DistanceM) continue;
                best.RemoveAt(dup); seen.RemoveAt(dup);
            }

            int at = best.Count;
            while (at > 0 && best[at - 1].DistanceM > d) at--;
            best.Insert(at, new EdgeCandidate(e, tt, cp, d));
            seen.Insert(at, key);
            if (best.Count > k) { best.RemoveAt(best.Count - 1); seen.RemoveAt(seen.Count - 1); }
        }
        return best;
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

    /// <summary>
    /// Zkonvertuje síť na <see cref="MapMsg"/> pro logování/vizualizaci (uzly v LLA stupních, hrany
    /// deduplikované na jednu úsečku — síť má forward i reverzní hranu). Konvence <c>ToLogMessage</c>
    /// jako u ostatních domén (ICP, Collider, EKFStep, navigace).
    /// </summary>
    /// <param name="name">Jméno mapy do zprávy.</param>
    /// <param name="prekryv">Naučené šířky uzlů; <c>null</c> = jen mapa. Díky němu kreslí World
    /// pohled i webový půdorys <b>totéž, proti čemu se koreluje</b> — nesoulad mezi obrázkem
    /// a výpočtem v tomhle projektu už jednou stál hodiny. Viz doc/plan-naucena-sirka-do-mapy.md.</param>
    public MapMsg ToLogMessage(string? name = null, RoadWidthOverrides? prekryv = null)
    {
        var msg = new MapMsg { Name = name ?? string.Empty };
        var index = new Dictionary<long, int>();
        var seen = new HashSet<(long, long, long)>();

        foreach (var e in _edges)
        {
            int fi = AddNode(msg, index, e.From, prekryv);
            int ti = AddNode(msg, index, e.To, prekryv);

            long a = e.From.Id, b = e.To.Id;
            var key = a < b ? (a, b, e.WayId) : (b, a, e.WayId);
            if (!seen.Add(key)) continue;   // obousměrnou hranu kresli jen jednou

            msg.Edges.Add(new MapMsg.MapEdge { From = fi, To = ti, WayId = e.WayId, LengthMeters = e.LengthMeters });
        }
        return msg;

        static int AddNode(MapMsg msg, Dictionary<long, int> index, Node n, RoadWidthOverrides? p)
        {
            if (index.TryGetValue(n.Id, out int i)) return i;
            i = msg.Nodes.Count;
            index[n.Id] = i;
            msg.Nodes.Add(new MapMsg.MapNode
            {
                Id = n.Id,
                LatDeg = Conversions.Rad2Deg(n.Location.Latitude),
                LonDeg = Conversions.Rad2Deg(n.Location.Longitude),
                WidthMeters = p != null && p.TryGet(n.Id, out double w) ? w : n.Width,
            });
            return i;
        }
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
