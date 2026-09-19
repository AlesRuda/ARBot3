using System;
using System.Collections.Generic;
using System.Linq;

namespace ARBot.Common.Maps.OsmNav.Osm;

/// <summary>
/// <b>Ostrovy sítě cest a jejich odříznutí.</b> Komponenty souvislosti nad cestami, které
/// <see cref="TravelProfile"/> pouští, a zahození všech kromě té největší (podle délky).
///
/// <para><b>Proč (soutěž 19. 9. 2026):</b> v <c>OSM/Robotour2026-ver1.osm</c> je dlážděné náměstí
/// (<c>highway=pedestrian</c> + <c>area=yes</c>, way 956523901, 139 m), spojené se zbytkem sítě
/// <b>jen schody</b> — a ty profil Robot nepouští, takže je to pod ním <b>ostrov</b>. Robot na
/// něj fyzicky nevyjede, ale <b>GPS ho tam posadila</b>: póza se přichytila na hranu ostrova
/// (1,5 m) místo na chodník o pár metrů dál, a z ostrova nevede k žádnému cíli trasa. Mise
/// zamítla každý QR kód hláškou „nevede trasa", ačkoli cíl byl uzel mapy. Přichycení pózy na
/// nejbližší hranu dělají tři místa (<c>GlobalNavigator.Probe</c>, <c>Navigator.Update</c>,
/// <c>Router.Plan</c>) a všechna by se musela učit „nejbližší hrana, ze které se dá dojet";
/// síť se ale načítá <b>jednou</b>. Hrana, na kterou se robot nemůže dostat, je pro navigaci
/// vždy jen past — proto se ostrovy zahodí už tady.</para>
///
/// <para><b>Co je „největší":</b> délka cest v metrech, ne počet uzlů — hustě natrasované
/// náměstí má 40 uzlů na 139 m, chodníky 169 uzlů na 4,3 km, ale u jiné mapy by to mohlo být
/// obráceně. Souvislost se počítá <b>neorientovaně</b> (jednosměrky nespojují síť hůř, jen
/// dráž) a jen přes <b>sdílené uzly</b>: dva uzly 0,9 m od sebe, které nejsou týž uzel,
/// nespojují nic — přesně tak vypadá ta soutěžní mapa.</para>
///
/// <para>⚠️ Je to heuristika pro mapu <b>jednoho areálu</b>. Mapa s dvěma velkými oddělenými částmi
/// by přišla o tu menší, proto je to za přepínačem (<c>mapprune=</c>) a co se zahodilo, jde do
/// <c>Trace</c> — a hlavně: zahozený ostrov, na kterém by robot <b>legitimně stál</b>, by se
/// projevil tím, že se póza přichytí na cestu opodál a robot pojede k ní. To je totéž, co dnes
/// dělá na trávníku vedle chodníku, ne nová vada.</para>
/// </summary>
public static class NetworkIslands
{
    /// <summary>Jedna komponenta souvislosti přijatých cest.</summary>
    public sealed record Island(IReadOnlyList<long> WayIds, int NodeCount, double LengthMeters);

    /// <summary>Výsledek rozkladu: komponenty seřazené od největší (podle délky).</summary>
    public sealed record Report(IReadOnlyList<Island> Components)
    {
        /// <summary>Kolik komponent se zahodilo (vše kromě první).</summary>
        public int Dropped => Math.Max(0, Components.Count - 1);
        public bool IsConnected => Components.Count <= 1;
    }

    /// <summary>
    /// Komponenty souvislosti sítě pod daným profilem, největší první. Nezahazuje nic — jen měří.
    /// </summary>
    public static Report Analyze(OsmData data, TravelProfile profile)
    {
        var nodes = data.Nodes.ToDictionary(n => n.Id);
        var accepted = data.Ways.Where(profile.AcceptsWay).ToList();

        // Union-find nad id uzlu; cesta spojuje vsechny sve uzly.
        var parent = new Dictionary<long, long>();
        long Find(long x)
        {
            while (parent.TryGetValue(x, out long p) && p != x) { parent[x] = parent.TryGetValue(p, out long pp) ? pp : p; x = parent[x]; }
            return x;
        }
        void Union(long a, long b)
        {
            long ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }
        foreach (var w in accepted)
        {
            foreach (var id in w.NodeRefs) parent.TryAdd(id, id);
            for (int i = 1; i < w.NodeRefs.Count; i++) Union(w.NodeRefs[i - 1], w.NodeRefs[i]);
        }

        var groups = new Dictionary<long, (List<long> ways, HashSet<long> nodes, double len)>();
        foreach (var w in accepted)
        {
            if (w.NodeRefs.Count == 0) continue;
            long root = Find(w.NodeRefs[0]);
            if (!groups.TryGetValue(root, out var g)) groups[root] = g = (new List<long>(), new HashSet<long>(), 0);
            g.ways.Add(w.Id);
            foreach (var id in w.NodeRefs) g.nodes.Add(id);
            for (int i = 1; i < w.NodeRefs.Count; i++)
                if (nodes.TryGetValue(w.NodeRefs[i - 1], out var a) && nodes.TryGetValue(w.NodeRefs[i], out var b))
                    g.len += Dist(a, b);
            groups[root] = g;
        }

        var comps = groups.Values
            .Select(g => new Island(g.ways, g.nodes.Count, g.len))
            .OrderByDescending(c => c.LengthMeters).ThenByDescending(c => c.NodeCount)
            .ToList();
        return new Report(comps);
    }

    /// <summary>
    /// Vrátí data bez cest, které nepatří do největší komponenty (uzly a restrikce zůstávají —
    /// <see cref="GraphBuilder"/> si bere jen ty, na které se cesty odkazují). Souvislá síť se
    /// vrátí <b>beze změny</b> (tatáž instance).
    /// </summary>
    public static OsmData Prune(OsmData data, TravelProfile profile, out Report report)
    {
        report = Analyze(data, profile);
        if (report.IsConnected) return data;

        var keep = new HashSet<long>(report.Components[0].WayIds);
        // Cesty, ktere profil nepousti, zustavaji - GraphBuilder je stejne preskoci a jiny profil
        // (napr. pri kresleni) by o ne nemel prijit.
        var ways = data.Ways.Where(w => !profile.AcceptsWay(w) || keep.Contains(w.Id)).ToList();
        return new OsmData(data.Nodes, ways, data.Restrictions);
    }

    /// <summary>Text pro <c>Trace</c>: co a proč se zahodilo.</summary>
    public static string Describe(Report r)
    {
        if (r.IsConnected) return "sit cest je souvisla (1 komponenta).";
        var main = r.Components[0];
        var rest = r.Components.Skip(1)
            .Select(c => $"{c.LengthMeters:F0} m / {c.NodeCount} uzlu (way {string.Join(",", c.WayIds.Take(4))}{(c.WayIds.Count > 4 ? ",..." : "")})");
        return $"sit cest ma {r.Components.Count} komponenty; ponechana nejvetsi ({main.LengthMeters:F0} m / "
               + $"{main.NodeCount} uzlu), ZAHOZENO {r.Dropped}: {string.Join("; ", rest)}. "
               + "Robot se na ostrov nedostane a poza prichycena na jeho hranu by nemela zadnou trasu; "
               + "mapprune=false vrati puvodni sit.";
    }

    private static double Dist(OsmNodeRaw a, OsmNodeRaw b)
    {
        const double R = 6371000;
        double lat = (a.Lat + b.Lat) / 2 * Math.PI / 180;
        double dy = (a.Lat - b.Lat) * Math.PI / 180 * R;
        double dx = (a.Lon - b.Lon) * Math.PI / 180 * R * Math.Cos(lat);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
