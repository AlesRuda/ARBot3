using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Coordinates
{
    public class ECEF : Matrix
    {
        public ECEF()
            : base(3, 1)
        {
        }

        public ECEF(Matrix m)
            : base(m.in_Mat)
        {
            if (m.NoCols != 1 || m.NoRows != 3)
                throw new ArgumentException("Pozadovany rozmer matice je 3x1.");
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="ellipsoid">Elipsoid popisujici Zem</param>
        /// <param name="latitude">Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.</param>
        /// <param name="longitude">Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.</param>
        /// <param name="altitude">Vyska nad povrchem</param>
        public ECEF(Ellipsoid ellipsoid, double latitude, double longitude, double altitude)
            : this()
        {
            double slat = Math.Sin(latitude);
            double clat = Math.Cos(latitude);
            double slng = Math.Sin(longitude);
            double clng = Math.Cos(longitude);

            double N = ellipsoid.SemiMajorAxis / Math.Sqrt(1 - ellipsoid.EccentricitySquared * slat * slat);

            double c = (N + altitude) * clat;
            double x = c * clng;
            double y = c * slng;
            double z = (N * (1 - ellipsoid.EccentricitySquared) + altitude) * slat;
            X = x;
            Y = y;
            Z = z;
        }


        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="ellipsoid">Elipsoid popisujici Zem</param>
        /// <param name="latitude">LLA souradnice</param>
        public ECEF(Ellipsoid ellipsoid, LLA lla)
            : this(ellipsoid, lla.Latitude, lla.Longitude, lla.Altitude)
        {
        }

        /// <summary>
        /// Miri na nutly polednik
        /// </summary>
        public double X
        {
            get
            {
                return this[0, 0];
            }
            set
            {
                this[0, 0] = value;
            }
        }
        /// <summary>
        /// Miri smerem na vychod
        /// </summary>
        public double Y
        {
            get
            {
                return this[1, 0];
            }
            set
            {
                this[1, 0] = value;
            }
        }
        /// <summary>
        /// Roste smerem na sever
        /// </summary>
        public double Z
        {
            get
            {
                return this[2, 0];
            }
            set
            {
                this[2, 0] = value;
            }
        }
        public double Radius
        {
            get
            {
                return Math.Sqrt(X * X + Y * Y + Z * Z);
            }
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
            return new ECEF(x1.Mul(d));
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

        /*
                public Matice Rotate()
                {
                    double la = Math.Atan2(Z, Math.Sqrt(X * X + Y * Y));
                    double lo = Math.Atan2(Y, X);

                    Matice t = new Matice(3, 3);
                    t[0, 0] = Math.Cos(lo);
                    t[0, 1] = -Math.Sin(lo);
                    t[1, 0] = Math.Sin(lo);
                    t[1, 1] = Math.Cos(lo);
                    t[2, 2] = 1;

                    Matice t1 = new Matice(3, 3);
                    t1[0, 0] = Math.Cos(la);
                    t1[1, 1] = 1;
                    t1[2, 2] = Math.Cos(la);
                    t1[0, 2] =-Math.Sin(la);
                    t1[2, 0] =Math.Sin(la);

                    return t.Mul(t1);
                }

                public Matice RotateBack()
                {
                    double la = -Math.Atan2(Z, Math.Sqrt(X * X + Y * Y));
                    double lo = -Math.Atan2(Y, X);

                    Matice t = new Matice(3, 3);
                    t[0, 0] = Math.Cos(lo);
                    t[0, 1] = Math.Sin(lo);
                    t[1, 0] = -Math.Sin(lo);
                    t[1, 1] = Math.Cos(lo);
                    t[2, 2] = 1;

                    Matice t1 = new Matice(3, 3);
                    t1[0, 0] = Math.Cos(la);
                    t1[1, 1] = 1;
                    t1[2, 2] = Math.Cos(la);
                    t1[0, 2] = Math.Sin(la);
                    t1[2, 0] = -Math.Sin(la);

                    return t.Mul(t1);
                }
                */

        public override string ToString()
        {
            return string.Format("(X={0}, Y={1}, Z={2})", X, Y, Z);
        }
    }
}
