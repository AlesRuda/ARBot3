using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Predstavuje obal robotu.
    /// V bode [0, 0] je krucnice o r1.
    /// Ve vzdalenosti length od [0, 0] je krucnice o r2.
    /// Kruznice jsou spojeny tecnami.
    /// </summary>
    [Obsolete("Use Colider2 insted")]
    public class Collider
    {
        public class Line
        {
            double dx, dy;
            double ax, ay;

            public Line(Point2D a, Point2D b)
            {
                ax = a.X;
                ay = a.Y;

                dx = b.X - ax;
                dy = b.Y - ay;
            }

            /// <summary>
            /// z Bodu [0, 0] do [x, y]
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            public Line(double x, double y)
            {
                dx = x;
                dy = y;
            }
            /// <summary>
            /// Parametr kde se protina this a l
            /// 0 pro bod a 1 pro bod b
            /// </summary>
            /// <param name="l"></param>
            /// <returns></returns>
            public double GetT(Line l)
            {
                return ((ay - l.ay) * dx + (l.ax - ax) * dy) / (l.dy * dx - l.dx * dy);
            }

        }

        private double diff = 0.001;
        private double length;
        private double r1;
        private double r2;
        private double angle;
        private Point2D A, B, C, D, E, X;
        private Line BC, CD, DE, EB;


        public Collider(double length, double r1, double r2, double angle)
        {
            double pi2 = Math.PI / 2;
            this.length = length;
            this.r1 = r1;
            this.r2 = r2;
            this.angle = angle;
            X = new Point2D(-diff * Math.Cos(angle), -diff * Math.Sin(angle));
            A = new Point2D((length + diff) * Math.Cos(angle), (length + diff) * Math.Sin(angle));
            B = new Point2D(A.X + r2 * Math.Cos(angle - pi2), A.Y + r2 * Math.Sin(angle - pi2));
            C = new Point2D(X.X + r1 * Math.Cos(angle - pi2), X.Y + r1 * Math.Sin(angle - pi2));
            D = new Point2D(X.X + r1 * Math.Cos(angle + pi2), X.Y + r1 * Math.Sin(angle + pi2));
            E = new Point2D(A.X + r2 * Math.Cos(angle + pi2), A.Y + r2 * Math.Sin(angle + pi2));
            BC = new Line(B, C);
            CD = new Line(C, D);
            DE = new Line(D, E);
            EB = new Line(E, B);
        }

        /// <summary>
        /// bod [x, y] je uvnitr tvaru coluderu. 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool Inside(double x, double y)
        {
            double t;

            Line l = new Line(x, y);

            t = CD.GetT(l);
            if (t < 1 && t > 0)
                return r1 >= Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2));

            t = EB.GetT(l);
            if (t < 1 && t > 0)
                return r2 >= Math.Sqrt(Math.Pow(x - A.X, 2) + Math.Pow(y - A.Y, 2));

            t = BC.GetT(l);
            if (t < 1 && t > 0)
                return false;

            t = DE.GetT(l);
            if (t < 1 && t > 0)
                return false;

            return true;
        }
/*
        public IEnumerator<Point2D> GetEnumerator(double resolutin)
        {
            Point2D[] ns = new Point2D[]
            {
                new Point2D(-resolutin, 0),
                new Point2D(0, -resolutin),
                new Point2D(resolutin, 0),
                new Point2D(0, resolutin)
            };
            Queue<Point2D> q = new Queue<Point2D>();
            Dictionary<Point2D, Point2D> dic = new Dictionary<Point2D, Point2D>();
            dic.Add(new Point2D(0, 0), new Point2D(0, 0));
            q.Enqueue(new Point2D(0, 0));
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                yield return p;
                foreach(var n in ns)
                {
                    var p1 = p + n;
                    if(Inside(p1.X, p1.Y) && !dic.ContainsKey(p1))
                    {
                        dic.Add(p1, p1);
                        q.Enqueue(p1);
                    }
                }
            }
        }*/
    }
}
