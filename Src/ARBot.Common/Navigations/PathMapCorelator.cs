using ARBot.Common.Algorithms;
using ARBot.Common.Algorithms.Simplification;
using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using ARBot.Common.Logs;
using ARBot.Common.Maps;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Srovnava mareni okraju vozovky s mapou
    /// </summary>
    public class PathMapCorelator
    {
        public PointMatchBase MatchAlg = new Kabsch();

        /// <summary>
        /// Popisuje pozorovane body a nalezene prirazeni hranicnim segmentum.
        /// </summary>
        public class PointInfo
        {
            /// <summary>
            /// Pozorovany bod
            /// </summary>
            public Point2D OriginalPoint;
            /// <summary>
            /// Pozorovany bod upraveny transformaci 
            /// </summary>
            public Point2D TransformedPoint;
            /// <summary>
            /// Bod pozorovan jako levy okraj cesty
            /// </summary>
            public bool IsLeft;
            /// <summary>
            /// Orientace pohledu pri pozorovani bodu
            /// </summary>
            public double EyeOrientation;
            /// <summary>
            /// Orientace spojnice bodu
            /// </summary>
            public double Orientation;

            /// <summary>
            /// Prirazeny segment hranice
            /// </summary>
            public EdgeSegment Edge;
            /// <summary>
            /// Bod na hrane.
            /// Spocteny jako kolmy prumet OriginalPoint na hranu.
            /// ma smysl jen pokud Edge!=null, jinak muze obsahovat starou hodnotu
            /// </summary>
            public Point2D EdgePoint;
        }

        /// <summary>
        /// Hranicni segment
        /// </summary>
        public class EdgeSegment:LineSegment2D
        {
            public EdgeSegment Oposit;
            /// <summary>
            /// ID cesty
            /// </summary>
            public long WayID;

            double angle;
            /// <summary>
            /// Start, end odpovida smeru jizdy
            /// </summary>
            /// <param name="start"></param>
            /// <param name="end"></param>
            /// <param name="startWidth"></param>
            /// <param name="endWidth"></param>
            public static List<EdgeSegment> FromWay(Point2D start, Point2D end, double startWidth, double endWidth)
            {
                var l = new List<EdgeSegment>();
                var n = new Line2D(start, end).Normal;
                n = n / n.Length;
                var s1 = new EdgeSegment(start - n * startWidth / 2, end - n * endWidth / 2);
                var s2 = new EdgeSegment(end + n * endWidth / 2, start + n * startWidth / 2);
                s1.Oposit = s2;
                s2.Oposit = s1;
                l.Add(s1);
                l.Add(s2);
                return l;
            }

            /// <summary>
            /// Start, end odpovida smeru jizdy
            /// </summary>
            /// <param name="start"></param>
            /// <param name="end"></param>
            /// <param name="startWidth"></param>
            /// <param name="endWidth"></param>
            public static List<Point2D> PolyWay(Point2D start, Point2D end, double startWidth, double endWidth)
            {
                var l = new List<Point2D>();
                var n = new Line2D(start, end).Normal;
                n = n / n.Length;
                l.Add(start - n * startWidth / 2);
                l.Add(end - n * endWidth / 2);
                l.Add(end + n * endWidth / 2);
                l.Add(start + n * startWidth / 2);
                return l;
            }

            /// <summary>
            /// Start, end odpovida smeru jizdy
            /// </summary>
            /// <param name="start"></param>
            /// <param name="end"></param>
            public EdgeSegment(Point2D start, Point2D end):base(start, end)
            {
                angle = Line.Angle;
            }

            /// <summary>
            /// Odrizne ze this a e segmentu kraci casti pokud se protinaji
            /// </summary>
            /// <param name="e"></param>
            public void Trim(EdgeSegment e)
            {
                var i = Intersection(e);
                if (i.HasValue)
                {
                    if (Start.IsLeft(e.Line) > 0)
                        Start = i.Value;
                    else
                        End = i.Value;
                    if (e.Start.IsLeft(Line) > 0)
                        e.Start = i.Value;
                    else
                        e.End = i.Value;
                }
                return;
            }
            /// <summary>
            /// Najde na segmentu kolmy prumet bodu point.
            /// </summary>
            /// <param name="p">Pozorovany bod</param>
            /// <param name="isLeft">Bod je levym krajem</param>
            /// <param name="orientation">Smer pohledu pri pozorovani bodu</param>
            /// <returns></returns>
            public Point2D? NearestPoint(Point2D p, bool isLeft, double eyeOrientation, double orientation)
            {
                if (Oposit == null)
                    return null;
                // v tomhle vypoctu je robot na pozici 0,0
                // zjisitm zda se jedna o levy ci pravy segment
                // pozici robotu povedu kolmici na pohled, spoctu prusecik se segmentem a jeho opositem
                var n = new Vector2D(eyeOrientation);
                var l = new Line2D(n, new Point2D(0, 0));
                var nl = new Line2D(n.Normal, new Point2D(0, 0));
                var i1 = Line.Intersection(l);
                var i2 = Oposit.Line.Intersection(l);

                var left = i1.IsLeft(nl) > i2.IsLeft(nl);
                if (left != isLeft)
                    return null;

                // pokud je to bod leve strany tak hledam linii jdouci v opacnem smeru.
                var orientationDiff = Math.Abs(Conversions.NormalizeHalfOrientation(orientation - angle));
                //tohle resi spravny smer linie +-22,5 stupne
                if (Math.Abs(orientationDiff) > Math.PI / 4)
                    return null;

                return Intersection(p);
            }
        }


        public List<EdgeSegment> Edges;
        public List<PointInfo> Points;

        //            public Processor;

        public (Point2D Point, double EyeOrientation, double Orientation) Transform(PointInfo p)
        {
            var m = ToMat(p.OriginalPoint)*Scale;
            m = Rotation*m + Translation;
//            var m = ToMat(p.OriginalPoint);
  //          m = m + Translation;
            p.TransformedPoint = new Point2D(m[0, 0], m[1, 0]);
            return (p.TransformedPoint, Conversions.NormalizeOrientation(p.EyeOrientation + OrientationDiff), Conversions.NormalizeOrientation(p.Orientation + OrientationDiff));
        }

        Tuple<EdgeSegment, Point2D?> NearesetWay(PointInfo p)
        {
            EdgeSegment es = null;
            Point2D? ret = null;
            double minDist = 6;

            var p1 = Transform(p);

            foreach (var e in Edges)
            {
                var np = e.NearestPoint(p1.Point, p.IsLeft, p1.EyeOrientation, p1.Orientation);
                if (np != null)
                {
                    var dist = (np.Value - p1.Point).Length;
                    if (dist < minDist)
                    {
                        es = e;
                        minDist = dist;
                        ret = np;
                    }
                }
            }

            return new Tuple<EdgeSegment, Point2D?>(es, ret);
        }

        /// <summary>
        /// Matice rotace pozorovani na mapu
        /// </summary>
        public Matrix<double> Rotation = Matrix<double>.Build.DenseIdentity(2);

        /// <summary>
        /// Matice translace pozorovani na mapu
        /// </summary>
        public Matrix<double> Translation= Matrix<double>.Build.DenseOfArray(new double[2, 1] { { 0 }, { 0 } });
        /// <summary>
        /// Zvetseni pozorovani na mapu
        /// </summary>
        public double Scale = 1;
        /// <summary>
        /// Uhel pootoceni pozorovani na mapu
        /// </summary>
        public double OrientationDiff;

        Matrix<double> ToMat(Point2D p)
        {
            Matrix<double> m = Matrix<double>.Build.DenseOfArray(new double[2, 1] { { p.X }, { p.Y } });
            return m;
        }

        Matrix<double> ToMat(Vector2D p)
        {
            Matrix<double> m = Matrix<double>.Build.DenseOfArray(new double[2, 1] { { p.X }, { p.Y } });
            return m;
        }

        bool Match()
        {
            var l = Points.Where(i => i.Edge != null)
                .Select(i => new KabschUmeyama.Pair() { A = ToMat(i.EdgePoint), B = ToMat(i.OriginalPoint) });
            if (!l.Any())
                return false;

            MatchAlg.Process(l);

            //            Rotation = ku.Rotation * Rotation;
            //            Translation += ku.Rotation * Translation + ku.Translation;
//            Scale *= ku.Scale;
            Rotation = MatchAlg.Rotation;
            Translation = MatchAlg.Translation;
            Scale = MatchAlg.Scale;

            OrientationDiff = Math.Atan2(Rotation[1, 0], Rotation[0, 0]);
            return true;
        }
        /// <summary>
        /// Hleda nejlepsi prirazi hraniccnich segmentu pozorovanym bodum a z toho
        /// vyplyvajici transformace Rotation, Scale, Translation
        /// </summary>
        public void Process(int iteration)
        {
            Translation = Matrix<double>.Build.DenseOfArray(new double[2, 1] { { 0 }, { 0 } }); ;
            Rotation = Matrix<double>.Build.DenseIdentity(2);
            OrientationDiff = 0;
            bool changed = true;
            while (changed && iteration > 0)
            {
                changed = false;
                foreach (var p in Points)
                {
                    var w = NearesetWay(p);
                    if (p.Edge != w.Item1)
                    {
                        p.Edge = w.Item1;
                        changed = true;
                    }
                    if (w.Item2.HasValue)
                        p.EdgePoint = w.Item2.Value;
                }

                var groups = Points.Where(p => p.Edge != null).GroupBy(p => p.Edge);
                foreach (var i in groups.Where(g => g.Count() < 2))
                    foreach (var j in i)
                        j.Edge = null;

                // odstraneni vdalenejsich 1 sigma
                //pridam prumer vzdalenosti od hrany
                var filtered = Points.Where(p => p.Edge != null).ToList();

                if (filtered.Any())
                {
                    var avg = filtered.Average(i => (i.EdgePoint - i.TransformedPoint).Length);
                    //pridam rozptyl vzdalenosti
                    var std = 2 * Math.Sqrt(filtered.Average(i => Math.Pow((i.EdgePoint - i.TransformedPoint).Length - avg, 2)));

                    // odstraneni bodu, ktere jsou vzdalenejsi 1 sigma
                    foreach (var i in filtered.Where(p => Math.Abs((p.EdgePoint - p.TransformedPoint).Length - avg) > std))
                        i.Edge = null;
                }

                /*
                // odstraneni vdalenejsich 1 sigma pro kazdou hranu
                //pridam prumer vzdalenosti od hrany
                var edges =groups.Select(g => new { Edge = g.Key, Points = g.ToList(), Avg = g.Average(i => (i.EdgePoint - i.TransformedPoint).Length) }).ToList();
                //pridam rozptyl vzdalenosti
                var edges2 = edges.Select(v => new { Edge = v.Edge, Points = v.Points, Avg=v.Avg, Std = Math.Sqrt(v.Points.Average(i => Math.Pow((i.EdgePoint - i.TransformedPoint).Length - v.Avg, 2))) });

                // odstraneni bodu, ktere jsou vzdalenejsi 1 sigma
                foreach (var i in edges2)
                    foreach (var j in i.Points.Where(p => Math.Abs((p.EdgePoint - p.TransformedPoint).Length-i.Avg) > i.Std))
                        j.Edge = null;
    */

                // odstraneni bodu vzdalenejsich 1m od hrany
                /*      foreach (var i in groups.Where(g => g.Count() >3))
                          foreach (var j in i.Where(p=>(p.EdgePoint-p.TransformedPoint).Length>1))
                              j.Edge = null;
                */
                changed = true;
                iteration--;
                if (changed)
                    changed = Match();
            }
        }

        private void EdgesFrom(IEnumerable<MapWay> ways, Point2D offset)
        {
            Edges = new List<EdgeSegment>();
            foreach (var w in ways)
            {
                Edges.AddRange(EdgeSegment.FromWay(new Point2D(w.Start.Position.Y, w.Start.Position.Z)+offset, new Point2D(w.End.Position.Y, w.End.Position.Z)+offset, w.Start.Width, w.End.Width));
            }
        }

        private void EdgesFrom(GraphNavigationMsg map, Point2D offset)
        {
            Edges = new List<EdgeSegment>();
            if (map.Vertexes != null)
            {
                if (map.Edges != null)
                {
                    foreach (var e in map.Edges)
                    {
                        var start = map.Vertexes[e.From];
                        var end = map.Vertexes[e.To];

                        foreach(var es in EdgeSegment.FromWay(new Point2D(start.X, start.Y) + offset, new Point2D(end.X, end.Y) + offset, start.Width, end.Width))
                        {
                            foreach (var ee1 in Edges)
                                ee1.Trim(es);
                            Edges.Add(es);
                        }
                    }
                }
            }
        }
        private void UnionEdgesFrom(GraphNavigationMsg map, Point2D offset)
        {
            Edges = new List<EdgeSegment>();
            List<List<Point2D>> l = new List<List<Point2D>>();
            if (map.Vertexes != null)
            {
                if (map.Edges != null)
                {
                    foreach (var e in map.Edges)
                    {
                        var start = map.Vertexes[e.From];
                        var end = map.Vertexes[e.To];
                        l.Add(EdgeSegment.PolyWay(new Point2D(start.X, start.Y) + offset, new Point2D(end.X, end.Y) + offset, start.Width, end.Width));
                    }
                }
            }
            var poly=Point2D.PolyUnion(l, 0.001);
            Point2D? old = null;
            if (poly.Count > 2)
            {
                foreach (var p in poly)
                {
                    if (old != null)
                    {
                        Edges.Add(new EdgeSegment(old.Value, p));
                    }
                    old = p;
                }
                Edges.Add(new EdgeSegment(old.Value, poly.First()));
            }
        }
        private void PointsFrom(ICPMsg localMap, Point2D offset)
        {
            Points = new List<PointInfo>();
            foreach (var s in localMap.Points.Where(i=>i.Type==10))
            {
                var p = new Point2D(s.X, s.Y);
                if (p.Distance < 10)
                    Points.Add(new PointInfo() { OriginalPoint = p + offset, IsLeft = s.SubType == 0, Orientation = s.Orientation ?? 0 });
            }
        }
        private void PointsFrom(GraphMapBase localMap, Point2D offset)
        {
            Points = new List<PointInfo>();
            foreach (var s in localMap.States.Where(i => i.Type == 10))
            {
                Points.Add(new PointInfo() { OriginalPoint = s.Point+offset, IsLeft = s.SubType == 0, Orientation = s.Orientation ?? 0 });
            }
        }

        private void PointsFrom(IEnumerable<PathEdge2> l, Point2D offset)
        {
            Point2D? s = null;
            var l2 = l.ToList();
            for (int i = 0; i < l2.Count; i++)
            {
                var ep = i == 0 ? (Point2D?)null : l2[i - 1].WorldPoint2D;
                var e = l2[i].WorldPoint2D;
                var en = i <l2.Count-1 ? l2[i + 1].WorldPoint2D : (Point2D?)null;

                var op = ep != null ? (ep.Value - e.Value).Angle:(double?)null;
                var on = en != null ? (e.Value-en.Value).Angle : (double?)null;

                if ((op != null || on != null) && (op != null && on != null && Math.Abs(Conversions.NormalizeOrientation(on.Value - op.Value)) < Math.PI / 6))
                    Points.Add(new PointInfo()
                    {
                        OriginalPoint = e.Value + offset,
                        IsLeft = l2[i].Left,
                        EyeOrientation= l2[i].Orientation.Value,
                        Orientation = Conversions.CircularMean(op ?? on.Value, on ?? op.Value)
                    });
            }
        }

        /// <summary>
        /// Predpoklada, ze na vstupu jsou jen platne body WorldPoint2D.HasValue==true
        /// </summary>
        /// <param name="l"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        (int Index, double Length2) NearestIndex(List<PathEdge2> l, Point2D p)
        {
            double min = double.MaxValue;
            int minIdx = -1;
            Point2D p1;
            for (int i = 0; i < l.Count; i++)
            {
                var pe = l[i];
                p1 = pe.WorldPoint2D.Value;
                var dist = (p1 - p).LengthSquerd;
                if (dist < min)
                {
                    min = dist;
                    minIdx = i;
                }
            }
            return (minIdx, min);
        }

        /// <summary>
        /// Dva nejblizsi indexy
        /// </summary>
        /// <param name="l"></param>
        /// <param name="p"></param>
        /// <returns></returns>
        (int First, int Second) NearestIndex2(List<PathEdge2> l, Point2D p)
        {
            double min1 = double.MaxValue;
            int minIdx1 = -1;
            double min2 = double.MaxValue;
            int minIdx2 = -1;
            Point2D p1;
            for (int i = 0; i < l.Count; i++)
            {
                var pe = l[i];
                if (pe.WorldPoint2D.HasValue && !p.Equals(pe.WorldPoint2D.Value))
                {
                    p1 = pe.WorldPoint2D.Value;
                    var dist = (p1 - p).LengthSquerd;
                    if (dist < min1)
                    {
                        min2 = min1;
                        minIdx2 = minIdx1;
                        min1 = dist;
                        minIdx1 = i;
                    }
                    else if (dist < min2)
                    {
                        min2 = dist;
                        minIdx2 = i;
                    }
                }
            }
            return (minIdx1, minIdx2);
        }


        List<PathEdge2> OrderNearest(List<PathEdge2> c)
        {
            List<PathEdge2> ret = new List<PathEdge2>();
            var toProcess = c.Where(i => i.WorldPoint2D.HasValue).ToList();
            var f = NearestIndex(toProcess, new Point2D());

            if (f.Index>-1)
            {
                var first = toProcess[f.Index];
                toProcess.RemoveAt(f.Index);
                ret.Add(first);
                f = NearestIndex(toProcess, first.WorldPoint2D.Value);
                if (f.Index > -1)
                {
                    var l = f;

                    while (toProcess.Count > 0)
                    {
                        if (f.Length2 < l.Length2)
                        {
                            ret.Insert(0, toProcess[f.Index]);
                            toProcess.RemoveAt(f.Index);
                            if (toProcess.Count > 0)
                            {
                                if (l.Index > f.Index)
                                    l.Index--;
                                else if (l.Index == f.Index)
                                    l = NearestIndex(toProcess, ret[ret.Count - 1].WorldPoint2D.Value);
                                f = NearestIndex(toProcess, ret[0].WorldPoint2D.Value);
                                first = toProcess[f.Index];
                            }
                        }
                        else
                        {
                            ret.Add(toProcess[l.Index]);
                            toProcess.RemoveAt(l.Index);
                            if (toProcess.Count > 0)
                            {
                                if (f.Index > l.Index)
                                    f.Index--;
                                else if (l.Index == f.Index)
                                    f = NearestIndex(toProcess, ret[0].WorldPoint2D.Value);
                                l = NearestIndex(toProcess, ret[ret.Count - 1].WorldPoint2D.Value);
                            }
                        }
                    }
                }
            }
            if(ret.Count>0 && ret[0].WorldPoint2D.Value.Distance< ret[ret.Count-1].WorldPoint2D.Value.Distance)
                ret.Reverse();
            return ret;
        }

        /// <summary>
        /// Vrati obsah l serazeny tak aby suma vzdalenosti mezi sousedy byla minimlani - proste aby tvorili radu
        /// </summary>
        /// <param name="l"></param>
        /// <returns></returns>
        List<PathEdge2> OrderNearest2(List<PathEdge2> l)
        {
            List<PathEdge2> ret = new List<PathEdge2>();

            // propocet nejblizsich sousedu
            var idxs = new (int First, int Second)[l.Count];
            for (int i = 0; i < l.Count; i++)
            {
                var pe = l[i];
                if (pe.WorldPoint2D.HasValue)
                {
                    Point2D last = pe.WorldPoint2D.Value;
                    idxs[i] = NearestIndex2(l, pe.WorldPoint2D.Value);
                }
                else
                    idxs[i] = (-1, -1);
            }

            //najit zacatek tj. ten ktery na obes terany vzajemne na sebe neukazuje
            int startIdx= -1;
            for (int i = 0; i < l.Count; i++)
            {
                var v = idxs[i];
                if (v.First!=-1 && v.Second!=-1)
                {
                    var f = idxs[v.First];
                    var s = idxs[v.Second];
                    if (!((f.First == v.First || f.Second == v.First) && (s.First == v.Second || s.Second == v.Second)))
                    {
                        startIdx = i;
                        break;
                    }
                }
            }

            while (startIdx != -1)
            {
                ret.Add(l[startIdx]);

                int i = startIdx;
                var v = idxs[i];
                var f = idxs[v.First];
                var s = idxs[v.Second];
                startIdx = -1;
                if (f.First == v.First || f.Second == v.First)
                    startIdx = v.First;
                else if (s.First == v.Second || s.Second == v.Second)
                    startIdx = v.Second;
                idxs[startIdx] = (-1, -1);
            }

            return ret;
        }


        private void AddSiplifiedPoints(IEnumerable<PathEdge2> l, Point2D offset)
        {
            DouglasPeuckerReduction r = new DouglasPeuckerReduction(0.01);
            List<PathEdge2> l3 = new List<PathEdge2>();
            var l2 = OrderNearest(l.Where(i => i.WorldPoint2D.HasValue && i.WorldPoint2D.Value.Distance > 0.3 && i.WorldPoint2D.Value.Distance < 10).ToList());
            if (l2.Count > 0)
            {
                Point2D? po = null;
                foreach (var p in l2)
                {
                    if (po != null)
                    {
                        if ((po.Value - p.WorldPoint2D.Value).Length > 0.1)
                        {
                            l3.Add(p);
                            po = p.WorldPoint2D.Value;
                        }
                    }
                    else
                        po = p.WorldPoint2D.Value;
                }

                PointsFrom(l3, offset);
            }
        }

        private void PointsFrom(PathEdgeMsg pem, Point2D offset)
        {
            Points = new List<PointInfo>();
            AddSiplifiedPoints(pem.Edges.Where(i=>!i.Left), offset);
            AddSiplifiedPoints(pem.Edges.Where(i => i.Left), offset);
        }

        public PathMapCorelator(GraphNavigationMsg map, ICPMsg localMap, Point2D offset)
        {
            Init(map, localMap, offset);
        }

        public PathMapCorelator(GraphNavigationMsg map, PathEdgeMsg pem, Point2D offset)
        {
            Init(map, pem, offset);
        }

        public PathMapCorelator()
        {
        }

        public void Init(GraphNavigationMsg map, PathEdgeMsg pem, Point2D offset)
        {
            EdgesFrom(map, -offset);
            //UnionEdgesFrom(map, -offset);
            PointsFrom(pem, new Point2D(0, 0));
        }
        public void Init(GraphNavigationMsg map, ICPMsg localMap, Point2D offset)
        {
            EdgesFrom(map, -offset);
            PointsFrom(localMap, new Point2D(0, 0));
        }
    }
}
