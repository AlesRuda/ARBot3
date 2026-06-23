using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Primka v 2D
    /// a*x+b*y+c=0
    /// </summary>
    public class Line2D:Vector2D
    {
        public double A
        {
            get
            {
                return -Y;
            }
            set
            {
                Y = -value;
            }
        }
        public double B
        {
            get
            {
                return X;
            }
            set
            {
                X = value;
            }
        }
        public double C;

        /// <summary>
        /// Nad polem bodu spocte linearni regresi
        /// x=a*y+b;
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        public static Line2D LinearRegesion(IEnumerable<Point2D> points)
        {
            double x, y;
            double n = 0;
            double sxy = 0;
            double sx = 0;
            double sy = 0;
            double sx2 = 0;
            double sy2 = 0;
            foreach (var point in points)
            {
                x = point.X;
                y = point.Y;

                n += 1;

                sxy += x * y;
                sx += x;
                sy += y;
                sx2 += x * x;
                sy2 += y * y;
            }
            if (n == 0)
                return null;
            double dx = (n * sx2 - sx * sx);
            double dy = (n * sy2 - sy * sy);

            if (Math.Abs(dx) > Math.Abs(dy))
            {
                if (dx == 0)
                    return null;
                return new Line2D(-(n * sxy - sx * sy) / dx, 1, -(sx2 * sy - sx * sxy) / dx);
            }
            else
            {
                if (dy == 0)
                    return null;
                return new Line2D(-1, (n * sxy - sx * sy) / dy, (sy2 * sx - sy * sxy) / dy);
            }
        }


        /// <summary>
        /// Primka z parametru
        /// </summary>
        public Line2D(double a, double b, double c)
        {
            Y = -a;
            X = b;
            C = c;
        }

        /// <summary>
        /// Primka z parametru
        /// </summary>
        public Line2D(RegresionMode m, double a, double b)
        {
            Y = m == RegresionMode.X ? a : 1;
            X = m == RegresionMode.X ? 1 : a;
            C = m == RegresionMode.X ? -b : b;
        }

        /// <summary>
        /// Primka ze dvou bodu
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        public Line2D(Point2D from, Point2D to)
        {
            Y = (to.Y - from.Y);
            X = (to.X - from.X);

            if (Length == 0)
                throw new Exception("Body jsou totozne");
            C = Y * from.X - X * from.Y;
        }

        /// <summary>
        /// Primka z normaly prochazejici bodem
        /// </summary>
        /// <param name="normal"></param>
        /// <param name="p"></param>
        public Line2D(Vector2D normal, Point2D p)
        {
            Y = -normal.X;
            X = normal.Y;

            C = Y * p.X - X * p.Y;
        }

        /// <summary>
        /// Primka v opacnem smeru 
        /// </summary>
        /// <param name="normal"></param>
        /// <param name="p"></param>
        public Line2D Reverse()
        {
            return new Line2D(-A, -B, -C);
        }

        /// <summary>
        /// Kolmice bodem (ve smeru normaly)
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public Line2D Perpendicular(Point2D p)
        {
            var n1 = new Vector2D(-X, -Y);
            return new Line2D(n1, p);
        }

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
            if (Math.Abs(a1) > Math.Abs(b1))
            {
                var y = (a2 * c1 - c2 * a1) / (b2 * a1 - a2 * b1);
                var x = -(b1 * y + c1) / a1;
                return new Point2D(x, y);
            }
            else
            {
                var x = (b2 * c1 - c2 * b1) / (a2 * b1 - b2 * a1);
                var y = -(a1 * x + c1) / b1;
                return new Point2D(x, y);
            }
        }

        /// <summary>
        /// Vzdalenost bodu [x, y] od prinky
        /// a*x+b*y+c=0
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public static double Distance(double a, double b, double c, double x, double y)
        {
            return Math.Abs(a * x + b * y + c) / Math.Sqrt(a * a + b * b);
        }
        /// <summary>
        /// Prusecik dvou primek 
        /// a*x+b*y+c=0
        /// </summary>
        /// <param name="l"></param>
        /// <returns></returns>
        public Point2D Intersection(Line2D l)
        {
            return Intersection(-Y, X, C, -l.Y, l.X, l.C);
        }
        /// <summary>
        /// Prusecik primky s normalou k ni jdouci bodem 
        /// a*x+b*y+c=0
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public Point2D Intersection(Point2D p)
        {
            var l = Perpendicular(p);
            return Intersection(-Y, X, C, -l.Y, l.X, l.C);
        }
        /// <summary>
        /// Vzdalenost bodu p od prinky
        /// a*x+b*y+c=0
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public double Distance(Point2D p)
        {
            return Math.Abs(-Y * p.X + X * p.Y + C) / Length;
        }
        /// <summary>
        /// Vzdalenost bodu p od prinky
        /// a*x+b*y+c=0
        /// </summary>
        /// <returns></returns>
        public double Distance(double x, double y)
        {
            return Math.Abs(-Y * x + X * y + C) / Length;
        }
        /// <summary>
        /// Vzdalenost dvou primek, musi mit shodnou normalu
        /// Pokud je this ve smeru normaly je vysledek kladny.
        /// </summary>
        /// <param name="l"></param>
        /// <returns></returns>
        public double Distance(Line2D l)
        {
            if (X != l.X || Y != l.Y)
                throw new Exception("Rozdilne normaly.");
            return (l.C-C) / Length;
        }

        /// <summary>
        /// Rovnobezka ve vzdalenosti dist a stejneho smeru
        /// </summary>
        /// <param name="dist"></param>
        /// <returns></returns>
        public Line2D Parallel(double dist)
        {
            return new Line2D(-Y, X, C+dist*Length);
        }

        /// <summary>
        /// Spocte sosuradnici X na zaklade y
        /// </summary>
        /// <param name="y"></param>
        /// <returns></returns>
        public double XVal(double y)
        {
            return (X * y + C) / Y;
        }
        /// <summary>
        /// Spocte Y na zaklade x
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public double YVal(double x)
        {
            return (Y * x - C) / X;
        }

        public RegresionMode Mode
        {
            get
            {
                if (Math.Abs(X) > Math.Abs(Y))
                    return RegresionMode.X;
                else
                    return RegresionMode.Y;
            }
        }

        public Point2D[] CircleIntersect(Point2D center, double r)
        {
            /*
            double d = A * center.X + B * center.Y + C;
            double dd = d * d;
            double BB = B * B;
            double rr=r*r;
            double DD = BB * rr - dd + rr;
            if (DD < 0)
                return new Point2D[0];
            double D = Math.Pow(DD, 0.5);

            if(DD==0)
            {
                double y = center.Y - (B * d) / (BB + 1);
                return new Point2D [] { new Point2D(XVal(y), y)};
            }

            double y1 = center.Y - (B * d + D) / (BB + 1);
            double y2 = center.Y - (B * d - D) / (BB + 1);
            return new Point2D[] { new Point2D(XVal(y1), y1), new Point2D(XVal(y2), y2) };
            */

            if (A == 0)
            {
                double y = -C / B;
                double v = r * r - (y - center.Y) * (y - center.Y);

                if (v < 0)
                    return null;

                double x1 = center.X+Math.Sqrt(v);
                double x2 = center.X - Math.Sqrt(v);

                return new Point2D[] { new Point2D(x1, y), new Point2D(x2, y) };

            }
            else
            {
                double CC = C * C;
                double BB = B * B;
                double AA = A * A;
                double rr = r * r;

                double xc = center.X;
                double yc = center.Y;

                double xc2 = xc * xc;
                double yc2 = yc * yc;

                double dd = AA + BB;

                double v = AA * rr - AA * xc2 - 2 * A * B * xc * yc - 2 * A * C * xc + BB * rr - BB * yc2 - 2 * B * C * yc - CC;

                if (v < 0)
                    return null;

                double x1 = (B * (B * C + BB * yc + A * Math.Sqrt(v) + A * B * xc)) / (A * dd) - (C + A * xc + B * yc) / A;
                double x2 = (B * (B * C + BB * yc - A * Math.Sqrt(v) + A * B * xc)) / (A * dd) - (C + A * xc + B * yc) / A;

                double y1 = -(B * C + BB * yc + A * Math.Sqrt(v) + A * B * xc) / dd;
                double y2 = -(B * C + BB * yc - A * Math.Sqrt(v) + A * B * xc) / dd;

                return new Point2D[] { new Point2D(x1, y1), new Point2D(x2, y2) };
            }
        }
    }
}
