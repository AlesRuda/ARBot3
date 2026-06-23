using ARBot.Common.Common;
using ARBot.Common.KDTree;
using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.LocalMaps
{
    /// <summary>
    /// Slouci nove body s body v polygonu
    /// </summary>
    public class PolyGraphMapType : GraphMapType
    {
        /// <summary>
        /// Oblast ve ktere budou aktualizovany body
        /// </summary>
        public List<Point2D> Polygon;
        /// <summary>
        /// Vzdalenost mezi body. Pokud je nove pridavany bod blize existujicimu nebude pridan.
        /// </summary>
        public double Diameter;
        /*
                KDTree<Point2D> kd;
                private void Init()
                {
                    kd = new KDTree<Point2D>(2);
                }

                private void Add(Point2D p)
                {
                    kd.AddPoint(new double[] { p.X, p.Y }, p);
                }

                private Point2D? Collision(Point2D p)
                {
                    var pc = new double[] { p.X, p.Y };
                    var pts = kd.NearestNeighbors(pc, 1, Diameter).ToArray();
                    return pts.Count() > 0?(Point2D?)pts[0]:null;
                }
                */


        LinkedList<Point2D>[,] arr;
        int width;
        int off;
        private void Init()
        {
            width = (int)(20.0 / Diameter);
            off = (int)(10.0 / Diameter);
            arr = new LinkedList<Point2D>[width, width];
        }

        private bool Add(Point2D p)
        {
            int x = (int)(p.X / Diameter + off);
            int y = (int)(p.Y / Diameter + off);
            if (x >= 0 && x < width && y >= 0 && y < width)
            {
                var l = arr[x, y];
                if (l == null)
                    arr[x, y] = l = new LinkedList<Point2D>();
                l.AddFirst(p);
                return true;
            }
            return false;
        }

        private IEnumerable<Point2D> Collision(Point2D p)
        {
            int x = (int)(p.X / Diameter + off);
            int y = (int)(p.Y / Diameter + off);
            if (x >= 0 && x < width && y >= 0 && y < width)
                return arr[x, y];
            return null;
        }

        /// <summary>
        /// Nahradi vsechny body urceneho typu
        /// </summary>
        /// <param name="gm"></param>
        /// <param name="type">Typ stavu</param>
        /// <param name="points">Pozice prekazek s pocatkem v [0.0]</param>
        public override void Update(GraphMap gm, int type, IEnumerable<ICPObservationPoint> points)
        {
            Dictionary<Point2D, ICPStatePoint> dic = new Dictionary<Point2D, ICPStatePoint>(gm.States.Count);
            Point2D p;
            if(Polygon==null)
            {
                Debug.WriteLine("PolyGraphMapType.Update - Polygon==null");
                return;
            }

            using (new PerformanceToken("Poly"))
            {
                using (new PerformanceToken("Init"))
                {
                    Init();
                }
                using (new PerformanceToken("Add"))
                {
                    foreach (var s in gm.States)
                    {
                        if (s.Type == type)
                        {
                            p = s.Point;
                            if (p.IsInPoly(Polygon) && !dic.ContainsKey(p))
                            {
                                if (Add(p))
                                    dic.Add(p, s);
                            }
                        }
                    }
                }
                using (new PerformanceToken("Merge"))
                {

                    foreach (var p1 in points)
                    {
                        var l = Collision(p1.Point);
                        if (l != null)
                        {
                            foreach(var pt in l)
                                dic.Remove(pt);
                        }
                        else
                        {
                            if (Add(p1.Point))
                                gm.States.Add(new ICPStatePoint() { Point = p1.Point, Type = type, SubType = p1.SubType, Orientation=p1.Orientation });
                        }
                    }
                }
                using (new PerformanceToken("Remove"))
                {
                    foreach (var kv in dic)
                    {
                        gm.States.Remove(kv.Value);
                    }
                }
            }
        }
    }
}
