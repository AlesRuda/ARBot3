using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using ARBot.Common.Logs;
using ARBot.Common.Maps;
using System.Diagnostics;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Slouzi pro vypocet korelace mezi mapou a lokalni mapou
    /// </summary>
    public class MapCorelator
    {
        bool fazova = true;
        double u = 0.01;
        public MapCorelator(ILocalMap lm, Map m)
        {
            if (lm == null)
                throw new ArgumentNullException("lm");
            if (m == null)
                throw new ArgumentNullException("m");
            LocalMap = lm;
            Map = m;
            int w = lm.Width;
            int h = lm.Height;

            corelation = new Complex[w, h];
            gause = new double[w, h];

            double xd = ((double)lm.Width-1) / 2;
            double yd = ((double)lm.Height-1) / 2;
            double d = 0.5;
            double xd2 = 2 * Math.Pow(xd * d, 2);
            double yd2 = 2 * Math.Pow(yd * d, 2);


            for (int y = 0; y < lm.Height; y++)
            {
                for (int x = 0; x < lm.Width; x++)
                {
                    gause[x, y] = Math.Exp(-Math.Pow(x-xd, 2) / xd2 - Math.Pow(y-yd, 2) / yd2);
//                    gause[x, y] = 1;
                }
            }
        }

        private const int RecalculateLimit = 5;
        private Point lastLocalMapCenter;
        private Complex[,] mapObraz;
        private Complex[,] localMapObraz;
        private Complex[,] map;
        private Complex[,] localMap;
        private Complex[,] corelation;
        double[,] gause;

        private ILocalMap LocalMap;
        private Map Map;

        public MapCorelatorResult LastResult;

        /// <summary>
        /// Vypocet korelace
        /// </summary>
        /// <param name="xo">Pozice robotu v metech, roste smerem na vychod.</param>
        /// <param name="yo">Pozice robotu v metech, roste smerem na sever.</param>
        public MapCorelatorResult Process(double xo, double yo)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            ILocalMap lm =LocalMap;
            Point center = lm.Center;
            int w = lm.Width;
            int h = lm.Height;

//            if (mapObraz == null || Math.Abs(center.X - lastLocalMapCenter.X) > RecalculateLimit || Math.Abs(center.Y - lastLocalMapCenter.Y) > RecalculateLimit)
            {
                mapObraz = new Complex[w, h];

                int w2 = w / 2;
                int h2 = h / 2;

                DrawEngine de = new DrawEngine() { XMin = -w2, YMin = -h2, XMax = w - 1-w2, YMax = h - 1-h2, Clipping = true };

                de.PixelSetter = (x, y) =>
                {
                    mapObraz[x+w2, y+h2].Re = gause[x + w2, y + h2];
                };
                Map.Draw(de, xo, yo, lm.Resolution);

                map = (Complex[,])mapObraz.Clone();

                FourierTransform.FFT2(mapObraz, FourierTransform.Direction.Forward);

                lastLocalMapCenter = center;
            }
            center = lastLocalMapCenter;

            localMapObraz = new Complex[w, h];

            Point oldCenter = lm.Center;

            try
            {
                lm.Center = center;
                int xd = lm.Width / 2;
                int yd = lm.Height / 2-1;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        localMapObraz[x, y].Re = lm[x - xd, -y + yd].Value*gause[x, y];
                    }
                }
            }
            finally
            {
                lm.Center = oldCenter;
            }

            localMap = (Complex[,])localMapObraz.Clone();

            FourierTransform.FFT2(localMapObraz, FourierTransform.Direction.Forward);

            //            mapObraz = (Complex[,])localMapObraz.Clone(); ;
            MapCorelatorResult ret = Process(mapObraz, localMapObraz);

            sw.Stop();
            ret.ProcessingTime = sw.Elapsed;
            return ret;
        }


        public MapCorelatorResult Process(Complex[,] mapObraz, Complex[,] localMapObraz)
        {
            int w = mapObraz.GetLength(0);
            int h = mapObraz.GetLength(1);

            int w2 = w / 2;
            int h2 = h / 2;

            if (localMapObraz.GetLength(0) != w)
                throw new ArgumentException("Wrong width.");
            if (localMapObraz.GetLength(1) != h)
                throw new ArgumentException("Wrong height.");

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Complex c = localMapObraz[x, y];
                    Complex o = mapObraz[x, y];
                    c.Im = -c.Im;
                    if(fazova)
                    {
                        corelation[x, y] = (o * c)/((o.Magnitude+u)*  (c.Magnitude+u));
                    }
                    else
                        corelation[x, y] = o * c;
                }
            }


            FourierTransform.FFT2(corelation, FourierTransform.Direction.Backward);

            double sx = 0;
            double sy = 0;
            double sx2 = 0;
            double sy2 = 0;
            double sxy = 0;
            double n = 0;

            double maximum = 0;
            double maximumAtX=0;
            double maximumAtY=0;
            double offsetX=0;
            double offsetY=0;
            double varianceX=0;
            double varianceY=0;
            double covariance = 0;

            for (int y = -h2; y < h2; y++)
            {
                for (int x = -w2; x < w2; x++)
                {
                    int x1 = (x < 0 ? x + w : x);
                    int y1 = (y < 0 ? y + h : y);

                    double c = Math.Abs(corelation[x1, y1].Re);
                    double c1 = c;
                    c = c * c;

                    double x2 = (x+0.5) * c;
                    double y2 = (y+0.5) * c;

                    n += c;

                    if (c1 > maximum)
                    {
                        maximum = c1;
                        maximumAtX = x;
                        maximumAtY = y;
/*                        if(fazova)
                        {
                            sx = 0;
                            sy = 0; 
                            sx2 = 0; 
                            sy2 = 0; 
                            sxy = 0; 
                        }*/
                    }

//                    if (!fazova || c1 >= maximum)
                    {
                        sx += x2;
                        sy += y2;
                        sx2 += (x + 0.5) * x2;
                        sy2 += (y + 0.5) * y2;
                        sxy += (x + 0.5) * y2;
                    }
                }
            }

            double xa = 0;
            double ya = 0;

            if (n != 0)
            {
                // souradnice stredu - prumerna souradnice
                xa = sx / n;
                ya = sy / n;

                varianceX = (sx2 / n) - (xa * xa);
                varianceY = (sy2 / n) - (ya * ya);
                covariance= (sxy / n) - (xa * ya);

                double pv0 = 1 + varianceX;
                double pv3 = 1 + varianceY;
                double pv1 = covariance;

                double det = pv0 * pv3 - pv1 * pv1;

                double p1 = det == 0 ? 0 : pv3 / det;
                double p2 = det == 0 ? 0 : pv0 / det;
                double p3 = det == 0 ? 0 : -pv1 / det;

                offsetX = (xa * p1 + ya * p3) * LocalMap.Resolution;
                offsetY = (xa * p3 + ya * p2) * LocalMap.Resolution;
  //              offsetX = varianceX<varianceY? maximumAtX * LocalMap.Resolution*0.01:0;
    //            offsetY = varianceX>varianceY? maximumAtY * LocalMap.Resolution * 0.01:0;
            }

            LastResult = new MapCorelatorResult() { Maximum = maximum, MaximumAtX = maximumAtX, MaximumAtY = maximumAtY, OffsetX = offsetX, OffsetY = offsetY, VariaceX = varianceX, VariaceY = varianceY, Covariance=covariance, AvgX= xa, AvgY=ya};
            return LastResult;
        }

        /// <summary>
        /// Kresba mapy
        /// </summary>
        /// <returns></returns>
        public Blob ToLogMessage()
        {
            Blob b = new Blob();
            b.Name = "Map draw";
            b.Height = LocalMap.Height;
            b.Width = LocalMap.Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[LocalMap.Height * LocalMap.Width];

            int xd = LocalMap.Width / 2;
            int yd = LocalMap.Height / 2;

            for (int x = 0; x < LocalMap.Width; x++)
            {
                for (int y = 0; y < LocalMap.Height; y++)
                {
                    //                    Debug.WriteLine("ToLogMessage {0}, {1}", x, y);
                    b.Data[x + y * LocalMap.Width] = map != null ? (byte)(map[x, y].Magnitude*255) : (byte)0;
                }
            }
            return b;
        }
        /// <summary>
        /// Vysledek korelace znormovany na maximum 255.
        /// </summary>
        /// <returns></returns>
        public Blob ToLogMessage2()
        {
            Blob b = new Blob();
            b.Name = "Corelation";
            b.Height = LocalMap.Height;
            b.Width = LocalMap.Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[LocalMap.Height * LocalMap.Width];

            int xd = LocalMap.Width / 2;
            int yd = LocalMap.Height / 2;

            for (int x = 0; x < LocalMap.Width; x++)
            {
                for (int y = 0; y < LocalMap.Height; y++)
                {
                    int x1 = x < xd ? xd + x :x-xd;
                    int y1 = y < yd ? yd + y : y - yd;
                    //                    b.Data[x1 + y1 * LocalMap.Width] = corelation != null ? (byte)(corelation[x, y].Re / LastResult.Maximum * 255) : (byte)0;
                                        b.Data[x1 + y1 * LocalMap.Width] = corelation != null ? (byte)(corelation[x, y].Re* corelation[x, y].Re / (LastResult.Maximum* LastResult.Maximum) * 255) : (byte)0;
                    //                    b.Data[x1 + y1 * LocalMap.Width] = corelation != null && corelation[x, y].Re > LastResult.Maximum*0.9 ? (byte)255 : (byte)0;
                    //                    b.Data[x + y * LocalMap.Width] = (byte)(gause[x, y] *255);
                }
            }
            return b;
        }
        /// <summary>
        /// Korelace
        /// </summary>
        /// <returns></returns>
        public Blob ToLogMessage3()
        {
            Blob b = new Blob();
            b.Name = "Corelation";
            b.Height = LocalMap.Height;
            b.Width = LocalMap.Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = new byte[LocalMap.Height * LocalMap.Width];

            int xd = LocalMap.Width / 2;
            int yd = LocalMap.Height / 2;

            for (int x = 0; x < LocalMap.Width; x++)
            {
                for (int y = 0; y < LocalMap.Height; y++)
                {
                    b.Data[x + y * LocalMap.Width] = localMap != null ? (byte)(localMap[x, y].Re) : (byte)0;
                }
            }
            return b;
        }

    }
}
