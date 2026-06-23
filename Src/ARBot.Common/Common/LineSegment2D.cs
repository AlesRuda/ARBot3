using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Usecka v 2D prostoru
    /// </summary>
    public class LineSegment2D
    {
        public LineSegment2D(Point2D start, Point2D end)
        {
            this.start = start;
            this.end = end;
            Line = new Line2D(start, end);
        }

        public Line2D Line { get; private set; }
        /// <summary>
        /// Krajni body segmentu
        /// </summary>
        Point2D start, end;

        /// <summary>
        /// Krajni bod segmentu
        /// </summary>
        public Point2D Start
        {
            get
            {
                return start;
            }
            set
            {
                if (!start.Equals(value))
                {
                    start = value;
                    Line = new Line2D(start, end);
                }
            }
        }

        /// <summary>
        /// Krajni bod segmentu
        /// </summary>
        public Point2D End
        {
            get
            {
                return end;
            }
            set
            {
                if (!end.Equals(value))
                {
                    end = value;
                    Line = new Line2D(start, end);
                }
            }
        }

        /// <summary>
        /// Spocte prusecik usecky s useckou ls.
        /// </summary>
        /// <param name="ls">Usecka</param>
        /// <returns></returns>
        public Point2D? Intersection(LineSegment2D ls)
        {
            var s1_x = end.X - start.X;
            var s1_y = end.Y - start.Y;
            var s2_x = ls.end.X - ls.start.X;
            var s2_y = ls.end.Y - ls.start.Y;

            var s = (-s1_y * (start.X - ls.start.X) + s1_x * (start.Y - ls.start.Y)) / (-s2_x * s1_y + s1_x * s2_y);
            var t = (s2_x * (start.Y - ls.start.Y) - s2_y * (start.X - ls.start.X)) / (-s2_x * s1_y + s1_x * s2_y);

            if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
                return new Point2D(start.X + (t * s1_x), start.Y + (t * s1_y));

            return null;
        }
        /// <summary>
        /// Spocte prusecik usecky s kolmici jdouci bode p.
        /// </summary>
        /// <param name="ls">Usecka</param>
        /// <returns></returns>
        public Point2D? Intersection(Point2D p)
        {
            var d21 = end - start;
            var d = p - start;

            double r2 = d21.X * d21.X + d21.Y * d21.Y;
            if (r2 == 0)
                return null;
            var pos = (d21.X * d.X + d21.Y * d.Y) / r2;
            if(pos>=0 && pos<=1)
                return new Point2D(start.X + pos * d21.X, start.Y + pos * d21.Y);
            return null;
        }
    }
}
