using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Rotacni elipsoid dany dvojici poloos - model Zeme pro prevody souradnic
    /// (<see cref="ECEF"/>, <see cref="GeoReference"/>) a vzdalenosti (<see cref="GreatCircle"/>).
    /// </summary>
    public class Ellipsoid
    {
        /// <summary>Hlavni (rovnikova) poloosa <c>a</c> [m].</summary>
        public double SemiMajorAxis { get; protected set;}
        /// <summary>Vedlejsi (polarni) poloosa <c>b</c> [m].</summary>
        public double SemiMinorAxis { get; protected set;}
        /// <summary><c>a²</c>.</summary>
        public double SemiMajorAxisSquared { get; protected set; }
        /// <summary><c>b²</c>.</summary>
        public double SemiMinorAxisSquared { get; protected set; }

        /// <summary>
        /// Zplosteni <c>f = (a − b) / a</c> (pro WGS84 ≈ 1/298,257, tedy 0,00335).
        /// <para>Drive se tato vlastnost jmenovala <c>Eccentricity</c>, coz bylo matouci - je to
        /// zplosteni, NE excentricita. Excentricita je <c>e = sqrt(1 − b²/a²)</c> ≈ 0,0818,
        /// tedy o rad jinde; jeji druha mocnina je <see cref="EccentricitySquared"/>.</para>
        /// </summary>
        public double Flattening { get; protected set; }

        /// <summary>Druha mocnina prvni excentricity <c>e² = 1 − b²/a²</c> (WGS84 ≈ 0,00669438).</summary>
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

            Flattening = 1 - semiMinorAxis/SemiMajorAxis;          // = (a - b) / a
            EccentricitySquared = 1 - SemiMinorAxisSquared/SemiMajorAxisSquared;

        }

        public static Ellipsoid Sphere { get; private set; }
        public static Ellipsoid Wgs84 { get; private set; }
    }
}
