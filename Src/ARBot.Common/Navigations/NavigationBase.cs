using ARBot.Common.Common;
using ARBot.Common.Logs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Predek pro navigace 
    /// </summary>
    public class NavigationBase: DataObject
    {
        public GraphStateBase Target { get; protected set; }
        public virtual GraphStateBase Start { get; set; }
        protected bool ObstaclesChanged = false;
        private List<Point2D> obstacles;
        public virtual List<Point2D> Obstacles
        {
            get
            {
                return obstacles;
            }
            set
            {
                if(obstacles!=value)
                {
                    ObstaclesChanged = true;
                    obstacles = value;
                }
            }
        }

        public virtual GridNavigationResult Process(GraphStateBase target)
        {
            return null;
        }

        /// <summary>
        /// Projekce bodu <paramref name="p"/> na (nekonecnou) primku prochazejici body p0 a p1 =
        /// pata kolmice. Parametr pos je relativni pozice na primce mezi p0 (pos=0) a p1 (pos=1),
        /// NENI orezany do [0,1] (muze byt i mimo usecku).
        /// </summary>
        /// <param name="p0">Prvni bod na primce</param>
        /// <param name="p1">Druhy bod na primce</param>
        /// <param name="p">Bod na kolmici</param>
        /// <param name="pos">Relativni pozice pruseciku usecky s kolmici z bodu p. 0=na bodu p0, 1=na bodu p1</param>
        /// <returns></returns>
        protected static Point2D? ProjectOntoLine(
            Point2D p0,
            Point2D p1,
            Point2D p,
            out double pos)
        {
            var d21 = p1 - p0;
            var d = p - p0;

            double r2 = d21.X * d21.X + d21.Y * d21.Y;
            if (r2 == 0)
            {
                pos = -1;
                return null;
            }
            pos = (d21.X * d.X + d21.Y * d.Y) / r2;
            return new Point2D(p0.X + pos * d21.X, p0.Y + pos * d21.Y);
        }

        /// <summary>
        /// Informace o pruseciku
        /// </summary>
        public class IntersectI
        {
            /// <summary>
            /// Zkoumany bod
            /// </summary>
            public Point2D P;
            /// <summary>
            /// Prusecik kolmice prochazejici P na usecku.
            /// </summary>
            public Point2D? I;
            /// <summary>
            /// Relativni pozice pruseciku usecky s kolmici z bodu p. 0=na bodu From, 1=na bodu To
            /// </summary>
            public double Pos;
            /// <summary>
            /// Vzdalenost p od (from, to)
            /// </summary>
            public double Length;
        }

        /// <summary>
        /// Spocte zda prekazka je bliz trase (from, to) nez safeDistance.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        protected static IEnumerable<IntersectI> IntersectInfo(IEnumerable<Point2D> l, Point2D from, Point2D to)
        {
            var line=new Line2D(from, to);
            foreach (var p in l)
            {
                double pos;
                var i = ProjectOntoLine(from, to, p, out pos);
                if (i != null && pos >= 0 && pos <= 1)
                    yield return new IntersectI() { P = p, I = i, Pos = pos, Length = (i.Value - p).Length };
            }
        }



        /// <summary>
        /// Spocte zda prekazka je bliz trase (from, to) nez safeDistance.
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        protected bool Colision(IEnumerable<Point2D> l, Point2D from, Point2D to, double safeDistance)
        {
            foreach (var i in IntersectInfo(l, from, to))
            {
                if (i.Pos >= 0 && i.Pos <= 1 && i.Length < safeDistance)
                    return true;
            }

            return false;
        }

        public virtual IEnumerable<Message> ToLogMessages()
        {
            return null;
        }
    }
}
