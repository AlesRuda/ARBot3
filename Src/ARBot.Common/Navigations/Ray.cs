using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Informace o mereni lidaru vhledem k telu lidaru.
    /// </summary>
    public class Ray
    {
        /// <summary>
        /// Uhel vzorku v radianech v matematickem smeru, 0 pred lidarem.
        /// </summary>
        public float Angle;

        /// <summary>
        /// Vzdalenost
        /// </summary>
        public float? Distance;

        /// <summary>
        /// Strmost hrany, + vzdalovani, - priblizovani 
        /// </summary>
        public float? Diff;

        /// <summary>
        /// Casovy okamzik vzorku.
        /// </summary>
        public DateTime TimeStamp;

        /// <summary>
        /// Bod vzorku
        /// </summary>
        public Point2D Point
        {
            get
            {
                return new Point2D() { X = Distance.GetValueOrDefault(0) * MathF.Cos(Angle), Y = Distance.GetValueOrDefault(0) * MathF.Sin(Angle) };
            }
        }
    }
}
