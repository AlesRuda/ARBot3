using System;
using ARBot.Common.Common;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Referencni bod lokalni ENU roviny - misto, kde plati [X, Y] = [0, 0].
    /// Prevadi geodeticke souradnice (LLA) na lokalni metry: X na vychod, Y na sever
    /// (shodne se svetovou konvenci fuzniho filtru). Vnitrne pres ECEF a rotaci do ENU
    /// v pocatku. Pro oblast pohybu robota (radove km) je presnost mm-cm.
    /// </summary>
    public class GeoReference
    {
        private readonly Ellipsoid ellipsoid;
        private readonly ECEF originEcef;
        private readonly double sinLat, cosLat, sinLon, cosLon;

        /// <summary>Pocatek lokalni roviny (kde X=Y=0).</summary>
        public LLA Origin { get; }

        public GeoReference(LLA origin, Ellipsoid ellipsoid = null)
        {
            if (origin == null)
                throw new ArgumentNullException(nameof(origin));
            this.ellipsoid = ellipsoid ?? Ellipsoid.Wgs84;
            Origin = origin;
            sinLat = Math.Sin(origin.Latitude);
            cosLat = Math.Cos(origin.Latitude);
            sinLon = Math.Sin(origin.Longitude);
            cosLon = Math.Cos(origin.Longitude);
            originEcef = new ECEF(this.ellipsoid, origin);
        }

        /// <summary>Vytvori referenci z hodnot ve stupnich.</summary>
        public static GeoReference FromDegrees(double latDeg, double lonDeg, double altitude = 0, Ellipsoid ellipsoid = null)
            => new GeoReference(new LLA(Conversions.Deg2Rad(latDeg), Conversions.Deg2Rad(lonDeg), altitude), ellipsoid);

        /// <summary>Prevede LLA na lokalni ENU [X=vychod, Y=sever] v metrech vzhledem k pocatku.</summary>
        public Point2D ToLocal(LLA p)
        {
            var pe = new ECEF(ellipsoid, p);
            double dx = pe.X - originEcef.X;
            double dy = pe.Y - originEcef.Y;
            double dz = pe.Z - originEcef.Z;
            double east = -sinLon * dx + cosLon * dy;
            double north = -sinLat * cosLon * dx - sinLat * sinLon * dy + cosLat * dz;
            return new Point2D(east, north);
        }

        /// <summary>Prevede zemepisnou sirku/delku (radiany) na lokalni ENU.</summary>
        public Point2D ToLocal(double latitudeRad, double longitudeRad, double altitude = 0)
            => ToLocal(new LLA(latitudeRad, longitudeRad, altitude));

        /// <summary>Inverze: lokalni ENU (east, north, up) zpet na LLA.</summary>
        public LLA ToLLA(double east, double north, double up = 0)
        {
            double dx = -sinLon * east - sinLat * cosLon * north + cosLat * cosLon * up;
            double dy = cosLon * east - sinLat * sinLon * north + cosLat * sinLon * up;
            double dz = cosLat * north + sinLat * up;
            var pe = new ECEF { X = originEcef.X + dx, Y = originEcef.Y + dy, Z = originEcef.Z + dz };
            return new LLA(ellipsoid, pe);
        }
    }
}
