using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Algorithms.Graphs
{
    public class Vertex2D:Vertex
    {
        public double X;
        public double Y;

        public override string ToString()
        {
            return string.Format("{3}, {4}, [{0:N2}, {1:N2}], {2:N2}", X, Y, Distance, Final, DistanceCalculated);
        }
    }
}
