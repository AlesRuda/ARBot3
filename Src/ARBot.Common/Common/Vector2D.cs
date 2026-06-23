using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// 2D vektor
    /// </summary>
    public class Vector2D
    {
        public double X;
        public double Y;

        public Vector2D()
        {
        }
        public Vector2D(double x, double y)
        {
            X = x;
            Y = y;
        }
        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="angle">Smer vektoru, v radianech a matematickem smyslu</param>
        public Vector2D(double angle)
        {
            X = Math.Cos(angle);
            Y = Math.Sin(angle);
        }
        public Vector2D(ECEF ecef)
        {
            X = ecef.Y;
            Y = ecef.Z;
        }

        /// <summary>
        /// Delka vektoru
        /// </summary>
        public double Length
        {
            get
            {
                return Math.Sqrt(X * X + Y * Y);
            }
        }

        /// <summary>
        /// Kvadrat delky vektoru
        /// </summary>
        public double LengthSquerd
        {
            get
            {
                return X * X + Y * Y;
            }
        }

        /// <summary>
        /// Uhel vektoru v radianech v matematickem smyslu
        /// </summary>
        public double Angle
        {
            get
            {
                return Math.Atan2(Y, X);
            }
        }

        /// <summary>
        /// Uhel od this k vektoru to v radianech v matematickem smyslu
        /// </summary>
        public double AngleBetween(Vector2D to)
        {
            double sin = X * to.Y - to.X * Y;
            double cos = X * to.X + Y * to.Y;

            return Math.Atan2(sin, cos);
        }

        /// <summary>
        /// Leva normala
        /// </summary>
        public Vector2D Normal
        {
            get
            {
                return new Vector2D(-Y, X);
            }
        }

        public override string ToString()
        {
            return string.Format("[{0}, {1}]", X, Y);
        }

        public static Vector2D operator +(Vector2D a, Vector2D b)
        {
            return new Vector2D(a.X + b.X, a.Y + b.Y);
        }
        public static Vector2D operator -(Vector2D a, Vector2D b)
        {
            return new Vector2D(a.X - b.X, a.Y - b.Y);
        }
        public static Point2D operator +(Vector2D a, Point2D b)
        {
            return new Point2D(a.X + b.X, a.Y + b.Y);
        }
        public static Point2D operator +(Point2D a, Vector2D b)
        {
            return new Point2D(a.X + b.X, a.Y + b.Y);
        }
        public static Point2D operator -(Point2D a, Vector2D b)
        {
            return new Point2D(a.X - b.X, a.Y - b.Y);
        }
        /// <summary>
        /// Skalarni soucin
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static double operator *(Vector2D a, Vector2D b)
        {
            return a.X*b.X+a.Y*b.Y;
        }
        public static Vector2D operator *(double k, Vector2D b)
        {
            return new Vector2D(k*b.X, k*b.Y);
        }
        public static Vector2D operator *(Vector2D b, double k)
        {
            return new Vector2D(k * b.X, k * b.Y);
        }
        public static Vector2D operator /(Vector2D b, double k)
        {
            return new Vector2D(b.X/k, b.Y/k);
        }

    }
}
