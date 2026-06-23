using ARBot.Common.Common;
using ARBot.Common.KDTree;
using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.LocalMaps
{
    /// <summary>
    /// Prepise mista bunek novymi body
    /// </summary>
    public class MergeGraphMapType : GraphMapType
    {
        /// <summary>
        /// Vzdalenost mezi body. Pokud je nove pridavany bod blize existujicimu nebude pridan.
        /// </summary>
        public double Diameter;
        /// <summary>
        /// Velikost ctvercoveho agregacniho pole.
        /// </summary>
        public double Width;
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


        ICPStatePoint[,] arr;
        bool[,] arrState;
        int width;
        int off;
        private void Init()
        {
            width = (int)(Width / Diameter);
            off = (int)(Width / Diameter /2);
            arr = new ICPStatePoint[width, width];
            arrState = new bool[width, width];
        }

        private ICPStatePoint Add(ICPStatePoint p, bool state)
        {
            int x = (int)(p.Point.X / Diameter + off);
            int y = (int)(p.Point.Y / Diameter + off);
            if (x >= 0 && x < width && y >= 0 && y < width)
            {
                var ret = arrState[x, y] ? arr[x, y] : null;
                arr[x, y] = p;
                arrState[x, y] = state;
                return ret; 
            }
            return null;
        }

        private ICPStatePoint Clear(ICPStatePoint p)
        {
            int x = (int)(p.Point.X / Diameter + off);
            int y = (int)(p.Point.Y / Diameter + off);
            if (x >= 0 && x < width && y >= 0 && y < width)
            {
                var ret = arrState[x, y] ? arr[x, y] : null;
                arr[x, y] = null;
                arrState[x, y] = false;
                return ret;
            }
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
            using (new PerformanceToken("Merge"))
            {
                using (new PerformanceToken("Init"))
                {
                    Init();
                }
                using (new PerformanceToken("Add"))
                {
                    foreach (var s in gm.States)
                    {
                        var pt = Add(s, true);
                        if (pt != null)
                        {
                            gm.States.Remove(pt);
                        }
                    }
                }
                using (new PerformanceToken("Merge"))
                {
                    foreach (var p1 in points)
                    {
                        if (p1.Probability > 0.5)
                        {
                            var pt = Add(new ICPStatePoint() { Point = p1.Point, Type = type, SubType = p1.SubType }, false);
                            if (pt != null)
                                gm.States.Remove(pt);
                        }
                        else
                        {
                            var pt = Clear(new ICPStatePoint() { Point = p1.Point, Type = type, SubType = p1.SubType });
                            if (pt != null)
                                gm.States.Remove(pt);
                        }
                    }
                }
                using (new PerformanceToken("Remove"))
                {
                    for (int x = 0; x < Width; x++)
                    {
                        for (int y = 0; y < Width; x++)
                        {
                            if (!arrState[x, y] && arr[x, y] != null)
                                gm.States.Add(arr[x, y]);
                        }
                    }
                }
            }
        }
    }
}
