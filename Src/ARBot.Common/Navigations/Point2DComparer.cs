using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public class Point2DComparer : IEqualityComparer<Point2D>
    {
        double dist;
        public Point2DComparer(double dist)
        {
            this.dist = dist;
        }
        public bool Equals(Point2D x, Point2D y)
        {
            double d = Math.Sqrt(Math.Pow(x.X - y.X, 2) + Math.Pow(x.Y - y.Y, 2));
            return d <= dist;
        }

        public int GetHashCode(Point2D obj)
        {
            double x = 0.5 * obj.X / dist;
            double y = 0.5 * obj.Y / dist;
            return string.Format("{0:N0}_{1:N0}", x, y).GetHashCode();
        }
    }
}
