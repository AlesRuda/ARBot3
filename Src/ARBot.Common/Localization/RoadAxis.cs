using System;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>Osa cesty podle mapy vztazena k poze robotu.</summary>
    public struct RoadAxisMatch
    {
        /// <summary>Nasla se pouzitelna hrana?</summary>
        public bool Found;

        /// <summary>Znamenkovy odstup pozy od osy hrany [m]; <b>kladne = poza je vlevo od osy</b>.</summary>
        public double Lateral;

        /// <summary>
        /// Smer hrany vuci kurzu robotu [rad] v rozsahu (−90°, 90°] — primka nema orientaci,
        /// takze se porovnava jen sklon.
        /// </summary>
        public double HeadingRelRad;

        /// <summary>Sirka cesty podle mapy v miste prumetu [m] (interpolovana mezi uzly).</summary>
        public double WidthM;

        /// <summary>Leva normala hrany v lokalni ENU rovine (orientovana podle kurzu robotu).</summary>
        public double NormalX, NormalY;

        /// <summary>Bod na ose hrany (kolmy prumet pozy) v lokalni ENU rovine [m].</summary>
        public double AxisX, AxisY;

        /// <summary>Id cesty (OSM way) - klic pro filtr sirky.</summary>
        public long WayId;

        /// <summary>Vzdalenost pozy od hrany [m] podle <see cref="RoadNetwork.NearestEdge"/>.</summary>
        public double DistanceM;
    }

    /// <summary>
    /// Prevod „poza + mapa" na osu cesty: kterou hranu robot jede, jak je od jeji osy vzdaleny
    /// a o kolik je vuci ni stoceny. Mapova protistrana ke koridoru z kamer
    /// (<see cref="RoadCorridor"/>) — teprve rozdil obou stran je merenie do fuze.
    ///
    /// <para><b>Orientace hrany se srovnava s kurzem robotu.</b> Hrany site jsou orientovane
    /// (obousmerna cesta je dve hrany), takze bez toho by se leva a prava strana obcas prohodila
    /// a znamenko pricne korekce by preskakovalo. Proto se smer hrany prevraci tak, aby mel s kurzem
    /// robotu kladny skalarni soucin — pak „vlevo od osy" znamena totez, co „vlevo" v ramci robotu.</para>
    ///
    /// <para>Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public static class RoadAxis
    {
        /// <summary>
        /// Najde nejblizsi hranu k poze a vrati vztah pozy k jeji ose.
        /// </summary>
        /// <param name="network">Silnicni sit.</param>
        /// <param name="origin">Pocatek lokalni ENU roviny (tez sit -&gt; metry).</param>
        /// <param name="x">Poloha robotu na vychod [m].</param>
        /// <param name="y">Poloha robotu na sever [m].</param>
        /// <param name="theta">Kurz robotu [rad], matematicky (0 = vychod).</param>
        public static RoadAxisMatch Match(RoadNetwork network, GeoReference origin,
                                          double x, double y, double theta)
        {
            var m = new RoadAxisMatch();
            if (network == null || origin == null) return m;

            var edge = network.NearestEdge(origin.ToLLA(x, y), out double t, out _, out double dist);
            if (edge == null) return m;

            var a = origin.ToLocal(edge.From.Location);
            var b = origin.ToLocal(edge.To.Location);
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double len = Math.Sqrt(ex * ex + ey * ey);
            if (len < 1e-6) return m;
            ex /= len; ey /= len;

            // Srovnat smer hrany s kurzem robotu (viz poznamka v hlavicce tridy).
            double hx = Math.Cos(theta), hy = Math.Sin(theta);
            double wA = edge.From.Width, wB = edge.To.Width;
            double tt = Math.Max(0, Math.Min(1, t));
            if (ex * hx + ey * hy < 0)
            {
                ex = -ex; ey = -ey;
                // Sirka se interpoluje podel hrany; po prevraceni smeru je parametr 1-t.
                tt = 1 - tt;
                var sw = wA; wA = wB; wB = sw;
            }

            double nx = -ey, ny = ex;                       // leva normala
            m.Found = true;
            m.Lateral = nx * (x - a.X) + ny * (y - a.Y);
            m.HeadingRelRad = Normalize(Math.Atan2(ey, ex) - theta);
            m.WidthM = wA + (wB - wA) * tt;
            m.NormalX = nx; m.NormalY = ny;
            m.AxisX = x - m.Lateral * nx;                   // kolmy prumet pozy na osu
            m.AxisY = y - m.Lateral * ny;
            m.WayId = edge.WayId;
            m.DistanceM = dist;
            return m;
        }

        /// <summary>Normalizace uhlu na (−90°, 90°] — primka nema orientaci.</summary>
        private static double Normalize(double a)
        {
            while (a > Math.PI / 2) a -= Math.PI;
            while (a <= -Math.PI / 2) a += Math.PI;
            return a;
        }
    }
}
