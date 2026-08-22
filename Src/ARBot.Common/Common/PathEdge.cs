using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Hranice cesty v souradnicich kamery
    /// </summary>
    public class PathEdge
    {
        /// <summary>
        /// Y souradnice
        /// </summary>
        public int Y;
        /// <summary>
        /// X souradnice stredu cesty
        /// </summary>
        public int X(int? left, int? right)
        {
            int? l = Left;
            int? r = Right;

            if (l == null)
                l = left;
            if (r == null)
                r = right;

            if (l == null || r == null)
                return l.GetValueOrDefault() + r.GetValueOrDefault();
            return (l.GetValueOrDefault() + r.GetValueOrDefault()) / 2;
        }
        /// <summary>
        /// Levy kraj 
        /// </summary>
        public int? Left;
        /// <summary>
        /// Pravy kraj
        /// </summary>
        public int? Right;

        /// <summary>
        /// Levy kraj jako <b>metricky bod v ramci robotu</b> [m] (<c>A == 0</c> = neplatny).
        /// Dopocitava <see cref="ARBot.Common.Vision.ColorPixelTo3D"/> na vlakne kamery.
        /// </summary>
        public Point4D LeftPoint;

        /// <summary>Pravy kraj jako metricky bod v ramci robotu [m] (<c>A == 0</c> = neplatny).</summary>
        public Point4D RightPoint;

        /// <summary>
        /// <b>Proc metry a proc uz tady</b> (21. 8. 2026): prepocet potrebuje hloubkovy obraz
        /// a projekci, tedy presne to, co ma po ruce vlakno kamery — a stoji 0,02 ms na snimek.
        /// Konzument (koridor pro lokalizaci) tak nepotrebuje projekce vubec a v zaznamu jsou
        /// metry, takze offline prepocet nezavisi na tom, jestli se do ramce vejde i barevna
        /// projekce.
        ///
        /// <para><b>Ramec je ROBOT, ne ENU.</b> Vlakno kamery pozu nezna a znat nema; tim zustava
        /// pozorovani nezavisle na odhadu pozy, coz je prave to, co z nej dela poctive merenie do
        /// fuze. Prevod do ENU patri az tam, kde se to porovnava s mapou.</para>
        ///
        /// <para>Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public bool HasMetricPoints => LeftPoint.A != 0 || RightPoint.A != 0;
    }
}
