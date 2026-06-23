using ARBot.Common.Algorithms.Graphs;
using ARBot.Common.Common;
using ARBot.Common.Logs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public abstract class GraphNavigationBase: NavigationBase
    {
        public GridNavigationResult Result { get; set; }
        /// <summary>
        /// Cesta od pocatku k cili
        /// </summary>
        public List<GraphStateBase> Ret { get; set; }

        public abstract IEnumerable<GraphStateBase> States { get; }
        public IEnumerable<Vertex> Vertexes { get; set; }

        /// <summary>
        /// Hleda posledni primy volny prujezd od prvniho non null prvku pole k nejakemu prvku.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="safeZone"></param>
        /// <returns></returns>
        private GridNavigationResult FindLastFree(List<GraphStateBase> list, double safeZone)
        {
            GraphStateBase f = null;
            GraphStateBase l = null;
            foreach (var m in list)
            {
                if (f == null)
                    f = m;
                else
                {
                    var r = m.Collision(f, safeZone);
                    if (!r)
                        l = m;
                }
            }
            if (l == null)
                return null;
            return new GridNavigationResult() { X = l.X, Y = l.Y, Direction = Math.Atan2(l.Y, l.X) };
        }
        /// <summary>
        /// Hleda smer kterym muzu jet.
        /// </summary>
        /// <param name="list"></param>
        /// <returns></returns>
        protected GridNavigationResult FindFree(List<GraphStateBase> list)
        {
            var r = FindLastFree(list, 0.2);
            if (r == null && list.Count > 0)
            {
                var v = list.FirstOrDefault(i => i.X != 0 && i.Y != 0);
                return new GridNavigationResult() { X = v.X, Y = v.Y, Direction = Math.Atan2(v.Y, v.X) };
            }
            return r;
        }
/*
        /// <summary>
        /// Vypocet navigace
        /// </summary>
        /// <param name="target">Cil vzhledem k robotu. Robot je umisten do pozice [0, 0]</param>
        /// <returns></returns>
        public abstract GridNavigationResult Process(GraphStateBase target);
        */
        public void GenObstacles(double x, double y)
        {
            var o = new List<Point2D>();
            for (double i = 0; i < 10; i += 0.5)
            {
                o.Add(new Point2D(-5, i + y));
                if (i < 3 || i > 5)
                    o.Add(new Point2D(3, i + y));
            }
            Obstacles = o;
        }
        Random r = new Random(0);

        public void RandomObstacles()
        {
            var o = new List<Point2D>();
            for (int i=0;i<40;i++)
            {
                o.Add(new Point2D(20*(r.NextDouble()-0.5), 20 * (r.NextDouble() - 0.5)));
            }
            Obstacles = o;
        }
        public abstract GraphNavigationMsg ToLogMessage();

        public override IEnumerable<Message> ToLogMessages()
        {
            return new List<Message>() { ToLogMessage() };
        }
    }
}
