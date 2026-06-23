using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Voronoi
{
    // use for sites and vertecies
    public class Site
    {
        public Point2D coord;
        public int sitenbr;

        public Site()
        {
            coord = new Point2D();
        }
    }
}
