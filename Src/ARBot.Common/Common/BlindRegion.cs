using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Slepe uhly pro mereni v polarnich souradnicich
    /// </summary>
    public class BlindRegion
    {
        /// <summary>
        /// Zacatek slepeho uhlu
        /// </summary>
        public double From;
        /// <summary>
        /// KOnec slepeho uhlu
        /// </summary>
        public double To;

        public bool InSide(double a)
        {
            return From < a && a < To;
        }
    }
}
