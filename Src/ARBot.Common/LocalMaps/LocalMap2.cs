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
    public class LocalMap2 : ILocalMap
    {
        double resolution = 0.1;

        private BayesPixel[,] points;

        /// <summary>
        /// Konstruktor
        /// </summary>
        public LocalMap2()
        {
            int w = Width;
            int h = Height;
            points = new BayesPixel[Width, Height];
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    points[x, y] = new BayesPixel();
                }
            }
        }

        public LocalMap2(double resolution):this()
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
                return 128;
            }
        }
        /// <summary>
        /// Vyska v pixelech. Center.Y se nachazi na Height/2
        /// </summary>
        public int Height
        {
            get
            {
                return 128;
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
        /// Posouva lokalni mapu
        /// </summary>
        /// <param name="xd">Posuv doprava</param>
        /// <param name="yd">Posuv nahoru</param>
        public void Move(int xd, int yd)
        {
            if (xd != 0 || yd != 0)
            {
                BayesPixel[,] p = new BayesPixel[Width, Height];

                for (int x = 0; x < Width; x++)
                {
                    for (int y = 0; y < Height; y++)
                    {
                        int x1=x - xd;
                        int y1=y - yd;

                        if (x1>=0 && x1<Width && y1>=0 && y1<Height)
                            p[x, y] = points[x1, y1];
                    }
                }

                points = p;
            }
        }

        private Point center;
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
                int x = value.X-center.X;
                int y = value.Y - center.Y;

                Move(x, y);

                center = value;
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
                int x1 = x+Width/2;
                int y1 = y+Height/2;

                if (x1 >= 0 && x1 < Width && y1 >= 0 && y1 < Height)
                    return points[x1, y1];
                return new BayesPixel();
            }
        }
        /// <summary>
        /// Aktualizuje lokalni mapu podle jine lokalni mapy
        /// </summary>
        /// <param name="scale"></param>
        public void Update(ILocalMap lm, double scale)
        {
            int w2 = Width / 2;
            int h2 = Height / 2;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    this[x - w2, y - h2].Update(0.5 + scale * (lm[(x - w2) * Resolution, (y - h2) * Resolution].Value - 0.5));
                }
            }
        }
        /// <summary>
        /// Aktualizuje lokalni mapu podle jine lokalni mapy
        /// </summary>
        /// <param name="lms"></param>
        public void Update(params ILocalMap[] lms)
        {
            foreach (ILocalMap lm in lms)
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
                    this[x - w2, y - h2].Update(lms.Aggregate(1.0, (prod, val) => prod * val[(x - w2) * Resolution, (y - h2) * Resolution].Value));
                }
            }
        }

        public ImageMsg ToLogMessage(string name)
        {
            var img = new ARBot.Common.Common.Image<ARBot.Common.Common.Gray>(Width, Height);
            byte[] data = img.Data;

            int xd = Width / 2;
            int yd = Height / 2-1;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    data[x + y * Width] = (byte)(this[x-xd, yd-y].Value * 255);
                }
            }
            return new ImageMsg(img, name);
        }
    }
}
