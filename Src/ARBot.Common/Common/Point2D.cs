using ARBot.Common.Common;
using ClipperLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common
{
    /// <summary>
    /// jeste existuje Point2DF, poboji pouziva float
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point2D : IEquatable<Point2D>
    {
        public float X;
        public float Y;

        public static Point2D FromPolar(float length, float angle)
        {
            return new Point2D(length*Math.Cos(angle), length*Math.Sin(angle));
        }
        public Point2D(double x, double y)
        {
            X = (float)x;
            Y = (float)y;
        }
        public Point2D(float x, float y)
        {
            X = x;
            Y = y;
        }
        public Point2D(MathNet.Numerics.LinearAlgebra.Matrix<double> m)
        {
            if(m.RowCount != 2 || m.ColumnCount != 1)
                throw new ArgumentException("Matrix must be 2x1");
            X = (float)m[0, 0];
            Y = (float)m[1, 0];
        }
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}, {1}", X, Y);
        }

        public static Point2D operator +(Point2D a, Point2D b)
        {
            Point2D x;
            x.X = a.X + b.X;
            x.Y = a.Y + b.Y;
            return x;
        }

        public static Point2D operator -(Point2D a)
        {
            Point2D x;
            x.X = -a.X;
            x.Y = -a.Y;
            return x;
        }

        /*        public static Point2D operator -(Point2D a, Point2D b)
                {
                    Point2D x;
                    x.X = a.X - b.X;
                    x.Y = a.Y - b.Y;
                    return x;
                }
                */
        public static Point2D operator /(Point2D a, double b)
        {
            Point2D x;
            x.X = a.X / (float)b;
            x.Y = a.Y / (float)b;
            return x;
        }
        public static Point2D operator /(Point2D a, float b)
        {
            Point2D x;
            x.X = a.X / b;
            x.Y = a.Y / b;
            return x;
        }
        /// <summary>
        /// Vzdalenost od pocatku
        /// </summary>
        public double Distance
        {
            get
            {
                return Math.Sqrt(Math.Pow(X, 2) + Math.Pow(Y, 2));
            }
        }

        /// <summary>
        /// Testuje kde se nachazi this vzhledem k primce body p1 a p2
        /// </summary>
        /// <returns>
        /// >0 for this left of the line through P1 and P2
        /// =0 for this on the line
        /// &lt;0 for this right of the line
        /// </returns>
        public double IsLeft(Point2D p1, Point2D p2)
        {
            return ((p2.X - p1.X) * (Y - p1.Y)-(X - p1.X) * (p2.Y - p1.Y));
        }

        /// <summary>
        /// Testuje kde se nachazi this vzhledem k primce body p1 a p2
        /// </summary>
        /// <returns>
        /// >0 for this left of the line through P1 and P2
        /// =0 for this on the line
        /// &lt;0 for this right of the line
        /// </returns>
        public double IsLeft(Line2D l)
        {
            return (l.X * Y + l.C) - X * l.Y;
        }

        /// <summary>
        /// Je bod v polygonu
        /// </summary>
        /// <param name="poly"></param>
        /// <returns></returns>
        public bool IsInPoly(List<Point2D> poly)
        {
            return WindingNumberPoly(poly) != 0;
        }

            /// <summary>
            /// Winding number test for a point in a polygon.
            /// </summary>
            /// <param name="poly">Reprezentuje uzavreny polygon. Uzavren je automaticky propojenim posledniho a prvniho bodu.</param>
            /// <returns>the winding number (=0 only when this is outside poly)</returns>
            public int WindingNumberPoly(List<Point2D> poly)
        {
            int wn = 0;    // the  winding number counter
            int cnt = poly.Count;
            int i1;
            // loop through all edges of the polygon
            for (int i = 0; i < cnt; i++)
            {   // edge from V[i] to  V[i+1]
                i1 = (i + 1) == cnt ? 0 : i + 1;
                if (poly[i].Y <= Y)
                {          // start y <= P.y
                    if (poly[i1].Y > Y)      // an upward crossing
                        if (IsLeft(poly[i], poly[i1]) > 0)  // this left of  edge
                            ++wn;            // have  a valid up intersect
                }
                else
                {                        // start y > P.y (no test needed)
                    if (poly[i1].Y <= Y)     // a downward crossing
                        if (IsLeft(poly[i], poly[i1]) < 0)  // this right of  edge
                            --wn;            // have  a valid down intersect
                }
            }
            return wn;
        }
        /// <summary>
        /// Dela union dvou polygomu.
        /// Zjednodusena verze. Pripustne jsou pouze polynomy se 4 vrcholy.
        /// Prvni musi byt ten levy a druhy ten pravy. Vrcholy musi jit v poradi levy dolni, pravy dolni, pravy horni a levy horni
        /// </summary>
        /// <param name="polys"></param>
        /// <returns></returns>
        public static List<Point2D> PolyUnion(List<Point2D> left, List<Point2D> right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");

            if (left.Count == 4 && right.Count == 4)
            {
                List<Point2D> l = new List<Point2D>();
                l.Add(left[0]);
                l.Add(left[1]);
                l.Add(right[0]);
                l.Add(right[1]);
                l.Add(right[2]);
                l.Add(right[3]);
                l.Add(left[2]);
                l.Add(left[3]);
                return l;
            }
            else
            {
                Debug.WriteLine(string.Format("left.Count=={0} ale je pozadovano 4", left.Count));
                Debug.WriteLine(string.Format("right.Count=={0} ale je pozadovano 4", right.Count));
                return (left.Count > right.Count) ? left : right;
            }
        }

        public static List<Point2D> PolyUnion(IEnumerable<List<Point2D>> polys, double resolution)
        {
            Clipper c = new Clipper();
            var subs = new List<List<IntPoint>>();
            c.AddPaths(polys.Select(pol => pol.Select(p => new IntPoint(p.X / resolution, p.Y / resolution)).ToList()).ToList(), PolyType.ptSubject, true);
            c.AddPaths(polys.Select(pol => pol.Select(p => new IntPoint(p.X / resolution, p.Y / resolution)).ToList()).ToList(), PolyType.ptClip, true);
            var solution = new List<List<IntPoint>>();
            bool succeeded = c.Execute(ClipType.ctUnion, solution, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            if(succeeded)
                return solution[0].Select(p => new Point2D(p.X * resolution, p.Y * resolution)).ToList();
            return new List<Point2D>();
        }

        /// <summary>
        /// Porovnani podle hodnoty - dva body jsou shodne, pokud maji stejne X i Y.
        /// </summary>
        public bool Equals(Point2D other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is Point2D p && Equals(p);
        }

        public override int GetHashCode()
        {
            return X.GetHashCode()^Y.GetHashCode();
        }

        public static bool operator ==(Point2D a, Point2D b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Point2D a, Point2D b)
        {
            return !a.Equals(b);
        }
        public static Common.Vector2D operator -(Point2D a, Point2D b)
        {
            return new Common.Vector2D(a.X- b.X, a.Y- b.Y);
        }
        public static Point2D operator *(MathNet.Numerics.LinearAlgebra.Matrix<double> t, Point2D b)
        {
            if(t.RowCount != 2 || t.ColumnCount != 2)
                throw new ArgumentException("Matrix must be 2x2");
            return new Point2D(t[0, 0]*b.X+t[0, 1]*b.Y, t[1, 0] * b.X + t[1, 1] * b.Y);
        }

        /// <summary>
        /// Explicitni konverze na sloupcovy vektor 2x1 (MathNet). Inverzni k Point2D(Matrix).
        /// </summary>
        public static explicit operator MathNet.Numerics.LinearAlgebra.Matrix<double>(Point2D p)
        {
            return MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new double[,] { { p.X }, { p.Y } });
        }

    }
}
