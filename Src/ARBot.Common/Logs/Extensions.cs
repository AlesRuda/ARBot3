using ARBot.Common.Common;
using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ARBot.Common.Logs
{
    public static partial class Extensions
    {
        public static Common.Rectangle Border(this List<Point2D> ss)
        {
            double xmin = ss[0].X;
            double ymin = ss[0].Y;
            double xmax = ss[0].X;
            double ymax = ss[0].Y;

            Point2D p;
            double x;
            double y;

            for (int i = 0; i < ss.Count; i++)
            {
                p = ss[i];
                x = p.X;
                y = p.Y;

                if (x < xmin)
                    xmin = x;
                else if (x > xmax)
                    xmax = x;

                if (y < ymin)
                    ymin = y;
                else if (y > ymax)
                    ymax = y;
            }

            return new Common.Rectangle(xmin, ymin, xmax, ymax);
        }

        //public static System.Windows.Media.Color GetPixelColor(this BitmapSource bitmap, int x, int y)
        //{
        //    System.Windows.Media.Color color;
        //    var bytesPerPixel = (bitmap.Format.BitsPerPixel + 7) / 8;
        //    var bytes = new byte[bytesPerPixel];
        //    var rect = new Int32Rect(x, y, 1, 1);

        //    if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height)
        //        return System.Windows.Media.Colors.Black;

        //    bitmap.CopyPixels(rect, bytes, bytesPerPixel, 0);

        //    if (bitmap.Format == PixelFormats.Pbgra32)
        //    {
        //        color = System.Windows.Media.Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
        //    }
        //    else if (bitmap.Format == PixelFormats.Bgr32)
        //    {
        //        color = System.Windows.Media.Color.FromArgb(0xFF, bytes[2], bytes[1], bytes[0]);
        //    }
        //    else if (bitmap.Format == PixelFormats.Bgra32)
        //    {
        //        color = System.Windows.Media.Color.FromArgb(0xFF, bytes[2], bytes[1], bytes[0]);
        //    }
        //    // handle other required formats
        //    else
        //    {
        //        color = System.Windows.Media.Colors.Black;
        //    }

        //    return color;
        //}
        //public static Bitmap ToBitmap(this Blob b)
        //{
        //    switch (b.Type)
        //    {
        //        case Blob.BlobType.Jpeg:
        //            return System.Drawing.Bitmap.FromStream(new MemoryStream(b.Data)) as System.Drawing.Bitmap;
        //        case Blob.BlobType.BGR:
        //            {
        //                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //                // lock destination bitmap data
        //                System.Drawing.Imaging.BitmapData dstData = bitmap.LockBits(
        //                    new System.Drawing.Rectangle(0, 0, b.Width, b.Height),
        //                    System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //                int dstStride = dstData.Stride;

        //                IntPtr dst = dstData.Scan0;
        //                int idx = 0;
        //                int stride = 3 * b.Width;

        //                if (stride != dstStride)
        //                {
        //                    // copy image
        //                    for (int y = 0; y < b.Height; y++)
        //                    {
        //                        System.Runtime.InteropServices.Marshal.Copy(b.Data, idx, dst, stride);

        //                        dst += dstStride;
        //                        idx += stride;
        //                    }
        //                }
        //                else
        //                {
        //                    System.Runtime.InteropServices.Marshal.Copy(b.Data, 0, dst, stride * b.Height);
        //                }

        //                // unlock destination images
        //                bitmap.UnlockBits(dstData);
        //                return bitmap;
        //            }
        //        case Blob.BlobType.BGR32:
        //            {
        //                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

        //                // lock destination bitmap data
        //                System.Drawing.Imaging.BitmapData dstData = bitmap.LockBits(
        //                    new System.Drawing.Rectangle(0, 0, b.Width, b.Height),
        //                    System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

        //                int dstStride = dstData.Stride;

        //                IntPtr dst = dstData.Scan0;
        //                int idx = 0;
        //                int stride = 4 * b.Width;

        //                if (stride != dstStride)
        //                {
        //                    // copy image
        //                    for (int y = 0; y < b.Height; y++)
        //                    {
        //                        System.Runtime.InteropServices.Marshal.Copy(b.Data, idx, dst, stride);

        //                        dst += dstStride;
        //                        idx += stride;
        //                    }
        //                }
        //                else
        //                {
        //                    System.Runtime.InteropServices.Marshal.Copy(b.Data, 0, dst, stride * b.Height);
        //                }

        //                // unlock destination images
        //                bitmap.UnlockBits(dstData);
        //                return bitmap;
        //            }
        //        case Blob.BlobType.Probability:
        //            {
        //                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //                // lock destination bitmap data
        //                System.Drawing.Imaging.BitmapData dstData = bitmap.LockBits(
        //                    new System.Drawing.Rectangle(0, 0, b.Width, b.Height),
        //                    System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //                int dstStride = dstData.Stride;

        //                IntPtr dst = dstData.Scan0;
        //                int idx = 0;
        //                int stride = b.Width;

        //                for (int y = 0; y < b.Height; y++)
        //                {
        //                    byte[] bytes = new byte[dstStride];
        //                    for (int x = 0; x < b.Width; x++)
        //                        bytes[3 * x] = bytes[3 * x + 1] = bytes[3 * x + 2] = b.Data[idx + x];

        //                    System.Runtime.InteropServices.Marshal.Copy(bytes, 0, dst, dstStride);

        //                    dst += dstStride;
        //                    idx += stride;
        //                }

        //                // unlock destination images
        //                bitmap.UnlockBits(dstData);
        //                return bitmap;
        //            }
        //        case Blob.BlobType.Gray16:
        //            {
        //                System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //                // lock destination bitmap data
        //                System.Drawing.Imaging.BitmapData dstData = bitmap.LockBits(
        //                    new System.Drawing.Rectangle(0, 0, b.Width, b.Height),
        //                    System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        //                int dstStride = dstData.Stride;

        //                IntPtr dst = dstData.Scan0;
        //                int idx = 0;
        //                int stride = b.Width * 2;

        //                for (int y = 0; y < b.Height; y++)
        //                {
        //                    byte[] bytes = new byte[dstStride];
        //                    for (int x = 0; x < b.Width; x++)
        //                    {
        //                        bytes[3 * x] = bytes[3 * x + 1] = bytes[3 * x + 2] = (byte)((b.Data[idx + x * 2] + 256 * b.Data[idx + x * 2 + 1]) / 32);
        //                    }

        //                    System.Runtime.InteropServices.Marshal.Copy(bytes, 0, dst, dstStride);

        //                    dst += dstStride;
        //                    idx += stride;
        //                }

        //                // unlock destination images
        //                bitmap.UnlockBits(dstData);
        //                return bitmap;
        //            }
        //    }

        //    return null;
        //}

//        public static Func<int, int, System.Windows.Media.Color> GetConv(this Blob b)
//        {
//            switch (b.Type)
//            {
//                case Blob.BlobType.RGB:
//                    return (x, y) =>
//                    {
//                        int idx = 3 * (x + y * b.Width);
//                        return System.Windows.Media.Color.FromArgb(255, b.Data[idx], b.Data[idx + 1], b.Data[idx + 2]);
//                    };
//                case Blob.BlobType.BGR:
//                    return (x, y) =>
//                    {
//                        int idx = 3 * (x + y * b.Width);
//                        return System.Windows.Media.Color.FromArgb(255, b.Data[idx + 2], b.Data[idx + 1], b.Data[idx]);
//                    };
//                case Blob.BlobType.Gray:
//                    return (x, y) =>
//                    {
//                        int idx = (x + y * b.Width);
//                        return System.Windows.Media.Color.FromArgb(255, b.Data[idx], b.Data[idx], b.Data[idx]);
//                    };
//                case Blob.BlobType.Gray16:
///*                    int m = int.MinValue;
//                    for(int i=0;i<b.Data.Length;i+=2)
//                        m=Math.Max(m, b.Data[i] + b.Data[i + 1] * 256);
//                    return (x, y) =>
//                    {
//                        int i = (x + y * b.Width)*2;
//                        byte v = (byte)(255 * (b.Data[i] + b.Data[i + 1] * 256) / m);
//                        return System.Windows.Media.Color.FromArgb(255, v, v, v);
//                    };*/
//                    int m = int.MinValue;
//                    for (int i = 0; i < b.Data.Length; i += 2)
//                        m = Math.Max(m, b.Data[i] + b.Data[i + 1] * 256);
//                    var lm = Math.Sqrt(m);
//                    return (x, y) =>
//                    {
//                        int i = (x + y * b.Width) * 2;
//                        byte v = (byte)(255 * Math.Sqrt(b.Data[i] + b.Data[i + 1] * 256) / lm);
//                        return System.Windows.Media.Color.FromArgb(255, v, v, v);
//                    };
//                case Blob.BlobType.HSV:
//                    return (x, y) =>
//                    {
//                        int idx = 3 * (x + y * b.Width);

//                        byte H, S, V;

//                        H = b.Data[idx + 2];
//                        S = b.Data[idx + 1];
//                        V = b.Data[idx];

//                        int hi = (H / 40) % 6;
//                        int f = H % 40;

//                        int p = V * (255 - S) / 256;
//                        int q = V * (255 - f * S / 40) / 256;
//                        int t = V * (255 - (40 - f) * S / 40) / 256;

//                        if (hi == 0)
//                            return System.Windows.Media.Color.FromArgb(255, V, (byte)t, (byte)p);
//                        else if (hi == 1)
//                            return System.Windows.Media.Color.FromArgb(255, (byte)q, V, (byte)p);
//                        else if (hi == 2)
//                            return System.Windows.Media.Color.FromArgb(255, (byte)p, V, (byte)t);
//                        else if (hi == 3)
//                            return System.Windows.Media.Color.FromArgb(255, (byte)p, (byte)q, V);
//                        else if (hi == 4)
//                            return System.Windows.Media.Color.FromArgb(255, (byte)t, (byte)p, V);
//                        else
//                            return System.Windows.Media.Color.FromArgb(255, V, (byte)p, (byte)q);
//                    };
//                case Blob.BlobType.UVYV:
//                    return (x, y) =>
//                    {
//                        int idx = 2 * (x + y * b.Width);

//                        int Y = b.Data[idx + 1];
//                        int U = b.Data[idx & 0xfffffffffc];
//                        int V = b.Data[(idx & 0xfffffffffc) + 2];

//                        int R = Math.Max(Math.Min((9535 * (Y - 16) + 13074 * (V - 128)) >> 13, 255), 0);
//                        int G = Math.Max(Math.Min((9535 * (Y - 16) - 6660 * (V - 128) - 3203 * (U - 128)) >> 13, 255), 0);
//                        int B = Math.Max(Math.Min((9535 * (Y - 16) + 16531 * (U - 128)) >> 13, 255), 0);
//                        return System.Windows.Media.Color.FromRgb((byte)R, (byte)G, (byte)B);
//                    };
//                case Blob.BlobType.Probability:
//                    return (x, y) =>
//                    {
//                        int idx = (x + y * b.Width);
//                        byte b1 = b.Data[idx];
//                        if (b1 > 128)
//                            return System.Windows.Media.Color.FromArgb(255, 0, b1, 0);
//                        else
//                            return System.Windows.Media.Color.FromArgb(255, b1, 0, 0);
//                    };
//                case Blob.BlobType.Fract16:
//                    return (x, y) =>
//                    {
//                        int idx = 2 * (x + y * b.Width);
//                        int v = b.Data[idx + 1];
//                        return System.Windows.Media.Color.FromArgb(255, 0, (byte)v, 0);
//                    };
//            }
//            return null;
//        }

//        public static WriteableBitmap ToBitmapSource(this Blob blob, Func<int, int, System.Windows.Media.Color> conv)
//        {
//            if(blob.Width==0 && blob.Height==0)
//                return new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
//            WriteableBitmap w = new WriteableBitmap(blob.Width, blob.Height, 96, 96, PixelFormats.Bgra32, null);
//            byte[] b = new byte[blob.Width * blob.Height * 4];
//            int i = 0;
//            for (int y = 0; y < blob.Height; y++)
//            {
//                for (int x = 0; x < blob.Width; x++)
//                {
//                    System.Windows.Media.Color c = conv(x, y);
//                    b[i++] = c.B;
//                    b[i++] = c.G;
//                    b[i++] = c.R;
//                    b[i++] = 255;
//                }
//            }

//            w.WritePixels(new Int32Rect(0, 0, blob.Width, blob.Height), b, blob.Width * 4, 0);

//            return w;

//        }

//        public static BitmapSource GetBitmapSource(this Blob b)
//        {
//            switch (b.Type)
//            {
//                case Blob.BlobType.Jpeg:
//                    return new ImageSourceConverter().ConvertFrom(b.Data) as BitmapSource;
//                default:
//                    Func<int, int, System.Windows.Media.Color> f = b.GetConv();
//                    if (f != null)
//                        return b.ToBitmapSource(f);
//                    break;
//            }
//            return null;
//        }


//        public static Bitmap ToBitmap(this Image<BGR> b)
//        {
//            if (b.Width == 0 || b.Height == 0)
//                return new Bitmap(1, 1);
//            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

//            // lock destination bitmap data
//            System.Drawing.Imaging.BitmapData dstData = bitmap.LockBits(
//                new System.Drawing.Rectangle(0, 0, b.Width, b.Height),
//                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

//            int dstStride = dstData.Stride;

//            IntPtr dst = dstData.Scan0;
//            int idx = 0;
//            int stride = 3 * b.Width;

//            if (stride != dstStride)
//            {
//                // copy image
//                for (int y = 0; y < b.Height; y++)
//                {
//                    System.Runtime.InteropServices.Marshal.Copy(b.Data, idx, dst, stride);

//                    dst += dstStride;
//                    idx += stride;
//                }
//            }
//            else
//            {
//                System.Runtime.InteropServices.Marshal.Copy(b.Data, 0, dst, stride * b.Height);
//            }

//            // unlock destination images
//            bitmap.UnlockBits(dstData);
//            return bitmap;
//        }

//        public static Bitmap ToBitmap(this Image<BGR32> b)
//        {
//            if (b.Width == 0 || b.Height == 0)
//                return new Bitmap(1, 1);
//            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(b.Width, b.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

//            // lock destination bitmap data
//            System.Drawing.Imaging.BitmapData dstData = bitmap.LockBits(
//                new System.Drawing.Rectangle(0, 0, b.Width, b.Height),
//                System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

//            int dstStride = dstData.Stride;

//            IntPtr dst = dstData.Scan0;
//            int idx = 0;
//            int stride = 4 * b.Width;

//            if (stride != dstStride)
//            {
//                // copy image
//                for (int y = 0; y < b.Height; y++)
//                {
//                    System.Runtime.InteropServices.Marshal.Copy(b.Data, idx, dst, stride);

//                    dst += dstStride;
//                    idx += stride;
//                }
//            }
//            else
//            {
//                System.Runtime.InteropServices.Marshal.Copy(b.Data, 0, dst, stride * b.Height);
//            }

//            // unlock destination images
//            bitmap.UnlockBits(dstData);
//            return bitmap;
//        }


        /// <summary>
        /// Nad polem bodu spocte linearni regresi
        /// x=a*y+b;
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static Line2D LinearRegesion(this IEnumerable<Point2D> points)
        {
            double x, y;
            double n = 0;
            double sxy = 0;
            double sx = 0;
            double sy = 0;
            double sx2 = 0;
            double sy2 = 0;
            foreach (var point in points)
            {
                x = point.X;
                y = point.Y;

                n += 1;

                sxy += x * y;
                sx += x;
                sy += y;
                sx2 += x * x;
                sy2 += y * y;
            }
            if (n == 0)
                return null;
            double dx = (n * sx2 - sx * sx);
            double dy = (n * sy2 - sy * sy);

            if (Math.Abs(dx) > Math.Abs(dy))
            {
                if (dx == 0)
                    return null;
                return new Line2D(-(n * sxy - sx * sy) / dx, 1, -(sx2 * sy - sx * sxy) / dx);
            }
            else
            {
                if (dy == 0)
                    return null;
                return new Line2D(-1, (n * sxy - sx * sy) / dy, (sy2 * sx - sy * sxy) / dy);
            }
        }

        /// <summary>
        /// Hleda hranice cesty
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public static IEnumerable<PathEdge> PathEdges(this Image<Gray> image)
        {
            List<PathEdge> l = new List<PathEdge>();

            Gray left = new Gray();
            left.Data = image.Data;

            Gray right = new Gray();
            right.Data = image.Data;

            int sumMax;
            int sumMin;
            int min;
            int max;
            int mini;
            int maxi;
            int x;
            int x1;
            int w = image.Width - 1;
            int sum;

            for (int y= image.Height-1; y>=0;y--)
            {
                left.Index = image.Index(0, y);
                right.Index = image.Index(x1 = w, y);

                sumMax = 0;
                sumMin = 0;
                min = int.MinValue;
                max = int.MinValue;

                mini = w;
                maxi = 0;
                sum = 0;

                for (x = 0; x <= w; x++, x1--)
                {
                    sum += left.Value > 128 ? 1 : 0;
                    sumMax += 128 - left.Value;
                    sumMin += 128 - right.Value;
                    left.Index += left.Count;
                    right.Index -= right.Count;
                    if (min < sumMin && sumMin>0)
                    {
                        min = sumMin;
                        mini = x1;
                    }
                    if (max < sumMax && sumMax>0)
                    {
                        max = sumMax;
                        maxi = x;
                    }
                }
                sum = 100 * sum / (w+1);
                if ((maxi != 0 || mini != w) && (sum<90 && sum>10))
                {
                    if(maxi>mini)
                    {
                        if (min != max)
                        {
                            if (min < max)
                                l.Add(new PathEdge() { Y = y, Left = maxi != 0 ? maxi : (int?)null, Right = null });
                            else
                                l.Add(new PathEdge() { Y = y, Left = null, Right = mini != w ? mini : (int?)null });
                        }
                    }
                    else
                        l.Add(new PathEdge() { Y = y, Left = maxi != 0 ? maxi : (int?)null, Right = mini != w ? mini : (int?)null });
                }
/*                if (maxi == 0 && mini == imgWidth - 1)
                    return -1;
                if (maxi != mini)
                    return (maxi + mini) / 2;
                if (maxi == mini)
                {
                    if (Src[mini] >= 128)
                        return mini;
                }
                return -2;
                */
            }
            return l;
        }

        /// <summary>
        /// Potencialni hranice cesty
        /// </summary>
        private class PossiblePathEdge
        {
            /// <summary>
            /// Pozice hrany
            /// </summary>
            public int X;
            /// <summary>
            /// Integral pravdepodobnosti sjizdnosti cesty 
            /// </summary>
            public int Sum;
            /// <summary>
            /// Vzestupna hrana, prechod z nesjizdneho a sjizdny
            /// </summary>
            public bool Rising;
        }



        /// <summary>
        /// Hleda hranice cesty
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public static IEnumerable<PathEdge> PathEdges2(this Image<Gray> image)
        {
            List<PathEdge> l = new List<PathEdge>();
            List<PossiblePathEdge> edges;

            Gray left = new Gray();
            left.Data = image.Data;

            int x;
            int sum;
            int sumState;
            byte val;
            byte? lastVal;
            bool state, lastState = false;
            int w = image.Width - 1;

            for (int y = image.Height - 1; y >= 0; y--)
            {
                edges = new List<PossiblePathEdge>();
                sum = 0;
                sumState = 0;
                lastVal = null;
                left.Index = image.Index(0, y);

                for (x = 0; x <= w; x++)
                {
                    val = left.Value;
                    left.Index += left.Count;
                    state = val > 128;
                    sum += 128 - val;
                    sumState += state ? 1 : 0;

                    if (lastVal.HasValue && lastState != state)
                    {
                        edges.Add(new PossiblePathEdge() { X = x, Rising = state, Sum = sum });
                    }
                    lastVal = val;
                    lastState = state;
                }

                var le = edges.Where((i) => i.Rising && i.Sum>0).OrderByDescending((i) => i.Sum).FirstOrDefault();
                var re = edges.Where((i) => !i.Rising && (i.Sum-sum)<0).OrderByDescending((i) => -i.Sum).FirstOrDefault();

                sumState = 100 * sumState / (w + 1);
                if (sumState < 90 && sumState > 10)
                {
                    if (le != null && re != null)
                    {
                        if (le.X < re.X)
                            l.Add(new PathEdge() { Y = y, Left = le.X, Right = re.X });
                    }
                    else
                    {
                        if (le != null)
                            l.Add(new PathEdge() { Y = y, Left = le.X, Right = null });
                        if (re != null)
                            l.Add(new PathEdge() { Y = y, Left = null, Right = re.X });
                    }
                }
            }
            return l;
        }
        /// <summary>
        /// Hleda hranice cesty
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public static IEnumerable<PathEdge> PathEdges3(this Image<Gray> image)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            List<PathEdge> l = new List<PathEdge>();
            List<PossiblePathEdge> edges;

            Gray left = new Gray();
            left.Data = image.Data;

            int x;
            int sum;
            int maxSum;
            byte val;
            bool state;
            bool? lastState;
            int w = image.Width - 1;
            int w256 = 256*w;

            for (int y = image.Height - 1; y >= 0; y--)
            {
                edges = new List<PossiblePathEdge>();
                sum = 0;
                lastState = null;
                left.Index = image.Index(0, y);

                for (x = 0; x <= w; x++)
                {
                    val = left.Value;
                    left.Index += left.Count;
                    state = val > 128;
                    sum += val;

                    if (lastState.HasValue && lastState != state)
                    {
                        edges.Add(new PossiblePathEdge() { X = x, Rising = state, Sum = 256 * x - 2*sum });
                    }
                    lastState = state;
                }

                PathEdge e = null;
                if(sum> w256 - sum)
                {
                    maxSum = sum;
                    e = new PathEdge() { Y = y };
                }
                else
                    maxSum = w256 - sum;
                PossiblePathEdge fl;
                PossiblePathEdge fr;
                int s1, s2;

                for (int i = 0; i < edges.Count; i++)
                {
                    fl = edges[i];
                    if (fl.Rising)
                    {
                        s1 = fl.Sum + sum;
                        if (s1 > maxSum)
                        {
                            maxSum = s1;
                            e = new PathEdge() { Left = fl.X, Y = y };
                        }
                        s2 = fl.Sum + w256 - sum;
                        for (int j = i + 1; j < edges.Count; j += 2)
                        {
                            fr = edges[j];
                            s1 = s2 - fr.Sum;
                            if (s1 > maxSum)
                            {
                                maxSum = s1;
                                e = new PathEdge() { Left = fl.X, Right = fr.X, Y = y };
                            }
                        }
                    }
                    else
                    {
                        s1 = -fl.Sum + w256- sum;
                        if (s1 > maxSum)
                        {
                            maxSum = s1;
                            e = new PathEdge() { Right = fl.X, Y = y };
                        }
                    }
                }
                if(e!=null)
                    l.Add(e);
            }
            sw.Stop();

            return l;
        }
    }
}
