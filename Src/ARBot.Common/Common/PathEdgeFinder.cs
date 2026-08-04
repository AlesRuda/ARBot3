using ARBot.Common.Algorithms;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Algorithms.ML;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps;
using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class PathEdgeFinder
    {
        public class EdgeItem
        {
            /// <summary>
            /// Body hrany
            /// </summary>
            public List<PathEdge2> Points { get; private set; }
            public Line2D LinearRegesion { get; private set; }
            public EdgeItem(List<PathEdge2> points)
            {
                Points = points;
//                LinearRegesion = points.LinearRegesion();
                LinearRegesion = RANSAC.LinearRegresion(points.Where(i => i.Used).ToList(), 3, 0.3, 0.99, (pe)=>pe.WordPoint2D.Value, pe=>pe.Inlier=true);
            }
        }

        public PathEdgeFinder()
        {
        }
        /// <summary>
        /// Podklady pro vypocet
        /// </summary>
        public List<PathEdgeFinderItem> Items { get; private set; }

        /// <summary>
        /// Okraje vozovky
        /// </summary>
        public List<PathEdge2> Edges { get; private set; }
        /// <summary>
        /// Nesjizdne body v plose po ktere jede robot, svetovou orientaci, ale pocatkem spojenym s robotem
        /// </summary>
        public List<Point2D> Obstacles { get; private set; }

        /// <summary>
        /// Aproximace leve hrany
        /// </summary>
        public Line2D Left { get; private set; }
        /// <summary>
        /// Aproximace prave hrany
        /// </summary>
        public Line2D Right { get; private set; }

        public MapWay Way { get; private set; }

        public Double? LeftDistance { get; private set; }
        public Double? RightDistance { get; private set; }

        public double? AngleDiff
        {
            get
            {
                if (Left != null && Right != null)
                    return Left.Angle - Right.Angle;
                return 0;
            }
        }

        /// <summary>
        /// Uhel smerovani cesty v matematickem smyslu v radianech spocteny z kamery
        /// </summary>
        public double? Angle
        {
            get
            {
                double a = 0;
                int cnt = 0;
                if (Left != null)
                {
                    a += Left.Angle;
                    cnt++;
                }
                if(Right != null)
                {
                    a += Right.Angle;
                    cnt++;
                }
                if(cnt>0)
                    return a/cnt;
                return null;
            }
        }


        List<PathEdge2> TransformEdges(List<Point> edgePoints, PathEdgeFinderItem i, bool left)
        {
            var worldPoints = i.DepthCameraProjection.TransformBack(
                edgePoints, i.Depth);

            List<PathEdge2> points = new List<PathEdge2>();
            for (int idx = 0; idx < edgePoints.Count; idx++)
            {
                var pe = new PathEdge2();
                pe.Name = i.Name;
                pe.Orientation = i.Orientation;
                pe.Point = edgePoints[idx];
                var wp = worldPoints[idx];
                if (wp.A == 1)
                {
                    pe.WordPoint = wp;
                    pe.Used = pe.WordPoint.Value.Length < 8 && wp.A==1;
                }
                pe.Left = left;
                points.Add(pe);
            }

            return points;
        }

        public void Process(NativeComputeUnit sc, IEnumerable<PathEdgeFinderItem> items, MapWay way, double maxAngleDiff, double currentOrientation)
        {
            float radius = 8;
            float pointDist = 0.4f; // vzdalenost testovanych bodu od sebe
            int pointRadius = 10; // pocet bodu x a y smerem ktere jsou testovany v okoli robotu
            int medianRadius = 1; // v tomto okoli bude pocitan median
            float medianDist = 0.1f;

            Obstacles = new List<Point2D>();

            //tohle muze zpusobit snapnuti na nejakou vzdalenou hranu a totalne to rozhodi robota
            //asi by bylo nutne brat v uvahu vzdalenost cesty
            //            var ways = way.GetNearestWays(radius);
            // pokud se budu snapovat jen na cestu po ktere jedu nejsem schopen zlepsovat odhad ve smeru cesty
            var ways = new List<MapWay>();
            ways.Add(way);

            Items = items.ToList();

            var leftEdgeItems = new List<EdgeItem>();
            var rightEdgeItems = new List<EdgeItem>();
            Edges = new List<PathEdge2>();

            EdgeItem ei;

//            Debug.WriteLine(string.Format("PathEdgeFinder.Process ways.count={0}, items.count={1}", ways.Count(), items.Count()));

            foreach (var i in items)
            {
                if (sc != null)
                    i.Edges = sc.PathEdges(i.Probability, i.ScaleX, i.ScaleY);
                else
                    i.Edges = i.Probability.PathEdges2().ToList();

                var points = TransformEdges(i.Edges.Where(e => e.Left.HasValue).Select(e => new Point(e.Left.Value, e.Y)).ToList(), i, true);
                Edges.AddRange(points); 

                //                leftEdgeItems.Add(new EdgeItem(points));
                ei = new EdgeItem(points);
                if (ei.LinearRegesion != null)
                    leftEdgeItems.Add(ei);

                points = TransformEdges(i.Edges.Where(e => e.Right.HasValue).Select(e => new Point(e.Right.Value, e.Y)).ToList(), i, false);

                Edges.AddRange(points);

                //                rightEdgeItems.Add(new EdgeItem(points));
                ei = new EdgeItem(points);
                if (ei.LinearRegesion != null)
                    rightEdgeItems.Add(ei);

                for (int x = -pointRadius; x <= pointRadius; x++)
                {
                    for (int y = -pointRadius; y <= pointRadius; y++)
                    {
                        int cnt = 0;
                        int obsCnt = 0;
                        float cx = 0, cy = 0;

                        for (int x3 = -medianRadius; x3 <= medianRadius; x3++)
                        {
                            for (int y3 = -medianRadius; y3 <= +medianRadius; y3++)
                            {
                                var yp = y * pointDist + y3 * medianDist;
                                var xp = x * pointDist + x3 * medianDist;

                                cnt++;
                                if (i.CameraProjection.Transform(xp, yp, ref cx, ref cy))
                                {
                                    int x2 = (int)(cx / i.ScaleX);
                                    int y2 = (int)(cy / i.ScaleY);

                                    if (i.Probability[x2, y2].Value < 128)
                                        obsCnt++;
                                }
                            }
                        }
                        if(obsCnt>=cnt/2)
                            Obstacles.Add(new Point2D(x * pointDist, y * pointDist));
                    }
                }
            }

//            Debug.WriteLine(string.Format("PathEdgeFinder.Process leftEdgeItems.count={0}, rightEdgeItems.count={1}", leftEdgeItems.Count(), rightEdgeItems.Count()));

            var crossLR = leftEdgeItems.SelectMany(i => rightEdgeItems.Select(j =>
                new
                {
                    Left = i,
                    Right = j,
                    LeftAlfa = Conversions.NormalizeHalfOrientation(i.LinearRegesion.Angle),
                    RightAlfa = Conversions.NormalizeHalfOrientation(j.LinearRegesion.Angle)
                }
            )).Where(c => Math.Abs(c.LeftAlfa - c.RightAlfa) < maxAngleDiff).ToList();

            var cross = crossLR.SelectMany(c => ways.Select(w =>
              new
              {
                  Left = c.Left,
                  Right = c.Right,
                  LeftAlfa = c.LeftAlfa,
                  RightAlfa = c.RightAlfa,
                  Way = w,
                  ExpectedDir = w==null?0:Conversions.NormalizeHalfOrientation(w.Angle)
              }
            )).OrderBy(c => Math.Abs((c.LeftAlfa + c.RightAlfa) / 2 - c.ExpectedDir)).ToList();

            var r = cross.FirstOrDefault();
            EdgeItem le = null;
            EdgeItem re = null;

            if (r != null)
            {
                le = r.Left;
                re = r.Right;
                Way = r.Way;
            }
            else
            {
                var lew = leftEdgeItems.SelectMany(i => ways.Select(w => new
                {
                    EdgeItem = i,
                    Way = w,
                    ExpectedDir = w == null ? 0 : Conversions.NormalizeHalfOrientation(w.Angle),
                    Angle = Conversions.NormalizeHalfOrientation(i.LinearRegesion.Angle)
                })).OrderBy(i => Math.Abs(i.Angle - i.ExpectedDir)).FirstOrDefault();

                var rew = rightEdgeItems.SelectMany(i => ways.Select(w => new
                {
                    EdgeItem = i,
                    Way = w,
                    ExpectedDir = w == null ? 0 : Conversions.NormalizeHalfOrientation(w.Angle),
                    Angle = Conversions.NormalizeHalfOrientation(i.LinearRegesion.Angle)
                })).OrderBy(i => Math.Abs(i.Angle - i.ExpectedDir)).FirstOrDefault();


                if (lew != null && rew != null)
                {
                    if (Math.Abs(lew.Angle - lew.ExpectedDir) > Math.Abs(rew.Angle - rew.ExpectedDir))
                    {
                        re = rew.EdgeItem;
                        Way = rew.Way;
                    }
                    else
                    {
                        le = lew.EdgeItem;
                        Way = lew.Way;
                    }
                }
                else if (lew != null)
                {
                    le = lew.EdgeItem;
                    Way = lew.Way;
                }
                else if (rew != null)
                {
                    re = rew.EdgeItem;
                    Way = rew.Way;
                }
            }

            if (le != null && way != null)
                foreach (var i in le.Points.Where(ii=>ii.Inlier))
                    i.WayID = Way?.ID;
            if (re != null && way != null)
                foreach (var i in re.Points.Where(ii => ii.Inlier))
                    i.WayID = Way?.ID;

            Point2D o = new Point2D(0, 0);
            var orientationLine = new Line2D(new Vector2D(currentOrientation).Normal, o);
            Left = le?.LinearRegesion;
            if (Left != null)
                LeftDistance = Left.Distance(o) * Math.Sign(Left.ProjectOntoLine(o).IsLeft(orientationLine));
            else
                LeftDistance = null;

            Right = re?.LinearRegesion;
            if (Right != null)
                RightDistance = Right.Distance(o) * Math.Sign(-Right.ProjectOntoLine(o).IsLeft(orientationLine));
            else
                RightDistance = null;
        }
        


        public PathEdgeMsg ToLogMessage(string name)
        {
            return new PathEdgeMsg(name, Items, Edges, Left, Right, LeftDistance, RightDistance, AngleDiff, Angle);
        }
    }
}
