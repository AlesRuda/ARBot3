using System;
using System.Numerics;
using ARBot.Common;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Bod v ECEF souradnicich. Interne sloupcovy vektor [X, Y, Z].
    /// </summary>
    public class ECEF : IEquatable<ECEF>
    {
        public ECEF()
        {
        }

        public ECEF(Matrix<double> m)
        {
            if (m.ColumnCount != 1 || m.RowCount != 3)
                throw new ArgumentException("Pozadovany rozmer matice je 3x1.");
            X = m[0, 0];
            Y = m[1, 0];
            Z = m[2, 0];
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="ellipsoid">Elipsoid popisujici Zem</param>
        /// <param name="latitude">Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.</param>
        /// <param name="longitude">Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.</param>
        /// <param name="altitude">Vyska nad povrchem</param>
        public ECEF(Ellipsoid ellipsoid, double latitude, double longitude, double altitude)
        {
            double slat = Math.Sin(latitude);
            double clat = Math.Cos(latitude);
            double slng = Math.Sin(longitude);
            double clng = Math.Cos(longitude);

            double N = ellipsoid.SemiMajorAxis / Math.Sqrt(1 - ellipsoid.EccentricitySquared * slat * slat);

            double c = (N + altitude) * clat;
            X = c * clng;
            Y = c * slng;
            Z = (N * (1 - ellipsoid.EccentricitySquared) + altitude) * slat;
        }


        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="ellipsoid">Elipsoid popisujici Zem</param>
        /// <param name="lla">LLA souradnice</param>
        public ECEF(Ellipsoid ellipsoid, LLA lla)
            : this(ellipsoid, lla.Latitude, lla.Longitude, lla.Altitude)
        {
        }

        /// <summary>
        /// Miri na nutly polednik
        /// </summary>
        public double X { get; set; }
        /// <summary>
        /// Miri smerem na vychod
        /// </summary>
        public double Y { get; set; }
        /// <summary>
        /// Roste smerem na sever
        /// </summary>
        public double Z { get; set; }

        public double Radius
        {
            get
            {
                return Math.Sqrt(X * X + Y * Y + Z * Z);
            }
        }

        /// <summary>
        /// Sloupcovy vektor 3x1 [X; Y; Z] pro maticove operace.
        /// </summary>
        public Matrix<double> ToColumn()
        {
            return Matrix<double>.Build.DenseOfArray(new double[,] { { X }, { Y }, { Z } });
        }

        /// <summary>
        /// Skalarni soucin s vektorem o.
        /// </summary>
        public double Dot(ECEF o)
        {
            return X * o.X + Y * o.Y + Z * o.Z;
        }

        public static ECEF operator +(ECEF x1, ECEF x2)
        {
            return new ECEF() {X=x1.X + x2.X, Y=x1.Y + x2.Y, Z=x1.Z + x2.Z};
        }

        public static ECEF operator -(ECEF x1, ECEF x2)
        {
            return new ECEF() { X = x1.X - x2.X, Y = x1.Y - x2.Y, Z = x1.Z - x2.Z };
        }

        public static ECEF operator *(ECEF x1, double d)
        {
            return new ECEF() { X = x1.X * d, Y = x1.Y * d, Z = x1.Z * d };
        }

        /// <summary>
        /// Prevede na vektor, ktery ma X smerem na vychod a Y smerem na sever. Z je ignorovano, protoze pro navigaci neni dulezite.
        /// </summary>
        /// <param name="e"></param>
        public static explicit operator Vector3(ECEF e)
        {
            return new Vector3((float)e.Y, (float)-e.Z, 0);
        }

        /// <summary>
        /// Prevede na vektor, ktery ma X smerem na vychod a Y smerem na sever. Z je ignorovano, protoze pro navigaci neni dulezite.
        /// </summary>
        /// <param name="e"></param>
        public static explicit operator Point2D(ECEF e)
        {
            return new Point2D((float)e.Y, (float)-e.Z);
        }

        public bool Equals(ECEF other)
        {
            return other != null && X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ECEF e && Equals(e);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("(X={0}, Y={1}, Z={2})", X, Y, Z);
        }
    }
}
