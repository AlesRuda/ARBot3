using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public class GridNavigationPixel
    {
        public int X;
        public int Y;
        public GridNavigationPixel Previous;
        /// <summary>
        /// Puvodni bod prekazky
        /// </summary>
        public Point? OriginObstacle;
        /// <summary>
        /// Vzdalenost od prekazky v metrech
        /// </summary>
        public double ObstacleDistance;
        /// <summary>
        /// Obtiznost prujezdu, cim vetsi cislo tim je slozitejsi projet
        /// </summary>
        public double Weight;
        public bool ObstacleDistanceCalculated;
        public BigInteger Potencial;
        public BigInteger OldPotencial;
        public double WayDistance;
        public bool Way;
        public int Direction;

        public Point Point => new Point(X, Y);
    }
}
