#nullable enable
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Xml;

namespace ARBot.Common.Maps.OsmNav.Osm;

public sealed record OsmNodeRaw(long Id, double Lat, double Lon, IReadOnlyDictionary<string, string> Tags);
public sealed record OsmWayRaw(long Id, IReadOnlyList<long> NodeRefs, IReadOnlyDictionary<string, string> Tags);
public sealed record TurnRestrictionRaw(long FromWay, long ViaNode, long ToWay, string Restriction);
public sealed record OsmData(
    IReadOnlyList<OsmNodeRaw> Nodes,
    IReadOnlyList<OsmWayRaw> Ways,
    IReadOnlyList<TurnRestrictionRaw> Restrictions);

/// <summary>Streamované čtení .osm XML (Overpass/JOSM). Jen via-node restrikce.
///
/// <para><b>JOSM a <c>action="delete"</c> (18. 9. 2026):</b> soubor uložený z JOSM po editaci
/// nese i objekty, které v něm uživatel SMAZAL — zůstávají v XML s atributem
/// <c>action="delete"</c> (JOSM je zahodí až po uploadu nebo po <i>File → Purge</i>). Do té doby
/// je čtečka brala jako živou síť: v <c>OSM/Robotour2026-ver1.osm</c> bylo 57 z 95 cest
/// s <c>highway</c> smazaných a robot by po nich navigoval, ačkoli je autor v mapě odstranil.
/// Takový objekt se přečte (subtree se musí odkonzumovat), ale do výsledku nejde.
/// <c>action="modify"</c> je běžná editace a bere se normálně.</para></summary>
public static class OsmXmlReader
{
    public static OsmData ReadString(string xml)
    {
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return Read(s);
    }

    public static OsmData Read(Stream stream)
    {
        var nodes = new List<OsmNodeRaw>();
        var ways = new List<OsmWayRaw>();
        var restrictions = new List<TurnRestrictionRaw>();

        var settings = new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true };
        using var reader = XmlReader.Create(stream, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.Name)
            {
                case "node": ReadNode(reader, nodes); break;
                case "way": ReadWay(reader, ways); break;
                case "relation": ReadRelation(reader, restrictions); break;
            }
        }
        return new OsmData(nodes, ways, restrictions);
    }

    private static double Dbl(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    private static long Lng(string s) => long.Parse(s, CultureInfo.InvariantCulture);

    private static void ReadNode(XmlReader r, List<OsmNodeRaw> nodes)
    {
        long id = Lng(r.GetAttribute("id")!);
        string? deleted = r.GetAttribute("action");
        double lat = Dbl(r.GetAttribute("lat")!);
        double lon = Dbl(r.GetAttribute("lon")!);
        var tags = new Dictionary<string, string>();
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
                if (r.NodeType == XmlNodeType.Element && r.Name == "tag")
                    tags[r.GetAttribute("k")!] = r.GetAttribute("v")!;
        }
        if (!Deleted(deleted)) nodes.Add(new OsmNodeRaw(id, lat, lon, tags));
    }

    /// <summary>JOSM: <c>action="delete"</c> = objekt v editoru smazaný, v souboru ale stále přítomný.</summary>
    private static bool Deleted(string? action) => action == "delete";

    private static void ReadWay(XmlReader r, List<OsmWayRaw> ways)
    {
        long id = Lng(r.GetAttribute("id")!);
        string? deleted = r.GetAttribute("action");
        var refs = new List<long>();
        var tags = new Dictionary<string, string>();
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.Name == "nd") refs.Add(Lng(r.GetAttribute("ref")!));
                else if (r.Name == "tag") tags[r.GetAttribute("k")!] = r.GetAttribute("v")!;
            }
        }
        if (!Deleted(deleted)) ways.Add(new OsmWayRaw(id, refs, tags));
    }

    private static void ReadRelation(XmlReader r, List<TurnRestrictionRaw> restrictions)
    {
        string? deleted = r.GetAttribute("action");
        var tags = new Dictionary<string, string>();
        long fromWay = -1, toWay = -1, viaNode = -1;
        bool viaIsNode = false;
        if (!r.IsEmptyElement)
        {
            int depth = r.Depth;
            while (r.Read() && !(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.Name == "tag")
                {
                    tags[r.GetAttribute("k")!] = r.GetAttribute("v")!;
                }
                else if (r.Name == "member")
                {
                    string type = r.GetAttribute("type") ?? "";
                    string role = r.GetAttribute("role") ?? "";
                    long refId = Lng(r.GetAttribute("ref")!);
                    if (role == "from" && type == "way") fromWay = refId;
                    else if (role == "to" && type == "way") toWay = refId;
                    else if (role == "via" && type == "node") { viaNode = refId; viaIsNode = true; }
                }
            }
        }

        if (tags.GetValueOrDefault("type") != "restriction") return;
        if (!tags.TryGetValue("restriction", out string? kind)) return;
        if (Deleted(deleted)) return;
        if (!viaIsNode || fromWay < 0 || toWay < 0) return; // via-way nepodporujeme
        restrictions.Add(new TurnRestrictionRaw(fromWay, viaNode, toWay, kind));
    }
}
