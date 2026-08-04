using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>Složka X (vpravo/východ).</summary>
        public double X;
        /// <summary>Složka Y (nahoru/sever).</summary>
        public double Y;

        /// <summary>Nulový vektor (0, 0).</summary>
        public Vector2D()
        {
        }
        /// <summary>Vektor ze složek.</summary>
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
        /// <summary>Vektor z ECEF: bere složky Y→X a Z→Y (projekce do roviny fúzního rámce).</summary>
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

        /// <summary>Textová reprezentace „[X, Y]" (invariantní kultura).</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0}, {1}]", X, Y);
        }

        /// <summary>Součet dvou vektorů.</summary>
        public static Vector2D operator +(Vector2D a, Vector2D b)
        {
            return new Vector2D(a.X + b.X, a.Y + b.Y);
        }
        /// <summary>Rozdíl dvou vektorů.</summary>
        public static Vector2D operator -(Vector2D a, Vector2D b)
        {
            return new Vector2D(a.X - b.X, a.Y - b.Y);
        }
        /// <summary>Posun bodu o vektor (vektor + bod = bod).</summary>
        public static Point2D operator +(Vector2D a, Point2D b)
        {
            return new Point2D(a.X + b.X, a.Y + b.Y);
        }
        /// <summary>Posun bodu o vektor (bod + vektor = bod).</summary>
        public static Point2D operator +(Point2D a, Vector2D b)
        {
            return new Point2D(a.X + b.X, a.Y + b.Y);
        }
        /// <summary>Posun bodu o opačný vektor (bod − vektor = bod).</summary>
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
        /// <summary>Násobení vektoru skalárem (skalár vlevo).</summary>
        public static Vector2D operator *(double k, Vector2D b)
        {
            return new Vector2D(k*b.X, k*b.Y);
        }
        /// <summary>Násobení vektoru skalárem (skalár vpravo).</summary>
        public static Vector2D operator *(Vector2D b, double k)
        {
            return new Vector2D(k * b.X, k * b.Y);
        }
        /// <summary>Dělení vektoru skalárem.</summary>
        public static Vector2D operator /(Vector2D b, double k)
        {
            return new Vector2D(b.X/k, b.Y/k);
        }

        /// <summary>
        /// Explicitni konverze na sloupcovy vektor 2x1 (MathNet).
        /// </summary>
        public static explicit operator MathNet.Numerics.LinearAlgebra.Matrix<double>(Vector2D v)
        {
            return MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new double[,] { { v.X }, { v.Y } });
        }

    }
}
