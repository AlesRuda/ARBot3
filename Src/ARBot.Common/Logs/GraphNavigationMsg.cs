using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Logs
{
    public class GraphNavigationMsg:Message
    {
        public class Vertex
        {
            public long ID { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width { get; set; }
            public double Distance { get; set; }
            public bool Final { get; set; }
            public bool DistanceCalculated { get; set; }

            public override string ToString()
            {
                return string.Format("{5}\r\nPos: [{0:N3}, {1:N3}]\r\nDist: {2:N3}\r\nFinal: {3}\r\nDistCalc: {4}", X, Y, Distance, Final, DistanceCalculated, ID);
            }
        }
        public class Edge
        {
            public Edge(GraphNavigationMsg p)
            {
                parent = p;
            }

            GraphNavigationMsg parent;
            public long ID { get; set; }
            public bool HightLight;
            public int From; //-1 nenalezeno
            public int To; //-1 nenalezeno
            public double Length;
            public bool Collision;
            public bool Path;
            public bool Graph;
            public Line2D Line
            {
                get
                {
                    if (parent != null)
                    {
                        if (parent.Vertexes.Count > Math.Max(From, To))
                        {
                            Vertex v1 = parent.Vertexes[From];
                            Vertex v2 = parent.Vertexes[To];
                            return new Line2D(new Point2D(v1.X, v1.Y), new Point2D(v2.X, v2.Y));
                        }
                    }
                    return null;
                }
            }
            public override string ToString()
            {
                double? w=null;
                double? a=null;
                if (parent != null)
                {
                    if (parent.Vertexes.Count > Math.Max(From, To))
                    {
                        Vertex v1 = parent.Vertexes[From];
                        Vertex v2 = parent.Vertexes[To];
                        var l = new Line2D(new Point2D(v1.X, v1.Y), new Point2D(v2.X, v2.Y));
                        a = Conversions.Rad2Deg(Conversions.Orientation2Azimut(l.Angle));
                        w = (v1.Width + v2.Width) / 2;
                    }
                }
                return string.Format(@"{3}
Len: {0:N3}
Angle: {1:N1}
Width: {2:N3}", Length, a, w, ID);
            }
        }

        public List<Vertex> Vertexes { get; private set; }
        public List<Edge> Edges { get; private set; }

        public double StartX, StartY;
        public double TargetX, TargetY;
        public double? ResultX, ResultY;

        /// <summary>
        /// Nazev zaznamu
        /// </summary>
        public string Name { get; private set; }
         
        public GraphNavigationMsg() : base("GN", 1)
        {
        }

        public GraphNavigationMsg(double startX, double startY, double targetX, double targetY,
            double? resultX, double? resultY, List<Vertex> vertexes, List<Edge> edges) : base("GN", 2)
        {
            StartX = startX;
            StartY = startY;
            TargetX = targetX;
            TargetY = targetY;
            ResultX = resultX;
            ResultY = resultY;

            Vertexes = vertexes;
            Edges = edges;
        }

        public GraphNavigationMsg(Map map, MapWay w, MapPoint p, Point2D center, double r) : base("GN", 2)
        {
            Name = "Map";

            Dictionary<MapPoint, int> points = new Dictionary<MapPoint, int>();
            Queue<MapPoint> newPoints = new Queue<MapPoint>();

            Dictionary<MapWay, int> ways = new Dictionary<MapWay, int>();

            if (p != null)
            {
                points.Add(p, points.Count);
                newPoints.Enqueue(p);
            }
            if (w != null)
            {
                ways.Add(w, 0);
                if (!points.ContainsKey(w.Start))
                {
                    points.Add(w.Start, points.Count);
                    newPoints.Enqueue(w.Start);
                }
                if (!points.ContainsKey(w.End))
                {
                    points.Add(w.End, points.Count);
                    newPoints.Enqueue(w.End);
                }
            }

            while(newPoints.Count>0)
            {
                var point = newPoints.Dequeue();
                foreach (MapWay way in point.Ways)
                {
                    MapPoint to = (way.Start.ID == point.ID) ? way.End : way.Start;
                    MapPoint from = (way.Start.ID == point.ID) ? way.Start : way.End;

                    ECEF ecef = from.Position;
                    var p1 = new Point2D(ecef.Y, ecef.Z);
                    if ((p1 - center).Length < r && !points.ContainsKey(to))
                    {
                        points.Add(to, points.Count);
                        newPoints.Enqueue(to);
                    }
                    if (!ways.ContainsKey(way))
                        ways.Add(way, 0);
                }
            }

            StartX = center.X;
            StartY = center.Y;
            TargetX = center.X;
            TargetY = center.Y;
            ResultX = null;
            ResultY = null;

            Vertexes = points.OrderBy(kv=>kv.Value).Select(kv=>new Vertex() { X = kv.Key.Position.Y, Y = kv.Key.Position.Z, Distance = kv.Key.Distance, DistanceCalculated = kv.Key.DistanceCalculated, Final = kv.Key.Final, Width=kv.Key.Width, ID=kv.Key.ID }).ToList();
            Edges = ways.Keys.Select(k=>new Edge(this) { From = points.ContainsKey(k.Start)?points[k.Start]:-1, To = points.ContainsKey(k.End) ? points[k.End]:-1, Length = k.WeigthDistance, Collision = false, Path = true, Graph = false, ID=k.ID, HightLight=k.HighLight }).ToList();
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "GN");

            bw.Write(StartX);
            bw.Write(StartY);
            bw.Write(TargetX);
            bw.Write(TargetY);
            Write(bw, ResultX);
            Write(bw, ResultY);

            bw.Write(Vertexes.Count);
            for (int i = 0; i < Vertexes.Count; i++)
            {
                bw.Write(Vertexes[i].ID);
                bw.Write(Vertexes[i].X);
                bw.Write(Vertexes[i].Y);
                bw.Write(Vertexes[i].Width);
                bw.Write(Vertexes[i].Distance);
                bw.Write(Vertexes[i].Final);
                bw.Write(Vertexes[i].DistanceCalculated);
            }

            bw.Write(Edges.Count);
            for (int i = 0; i < Edges.Count; i++)
            {
                bw.Write(Edges[i].ID);
                if(Verze==2)
                    bw.Write(Edges[i].HightLight);
                bw.Write(Edges[i].From);
                bw.Write(Edges[i].To);
                bw.Write(Edges[i].Length);
                bw.Write(Edges[i].Collision);
                bw.Write(Edges[i].Path);
                bw.Write(Edges[i].Graph);
            }
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            StartX = br.ReadDouble();
            StartY = br.ReadDouble();
            TargetX = br.ReadDouble();
            TargetY = br.ReadDouble();
            ResultX = ReadDouble(br);
            ResultY = ReadDouble(br);

            int cnt = br.ReadInt32();
            Vertexes = new List<Vertex>();

            for (int i = 0; i < cnt; i++)
            {
                double x, y, d, w = 0;
                bool f, dc;
                long id = 0;

                id = br.ReadInt64();

                x = br.ReadDouble();
                y = br.ReadDouble();
                w = br.ReadDouble();
                d = br.ReadDouble();
                f = br.ReadBoolean();
                dc = br.ReadBoolean();

                Vertexes.Add(new Vertex() { X = x, Y = y, Distance = d, Final = f, DistanceCalculated = dc, Width = w, ID = id });
            }

            cnt = br.ReadInt32();
            Edges = new List<Edge>();
            for (int i = 0; i < cnt; i++)
            {
                int f, t;
                double l;
                bool c, p, g;
                long id = 0;
                bool hl = false;

                id = br.ReadInt64();

                if (Verze == 2)
                    hl = br.ReadBoolean();

                f = br.ReadInt32();
                t = br.ReadInt32();
                l = br.ReadDouble();
                c = br.ReadBoolean();
                p = br.ReadBoolean();
                g = br.ReadBoolean();

                Edges.Add(new Edge(this) { From = f, To = t, Length = l, Collision = c, Path = p, Graph = g, ID = id, HightLight=hl });
            }
        }

        public override Message Build()
        {
            return new GraphNavigationMsg();
        }

        public override string ToString()
        {
            return string.Format("GraphNavigation {0}", Name);
        }

    }
}
