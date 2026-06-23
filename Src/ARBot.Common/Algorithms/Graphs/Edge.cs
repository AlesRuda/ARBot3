using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Algorithms.Graphs
{
    public class Edge
    {
        public object Tag;
        private Vertex from;
        private Vertex to;
        public Vertex From
        {
            get => from;
            set
            {
                if(from!=value)
                {
                    if (from != null)
                        from.Edges.Remove(this);
                    from = value;
                    if (from != null)
                        from.Edges.Add(this);
                }
            }
        }
        public Vertex To
        {
            get => to;
            set
            {
                if (to != value)
                {
                    if (to != null)
                        to.Edges.Remove(this);
                    to = value;
                    if (to != null)
                        to.Edges.Add(this);
                }
            }
        }
        public bool BiDirectional=true;
        public double Length;

        public override string ToString()
        {
            return string.Format("{0}->{1}", From, To);
        }
    }
}
