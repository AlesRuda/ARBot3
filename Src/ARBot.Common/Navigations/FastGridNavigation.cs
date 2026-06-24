using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using ARBot.Common.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Rychla navigace na mrizce
    /// </summary>
    public class FastGridNavigation : GridNavigationBase
    {
        protected double navigationDistnace;
        protected List<Point> intObstacles;
        List<Point2D> points;
        public double SafeDistance { get; protected set; }


        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="w">Sirka mrizky v pixelech</param>
        /// <param name="h">Vyska mrizky v pixelech</param>
        public FastGridNavigation(int w, int h, float resolution, double safeDistance, double navigationDistnace) : base(w, h, 8, resolution)
        {
            SafeDistance = safeDistance;
            this.navigationDistnace = navigationDistnace;
        }


        /// <summary>
        /// Konvertuje vzdalenost od prekazky na vahu prujezdnosti.
        /// Weight=1/sqrt(2*Acceleration*(ObstacleDistance-SaveDisntace))
        /// </summary>
        /// <param name="add"></param>
        /// <param name="scale"></param>
        protected override void InitWeights()
        {
        }


        public override GridNavigationResult Process(GraphStateBase tt)
        {
            int w = Width;
            int h = Height;
            int x1;
            int y1;

            GridNavigationPixel p1;
            double d, d1;

            Start = tt.Clone();
            Start.X = 0;
            Start.Y = 0;

            result = DirectResult(tt);
            if (result != null)
                return result;

            var rot = Rotate2D(-Orientation);
            var roti = Rotate2D(Orientation);

            if (ObstaclesChanged)
            {
                double w2 = (Width * Resolution) / 2;
                double h2 = (Height * Resolution) / 2;


                intObstacles = Obstacles.Select(p =>
                {
                    var o = rot * p;
                    return new Point((int)((w2 + o.X) / Resolution + 0.5), (int)((h2 - o.Y) / Resolution + 0.5));
                }).Where(i => i.X >= 0 && i.X < Width && i.Y >= 0 && i.Y < Height).ToList();
                ObstaclesChanged = false;
            }

            CalcObstacleDistances(intObstacles, SafeDistance);

            var f = new Vector3(w * Resolution / 2, h * Resolution / 2, 0);
            pixels[w / 2, h / 2].WayDistance = 0;
            Point pp = CalcTarget(1);

            Queue<GridNavigationPixel> q = new Queue<GridNavigationPixel>();
            q.Enqueue(pixels[Width / 2, Height / 2]);

            GridNavigationPixel target = null;

            while (q.Count>0)
            {
                var node = q.Dequeue();

                if (node.X == pp.X && node.Y == pp.Y)
                {
                    target = node;
                    break;
                }

                d = node.WayDistance;

                foreach (var n in neighborhoods[node.Direction])
                {
                    x1 = node.X + n.Neighborhood.X;
                    y1 = node.Y + n.Neighborhood.Y;
                    if (x1 >= 0 && x1 < w && y1 >= 0 && y1 < h)
                    {
                        p1 = pixels[x1, y1];
                        if (p1.ObstacleDistance>SafeDistance)
                        {
                            d1= d + n.Length;
                            if (d1 < p1.WayDistance)
                            {
                                p1.WayDistance = d1;
                                p1.Previous = node;
                                p1.Direction = n.Direction;
                                q.Enqueue(p1);
                            }
                        }
                    }
                }
            }

            // hledani cile
            directResult = false;

            points = new List<Point2D>();
            GridNavigationPixel target2 = target;
            while (target2 != null)
            {
                target2.Way = true;
                double dx= target2.X*Resolution- f.X, dy= f.Y- target2.Y * Resolution;

                Point2D p = new Point2D(dx, dy);
                var o = roti * p;
                points.Add(o);

                //                Debug.WriteLine(string.Format("GridNavigation.Process dx={0}, dy={1}, dist={2}", dx, dy, pixels[target2.Value.X, target2.Value.Y].WayDistance));
                if (dx*dx+dy*dy<= navigationDistnace * navigationDistnace)
                {
                    double dir1 = Math.Atan2(dy, dx);
                    double minDist = Math.Sqrt(Math.Pow(tt.X, 2) + Math.Pow(tt.Y, 2));

                    //                    Debug.WriteLine(string.Format("GridNavigation.Process returns {0}, {1}", dir1, minDist));
                    points.Add(new Point2D());
                    points.Reverse();
                    return result = new GridNavigationResult() { Direction = dir1 + Orientation, Y = minDist * Math.Sin(dir1+Orientation), X = minDist * Math.Cos(dir1 + Orientation) };
                }

                target2 = target2.Previous;
            }

//            Debug.WriteLine("GridNavigation.Process returns null");
            return result = null;
        }

        public override IEnumerable<Message> ToLogMessages()
        {
            return new List<Message>() { ToLogMessage2(), ToGrapMsg(points) };
        }
    }
}
