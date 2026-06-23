using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Voronoi
{
    public class SiteSorterYX : IComparer<Site>
    {
        public int Compare(Site p1, Site p2)
        {
            Point2D s1 = p1.coord;
            Point2D s2 = p2.coord;
            if (s1.Y < s2.Y) return -1;
            if (s1.Y > s2.Y) return 1;
            if (s1.X < s2.X) return -1;
            if (s1.X > s2.X) return 1;
            return 0;
        }
    }
}
