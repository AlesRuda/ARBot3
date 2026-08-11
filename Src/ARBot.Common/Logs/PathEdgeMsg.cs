using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Algorithms.Graphs;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class PathEdgeMsg : Message, INamedMessage
    {
        public PathEdgeMsg() : base("PathEdgeMsg", 4)
        {
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="name"></param>
        public PathEdgeMsg(string name, List<PathEdgeFinderItem> items, List<PathEdge2> edges,
            Line2D left, Line2D right,
            double? leftDistance, double? rightDistance, double? angleDiff, double? angle):this()
        {
            Name = name;
            if(items!=null)
                Items = items.ToDictionary(i=>i.Name, i=>i.Edges);
            else
                Items = new Dictionary<string, List<PathEdge>>();
            Edges = edges;
            Left = left;
            Right = right;
            LeftDistance = leftDistance;
            RightDistance = rightDistance;
            AngleDiff = angleDiff;
            Angle = angle;
        }

        /// <summary>
        /// Nazev zaznamu
        /// </summary>
        public string Name { get; private set; }


        public Dictionary<string, List<PathEdge>> Items { get; private set; }
        public List<PathEdge2> Edges { get; private set; }

        public Line2D Left { get; private set; }
        public Line2D Right { get; private set; }

        public Double? LeftDistance { get; private set; }
        public Double? RightDistance { get; private set; }

        public double? AngleDiff { get; private set; }

        /// <summary>
        /// Uhel smerovani cesty spocteny s kamery
        /// </summary>
        public double? Angle { get; private set; }


        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "PathEdge");

            bw.Write(Items.Count);
            foreach (var kv in Items)
            {
                bw.Write(kv.Key);

                bw.Write(kv.Value.Count);
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    bw.Write(kv.Value[i].Y);
                    Write(bw, kv.Value[i].Left);
                    Write(bw, kv.Value[i].Right);
                }
            }

            bw.Write(Edges?.Count??0);
            for (int i = 0; i < (Edges?.Count??0); i++)
            {
                Write(bw, Edges[i].Point);
                Write(bw, Edges[i].WorldPoint);
                bw.Write(Edges[i].Name);
                bw.Write(Edges[i].Left);
                bw.Write(Edges[i].Used);
                bw.Write(Edges[i].Inlier);
                Write(bw, Edges[i].WayID);
                Write(bw, Edges[i].Orientation);
            }

            bw.Write(Left != null);
            if (Left != null)
            {
                bw.Write(Left.A);
                bw.Write(Left.B);
                bw.Write(Left.C);
            }

            bw.Write(Right != null);
            if (Right != null)
            {
                bw.Write(Right.A);
                bw.Write(Right.B);
                bw.Write(Right.C);
            }

            Write(bw, LeftDistance);
            Write(bw, RightDistance);
            Write(bw, AngleDiff);
            Write(bw, Angle);
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            Items = new Dictionary<string, List<PathEdge>>();

            int cnt = br.ReadInt32();
            for (int i = 0; i < cnt; i++)
            {
                string itemName = br.ReadString();
                int cnt1 = br.ReadInt32();

                List<PathEdge> CameraEdges = new List<PathEdge>();
                for (int j = 0; j < cnt1; j++)
                {
                    int y = br.ReadInt32();
                    int? l = ReadInt32(br);
                    int? r = ReadInt32(br);

                    CameraEdges.Add(new PathEdge() { Y = y, Left = l, Right = r });
                }
                Items.Add(itemName, CameraEdges);
            }

            if (Verze == 1)
            {
                Edges = new List<PathEdge2>();

                cnt = br.ReadInt32();
                for (int i = 0; i < cnt; i++)
                {
                    double y = br.ReadDouble();
                    double x = br.ReadDouble();
                    Edges.Add(new PathEdge2()
                    {
                        WorldPoint = new Point4D() { X = (float)x, Y = (float)y, Z = 0, A = 1 },
                        Left=true,
                        Used=true
                    });
                }

                cnt = br.ReadInt32();
                for (int i = 0; i < cnt; i++)
                {
                    double y = br.ReadDouble();
                    double x = br.ReadDouble();
                    Edges.Add(new PathEdge2()
                    {
                        WorldPoint = new Point4D() { X = (float)x, Y = (float)y, Z = 0, A = 1 },
                        Left = false,
                        Used = true
                    });
                }
            }
            if (Verze >= 2)
            {
                Edges = new List<PathEdge2>();

                cnt = br.ReadInt32();
                for (int i = 0; i < cnt; i++)
                {
                    var p = ReadPoint(br);
                    Point4D? wp=null;
                    if (Verze <= 3)
                    {
                        var wp2d = ReadNullablePoint2D(br);
                        if (wp2d != null)
                            wp = new Point4D() { X = (float)wp2d.Value.X, Y = (float)wp2d.Value.Y, Z = 0, A = 1 };
                    }
                    else
                        wp = ReadNullablePoint4D(br);
                    string name = br.ReadString();
                    bool left = br.ReadBoolean();
                    bool used = br.ReadBoolean();
                    bool inlier = br.ReadBoolean();
                    long? wayID = ReadInt64(br);
                    double? orientation = null;

                    if (Verze >= 3)
                        orientation = ReadDouble(br);

                    Edges.Add(new PathEdge2()
                    {
                        Point = p,
                        WorldPoint = wp,
                        Name = name,
                        Left = left,
                        Used = used,
                        Inlier = inlier,
                        WayID = wayID,
                        Orientation=orientation
                    }); 
                }
            }


            if (br.ReadBoolean())
            {
                double a = br.ReadDouble();
                double b = br.ReadDouble();
                double c = br.ReadDouble();
                Left = new Line2D(a, b, c);
            }

            if (br.ReadBoolean())
            {
                double a = br.ReadDouble();
                double b = br.ReadDouble();
                double c = br.ReadDouble();
                Right = new Line2D(a, b, c);
            }

            LeftDistance = ReadDouble(br);
            RightDistance = ReadDouble(br);
            AngleDiff = ReadDouble(br);
            Angle = ReadDouble(br);
        }

        public override Message Build()
        {
            return new PathEdgeMsg();
        }

        public override string ToString()
        {
            return string.Format("PathEdgeMsg");
        }
    }
}
