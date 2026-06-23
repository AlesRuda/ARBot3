using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public abstract class GraphStateBase : ICloneable
    {
        public NavigationBase Owner { get; private set; }

        public GraphStateBase(NavigationBase owner)
        {
            this.Owner = owner;
        }
        public double X { get; set; } = 0;
        public double Y { get; set; } = 0;
        public double FromX { get; set; } = 0;
        public double FromY { get; set; } = 0;
        public double Length => Math.Sqrt(Math.Pow(X - FromX, 2) + Math.Pow(Y - FromY, 2));

        public abstract GraphStateBase Clone();
        /// <summary>
        /// kvadrat minimalni vzdalenosti, kdy je povazovano za dosazeni cile
        /// </summary>
        public abstract double MinDist2 { get; }

        /// <summary>
        /// minimalni vzdalenosti, kdy je povazovano za dosazeni cile
        /// </summary>
        public abstract double MinDist { get; }

        /// <summary>
        /// minimalni prostor pro prujezd
        /// </summary>
        public virtual double SafeZone => 0.4;

        object ICloneable.Clone()
        {
            return Clone();
        }

        public abstract bool Collision(GraphStateBase from, double safeZone);

        public abstract bool IsCollision {get;}

        //public Brush Brush
        //{
        //    get
        //    {
        //        return IsCollision ? Brushes.Red : Brushes.Blue;
        //    }
        //}

        public override string ToString()
        {
            return string.Format("{4}:{0}, {1}->{2}, {3}", FromX, FromY, X, Y, IsCollision);
        }
    }
}
