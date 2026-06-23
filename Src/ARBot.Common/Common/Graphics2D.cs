using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Operace s body a primkami
    /// </summary>
    public class Graphics2D
    {
        /// <summary>
        /// Prusecik dvou primek 
        /// a*x+b*y+c=0
        /// </summary>
        /// <param name="a1"></param>
        /// <param name="b1"></param>
        /// <param name="c1"></param>
        /// <param name="a2"></param>
        /// <param name="b2"></param>
        /// <param name="c2"></param>
        /// <returns></returns>
        public static Point2D Intersection(double a1, double b1, double c1, double a2, double b2, double c2)
        {
            Point2D p;
            if (a1 > b1)
            {
                p.Y = (a2 * c1 - c2 * a1) / (b2 * a1 - a2 * b1);
                p.X = -(b1 * p.Y + c1) / a1;
            }
            else
            {
                p.X = (b2 * c1 - c2 * b1) / (a2 * b1 - b2 * a1);
                p.Y = -(a1 * p.X + c1) / b1;
            }
            return p;
        }
    }
}
