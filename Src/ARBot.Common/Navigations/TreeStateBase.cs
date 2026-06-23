using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    public abstract class TreeStateBase : GraphStateBase
    {
        public TreeStateBase(GraphNavigationBase owner):base(owner)
        {
        }
        public double Orientation = 0;
        private double parentDistance = 0;
        public virtual double Distance => parentDistance + Length;
        public override bool IsCollision => Collision(Parent ?? this, 0);

        public abstract GraphStateBase NewState(double x, double y);

        private TreeStateBase parent;
        public TreeStateBase Parent
        {
            get
            {
                return parent;
            }
            set
            {
                if (parent != null)
                    parent.Children.Remove(this);
                parent = value;
                UpdateDistance();
                if (parent != null)
                    parent.Children.Add(this);
            }
        }

        public void UpdateDistance()
        {
            parentDistance = parent?.Distance ?? 0;
        }
        public List<TreeStateBase> Children = new List<TreeStateBase>();

    }
}
