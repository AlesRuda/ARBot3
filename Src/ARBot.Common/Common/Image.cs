using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Obrazek s rychlim pristupem
    /// </summary>
    /// <remarks>
    /// Pro rychly pristup se vytvori jeden pixel a nastavuje se mu index=image.Index(x, y), je to vyznamne rychlejsi jak pouzivat indexer, ktery musi alokovat vzdy novy pixel.
    /// Data jsou uchovavana v poli byte Data a je mozne je tak velmi rychle vymnenit za jine.
    /// </remarks>
    /// <summary>
    /// Netypovy zaklad <see cref="Image{T}"/> - umoznuje drzet/predavat obraz bez znalosti
    /// pixel typu (napr. vlastnost <c>Image</c> na <see cref="ARBot.Common.Logs.ImageMsg"/>).
    /// Nese rozmery, surova data a identitu pixelu (<see cref="PixelTypeName"/>).
    /// </summary>
    public abstract class Image
    {
        /// <summary>Sirka [px].</summary>
        public abstract int Width { get; }
        /// <summary>Vyska [px].</summary>
        public abstract int Height { get; }
        /// <summary>Pocet bajtu na pixel.</summary>
        public abstract int Step { get; }
        /// <summary>Surova pixelova data (delka = <see cref="DataLength"/>).</summary>
        public abstract byte[] Data { get; set; }
        /// <summary>Nazev pixel typu (<c>typeof(T).Name</c>, napr. "BGR32", "Gray16") - identita obrazu.</summary>
        public abstract string PixelTypeName { get; }

        /// <summary>Delka dat = Width*Height*Step.</summary>
        public int DataLength => Width * Height * Step;

        /// <summary>
        /// Vytvori <see cref="Image{T}"/> podle nazvu pixel typu (napr. "Gray16"). Slouzi k
        /// rekonstrukci pri deserializaci, kdy pixel typ neni znam staticky. Pixel typy zijou
        /// v namespace <c>ARBot.Common.Common</c>.
        /// </summary>
        public static Image Create(string pixelTypeName, int width, int height)
        {
            Type pixel = typeof(IPixel).Assembly.GetType("ARBot.Common.Common." + pixelTypeName);
            if (pixel == null || !typeof(IPixel).IsAssignableFrom(pixel))
                throw new NotSupportedException($"Neznamy pixel typ '{pixelTypeName}'.");
            Type imgType = typeof(Image<>).MakeGenericType(pixel);
            return (Image)Activator.CreateInstance(imgType, width, height);
        }
    }

    /// <typeparam name="T"></typeparam>
    public class Image<T>: Image, IEnumerable<T>, ICloneable where T : IPixel, new()
    {
        protected int width;
        protected int height;
        protected int step;

        byte[] data;

        T t;

        public override int Step => step;
        public override string PixelTypeName => typeof(T).Name;

        public Image(int width, int height)
        {
            t = new T();
            step = t.Count;
            this.width = width;
            this.height = height;
            data = new byte[DataLength];
            t.Data = data;
        }

        /// <summary>
        /// Hluboka kopie obrazku - stejne rozmery, ale VLASTNI (nezavisla) kopie dat, takze
        /// zmeny v kopii neovlivni original a naopak.
        /// </summary>
        public Image<T> Clone()
        {
            var copy = new Image<T>(width, height);
            copy.Data = (byte[])data.Clone();   // setter overi delku a nastavi i pixel.Data
            return copy;
        }

        object ICloneable.Clone() => Clone();

        //public static Image<BGR> BGRFromBitmap(string fn)
        //{
        //    return BGRFromBitmap(Image.FromFile(fn) as Bitmap);
        //}

        ///// <summary>
        ///// Convert a bitmap to a byte array
        ///// </summary>
        ///// <param name="bitmap">image to convert</param>
        ///// <returns>image as bytes</returns>
        //public static Image<BGR> BGRFromBitmap(Bitmap bitmap)
        //{
        //    //Code excerpted from Microsoft Robotics Studio v1.5
        //    BitmapData raw = null;  //used to get attributes of the image
        //    Image<BGR> image = null;
        //    try
        //    {
        //        //Freeze the image in memory
        //        raw = bitmap.LockBits(
        //            new System.Drawing.Rectangle(0, 0, (int)bitmap.Width, (int)bitmap.Height),
        //            ImageLockMode.ReadOnly,
        //            System.Drawing.Imaging.PixelFormat.Format24bppRgb
        //         );

        //        int size = raw.Height * raw.Stride;
        //        image = new Image<BGR>(raw.Width, raw.Height);

        //            //Copy the image into the byte[]
        //            System.Runtime.InteropServices.Marshal.Copy(raw.Scan0, image.Data, 0, size);
        //    }
        //    finally
        //    {
        //        if (raw != null)
        //        {
        //            //Unfreeze the memory for the image
        //            bitmap.UnlockBits(raw);
        //        }
        //    }
        //    return image;
        //}

        ///// <summary>
        ///// Convert a bitmap to a byte array
        ///// </summary>
        ///// <param name="bitmap">image to convert</param>
        ///// <returns>image as bytes</returns>
        //public static Image<BGR32> BGR32FromBitmap(Bitmap bitmap)
        //{
        //    //Code excerpted from Microsoft Robotics Studio v1.5
        //    BitmapData raw = null;  //used to get attributes of the image
        //    Image<BGR32> image = null;
        //    try
        //    {
        //        //Freeze the image in memory
        //        raw = bitmap.LockBits(
        //            new System.Drawing.Rectangle(0, 0, (int)bitmap.Width, (int)bitmap.Height),
        //            ImageLockMode.ReadOnly,
        //            System.Drawing.Imaging.PixelFormat.Format32bppRgb
        //         );

        //        int size = raw.Height * raw.Stride;
        //        image = new Image<BGR32>(raw.Width, raw.Height);

        //        //Copy the image into the byte[]
        //        System.Runtime.InteropServices.Marshal.Copy(raw.Scan0, image.Data, 0, size);
        //    }
        //    finally
        //    {
        //        if (raw != null)
        //        {
        //            //Unfreeze the memory for the image
        //            bitmap.UnlockBits(raw);
        //        }
        //    }
        //    return image;
        //}
        //public static Image<BGR32> BGR32FromBitmap(string fn)
        //{
        //    return BGR32FromBitmap(Image.FromFile(fn) as Bitmap);
        //}

        public override int Width
        {
            get
            {
                return width;
            }
        }
        public override int Height
        {
            get
            {
                return height;
            }
        }

        public override byte[] Data
        {
            get
            {
                return data;
            }
            set
            {
                if (DataLength != value.Length)
                    throw new Exception("Wrong length.");
                data = value;
                t.Data = data;
            }
        }

        public int Index(int x, int y)
        {
            return step * (x + y * width);
        }
        /// <summary>
        /// Zpristupnuje pixel podle souradnic.
        /// Rychlejsi je vytvorit si instanci pixelu, nastavit mu Data na image.Data.
        /// Pak pro pristup k urcitemu pixelu nastavit pixel.Index=image.Index(x, y).
        /// Bacha vraci stale stejnou instanci pixelu, ale mneni v ni Index.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public T this[int x, int y]
        {
            get
            {
                x = Math.Max(0, Math.Min(x, Width - 1));
                y = Math.Max(0, Math.Min(y, Height - 1));
                t.Index = Index(x, y);
                return t;
            }
        }


        /// <summary>
        /// Konvertuje kazdy pixel pomoci funkce cnv do noveho obrazku.
        /// </summary>
        /// <typeparam name="TDest"></typeparam>
        /// <param name="cnv"></param>
        /// <returns></returns>
        public Image<TDest> ConvertTo<TDest>(Action<T, TDest> cnv) where TDest : IPixel, new()
        {
            Image<TDest> i = new Image<TDest>(width, height);

            T srcPixel = new T();
            srcPixel.Data = Data;
            srcPixel.Index = 0;
            TDest destPixel = new TDest();
            destPixel.Data = i.Data;
            destPixel.Index = 0;

            for (int idx = 0; idx < width * height; idx++)
            {
                cnv(srcPixel, destPixel);

                srcPixel.Index += step;
                destPixel.Index += i.step;
            }

            return i;
        }

        /// <summary>
        /// Konvertuje kazdy pixel pomoci funkce cnv do noveho obrazku.
        /// </summary>
        /// <typeparam name="TDest"></typeparam>
        /// <param name="cnv"></param>
        /// <returns></returns>
        public void ForEach(Action<int, int, T> cnv)
        {
            T srcPixel = new T();
            srcPixel.Data = Data;
            srcPixel.Index = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    cnv(x, y, srcPixel);

                    srcPixel.Index += step;
                }
            }
        }

        /// <summary>
        /// Konvertuje kazdy pixel pomoci funkce cnv do noveho obrazku.
        /// </summary>
        /// <typeparam name="TDest"></typeparam>
        /// <param name="cnv"></param>
        /// <returns></returns>
        public void ForEach(int x1, int y1, int x2, int y2, Action<int, int, T> cnv)
        {
            T srcPixel = new T();
            srcPixel.Data = Data;
            srcPixel.Index = 0;

            for (int y = y1; y < y2; y++)
            {
                for (int x = x1; x < x2; x++)
                {
                    srcPixel.Index = Index(x, y);
                    cnv(x, y, srcPixel);
                }
            }
        }

        /// <summary>
        /// Resize z obrazku from do this instance
        /// </summary>
        /// <param name="from"></param>
        /// <returns></returns>
        public void Resize(Image<T> from)
        {

            T srcPixel = new T();
            srcPixel.Data = from.Data;
            srcPixel.Index = 0;

            double scaleX = (double)from.Width / (double)Width;
            double scaleY = (double)from.Height / (double)Height;

            ForEach((x, y, p) =>
            {
                srcPixel.Index = from.Index((int)((double)x * scaleX), (int)((double)y * scaleY));
                p.Values = srcPixel.Values;
            });
        }


        ///// <summary>
        ///// BitmapSource z Gray16 obrazku.
        ///// </summary>
        ///// <param name="i"></param>
        ///// <returns></returns>
        //public static WriteableBitmap FromGray16(Image<Gray16> i, double scale)
        //{
        //    Gray16 p = new Gray16();
        //    p.Data = i.Data;
        //    WriteableBitmap w = new WriteableBitmap(i.Width, i.Height, 96, 96, PixelFormats.Bgra32, null);
        //    byte[] b = new byte[i.Width * i.Height * 4];
        //    int idx = 0;
        //    for (int y = 0; y < i.Height; y++)
        //    {
        //        for (int x = 0; x < i.Width; x++)
        //        {
        //            p.Index = i.Index(x, y);
        //            byte b1 = (byte)(p.Value * scale);
        //            b[idx++] = b1;
        //            b[idx++] = b1;
        //            b[idx++] = b1;
        //            b[idx++] = 255;
        //        }
        //    }
        //    w.WritePixels(new Int32Rect(0, 0, i.Width, i.Height), b, i.Width * 4, 0);
        //    return w;
        //}
        ///// <summary>
        ///// Vraci bitmap source z obrazku.
        ///// </summary>
        ///// <returns></returns>
        //public BitmapSource ToBitmapSource()
        //{
        //    if (this is Image<Gray16>)
        //        return FromGray16(this as Image<Gray16>, 0.05);
        //    else
        //    {
        //        if (Width == 0 && Height == 0)
        //            return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[] { 0, 0, 0}, 3);
        //        IPixel p = new T();
        //        return BitmapSource.Create(Width, Height, 96, 96, p.Format, null, Data, p.Count * Width);
        //    }
        //}

        /// <summary>
        /// Vraci masku pruhlednosti z probability 
        /// </summary>
        /// <returns></returns>
        //public BitmapSource ToMask()
        //{
        //    if (this is Image<Gray>)
        //    {
        //        byte[] d = new byte[Data.Length*4];
        //        for(int i=0;i<Data.Length;i++)
        //            d[4 * i + 3] = Data[i];
        //        return BitmapSource.Create(Width, Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, d, 4 * Width);
        //    }
        //    else
        //    {
        //        return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[] { 0, 0, 0 }, 3);
        //    }
        //}

        /// <summary>
        /// pravdepodobnost sjizdnosti pocitana z 3x3
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static Image<Gray> MulProbability(Image<Gray> i)
        {
            Gray p = new Gray();
            p.Data = i.Data;
            var res = new Image<Gray>(i.Width, i.Height);
            Gray pRes = new Gray();
            pRes.Data = res.Data;
            for (int y = 0; y < i.Height; y++)
            {
                for (int x = 0; x < i.Width; x++)
                {
                    double sum = 255;
                    for (int y1 = -1; y1 < 2; y1++)
                    {
                        for (int x1 = -1; x1 < 2; x1++)
                        {
                            int x2 = x + x1;
                            int y2 = y + y1;
                            if (x2 >= 0 && x2 < i.Width && y2 >= 0 && y2 < i.Height)
                            {
                                p.Index = i.Index(x2, y2);
                                sum *= p.Value;
                                sum /= 255;
                            }
                        }
                    }
                    pRes.Index = res.Index(x, y);
                    pRes.Value = (byte)sum;
                }
            }
            return res;
        }

        /// <summary>
        /// Integralni obraz
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public static Image<Gray32> IntegralImage(Image<Gray> i)
        {
            var p = new Gray();
            p.Data = i.Data;
            var res = new Image<Gray32>(i.Width, i.Height);
            var pRes = new Gray32();
            pRes.Data = res.Data;
            for (int y = 0; y < i.Height; y++)
            {
                for (int x = 0; x < i.Width; x++)
                {
                    Int32 sum = 0;

                    p.Index = i.Index(x, y);
                    sum = p.Value;

                    if (x - 1 >= 0)
                    {
                        pRes.Index = res.Index(x - 1, y);
                        sum += pRes.Value;
                    }

                    if (y - 1 >= 0)
                    {
                        pRes.Index = res.Index(x, y-1);
                        sum += pRes.Value;
                    }

                    if (x - 1 >= 0 && y - 1 >= 0)
                    {
                        pRes.Index = res.Index(x-1, y-1);
                        sum -= pRes.Value;
                    }

                    pRes.Index = res.Index(x, y);
                    pRes.Value = sum;
                }
            }
            return res;
        }

        public static Int32 Sum(Image<Gray32> i, int fromX, int fromY, int toX, int toY)
        {
            var p = new Gray32();
            p.Data = i.Data;
            p.Index = i.Index(toX, toY);
            int sum = p.Value;
            p.Index = i.Index(toX, fromY);
            sum -= p.Value;
            p.Index = i.Index(fromX, toY);
            sum -= p.Value;
            p.Index = i.Index(fromX, fromY);
            sum += p.Value;
            return sum;
        }

        /// <summary>
        /// 2d pravdepodobnostni index hrany
        /// </summary>
        /// <param name="i">integralni obraz pravdepodobnosti sjizdnosti</param>
        /// <returns>P[x, y]=(x*y-I[x, y])+(I[w-1, h-1]+I[x, y]-I[w-1, y]-I[x, h-1])</returns>
        public static Image<Gray> Hrany(Image<Gray32> i, int r)
        {
            int tl = 64 * r * r;
            int th = 192 * r * r;
            float s = 255 * r * r;
            float[,] res = new float[i.Width, i.Height];
            float v;

            for (int y = r; y < i.Height-r; y++)
            {
                for (int x = r; x < i.Width-r; x++)
                {
                    int s1 = Sum(i, x - r, y - r, x, y);
                    int s2 = Sum(i, x, y - r, x+r, y);
                    int s3 = Sum(i, x, y, x+r, y+r);
                    int s4 = Sum(i, x-r, y, x, y+r);
                    v = 0;
                    if (s1 < tl && s3>th)
                        v = (s - s1 + s3) / s;
                    if (s1 > th && s3 < tl)
                        v = (s + s1 - s3) / s;

                    if (s2 < tl && s4 > th)
                        v = Math.Max(v, (s - s2 + s4) / s);
                    if (s2 > th && s4 < tl)
                        v = Math.Max(v, (s + s2 - s4) / s);
                    res[x, y] = v;
                }
            }

            var res2 = new Image<Gray>(i.Width, i.Height);
            var pRes2 = new Gray();
            pRes2.Data = res2.Data;

            float minv;
            float maxv;
            float v1;
            for (int y = 1; y < i.Height - 1; y++)
            {
                for (int x = 1; x < i.Width - 1; x++)
                {
                    v = res[x, y];
                    v1 = res[x - 1, y];
                    minv=maxv = v1;
                    v1 = res[x + 1, y];
                    minv = Math.Min(minv, v1);
                    maxv = Math.Max(maxv, v1);
                    v1 = res[x , y-1];
                    minv = Math.Min(minv, v1);
                    maxv = Math.Max(maxv, v1);
                    v1 = res[x , y+1];
                    minv = Math.Min(minv, v1);
                    maxv = Math.Max(maxv, v1);


                    if (minv<v && maxv<=v)
                    {
                        pRes2.Index = res2.Index(x, y);
                        pRes2.Value = 255;
                    }
                    //                    pRes2.Value = (byte)res[x, y];
                }
            }

            return res2;
        }


        public Image<RGB> ToRGBImage()
        {
            return ConvertTo<RGB>((p, rgb) =>
                {
                    Gray g = p as Gray;
                    if (g != null)
                    {
                        byte v = g.Value;
                        if (v < 128)
                            rgb.R = v;
                        else
                            rgb.G = v;
                    }
                    Gray16 g16 = p as Gray16;
                    if (g16 != null)
                    {
                        var v = (byte)(g16.Value*0.05);
                        rgb.R = v;
                        rgb.G = v;
                        rgb.B= v;
                    }
                });
        }
        public void Plot(IEnumerable<Point2D> points, double minx, double maxx, double miny, double maxy, Action<int, int> setter)
        {
            DrawEngine de = new DrawEngine() { XMin = 0, XMax = width - 1, YMin = 0, YMax = height-1, Clipping = true };
            de.PixelSetter = setter;

            foreach (var v in points)
            {
                int x = (int)(Width * (v.X - minx) / (maxx - minx));
                int y = (int)(Height * (v.Y - miny) / (maxy - miny));
                de.Line(new Point(x, 0), new Point(x, y));
            }
        }
        public void PlotXY(IEnumerable<Point2D> points, double minx, double maxx, double miny, double maxy, int radius, Action<int, int> setter)
        {
            DrawEngine de = new DrawEngine() { XMin = 0, XMax = width - 1, YMin = 0, YMax = height-1, Clipping = true };
            de.PixelSetter = setter;
            foreach (var v in points)
            {
                int x = (int)(Width * (v.X - minx) / (maxx - minx));
                int y = (int)(Height * (v.Y - miny) / (maxy - miny));
                de.FillCircle(new Point(x, y), radius);
            }
        }

        public void PlotLineXY(IEnumerable<Point2D> points, double minx, double maxx, double miny, double maxy, Action<int, int> setter)
        {
            DrawEngine de = new DrawEngine() { XMin = 0, XMax = width - 1, YMin = 0, YMax = height - 1, Clipping = true };
            de.PixelSetter = setter;
            de.PolyLine(points.Select(i => new Point((int)(Width * (i.X - minx) / (maxx - minx)), (int)(Height * (i.Y - miny) / (maxy - miny)))).ToArray());
        }

        public static Image<BGR> Plot(int width, int height, IEnumerable<Point2D> points, double minx, double maxx)
        {
            var i = new Image<BGR>(width, height);
            if(points.Any())
                i.Plot(points, minx, maxx, points.Min(v => v.Y), points.Max(v => v.Y), (x, y) => i[x, y].R = 255);
            return i;
        }

        public IEnumerator<T> GetEnumerator()
        {
            T srcPixel = new T();
            srcPixel.Data = Data;
            srcPixel.Index = 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    yield return srcPixel;

                    srcPixel.Index += step;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
