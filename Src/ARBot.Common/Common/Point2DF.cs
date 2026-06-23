using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Point2DF
    {
        public float X;
        public float Y;

        public Point2DF(float x, float y)
        {
            X = x;
            Y = y;
        }
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}, {1}", X, Y);
        }

        public static Point2DF operator +(Point2DF a, Point2DF b)
        {
            Point2DF x;
            x.X = a.X + b.X;
            x.Y = a.Y + b.Y;
            return x;
        }

        public static Point2DF operator -(Point2DF a, Point2DF b)
        {
            Point2DF x;
            x.X = a.X - b.X;
            x.Y = a.Y - b.Y;
            return x;
        }

        public static Point2DF operator /(Point2DF a, float b)
        {
            Point2DF x;
            x.X = a.X / b;
            x.Y = a.Y / b;
            return x;
        }
        /// <summary>
        /// Vzdalenost od pocatku
        /// </summary>
        public float Distance
        {
            get
            {
                return (float)Math.Sqrt(Math.Pow(X, 2) + Math.Pow(Y, 2));
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is Point2DF)
            {
                Point2DF p = (Point2DF)obj;
                return p.X == X && p.Y == Y;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return string.Format("{0}_{1}", X, Y).GetHashCode();
        }
    }
}
