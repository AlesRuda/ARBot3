using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    /// <summary>
    /// Reprezentuje usecku lezici mezi bodu p1 a p2 na primce a*x+b*y+c=0
    /// </summary>
    public struct TopologicalLine
    {
        Point2D point1;
        Point2D point2;


        public Point2D Point1 { get { return point1; } set { point1 = value; } }
        public Point2D Point2 { get { return point2; } set { point2 = value; } }
        public double a, b, c;
        public double c1, c2;

        public TopologicalLine(Point2D p1, Point2D p2)
        {
            point1 = p1;
            point2 = p2;

            a = (p1.Y - p2.Y);
            b = (p2.X - p1.X);

            double l = Math.Sqrt(a * a + b * b);
            if (l == 0)
                throw new Exception("Body jsou totozne");
            a = a / l;
            b = b / l;
            c = -a * p1.X - b * p1.Y;
            c1 = b * p1.X - a * p1.Y;
            c2 = b * p2.X - a * p2.Y;
        }

        public TopologicalLine(Point2D p, double r, double angle)
        {
            a = -Math.Sin(angle);
            b = Math.Cos(angle);

            point1 = p;
            point2 = p+new Point2D() { X = r * b, Y = -r * a };

            c = -a * p.X - b * p.Y;
            c1 = b * p.X - a * p.Y;
            c2 = b * point2.X - a * point2.Y;
        }

        public override string ToString()
        {
            return Point1.ToString() + "\t" + Point2.ToString() + "\t" + a.ToString(CultureInfo.CreateSpecificCulture("en-GB")) + "\t" + b.ToString(CultureInfo.CreateSpecificCulture("en-GB")) + "\t" + c.ToString(CultureInfo.CreateSpecificCulture("en-GB")) + "\t" + c1.ToString(CultureInfo.CreateSpecificCulture("en-GB")) + "\t" + c2.ToString(CultureInfo.CreateSpecificCulture("en-GB"));
        }
    }
}
