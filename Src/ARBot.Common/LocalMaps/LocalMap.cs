using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Logs;

namespace ARBot.Common.LocalMaps
{
    public class LocalMap:IEnumerable<Tile>, ILocalMap
    {
        double resolution = 0.05;

        /// <summary>
        /// Konstruktor
        /// </summary>
        public LocalMap()
        {
            int w = Width / Tile.Width;
            int h = Height / Tile.Height;
            data = new Tile[w, h];
            cache = new Dictionary<Point, Tile>();

            Center = new Point(0, 0);
        }

        public LocalMap(double resolution):this()
        {
            this.resolution = resolution;
        }
        /// <summary>
        /// Sirka v pixelech. Center.X se nachazi na Width/2.
        /// </summary>
        public int Width
        {
            get
            {
                return 64;
            }
        }
        /// <summary>
        /// Vyska v pixelech. Center.Y se nachazi na Height/2
        /// </summary>
        public int Height
        {
            get
            {
                return 64;
            }
        }

        /// <summary>
        /// Velikost pixelu v m
        /// </summary>
        public double Resolution
        {
            get
            {
                return resolution;
            }
        }

        /// <summary>
        /// Volana v okamziku zmeny dlazdic v lokalni mape.
        /// </summary>
        public event EventHandler<RepositionTilesEventArgs> RepositionTiles;

        Dictionary<Point, Tile> cache = new Dictionary<Point, Tile>();
        Tile[,] data;
        private Point center;
        private Point firstTile;
        /// <summary>
        /// Stred lokalni mapy v pixelech
        /// </summary>
        public Point Center
        {
            get
            {
                return center;
            }
            set
            {
                int x = value.X / Tile.Width-1;
                int y = value.Y / Tile.Height-1;
                Point ft = new Point(Tile.Width * x, Tile.Height * y);

                if (!ft.Equals(firstTile))
                {
                    firstTile=ft;
                    if (RepositionTiles != null)
                        RepositionTiles(this, new RepositionTilesEventArgs() { FirstTile = ft, Height = Height, Width = Width });
                }
                center = value;
            }
        }

        /// <summary>
        /// Posouva lokalni mapu
        /// </summary>
        /// <param name="xd">Posuv doprava</param>
        /// <param name="yd">Posuv nahoru</param>
        public void Move(int xd, int yd)
        {
            Center = new Point(Center.X + xd, Center.Y + yd);
        }

        private Tile GetTile(int x, int y, bool create)
        {
            Point key = new Point(x, y);
            Tile t;
            if (!cache.TryGetValue(key, out t) && create)
            {
                cache.Add(key, t = new Tile(key));
            }

            return t;
        }

        /// <summary>
        /// Zpristupnuje pixely s pocatkem v miste robota (Center)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public BayesPixel this[double x, double y]
        {
            get
            {
                return this[(int)(x / Resolution), (int)(y / Resolution)];
            }
        }

        /// <summary>
        /// Zpristupnuje pixely s pocatkem v miste robota (Center)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public BayesPixel this[int x, int y
/*            , [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = ""
            , [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = 0*/
            ]
        {
            get
            {
//                Debug.WriteLine("{2}:{3} LocalMap({0}, {1})", x, y, sourceFilePath, sourceLineNumber);
                int x1 = x + Center.X;
                int y1 = y + Center.Y;
                int tx;
                int ty;

                if (x1 < 0)
                    tx = (x1 + 1) / Tile.Width-1;
                else
                    tx = x1 / Tile.Width;

                if (y1 < 0)
                    ty = (y1 + 1) / Tile.Height - 1;
                else
                    ty = y1 / Tile.Height;

                int x2 = x1 - (tx * Tile.Width);
                int y2 = y1 - (ty * Tile.Height);
                try
                {
                    return GetTile(tx, ty, true)[x2, y2];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("{5}, {6}:LocalMap[{0}, {1}][{2}, {3}] {4}", tx, ty, x2, y2, ex.ToString(), x1, y1);
                    throw;
                }
            }
        }

        /// <summary>
        /// Aktualizuje lokalni mapu podle jine lokalni mapy
        /// </summary>
        /// <param name="lm"></param>
        /// <param name="scale"></param>
        public void Update(ILocalMap lm, double scale)
        {
            int w2 = Width / 2;
            int h2 = Height / 2;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    this[x - w2, y - h2].Update(0.5 + scale*(lm[(x - w2)*Resolution, (y - h2) * Resolution].Value-0.5));
                }
            }
        }
        /// <summary>
        /// Aktualizuje lokalni mapu podle jine lokalni mapy
        /// </summary>
        /// <param name="lms"></param>
        public void Update(params ILocalMap[] lms)
        {
            foreach(ILocalMap lm in lms)
            {
                if (lm.Width != Width)
                    throw new Exception("Lokalni mapy musi mit stejnou sirkou.");
                if (lm.Height != Height)
                    throw new Exception("Lokalni mapy musi mit stejnou vysku.");
            }
            int w2 = Width / 2;
            int h2 = Height / 2;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    this[x - w2, y - h2].Update(lms.Aggregate(1.0, (prod, val)=> prod*val[(x - w2) * Resolution, (y - h2) * Resolution].Value));
                }
            }
        }

        public IEnumerator<Tile> GetEnumerator()
        {
            foreach(Tile t in cache.Values)
                yield return t;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public Blob ToLogMessage(string name)
        {
            Blob b = new Blob();
            b.Name = name;
            b.Height = Height;
            b.Width = Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[Height * Width];

            int xd = Width / 2;
            int yd = Height / 2;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    //                    Debug.WriteLine("ToLogMessage {0}, {1}", x, y);
                    b.Data[x + y * Width] = (byte)(this[x-xd, yd-y].Value * 255);
                }
            }
            return b;
        }
    }
}
