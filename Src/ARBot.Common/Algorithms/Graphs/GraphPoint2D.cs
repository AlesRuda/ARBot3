using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Graphs
{
    public class GraphPoint2D:Graph
    {
        public override double Distance(Vertex v1, Vertex v2)
        {
            double x1= ((Vertex2D)v1).X;
            double y1 = ((Vertex2D)v1).Y;
            double x2 = ((Vertex2D)v2).X;
            double y2 = ((Vertex2D)v2).Y;

            return Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));
        }
    }
}
