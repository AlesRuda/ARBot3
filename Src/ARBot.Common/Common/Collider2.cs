using ARBot.Common.Logs;
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
    public class Collider2
    {
        private double length;
        private double angle;

        private bool includeR1;
        private double r1;
        private double r2;
        private double r12;
        private double r22;
        private Point2D A, B, C, D, E, X;
        private Line2D BC, CD, DE, EB;


        public Collider2(double length, double r1, double r2, double angle, bool includeR1 = true)
        {
            this.length = length;
            this.angle = angle;
            this.includeR1 = includeR1;

            double pi2 = Math.PI / 2;
            this.r1 = r1;
            this.r2 = r2;
            this.r12 = r1*r1;
            this.r22 = r2*r2;
            X = new Point2D(0, 0);
            A = new Point2D(length * Math.Cos(angle), length * Math.Sin(angle));
            B = new Point2D(X.X + r1 * Math.Cos(angle - pi2), X.Y + r1 * Math.Sin(angle - pi2));
            C = new Point2D(A.X + r2 * Math.Cos(angle - pi2), A.Y + r2 * Math.Sin(angle - pi2));
            D = new Point2D(A.X + r2 * Math.Cos(angle + pi2), A.Y + r2 * Math.Sin(angle + pi2));
            E = new Point2D(X.X + r1 * Math.Cos(angle + pi2), X.Y + r1 * Math.Sin(angle + pi2));
            BC = new Line2D(B, C);
            CD = new Line2D(C, D);
            DE = new Line2D(D, E);
            EB = new Line2D(E, B);
        }

        public Collider2(Point2D from, Point2D to, double r1, double r2, bool includeR1=true)
        {
            this.length = (from-to).Length;
            this.angle = (from - to).Angle;
            this.includeR1 = includeR1;

            double pi2 = Math.PI / 2;
            this.r1 = r1;
            this.r2 = r2;
            this.r12 = r1 * r1;
            this.r22 = r2 * r2;
            var l = new Line2D(from, to);
            var angle = l.Angle;
            X = from;
            A = to;
            B = new Point2D(X.X + r1 * Math.Cos(angle - pi2), X.Y + r1 * Math.Sin(angle - pi2));
            C = new Point2D(A.X + r2 * Math.Cos(angle - pi2), A.Y + r2 * Math.Sin(angle - pi2));
            D = new Point2D(A.X + r2 * Math.Cos(angle + pi2), A.Y + r2 * Math.Sin(angle + pi2));
            E = new Point2D(X.X + r1 * Math.Cos(angle + pi2), X.Y + r1 * Math.Sin(angle + pi2));
            BC = new Line2D(B, C);
            CD = new Line2D(C, D);
            DE = new Line2D(D, E);
            EB = new Line2D(E, B);
        }

        /// <summary>
        /// bod [x, y] je uvnitr tvaru colideru. 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public bool Inside(double x, double y)
        {
            return Inside(new Point2D(x, y));
        }

        /// <summary>
        /// bod p je uvnitr tvaru colideru. 
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public bool Inside(Point2D p)
        {
            if (r12 >= Math.Pow(p.X - X.X, 2) + Math.Pow(p.Y - X.Y, 2))
                return includeR1;

            if (p.IsLeft(BC) < 0)
                return false;

            if (p.IsLeft(DE) < 0)
                return false;

            if (p.IsLeft(CD) > 0 && p.IsLeft(EB) > 0)
                return true;

            if (r22 >= Math.Pow(p.X - A.X, 2) + Math.Pow(p.Y - A.Y, 2))
                return true;
            return false;
        }

        public ColliderMsg ToLogMessage()
        {
            return new ColliderMsg(length, r1, r2, angle);
        }
    }
}
