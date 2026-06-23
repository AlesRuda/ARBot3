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
    /// Predek pro navigace na mrizce
    /// </summary>
    public class GridNavigationBase: NavigationBase
    {
        public float Resolution { get; protected set; }
        protected BigInteger PotencialScale;
        protected GridNavigationPixel[,] pixels;
        protected DrawEngine de;
        protected GridNavigationNeighborhood[] neighborhood8;
        protected GridNavigationNeighborhood[] neighborhood4;
        protected GridNavigationNeighborhood[] neighborhood;
        protected List<GridNavigationNeighborhood[]> neighborhoods;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>
        /// Orientace matematickeho smeru 0 grafu vzhledem k svetovym souradnicim.
        /// 0 - vychod grafu smeruje na vychod
        /// pi/2 - vychod grafu smeruje na sever
        /// </summary>
        public double Orientation = 0;
        protected GridNavigationResult result;
        protected bool directResult;

        public GridNavigationNeighborhood[] GetNeighborhood(int neighborhoodCount)
        {
            var neighborhood = new GridNavigationNeighborhood[neighborhoodCount];
            if (neighborhood.Length == 4)
            {
                neighborhood[0] = new GridNavigationNeighborhood(0, 0, 1);
                neighborhood[1] = new GridNavigationNeighborhood(1, 1, 0);
                neighborhood[2] = new GridNavigationNeighborhood(2, 0, -1);
                neighborhood[3] = new GridNavigationNeighborhood(3, -1, 0);
            }
            if (neighborhood.Length ==8)
            {
                neighborhood[0] = new GridNavigationNeighborhood(0, 0, 1);
                neighborhood[1] = new GridNavigationNeighborhood(1, 1, 1);
                neighborhood[2] = new GridNavigationNeighborhood(2, 1, 0);
                neighborhood[3] = new GridNavigationNeighborhood(3, 1, -1);
                neighborhood[4] = new GridNavigationNeighborhood(4, 0, -1);
                neighborhood[5] = new GridNavigationNeighborhood(5, -1, -1);
                neighborhood[6] = new GridNavigationNeighborhood(6, -1, 0);
                neighborhood[7] = new GridNavigationNeighborhood(7, -1, 1);
            }
            return neighborhood;
        }


        public virtual IEnumerable<GridNavigationNeighborhood> GetNeighborhoods(int dir)
        {
            int cnt = neighborhood.Length;
            if (cnt==4)
            {
                dir += cnt;
                for (int i = -1; i < 2; i++)
                    yield return neighborhood[(dir + i) % cnt];
            }
            else
            {
                dir += cnt;
                for (int i = -2; i < 3; i++)
                    yield return neighborhood[(dir + i) % cnt];
            }
        }

        public IEnumerable<GridNavigationNeighborhood> GetNeighborhood(Point p)
        {
            int dir = pixels[p.X, p.Y].Direction;
            return GetNeighborhoods(dir);
        }



        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="w">Sirka mrizky v pixelech</param>
        /// <param name="h">Vyska mrizky v pixelech</param>
        public GridNavigationBase(int w, int h, int neighborhoodCount, float resolution)
        {
            Resolution = resolution;
            Start = new GraphState2D(this) { X = 0, Y = 0 };
            Resolution = 0.1f;
            Width = w;
            Height = h;

            neighborhood = GetNeighborhood(neighborhoodCount);
            neighborhood4 = GetNeighborhood(4);
            neighborhood8 = GetNeighborhood(8);
            neighborhoods = new List<GridNavigationNeighborhood[]>(neighborhoodCount);

            for (int i = 0; i < neighborhoodCount; i++)
                neighborhoods.Add(GetNeighborhoods(i).ToArray());

            de = new DrawEngine() { XMin = 0, YMin = 0, XMax = Width - 1, YMax = Height - 1, Clipping = true };
            pixels = new GridNavigationPixel[w, h];
            for(int x=0;x<w;x++)
            {
                for (int y = 0; y < h; y++)
                {
                    pixels[x, y] = new GridNavigationPixel();
                }
            }
        }

        /// <summary>
        /// Vypocte prekazky na zaklade udaju z lokalni mapy
        /// </summary>
        /// <param name="safeDistance">Bezpecna vzdalenost pro prujezd robota</param>
        /// <param name="dir">Smer k cili v radianech a matematickem smyslu.</param>
        public List<Point2D> ObstaclesFrom(ILocalMap lm)
        {
            if (lm.Width != Width)
                throw new ArgumentException("lm.Width != Width");
            if (lm.Height != Height)
                throw new ArgumentException("lm.Width != Width");
            if (lm.Resolution != Resolution)
                throw new ArgumentException("lm.Resolution != Resolution");

            double w2 = (Width * Resolution) / 2;
            double h2 = (Height * Resolution) / 2;

            var q = new List<Point2D>();
            int w = Width;
            int h = Height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (x > 0 && x < w - 1 && y > 1 && y < h - 1 && lm[x - Width / 2, Height / 2 - y].Value < 0.5)
                    {
                        double x1 = x * lm.Resolution-w2;
                        double y1 = h2-y * lm.Resolution;
                        q.Add(new Point2D(x1, y1));
                    }
                }
            }
            return q;
        }

        /// <summary>
        /// Vypocte prekazky na zaklade udaju z lokalni mapy
        /// </summary>
        //public List<Point2D> ObstaclesFrom(IEnumerable<RayEx> rays)
        //{
        //    return rays.Select(r => new Point2D(r.Distance.Value * Math.Cos(r.Angle), r.Distance.Value * Math.Sin(r.Angle))).ToList();
        //}

        /// <summary>
        /// Vypocte prekazky na zaklade udaju z lokalni mapy
        /// </summary>
        //public List<Point2D> ObstaclesFrom(IEnumerable<Ray> rays)
        //{
        //    return rays.Where(r=>r.Distance!=null).Select(r => new Point2D(r.Distance.Value * Math.Cos(r.Angle), r.Distance.Value * Math.Sin(r.Angle))).ToList();
        //}

        /// <summary>
        /// Target
        /// </summary>
        /// <param name="c">relativni pozice. 1 - na okraji, mensi 1 - zmenseni, vetsi jedne - zvetseni </param>
        /// <returns></returns>
        protected Point CalcTarget(double c)
        {
            var rot = Matrix.Rotate2D(-Orientation);
            var o = rot * new Point2D(Target.X, Target.Y);

            double x = Width / 2+o.X/ Resolution;
            double y = Height / 2 - o.Y/ Resolution;
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                double dir = Math.Atan2(o.Y, o.X);
                return BodNaOkraji(dir, c);
            }
            return new Point((int)x, (int)y);
        }

        /// <summary>
        /// Spocte bod na okraji.
        /// [0, 0] je vlevo dole
        /// </summary>
        /// <param name="dir">Smer v radianech v matematickem smyslu</param>
        /// <param name="c">relativni pozice. 1 - na okraji, mensi 1 - zmenseni, vetsi jedne - zvetseni </param>
        /// <returns></returns>
        protected Point BodNaOkraji(double dir, double c)
        {
            dir = Conversions.NormalizeOrientation(dir);
            double alfa = Math.Atan2(Height, Width);
            if (-alfa < dir && dir < alfa)
            {
                return new Point((int)(Width*(c+1)/2) - 1, (int)(Height / 2 * (1 - c*Math.Tan(dir))));
            }
            else if (alfa <= dir && dir < Math.PI - alfa)
            {
                return new Point((int)(Width / 2 * (1 - c*Math.Tan(dir - Math.PI / 2))), (int)(Height*(1-c)/2));
            }
            else if (-Math.PI + alfa < dir && dir <= -alfa)
            {
                return new Point((int)(Width / 2 * (1 + c*Math.Tan(dir - Math.PI / 2))), (int)(Height*(c+1)/2) - 1);
            }
            return new Point((int)(Width*(1-c)/2), (int)(Height / 2 * (1 + c*Math.Tan(dir - Math.PI))));
        }

        List<List<Point>> GetBlobs(List<Point> l)
        {
            var d = l.ToDictionary((i) => i);
            List<List<Point>> ret = new List<List<Point>>();
            while(d.Count>0)
            {
                var p = d.First().Key;
                List<Point> l1 = new List<Point>();
                ret.Add(l1);

                l1.Add(p);

                for(int i=0;i<l1.Count;i++)
                {
                    p = l1[i];
                    foreach(var n in neighborhood)
                    {
                        Point p1 = p+ n.Neighborhood;
                        if(d.ContainsKey(p1))
                        {
                            l1.Add(p1);
                            d.Remove(p1);
                        }
                    }
                }
            }
            return ret;
        }

        protected virtual void InitDistances()
        {
            int w = Width;
            int h = Height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = pixels[x, y];
                    p.X = x;
                    p.Y = y;
                    p.ObstacleDistanceCalculated = false;
                    p.ObstacleDistance = Double.MaxValue;
                    p.WayDistance = double.PositiveInfinity;
                    p.Way = false;
                    p.Direction = 0;
                }
            }
        }

        protected Queue<GridNavigationPixel> InitFromObstacles(IEnumerable<Point> l)
        {
            GridNavigationPixel p1;
            Queue<GridNavigationPixel> q = new Queue<GridNavigationPixel>();
            foreach (var p2 in l)
            {
                p1 = pixels[p2.X, p2.Y];
                q.Enqueue(p1);
                p1.ObstacleDistanceCalculated = true;
                p1.ObstacleDistance = 0;
                p1.OriginObstacle = p2;
            }
            return q;
        }
        /// <summary>
        /// Pocita vzdalenosti od prekazek.
        /// </summary>
        /// <param name="l">List prekazek</param>
        /// <param name="limit">Presne bodou urceny pouze vzdalenosti od prekazek do tohoto limitu. Ostatni vzdalenosti budou double.MaxValue.</param>
        protected void CalcObstacleDistances(IEnumerable<Point> l, double? limit)
        {
            Point pp;
            GridNavigationPixel p1;
            int x ;
            int y ;
            int cnt = 0;
            int w = Width;
            int h = Height;

            double d = 0;
            double dx = 0;
            double dy = 0;
            GridNavigationPixel p;

            InitDistances();

            Queue<GridNavigationPixel> q = InitFromObstacles(l);
            // vypocet vzdalenosti od prekazky pro kazdy pixel
            while (q.Count > 0)
            {
                cnt++;
                p = q.Dequeue();
                pp = p.OriginObstacle.Value;
                foreach (var n in neighborhood4)
                {
                    x = p.X + n.Neighborhood.X;
                    y = p.Y + n.Neighborhood.Y;

                    if (x >= 0 && x < w && y >= 0 && y < h)
                    {
                        dx = (x - pp.X);
                        dy = (y - pp.Y);
                        d = Resolution * Math.Sqrt(dx*dx + dy*dy);

                        p1 = pixels[x, y];
                        if ((limit==null || d<limit.Value) && p1.ObstacleDistance > d)
                        {
                            p1.OriginObstacle = pp;
                            p1.ObstacleDistance = d;
                            q.Enqueue(p1);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Konvertuje vzdalenost od prekazky na pravdepodobnost prujezdnosti.
        /// Probability=add+scale*(Distance-max(Disntace))
        /// </summary>
        /// <param name="add"></param>
        /// <param name="scale"></param>
        protected virtual void InitWeights()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    pixels[x, y].Weight = 1;
                }
            }
        }

        /// <summary>
        /// Testuje zda cesta k tt je volna
        /// </summary>
        /// <param name="tt"></param>
        /// <returns></returns>
        protected GridNavigationResult DirectResult(GraphStateBase tt)
        {
            directResult = true;
            var t = tt.Clone();
            var len = Math.Sqrt(Math.Pow(tt.X, 2) + Math.Pow(tt.Y, 2));
            var r = Math.Sqrt(Math.Pow(Width / 2 * Resolution, 2) + Math.Pow(Height / 2 * Resolution, 2));
            if (len > r)
                len = len / r;
            else
                len = 1;
            t.X = tt.X * len;
            t.Y = tt.Y * len;
            Target = t;
            if (!t.Collision(Start, 0))
            {
                //                Debug.WriteLine(string.Format("GridNavigation.Process no colision"));
                double dir1 = Math.Atan2(t.Y, t.X);
                return result = new GridNavigationResult() { Direction = dir1, Y = t.Y, X = t.X };
            }
            return null;
        }

        public override IEnumerable<Message> ToLogMessages()
        {
            return new List<Message>() { ToLogMessage2() };
        }

        /// <summary>
        /// Pravdepodobnost sjizdnosti
        /// </summary>
        /// <returns></returns>
        public virtual Blob ToLogMessage()
        {
            Blob b = new Blob();
            b.Name = "ObstacleDistances";
            b.Height = Height;
            b.Width = Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[Height * Width];

            double max = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if(pixels[x, y].Weight<double.PositiveInfinity)
                        max=Math.Max(max, pixels[x, y].Weight);
                }
            }

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    //                    Debug.WriteLine("ToLogMessage {0}, {1}", x, y);
                    //                    b.Data[x + y * Width] = (byte)(pixels[x, y].ObstacleDistance * 5);
                    if(pixels[x, y].Weight==double.PositiveInfinity)
                        b.Data[x + y * Width] = 255;
                    else
                        b.Data[x + y * Width] = (byte)(255.0*pixels[x, y].Weight/max);
                }
            }
            return b;
        }
        /// <summary>
        /// Vizualizace cesty
        /// </summary>
        /// <returns></returns>
        public virtual Blob ToLogMessage2()
        {
            if (directResult)
            {
                Blob b = new Blob();
                b.Name = "Way";
                b.Height = Height;
                b.Width = Width;
                b.Type = Blob.BlobType.Probability;
                b.Data = new byte[Height * Width];

                Point p1 = new Point(0, 0);
                if (result != null)
                {
                    int w2 = Width / 2;
                    int h2 = Height / 2;
                    DrawEngine de = new DrawEngine() { XMin = -w2, XMax = w2 - 1, YMin = -h2, YMax = h2 - 1, Clipping = true };
                    de.PixelSetter = (x, y) => b.Data[x + w2 + (h2 - y - 1) * Width] = 255;
                    de.Line(p1, new Point((int)(result.X / Resolution), (int)(result.Y / Resolution)));
                }

                return b;
            }
            else
            {
                Blob b = new Blob();
                b.Name = "Way";
                b.Height = Height;
                b.Width = Width;
                b.Type = Blob.BlobType.Probability;
                b.Data = new byte[Height * Width];

                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        //                    Debug.WriteLine("ToLogMessage {0}, {1}", x, y);
                        b.Data[x + y * Width] = pixels[x, y].Way ? (byte)255 : (pixels[x, y].WayDistance < 0 ? (byte)0 : (byte)(pixels[x, y].WayDistance));
                        //                    b.Data[x + y * Width] = pixels[x, y].WayDistanceCalculated ? (byte)255 : (byte) 0;
                    }
                }
                return b;
            }
        }
        /// <summary>
        /// Zprava reprezentujci cestu
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public GraphNavigationMsg ToGrapMsg(List<Point2D> points)
        {
            GraphNavigationMsg m = new GraphNavigationMsg();
            if (directResult)
            {
                var vertexes = new List<GraphNavigationMsg.Vertex>();
                vertexes.Add(new GraphNavigationMsg.Vertex() { Distance = 0, X = 0, Y = 0, ID = 0 });
                vertexes.Add(new GraphNavigationMsg.Vertex() { Distance = 0, X = Target.X, Y = Target.Y, ID = 1 });
                var edges = new List<GraphNavigationMsg.Edge>();
                edges.Add(new GraphNavigationMsg.Edge(null) { ID = 0, From = 0, To = 1, Path = true, Length = Target.Length });
                return new GraphNavigationMsg(0, 0, result.X, result.Y, result.X, result.Y, vertexes, edges);
            }
            else
            {
                if (result == null)
                    return null;
                var vertexes = new List<GraphNavigationMsg.Vertex>();
                var edges = new List<GraphNavigationMsg.Edge>();
                double d = 0;
                Point2D? pp = null;
                foreach (var p in points)
                {
                    if (pp != null)
                    {
                        d += (pp.Value - p).Length;
                        edges.Add(new GraphNavigationMsg.Edge(null) { ID = edges.Count, From = vertexes.Count - 1, To = vertexes.Count, Path = true, Length = Target.Length });
                    }
                    vertexes.Add(new GraphNavigationMsg.Vertex() { Distance = d, X = p.X, Y = p.Y, ID = vertexes.Count });
                    pp = p;
                }
                return new GraphNavigationMsg(0, 0, Target.X, Target.Y, result.X, result.Y, vertexes, edges);

            }
        }

        /// <summary>
        /// Vizualizace potencialu
        /// </summary>
        /// <returns></returns>
        public Blob ToLogMessage3()
        {
            Blob b = new Blob();
            b.Name = "Potencial";
            b.Height = Height;
            b.Width = Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[Height * Width];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    BigInteger p = pixels[x, y].Potencial;
                    //                    Debug.WriteLine("ToLogMessage {0}, {1}", x, y);
                    decimal d = PotencialScale==0?0:(decimal)((255 * p)/PotencialScale);
                    if (d > 255)
                        d = 255;
                    b.Data[x + y * Width] = pixels[x, y].Way ? (byte)255 : (byte)d;
                }
            }
            return b;
        }
        /// <summary>
        /// Vizualizace vzdalenosti od prekazek
        /// </summary>
        /// <returns></returns>
        public Blob ToLogMessage4()
        {
            Blob b = new Blob();
            b.Name = "ObstacleDistances";
            b.Height = Height;
            b.Width = Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[Height * Width];

            double max = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if(pixels[x, y].ObstacleDistance<double.MaxValue)
                        max = Math.Max(max, pixels[x, y].ObstacleDistance);
                }
            }

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    //                    Debug.WriteLine("ToLogMessage {0}, {1}", x, y);
                    //                    b.Data[x + y * Width] = (byte)(pixels[x, y].ObstacleDistance * 5);
                    b.Data[x + y * Width] = (byte)(255.0 * pixels[x, y].ObstacleDistance / max);
                }
            }
            return b;
        }


    }
}
