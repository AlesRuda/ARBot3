using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public class GraphState2D : GraphStateBase
    {
        public GraphState2D(NavigationBase owner) : base(owner)
        {
        }

        public override bool IsCollision => Collision(FromX, FromY, 0);

        public override double MinDist2 => throw new NotImplementedException();

        public override double MinDist => throw new NotImplementedException();

        public override GraphStateBase Clone()
        {
            var m = new GraphState2D(Owner);
            m.X = X;
            m.Y = Y;
            m.FromX = FromX;
            m.FromY = FromY;
            return m;
        }

        public bool Collision(double x, double y, double safeZone)
        {
            double r = Math.Sqrt(Math.Pow(x - X, 2) + Math.Pow(y - Y, 2));
            double l = r + 0.2;
            //                Debug.WriteLine(string.Format("Reflex: t={0}, l={1}, x={2}, s={3}", t, l, state.Model.CurrentState.X, s));
//            var c = new Collider2(l, SafeZone - 0.1 , SafeZone + safeZone, Math.Atan2(Y - y, X - x));
            var c = new Collider2(new Point2D(x, y), new Point2D(X, Y), SafeZone - 0.1, SafeZone + safeZone, false);

            if (Owner is VoronoiNavigation)
                return ((VoronoiNavigation)Owner).ObstaclesTree.NearestNeighbors(new double[] { x, y }, 1000, l + SafeZone + safeZone).Any(p =>
                {
                    return c.Inside(p);
                });
            else
                return Owner.Obstacles.Any(p =>
                {
                    return c.Inside(p);
                });
        }

        public override bool Collision(GraphStateBase from, double safeZone)
        {
            double x = from.X;
            double y = from.Y;
            return Collision(x, y, safeZone);
        }
        public override string ToString()
        {
            return string.Format("{4}, {5:N2}:{0:N2}, {1:N2}->{2:N2}, {3:N2}", FromX, FromY, X, Y, IsCollision, Length);
        }
    }
}
