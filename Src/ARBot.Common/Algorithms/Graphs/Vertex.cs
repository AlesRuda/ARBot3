using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Algorithms.Graphs
{
    public class Vertex
    {
        public Vertex Previous;
        public List<Edge> Edges { get; private set; }

        public Vertex()
        {
            Edges = new List<Edge>();
        }

        public double Distance;
        /// <summary>
        /// Vzdalenost od pocatku je urcena a kratsi neexistuje
        /// </summary>
        public bool Final;
        public bool Target;
        /// <summary>
        /// Vzdalenost od pocatku je, ale mozna existuje kratsi
        /// </summary>
        public bool DistanceCalculated;
        public object Tag;
    }
}
