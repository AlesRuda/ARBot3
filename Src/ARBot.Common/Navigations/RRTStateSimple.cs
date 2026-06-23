using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace ARBot.Common.Navigations
{
    public class RRTStateSimple : TreeStateBase
    {
        private double orientation = 1;
        public RRT RRT => Owner as RRT;

        /// <summary>
        /// Polovina rozchodu
        /// </summary>

        public RRTStateSimple(RRT rrt):base(rrt)
        {
        }

        public override double MinDist2 => 0.25;
        public override double MinDist => 0.5;

        public override bool Collision(GraphStateBase from, double safeZone)
        {
                double x = from.X;
                double y = from.Y;
                double r = Math.Sqrt(Math.Pow(x - X, 2) + Math.Pow(y - Y, 2));
                double l = r + 0.2;
            //                Debug.WriteLine(string.Format("Reflex: t={0}, l={1}, x={2}, s={3}", t, l, state.Model.CurrentState.X, s));
                Point2D f = new Point2D(from.X, from.Y);
                var c = new Collider2(f, new Point2D(X,Y) , SafeZone - 0.1 + safeZone, SafeZone - 0.1 + safeZone);

                return RRT.ObstaclesTree.NearestNeighbors(new double[] { x, y }, 1000, l + .4 + safeZone).Any(p =>
                {
                    return c.Inside(p);
                });
        }

        public override GraphStateBase NewState(double x, double y)
        {
            double dy = y - this.Y;
            double dx = x - this.X;
            double r = Math.Sqrt(Math.Pow(dy, 2) + Math.Pow(dx, 2));
            double r1 = Math.Max(Math.Min(r, MinDist), -MinDist);

            if(r==0)
            {
                r = 1;
                r1 = 1;
            }
            var newM = Clone() as RRTStateSimple;

            newM.orientation = Math.Atan2(dy, dx);
            newM.X += r1/r *dx;
            newM.Y += r1 / r * dy;

            return newM;
        }

        public override GraphStateBase Clone()
        {
            var m = new RRTStateSimple(RRT);
            m.X = X;
            m.Y = Y;
            return m;
        }
    }
}
