using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Reprezentuje okolni bod
    /// </summary>
    public struct GridNavigationNeighborhood
    {
        public GridNavigationNeighborhood(int dir, int x, int y)
        {
            Direction = dir;
            Neighborhood.X = x;
            Neighborhood.Y = y;
            Length = Math.Sqrt(x * x + y * y);
        }
        /// <summary>
        /// Bod
        /// </summary>
        public Point Neighborhood;
        /// <summary>
        /// Smer
        /// </summary>
        public int Direction;

        /// <summary>
        /// Vzdalenost
        /// </summary>
        public double Length;
    }
}
