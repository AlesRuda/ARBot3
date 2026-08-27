#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Maps.OsmNav.Routing;

/// <summary>
/// Goal-rooted pole cost-to-goal nad neměnnou <see cref="RoadNetwork"/>. LPA* od cíle
/// s h=0, počítané líně (<see cref="EnsureSettled"/>). Robot z libovolné hrany
/// sestupuje gradientem (<see cref="NextEdge"/>). Značky = řídký overlay (globální).
///
/// Cíl reálně ROZDĚLÍ svou nejbližší hranu dočasným uzlem T na dvě regulérní hrany
/// A→T a T→B (+ B→T, T→A u obousměrné). Původní hrana(y) se zastíní (zmizí z
/// <see cref="Nodes"/>/<see cref="Successors"/>/<see cref="Predecessors"/>). Díky tomu
/// robot na cílovém segmentu (i slepý cíl) dostane konečnou cost-to-goal bez
/// jakéhokoli speciálního „phantom" případu v Routeru/Navigatoru.
/// </summary>
public sealed class GoalField
{
    private readonly RoadNetwork _net;
    private readonly int _p;                       // permanentní hranice = _net.Count

    // split-overlay (Index >= _p jsou dočasné hrany)
    private readonly HashSet<int> _shadow = new();                        // zastíněné síťové hrany (e, rev)
    private readonly List<Edge> _tempEdges = new();                       // eAT, eTB, G, (eBT, eTA)
    private readonly List<double> _tempTrav = new();                      // podle (Index - _p)
    private readonly Dictionary<int, List<Edge>> _succOverride = new();
    private readonly Dictionary<int, List<Edge>> _predOverride = new();
    private readonly Dictionary<(int, int), double> _turn = new();        // explicitní turn ceny (temp + přesměrované)
    private readonly Dictionary<int, int> _tempReverse = new();           // eAT<->eTA, eTB<->eBT (Index<->Index)

    // globální cost-overlay na permanentních uzlech/přechodech (přežívá ClearGoal)
    private readonly Dictionary<int, double> _travOverride = new();
    private readonly Dictionary<(int, int), double> _turnOverride = new();

    // stav pole
    private double[] _g = System.Array.Empty<double>();
    private double[] _rhs = System.Array.Empty<double>();
    private readonly SortedSet<(double Key, int Index)> _open = new();
    private readonly Dictionary<int, double> _openKey = new();
    private Edge? _goal;
    private long _nextTempNodeId = -1;

    private readonly List<Edge> _all = new();        // dense, indexováno 1:1 podle Edge.Index (vč. zastíněných)
    private readonly List<Edge> _nodesList = new();  // filtrované (bez zastíněných) + temp, vč. G

    public RoadNetwork Network => _net;
    public LLA GoalPoint { get; private set; }
    public Edge Goal => _goal!;
    public IReadOnlyList<Edge> Nodes => _nodesList;

    public GoalField(RoadNetwork network, LLA goal)
    {
        _net = network;
        _p = network.Count;
        InsertGoal(goal);
    }

    // ---- split (nahrazuje starý augment-pahýl) ----

    /// <summary>
    /// Vloží cíl: nejbližší hranu sítě rozdělí dočasným uzlem T na dvě regulérní hrany
    /// A→T, T→B (+ B→T, T→A u obousměrné) a zastíní původní hranu(y). Kořenem pole je
    /// virtuální cíl G (nulová hrana T→T). Je-li už vložen jiný cíl, nejdřív <see cref="ClearGoal"/>.
    /// </summary>
    public void InsertGoal(LLA goal)
    {
        if (_goal is not null) ClearGoal();

        var e = _net.NearestEdge(goal, out double t, out LLA proj, out _)
                ?? throw new InvalidOperationException("Poblíž cíle není žádná přijatelná hrana.");
        var a = e.From; var bNode = e.To;
        var tNode = new Node(_nextTempNodeId--, proj);

        // Delka pulek je GEOMETRICKA, ne cenova: dela se z ni soucet delky trasy
        // (GlobalNavMsg.RouteLengthM, zkouska dosazitelnosti mise). Do 26. 8. 2026 se sem davala
        // NULA, takze hrana s realnou delkou o sobe tvrdila, ze je nulova - a posledni usek k cili
        // se do delky trasy NEZAPOCITAL vubec (chyba rostla s delkou rozriznute hrany).
        Edge MakeTemp(Node from, Node to, double trav, double lengthMeters)
        {
            var edge = new Edge(_p + _tempEdges.Count, from, to, lengthMeters, -1);
            _tempEdges.Add(edge); _tempTrav.Add(trav);
            return edge;
        }

        var eAT = MakeTemp(a, tNode, t * _net.BaseTraversalCost(e), t * e.LengthMeters);
        var eTB = MakeTemp(tNode, bNode, (1 - t) * _net.BaseTraversalCost(e), (1 - t) * e.LengthMeters);
        // Virtualni smycka cile (From == To) zadnou geometrii nema, takze nulova delka je spravne.
        var g = MakeTemp(tNode, tNode, 0, 0);

        _shadow.Add(e.Index);
        var succShadow = new Dictionary<int, Edge> { [e.Index] = eAT };  // succ-kontext: e -> eAT
        var predShadow = new Dictionary<int, Edge> { [e.Index] = eTB };  // pred-kontext: e -> eTB

        Edge? eBT = null, eTA = null;
        var rev = _net.FindReverse(e);
        if (rev is not null)
        {
            eBT = MakeTemp(bNode, tNode, (1 - t) * _net.BaseTraversalCost(rev), (1 - t) * rev.LengthMeters);
            eTA = MakeTemp(tNode, a, t * _net.BaseTraversalCost(rev), t * rev.LengthMeters);
            _tempReverse[eAT.Index] = eTA.Index; _tempReverse[eTA.Index] = eAT.Index;
            _tempReverse[eTB.Index] = eBT.Index; _tempReverse[eBT.Index] = eTB.Index;

            _shadow.Add(rev.Index);
            succShadow[rev.Index] = eBT;   // succ-kontext: rev -> eBT
            predShadow[rev.Index] = eTA;   // pred-kontext: rev -> eTA
        }

        Edge SuccSub(Edge x) => succShadow.TryGetValue(x.Index, out var s) ? s : x;
        Edge PredSub(Edge x) => predShadow.TryGetValue(x.Index, out var s) ? s : x;
        List<Edge> SubSucc(IReadOnlyList<Edge> src) { var r = new List<Edge>(src.Count); foreach (var x in src) r.Add(SuccSub(x)); return r; }
        List<Edge> SubPred(IReadOnlyList<Edge> src) { var r = new List<Edge>(src.Count); foreach (var x in src) r.Add(PredSub(x)); return r; }

        // --- A-strana: eAT, eTB, G ---
        _succOverride[eAT.Index] = new List<Edge> { eTB, g };
        _turn[(eAT.Index, eTB.Index)] = 0;
        _turn[(eAT.Index, g.Index)] = 0;

        _succOverride[eTB.Index] = SubSucc(_net.Successors(e));
        foreach (var z in _net.Successors(e)) _turn[(eTB.Index, SuccSub(z).Index)] = _net.BaseTurnCost(e, z);

        _predOverride[eAT.Index] = SubPred(_net.Predecessors(e));
        foreach (var x in _net.Predecessors(e)) _turn[(PredSub(x).Index, eAT.Index)] = _net.BaseTurnCost(x, e);

        _predOverride[eTB.Index] = new List<Edge> { eAT };
        _succOverride[g.Index] = new List<Edge>();
        _predOverride[g.Index] = new List<Edge> { eAT };

        // --- B-strana: eBT, eTA (jen u obousměrné hrany) ---
        if (rev is not null)
        {
            _succOverride[eBT!.Index] = new List<Edge> { eTA!, g };
            _turn[(eBT.Index, eTA!.Index)] = 0;
            _turn[(eBT.Index, g.Index)] = 0;

            _succOverride[eTA.Index] = SubSucc(_net.Successors(rev));
            foreach (var z in _net.Successors(rev)) _turn[(eTA.Index, SuccSub(z).Index)] = _net.BaseTurnCost(rev, z);

            _predOverride[eBT.Index] = SubPred(_net.Predecessors(rev));
            foreach (var x in _net.Predecessors(rev)) _turn[(PredSub(x).Index, eBT.Index)] = _net.BaseTurnCost(x, rev);

            _predOverride[eTA.Index] = new List<Edge> { eBT };
            _predOverride[g.Index] = new List<Edge> { eAT, eBT };
        }

        // --- Přesměruj sousedy zastíněných hran (druhá strana téhož vztahu) ---
        foreach (var x in _net.Predecessors(e)) _succOverride[x.Index] = SubSucc(_net.Successors(x));
        foreach (var z in _net.Successors(e)) _predOverride[z.Index] = SubPred(_net.Predecessors(z));
        if (rev is not null)
        {
            foreach (var x in _net.Predecessors(rev)) _succOverride[x.Index] = SubSucc(_net.Successors(x));
            foreach (var z in _net.Successors(rev)) _predOverride[z.Index] = SubPred(_net.Predecessors(z));
        }

        GoalPoint = goal;
        _goal = g;
        RebuildAllAndState();
    }

    /// <summary>Odstraní dočasný split i virtuální cíl (návrat k holé síti). Značkový overlay zůstává.</summary>
    public void ClearGoal()
    {
        _shadow.Clear();
        _tempEdges.Clear();
        _tempTrav.Clear();
        _succOverride.Clear();
        _predOverride.Clear();
        _turn.Clear();
        _tempReverse.Clear();
        _goal = null;

        _all.Clear(); _all.AddRange(_net.Edges);
        _nodesList.Clear(); _nodesList.AddRange(_net.Edges);

        _g = System.Array.Empty<double>();
        _rhs = System.Array.Empty<double>();
        _open.Clear();
        _openKey.Clear();
    }

    private void RebuildAllAndState()
    {
        _all.Clear();
        _all.AddRange(_net.Edges);
        _all.AddRange(_tempEdges);

        _nodesList.Clear();
        foreach (var ed in _net.Edges) if (!_shadow.Contains(ed.Index)) _nodesList.Add(ed);
        _nodesList.AddRange(_tempEdges);

        int n = _p + _tempEdges.Count;
        _g = new double[n]; _rhs = new double[n];
        for (int i = 0; i < n; i++) { _g[i] = double.PositiveInfinity; _rhs[i] = double.PositiveInfinity; }
        _rhs[_goal!.Index] = 0;
        _open.Clear(); _openKey.Clear();
        Enqueue(_goal);
    }

    // ---- sloučené čtení sítě + split + overlay ----
    // Pozn.: zastíněné originály (Index < _p v _shadow) jsou tímto voláním dosažitelné jen
    // přes _net.Successors/_net.Predecessors pro cizí (nezastíněné) hrany, které by na ně
    // ukazovaly - ale InsertGoal VŽDY přesměruje takové sousedy přes _succOverride/_predOverride
    // (viz "Přesměruj sousedy zastíněných hran" výše), takže zastíněná hrana samotná je
    // orphan a nikdy se nezeptá na vlastní Successors/Predecessors (je vyloučena z Nodes).
    public IReadOnlyList<Edge> Successors(Edge u)
        => _succOverride.TryGetValue(u.Index, out var s) ? s
           : (u.Index < _p ? _net.Successors(u) : System.Array.Empty<Edge>());
    public IReadOnlyList<Edge> Predecessors(Edge u)
        => _predOverride.TryGetValue(u.Index, out var p) ? p
           : (u.Index < _p ? _net.Predecessors(u) : System.Array.Empty<Edge>());
    private double Traversal(Edge v)
        => _travOverride.TryGetValue(v.Index, out var o) ? o
           : (v.Index < _p ? _net.BaseTraversalCost(v) : _tempTrav[v.Index - _p]);
    private double Turn(Edge u, Edge v)
    {
        if (_turnOverride.TryGetValue((u.Index, v.Index), out var ov)) return ov;
        if (_turn.TryGetValue((u.Index, v.Index), out var ex)) return ex;
        if (u.Index < _p && v.Index < _p && !_shadow.Contains(u.Index) && !_shadow.Contains(v.Index))
            return _net.BaseTurnCost(u, v);
        return double.PositiveInfinity;
    }
    public double EdgeCost(Edge u, Edge v)
    {
        double tc = Turn(u, v);
        return double.IsPositiveInfinity(tc) ? tc : tc + Traversal(v);
    }

    /// <summary>Traversal cena hrany (síťové i dočasné půlky splitu) — pro field-aware volbu směru.</summary>
    public double BaseTraversalCost(Edge e) => Traversal(e);

    /// <summary>
    /// Reverzní hrana pro field-aware volbu směru — funguje jak pro síťové hrany,
    /// tak pro dočasné půlky splitu (eAT&lt;-&gt;eTA, eTB&lt;-&gt;eBT).
    /// </summary>
    public Edge? FindReverse(Edge e)
    {
        if (e.Index < _p)
        {
            var r = _net.FindReverse(e);
            return (r is not null && _shadow.Contains(r.Index)) ? null : r;
        }
        return _tempReverse.TryGetValue(e.Index, out var idx) ? _tempEdges[idx - _p] : null;
    }

    /// <summary>
    /// Najde nejbližší uzel edge-based grafu (síťovou hranu NEBO dočasnou půlku splitu) k bodu
    /// <paramref name="p"/>. Prochází <see cref="Nodes"/> (bez zastíněných originálů, vč. temp
    /// půlek), ignoruje nekonečný traversal a nulové (virtuální) hrany typu G.
    /// </summary>
    public Edge? NearestNode(LLA p, out double t, out LLA proj, out double dist)
    {
        Edge? best = null; dist = double.PositiveInfinity; t = 0; proj = p;
        foreach (var edge in _nodesList)
        {
            if (edge.From.Id == edge.To.Id) continue; // virtuální G
            if (double.IsPositiveInfinity(Traversal(edge))) continue;
            var (cp, d, tt) = p.ProjectOntoSegment(edge.From.Location, edge.To.Location);
            if (d < dist) { dist = d; best = edge; t = tt; proj = cp; }
        }
        return best;
    }

    // ---- LPA* (h=0) ----
    private double KeyOf(int idx) => Math.Min(_g[idx], _rhs[idx]);
    private void Enqueue(Edge s)
    {
        double k = KeyOf(s.Index);
        _open.Add((k, s.Index)); _openKey[s.Index] = k;
    }
    private void Remove(Edge s)
    {
        if (_openKey.TryGetValue(s.Index, out var k)) { _open.Remove((k, s.Index)); _openKey.Remove(s.Index); }
    }
    private void UpdateVertex(Edge u)
    {
        if (u.Index != _goal!.Index)
        {
            double best = double.PositiveInfinity;
            foreach (var sp in Successors(u))
            {
                double c = EdgeCost(u, sp);
                // ∞ hrana (uzavřená/zakázaná) nebo ještě neusazený/nedosažitelný successor
                // nemůže zlepšit rhs - přeskoč, aby se ∞ nešířilo přes Math.Min zbytečně.
                if (double.IsPositiveInfinity(c) || double.IsPositiveInfinity(_g[sp.Index])) continue;
                best = Math.Min(best, c + _g[sp.Index]);
            }
            _rhs[u.Index] = best;
        }
        Remove(u);
        if (_g[u.Index] != _rhs[u.Index]) Enqueue(u);
    }

    /// <summary>Dopočítá pole, dokud se daný uzel neusadí (g == rhs a fronta nemá menší klíč).</summary>
    public void EnsureSettled(Edge node)
    {
        while (_open.Count > 0)
        {
            var top = _open.Min;
            double targetKey = KeyOf(node.Index);
            bool topSmaller = top.Key < targetKey;
            // Standardní LPA* stop podmínka: dokud fronta obsahuje uzel s klíčem MENŠÍM než
            // cílový uzel, jeho g může ještě klesnout (musí se nejdřív usadit něco "blíž"),
            // takže node ještě nemá finální (konzistentní g==rhs) hodnotu.
            if (!topSmaller && _rhs[node.Index] == _g[node.Index]) break;

            Edge u = _all[top.Index];
            _open.Remove(top); _openKey.Remove(top.Index);
            if (_g[u.Index] > _rhs[u.Index])
            {
                _g[u.Index] = _rhs[u.Index];
                foreach (var pr in Predecessors(u)) UpdateVertex(pr);
            }
            else
            {
                _g[u.Index] = double.PositiveInfinity;
                UpdateVertex(u);
                foreach (var pr in Predecessors(u)) UpdateVertex(pr);
            }
        }
    }

    public double CostToGoal(Edge node) => _g[node.Index];

    public Edge? NextEdge(Edge current)
    {
        Edge? best = null; double bestVal = double.PositiveInfinity;
        foreach (var sp in Successors(current))
        {
            double c = EdgeCost(current, sp);
            // stejný důvod jako v UpdateVertex: ∞ hrana nebo nedosažitelný successor nemůže
            // být nejlevnější krok k cíli - přeskoč, ať se ∞ nevybere jako "gradient".
            if (double.IsPositiveInfinity(c) || double.IsPositiveInfinity(_g[sp.Index])) continue;
            double val = c + _g[sp.Index];
            if (val < bestVal) { bestVal = val; best = sp; }
        }
        return best;
    }

    // ---- inkrementální overlay (Task 4) ----

    /// <summary>
    /// Značky jsou GLOBÁLNÍ model světa na REÁLNÝCH (permanentních) hranách sítě, ne na
    /// dočasných půlkách splitu cíle. Dočasné indexy (&gt;= _p) se po ClearGoal+InsertGoal
    /// RECYKLUJÍ (_p, _p+1, ...), takže uložení pod temp indexem by po dalším InsertGoal
    /// potichu "prosáklo" do úplně jiného (nového) splitu. Proto je zde no-op — sign na
    /// dočasné půlce (typicky vlastní zastíněný segment cíle) se ignoruje záměrně.
    /// </summary>
    public void SetTraversalCost(Edge edge, double newCost)
    {
        if (edge.Index >= _p) return; // dočasná půlka splitu - značky se na ni neukládají
        _travOverride[edge.Index] = newCost;
        foreach (var pr in Predecessors(edge)) UpdateVertex(pr);
    }

    /// <summary>Stejný důvod jako u <see cref="SetTraversalCost"/> - turn overlay jen mezi permanentními hranami.</summary>
    public void SetTurnCost(Edge from, Edge to, double cost)
    {
        if (from.Index >= _p || to.Index >= _p) return; // dočasná půlka splitu - ignoruj
        _turnOverride[(from.Index, to.Index)] = cost;
        UpdateVertex(from);
    }
}
