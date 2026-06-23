using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Coordinates
{
    public class Ellipsoid
    {
        public double SemiMajorAxis { get; protected set;}
        public double SemiMinorAxis { get; protected set;}
        public double SemiMajorAxisSquared { get; protected set; }
        public double SemiMinorAxisSquared { get; protected set; }
        public double Eccentricity { get; protected set; }
        public double EccentricitySquared { get; protected set;}

        static Ellipsoid()
        {
            Wgs84 = new Ellipsoid(6378137.0, 6356752.3141);
            Sphere = new Ellipsoid(6378137.0, 6378137.0);
        }

        public Ellipsoid(double semiMajorAxis, double semiMinorAxis)
        {
            SemiMinorAxis = semiMinorAxis;
            SemiMajorAxis = semiMajorAxis;

            SemiMinorAxisSquared = semiMinorAxis * semiMinorAxis;
            SemiMajorAxisSquared = semiMajorAxis * semiMajorAxis;

            Eccentricity = 1 - semiMinorAxis/SemiMajorAxis;
            EccentricitySquared = 1 - SemiMinorAxisSquared/SemiMajorAxisSquared;

        }

        public static Ellipsoid Sphere { get; private set; }
        public static Ellipsoid Wgs84 { get; private set; }
    }
}
