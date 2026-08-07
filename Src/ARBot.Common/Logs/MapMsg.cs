using System;
using System.Collections.Generic;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Síť z OsmNav (<see cref="ARBot.Common.Maps.OsmNav.Graph.RoadNetwork"/>) pro vizualizaci ve world
    /// (geo) pohledu. Nese <b>uzly</b> (poloha ve stupních WGS84) a <b>hrany</b> (indexy From/To do pole
    /// uzlů + WayId + délka). Souřadnice jsou <b>geografické</b>, takže se kreslí přímo (přes Web Mercator)
    /// — na rozdíl od <see cref="GraphNavigationMsg"/>, který je v lokálních ENU metrech.
    ///
    /// Vytváří se z OsmNav sítě metodou <c>RoadNetwork.ToLogMessage()</c> (obousměrné hrany se deduplikují).
    /// </summary>
    [Serializable]
    public class MapMsg : Message
    {
        /// <summary>Uzel mapy (poloha ve stupních + šířka cesty v uzlu [m]).</summary>
        public struct MapNode
        {
            public long Id;
            public double LatDeg;
            public double LonDeg;
            public double WidthMeters;
        }

        /// <summary>Hrana mapy: indexy do <see cref="Nodes"/> + WayId + délka [m].</summary>
        public struct MapEdge
        {
            public int From;
            public int To;
            public long WayId;
            public double LengthMeters;
        }

        public List<MapNode> Nodes { get; set; } = new List<MapNode>();
        public List<MapEdge> Edges { get; set; } = new List<MapEdge>();

        /// <summary>Popis mapy (např. název zdrojového souboru).</summary>
        public string Name { get; set; } = string.Empty;

        public MapMsg() : base("Map", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? string.Empty);

            bw.Write(Nodes.Count);
            for (int i = 0; i < Nodes.Count; i++)
            {
                bw.Write(Nodes[i].Id);
                bw.Write(Nodes[i].LatDeg);
                bw.Write(Nodes[i].LonDeg);
                bw.Write(Nodes[i].WidthMeters);
            }

            bw.Write(Edges.Count);
            for (int i = 0; i < Edges.Count; i++)
            {
                bw.Write(Edges[i].From);
                bw.Write(Edges[i].To);
                bw.Write(Edges[i].WayId);
                bw.Write(Edges[i].LengthMeters);
            }
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();

            int nc = br.ReadInt32();
            Nodes = new List<MapNode>(nc);
            for (int i = 0; i < nc; i++)
                Nodes.Add(new MapNode
                {
                    Id = br.ReadInt64(),
                    LatDeg = br.ReadDouble(),
                    LonDeg = br.ReadDouble(),
                    WidthMeters = br.ReadDouble(),
                });

            int ec = br.ReadInt32();
            Edges = new List<MapEdge>(ec);
            for (int i = 0; i < ec; i++)
                Edges.Add(new MapEdge
                {
                    From = br.ReadInt32(),
                    To = br.ReadInt32(),
                    WayId = br.ReadInt64(),
                    LengthMeters = br.ReadDouble(),
                });
        }

        public override Message Build() => new MapMsg();

        public override string ToString() => string.Format("MapMsg {0} nodes={1} edges={2}", Name, Nodes.Count, Edges.Count);
    }
}
