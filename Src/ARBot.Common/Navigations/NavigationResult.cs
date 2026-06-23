using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Vysledek gridove navigace
    /// </summary>
    public class GridNavigationResult
    {
        /// <summary>
        /// Smer jizdy v radianech a matematickem smyslu
        /// </summary>
        public double Direction { get; set; }
        /// <summary>
        /// Navigacni cil X.
        /// </summary>
        public double X { get; set; }
        /// <summary>
        /// Navigacni cil Y.
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("X={0}, y={1}, Direction={2}", X, Y, Direction);
        }
    }
}
