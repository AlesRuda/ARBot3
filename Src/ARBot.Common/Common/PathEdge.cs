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
    }
}
