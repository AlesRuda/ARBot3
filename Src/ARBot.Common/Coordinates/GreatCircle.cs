using System;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Vzdalenost a azimut mezi dvema <see cref="LLA"/> po povrchu zvoleneho <see cref="Ellipsoid"/>u
    /// (geodetika, Vincentyho inverzni uloha).
    ///
    /// <para><b>Proc elipsoid a ne koule.</b> Drive se tu pocitalo haversinem na kouli
    /// R = 6 371 000 m, zatimco <see cref="GeoReference"/> prevadi na lokalni metry pres WGS84
    /// (ECEF). Ty dva modely se rozchazely: na sirce 50° vyslo 10,000 m v lokalnim ENU jako
    /// 9,969 m v grafu, tedy o 0,31 % min. Ve smeru vychod-zapad je totiz smerodatny polomer
    /// krivosti v prvnim vertikalu N(50°) ≈ 6 390 693 m, ne stredni polomer koule. Delky hran
    /// v grafu se tim rozchazely s metrickym svetem, ve kterem robot skutecne jede.
    /// Vychozi model je proto <see cref="Ellipsoid.Wgs84"/> - stejny, s jakym pocita
    /// <see cref="GeoReference"/> i fuze.</para>
    ///
    /// <para><b>Koule je zvlastni pripad.</b> Pro <c>a == b</c> (napr. <see cref="Ellipsoid.Sphere"/>)
    /// se vzorec sam degeneruje na obycejnou great-circle vzdalenost o polomeru <c>b</c> - iterace
    /// skonci hned v prvnim kroku. Chces-li presne puvodni chovani, predej
    /// <c>new Ellipsoid(6371000, 6371000)</c>.</para>
    ///
    /// <para><b>Cena.</b> Iterativni vypocet, pro bezne (kratke) vzdalenosti 2-3 iterace.
    /// Vola se pri stavbe grafu na hranu, ne v ridici smycce.</para>
    /// </summary>
    public class GreatCircle
    {
        /// <summary>Maximalni pocet iteraci; bezne staci 2-3, limit je pojistka pro protilehle body.</summary>
        private const int MaxIterations = 100;

        /// <summary>Konvergencni prah na lambda [rad] (~0,06 mm na povrchu).</summary>
        private const double Tolerance = 1e-12;

        private readonly double a;    // hlavni poloosa [m]
        private readonly double b;    // vedlejsi poloosa [m]
        private readonly double f;    // zplosteni (a-b)/a

        /// <summary>Model, na kterem se pocita.</summary>
        public Ellipsoid Ellipsoid { get; }

        /// <summary>Sdilena instance nad WGS84 (tentyz model jako <see cref="GeoReference"/>).</summary>
        public static GreatCircle Wgs84 { get; } = new GreatCircle(Ellipsoid.Wgs84);

        /// <summary>Sdilena instance nad kouli o rovnikovem polomeru (<see cref="Ellipsoid.Sphere"/>).</summary>
        public static GreatCircle Sphere { get; } = new GreatCircle(Ellipsoid.Sphere);

        /// <param name="ellipsoid">Model Zeme; <c>null</c> = <see cref="Ellipsoid.Wgs84"/>.</param>
        public GreatCircle(Ellipsoid ellipsoid = null)
        {
            Ellipsoid = ellipsoid ?? Ellipsoid.Wgs84;
            a = Ellipsoid.SemiMajorAxis;
            b = Ellipsoid.SemiMinorAxis;
            f = Ellipsoid.Flattening;   // (a - b) / a
        }

        /// <summary>Vzdalenost po povrchu [m]. Pro shodne body vraci 0.</summary>
        public double Distance(LLA from, LLA to)
        {
            Solve(from, to, out double s, out _);
            return s;
        }

        /// <summary>
        /// Pocatecni azimut z <paramref name="from"/> do <paramref name="to"/> [rad],
        /// 0 = sever, kladny po smeru hodinovych rucicek. Pro shodne body vraci 0.
        /// </summary>
        public double Bearing(LLA from, LLA to)
        {
            Solve(from, to, out _, out double azimuth);
            return azimuth;
        }

        /// <summary>
        /// Vincentyho inverzni uloha: z dvojice zemepisnych souradnic spocte delku geodetiky
        /// a pocatecni azimut. Pro <c>f == 0</c> (koule) je vysledkem prima great-circle
        /// vzdalenost <c>b·σ</c>, protoze vsechny opravy vyjdou nulove.
        /// </summary>
        private void Solve(LLA from, LLA to, out double distance, out double azimuth)
        {
            distance = 0;
            azimuth = 0;
            if (from == null || to == null) return;

            // Redukovane sirky (na pomocne kouli).
            double u1 = Math.Atan((1 - f) * Math.Tan(from.Latitude));
            double u2 = Math.Atan((1 - f) * Math.Tan(to.Latitude));
            double sinU1 = Math.Sin(u1), cosU1 = Math.Cos(u1);
            double sinU2 = Math.Sin(u2), cosU2 = Math.Cos(u2);

            double L = to.Longitude - from.Longitude;
            double lambda = L, lambdaPrev;
            double sinSigma = 0, cosSigma = 0, sigma = 0, cos2Alpha = 1, cos2SigmaM = 0;
            double sinLambda = 0, cosLambda = 0;

            int i = 0;
            do
            {
                sinLambda = Math.Sin(lambda);
                cosLambda = Math.Cos(lambda);

                double t1 = cosU2 * sinLambda;
                double t2 = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;
                sinSigma = Math.Sqrt(t1 * t1 + t2 * t2);
                if (sinSigma == 0) return;   // shodne body

                cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
                sigma = Math.Atan2(sinSigma, cosSigma);

                double sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
                cos2Alpha = 1 - sinAlpha * sinAlpha;

                // Na rovnikove linii je cos2Alpha == 0 a cos2SigmaM neni definovane - bere se 0.
                cos2SigmaM = cos2Alpha == 0 ? 0 : cosSigma - 2 * sinU1 * sinU2 / cos2Alpha;

                double c = f / 16 * cos2Alpha * (4 + f * (4 - 3 * cos2Alpha));
                lambdaPrev = lambda;
                lambda = L + (1 - c) * f * sinAlpha *
                         (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));
            }
            while (Math.Abs(lambda - lambdaPrev) > Tolerance && ++i < MaxIterations);

            // Nekonvergovalo = temer protilehle body. V nasem meritku (mapa desitek metru az
            // kilometru) nenastane; radeji vratime rozumnou aproximaci nez NaN.
            if (i >= MaxIterations)
            {
                distance = b * sigma;
                azimuth = NormalizeAzimuth(Math.Atan2(cosU2 * sinLambda, cosU1 * sinU2 - sinU1 * cosU2 * cosLambda));
                return;
            }

            double u2sq = cos2Alpha * (a * a - b * b) / (b * b);
            double aa = 1 + u2sq / 16384 * (4096 + u2sq * (-768 + u2sq * (320 - 175 * u2sq)));
            double bb = u2sq / 1024 * (256 + u2sq * (-128 + u2sq * (74 - 47 * u2sq)));
            double deltaSigma = bb * sinSigma * (cos2SigmaM + bb / 4 *
                (cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)
                 - bb / 6 * cos2SigmaM * (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * cos2SigmaM * cos2SigmaM)));

            distance = b * aa * (sigma - deltaSigma);
            azimuth = NormalizeAzimuth(Math.Atan2(cosU2 * sinLambda, cosU1 * sinU2 - sinU1 * cosU2 * cosLambda));
        }

        /// <summary>Azimut do &lt;0; 2π).</summary>
        private static double NormalizeAzimuth(double rad)
        {
            double twoPi = 2 * Math.PI;
            rad %= twoPi;
            return rad < 0 ? rad + twoPi : rad;
        }
    }
}
