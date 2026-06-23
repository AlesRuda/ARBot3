using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Obrazek s rychlim pristupem
    /// </summary>
    /// <remarks>
    /// Pro rychly pristup se vytvori jeden pixel a nastavuje se mu index=image.Index(x, y), je to vyznamne rychlejsi jak pouzivat indexer, ktery musi alokovat vzdy novy pixel.
    /// Data jsou uchovavana v poli byte Data a je mozne je tak velmi rychle vymnenit za jine.
    /// Priznak SharedData indikuje ze data jsou sdilena a mohou byt mnenena nezavisle na obrazku.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public class VirtualizedImage<T>: Object where T : struct
    {
        protected int minX = 0;
        protected int minY = 0;
        protected int maxX = 0;
        protected int maxY = 0;

        protected int tileWidth=32;
        protected int tileHeight=32;


        Dictionary<Point, T?[,]> data;

        T?[,] currentTile;
        Point currentTilePoint;

        public VirtualizedImage(int tileWidth, int tileHeight)
        {
            this.tileWidth = tileWidth;
            this.tileHeight = tileHeight;
            data = new Dictionary<Point, T?[,]>();
            currentTilePoint = new Point(0, 0);
            currentTile = new T?[tileWidth, tileHeight];
            data.Add(currentTilePoint, currentTile);
        }


        public Int32Rect Bounds
        {
            get
            {
                return new Int32Rect(minX, minY, maxX - minX, maxY - minY);
            }
        }

        public Point TileIndex(int x, int y)
        {
            int x1 = x % tileWidth;
            if (x1 < 0)
                x = x - x1 - tileWidth;
            else
                x = x - x1;

            int y1 = y % tileWidth;
            if (y1 < 0)
                y = y - y1 - tileHeight;
            else
                y = y - y1;

            return new Point(x, y);
        }

        public T? this[int x, int y]
        {
            get
            {
                var ti = TileIndex(x, y);
                if(!ti.Equals(currentTilePoint))
                {
                    T?[,] ct;
                    if (!data.TryGetValue(ti, out ct))
                        return null;
                    currentTile = ct;
                    currentTilePoint = ti;
                }
                return currentTile[x - currentTilePoint.X, y - currentTilePoint.Y];
            }
            set
            {
                var ti = TileIndex(x, y);
                if (!ti.Equals(currentTilePoint))
                {
                    if (!data.TryGetValue(ti, out currentTile))
                    {
                        currentTile = new T?[tileWidth, tileHeight];
                        data.Add(ti, currentTile);
                    }
                    currentTilePoint = ti;
                }
                currentTile[x - currentTilePoint.X, y - currentTilePoint.Y]=value;
            }
        }

        /// <summary>
        /// Konvertuje na obrazek.
        /// </summary>
        /// <typeparam name="TPixel">Typ ciloveho pixelu</typeparam>
        /// <param name="r">Misto vyrezu</param>
        /// <param name="cnv">Convertuje T na pixel</param>
        /// <returns></returns>
        public Image<TPixel> ToImage<TPixel>(Int32Rect r, Action<T?, TPixel> cnv) where TPixel : IPixel, new()
        {
            int w = r.Width;
            int h = r.Height;

            Image<TPixel> i = new Image<TPixel>(w, h);

            TPixel destPixel = new TPixel();
            destPixel.Data = i.Data;
            destPixel.Index = 0;

            for (int y = 0, y1=r.Y; y < h; y++, y1++)
            {
                for (int x = 0, x1 = r.X; x < w; x++, x1++)
                {
                    cnv(this[x1, y1], destPixel);

                    destPixel.Index += i.Step;
                }
            }

            return i;
        }

        /// <summary>
        /// Promita obrazek do virtualniho pole
        /// </summary>
        /// <typeparam name="TPixel">Typ ciloveho pixelu</typeparam>
        /// <param name="img">Promitany obrazek</param>
        /// <param name="resolution">Rozliseni v pixelech na metr.</param>
        /// <param name="cp">Projekce</param>
        /// <param name="cnv">Convertuje TPixel na T</param>
        /// <returns></returns>
        public void Project<TPixel>(Image<TPixel> img, double resolution, double diameter, CameraProjection cp, Func<TPixel, T> cnv) where TPixel : IPixel, new()
        {
            int w = img.Width;
            int h = img.Height;
            int w2 = img.Width/2;
            int h2 = img.Height/2;


            TPixel src = new TPixel();
            src.Data = img.Data;
            src.Index = 0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double x1 = 0, y1 = 0;
                    if (cp.TransformBack(x, y, ref x1, ref y1))
                    {

                        if (Math.Abs(cp.offset.X - x1) < diameter && Math.Abs(cp.offset.Y - y1) < diameter)
                        {
                            int xd = (int)(x1 * resolution);
                            int yd = (int)(-y1 * resolution);
                            this[xd, yd] = cnv(src);
                        }
                    }
                    src.Index += img.Step;
                }
            }
        }
    }
}
