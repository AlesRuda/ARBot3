using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Coordinates
{
    public class LLA
    {
        public LLA()
        {
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="latitude">Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.</param>
        /// <param name="longitude">Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.</param>
        /// <param name="altitude">Vyska nad povrchem</param>
        public LLA(double latitude, double longitude, double altitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = altitude;
        }

        /// <summary>
        /// Konsturktor. Bod na povrchu.
        /// </summary>
        /// <param name="latitude">Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.</param>
        /// <param name="longitude">Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.</param>
        public LLA(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
            Altitude = 0;
        }

     

        /// <summary>
        /// Konstruktor z ecef na sfere
        /// </summary>
        /// <param name="ecef"></param>
        public LLA(ECEF ecef)
        {
            Latitude = Math.Atan2(ecef.Z, Math.Sqrt(ecef.X * ecef.X + ecef.Y * ecef.Y));
            Longitude = Math.Atan2(ecef.Y, ecef.X);
            Altitude = 0;
        }


        /// <summary>
        /// Konstruktor z ecef na elipsoidu
        /// </summary>
        /// <param name="ellipsoid"></param>
        /// <param name="ecef"></param>
        public LLA(Ellipsoid ellipsoid, ECEF ecef)
        {
            double x = ecef.X;
            double y = ecef.Y;
            double z = ecef.Z;

            double p=Math.Sqrt(x*x + y*y);
            double f=Math.Atan(z*ellipsoid.SemiMajorAxis/(p*ellipsoid.SemiMinorAxis));
            double N=ellipsoid.SemiMajorAxis/Math.Sqrt(1-ellipsoid.EccentricitySquared*Math.Sin(f)*Math.Sin(f));

            double ee=(ellipsoid.SemiMajorAxisSquared-ellipsoid.SemiMinorAxisSquared)/ellipsoid.SemiMinorAxisSquared;

            double sf=Math.Sin(f);
            double cf=Math.Cos(f);

            Latitude = Math.Atan2((z + ee*ellipsoid.SemiMinorAxis*sf*sf*sf),(p-ellipsoid.EccentricitySquared*ellipsoid.SemiMajorAxis*cf*cf*cf));
            Longitude = Math.Atan2(y, x);

            double slat = Math.Sin(Latitude);
            double clat = Math.Cos(Latitude);
            N = ellipsoid.SemiMajorAxis / Math.Sqrt(1 - ellipsoid.EccentricitySquared * slat * slat);
            Altitude = p / clat - N;

/*
 * 
            double e2 = ellipsoid.EccentricitySquared;
            double ex2 = ellipsoid.SemiMajorAxisSquared / ellipsoid.SemiMinorAxisSquared - 1;
            double e4 = e2 * e2;
            double e = Math.Sqrt(e2);
            double E2 = ellipsoid.SemiMinorAxisSquared - ellipsoid.SemiMinorAxisSquared;

            double r2 = x * x + y * y;
            double r = Math.Sqrt(r2);
            double z2 = z * z;
            double F = 54 * e2 * z2;
            double G = r2 + (1 - e2) * z2 - e2 * E2;
            double C = e4 * F * r2 / (G * G * G);
            double S = Math.Pow(1 + C + Math.Sqrt(C * C + 2 * C), 1 / 3);
            double P = F / (3 * (S + 1 / S + 1) * (S + 1 / S + 1) * G * G);
            double Q = Math.Sqrt(1 + 2 * e4 * P);
            double r0 = (-(P * e2 * r) / (1 + Q)) + Math.Sqrt(ellipsoid.SemiMajorAxisSquared / 2 * (1 + 1 / Q) - ((P * (1 - e2) * z * z) / (Q * (1 + Q))) - P * r2 / 2);
            double i = (r - e2 * r0) * (r - e2 * r0);
            double U = Math.Sqrt(i + z * z);
            double V = Math.Sqrt(i + (1 - e2) * z * z);
            i = ellipsoid.SemiMinorAxisSquared / (ellipsoid.SemiMajorAxis * V);
            double Z0 = i * z;
            Altitude = U * (1 - i);
            Latitude = Math.Atan((z + ex2 * Z0) / r);
            Longitude = Math.Atan2(y, x);
*/
        }

        /// <summary>
        /// Zemepisna sirka v radianech. S nulou na rovniku. Severni pol ma 90 stupnu.
        /// </summary>
        public double Latitude { get; set; }
        /// <summary>
        /// Zemepisna delka v radianech. S nulou na nultem poledniku. Roste smerem na vychod.
        /// </summary>
        public double Longitude { get; set; }
        /// <summary>
        /// Vyska nad povrchem
        /// </summary>
        public double Altitude { get; set; }

        public double Distance(Ellipsoid e, LLA point)
        {
            var R = e.SemiMajorAxis; // metres
            var f1 = Latitude;
            var f2 = point.Latitude;
            var df = (point.Latitude - Latitude);
            var dl = (point.Longitude - Longitude);

            var a = Math.Sin(df / 2) * Math.Sin(df / 2) +
                    Math.Cos(f1) * Math.Cos(f2) *
                    Math.Sin(dl / 2) * Math.Sin(dl / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        public override bool Equals(object obj)
        {
            LLA lla = obj as LLA;
            if (lla == null)
                return false;

            return Distance(Ellipsoid.Sphere, lla) < 0.001;
        }

        public override int GetHashCode()
        {
            return Latitude.GetHashCode()+Longitude.GetHashCode()+Altitude.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("{0}, {1}, {2}", Conversions.Rad2Deg(Latitude), Conversions.Rad2Deg(Longitude), Altitude);
        }
    }
}
