using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public struct Point
    {
        public int X;
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public override string ToString()
        {
            return string.Format("[{0}, {1}]", X, Y);
        }

        public override int GetHashCode()
        {
            return X+47*Y;
        }

        public override bool Equals(object obj)
        {
            Point p = (Point)obj;
            return X == p.X && Y == p.Y;
        }

        /// <summary>
        /// Delka vektoru
        /// </summary>
        public double Length
        {
            get
            {
                return Math.Sqrt(X * X + Y * Y);
            }
        }


        public static Point operator -(Point x1, Point x2)
        {
            return new Point(x1.X - x2.X, x1.Y - x2.Y);
        }
        public static Point operator +(Point x1, Point x2)
        {
            return new Point(x1.X + x2.X, x1.Y + x2.Y);
        }
    }
}
