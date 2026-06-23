using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Voronoi
{
    public class Halfedge
    {
        public Halfedge ELleft, ELright;
        public Edge ELedge;
        public bool deleted;
        public int ELpm;
        public Site vertex;
        public double ystar;
        public Halfedge PQnext;

        public Halfedge()
        {
            PQnext = null;
        }
    }
}
