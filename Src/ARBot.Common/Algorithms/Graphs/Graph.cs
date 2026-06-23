using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Algorithms.Graphs
{
    public class Graph
    {
        public List<Vertex> Vertexes { get; set; }
        public List<Edge> Edges { get; set; }

        public Graph()
        {
            Vertexes = new List<Vertex>();
            Edges = new List<Edge>();
        }

        /// <summary>
        /// Inicializuje priznaky pro vypocet nejkratsi cesty.
        /// </summary>
        /// <returns></returns>
        public void Init()
        {
            foreach (Vertex v in new List<Vertex>(Vertexes))
            {
                v.Distance = 0;
                v.Final = false;
                v.Target = false;
                v.DistanceCalculated = false;
                v.Previous = null;
            }
        }

        /// <summary>
        /// Vzdalenost mezi dvema vrcholy
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        public virtual double Distance(Vertex v1, Vertex v2)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Prusecik hrany e s kolmici vrcholem v.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="v"></param>
        /// <param name="ipos">Relativni pozice pruseciku vuci zacatku (0) a konci (1) hrany.</param>
        /// <returns></returns>
        public virtual Vertex Intersect(Edge e, Vertex v, out double ipos)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        /// Vrati nejblizsi hranu k bodu v. 
        /// </summary>
        /// <param name="v">Bod ke kteremu hledame hranu</param>
        /// <param name="dist">Vzdalenost bodu od cesty</param>
        /// <param name="edgeSelector">Vybira prohledavane hrany</param>
        /// <returns></returns>
        public Edge GetNearestEdge(Vertex v, out double dist, Func<Edge, bool> edgeSelector)
        {
            Edge ret = null;
            double d = double.MaxValue, d1;
            double ipos;
            dist = 0;
            foreach (Edge e in edgeSelector == null ? Edges : Edges.Where(i => edgeSelector(i)))
            {
                Vertex i = Intersect(e, v, out ipos);
                if (ipos >= 0 && ipos <= 1)
                {
                    d1 = Distance(i, v);
                    if (d > d1 || ret == null)
                    {
                        d = d1;
                        ret = e;
                        dist = d1;
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// Najde nejblizsi vrchol vrcholu v
        /// </summary>
        /// <param name="v"></param>
        /// <param name="dist"></param>
        /// <param name="all"></param>
        /// <returns></returns>
        public Vertex GetNearestVertex(Vertex v, double pathWeigh, double distWeight, out double dist, Func<Vertex, bool> vertexSelector)
        {
            Vertex point = null;
            double d = double.MaxValue, d1;
            dist = 0;
            foreach (Vertex cp in vertexSelector == null ? Vertexes : Vertexes.Where(i => vertexSelector(i)))
            {
                d1 = pathWeigh*cp.Distance + distWeight*Distance(cp, v);
                if (d > d1 || point == null)
                {
                    d = d1;
                    point = cp;
                    dist = d;
                }
            }
            return point;
        }

        /// <summary>
        /// Najde vrchol s nejkratsi cestou k vrcholu v
        /// </summary>
        /// <param name="v"></param>
        /// <param name="dist"></param>
        /// <param name="all"></param>
        /// <returns></returns>
        public Vertex GetShortestVertex(Vertex v, out double dist, Func<Vertex, bool> vertexSelector)
        {
            Vertex point = null;
            double d = double.MaxValue, d1;
            dist = 0;
            foreach (Vertex cp in vertexSelector == null ? Vertexes : Vertexes.Where(i => vertexSelector(i)))
            {
                d1 = cp.Distance+Distance(cp, v);
                if (d > d1 || point == null)
                {
                    d = d1;
                    point = cp;
                    dist = d;
                }
            }
            return point;
        }

        /// <summary>
        /// Najde predchazejici vrchol
        /// </summary>
        /// <param name="v"></param>
        /// <param name="edgeSelector"></param>
        /// <returns></returns>
        public Vertex GetPreviousVertex(Vertex v)
        {
            return v.Previous;
        }

        /// <summary>
        /// Spocte vzdalensti do vsech vrcholu.
        /// Pred volanim je nutne nastavit pocatecni body (priznak Final=false, DistanceCalulated=true a Distance=pocatecni vzdalenost, typicky 0) 
        /// </summary>
        public void CalculateDistances()
        {
            Vertex vm;
            do
            {
                vm = null;
                double d = 0;
                foreach (Vertex v in Vertexes.Where(i => !i.Final && i.DistanceCalculated))
                {
                    if (vm == null || v.Distance < d)
                    {
                        vm = v;
                        d = v.Distance;
                    }
                }
                if (vm != null)
                {
                    vm.Final = true;
                    foreach (Edge e in vm.Edges.Where(i => i.BiDirectional || vm == i.From))
                    {
                        Vertex v2 = (vm == e.From) ? e.To : e.From;
                        if (!v2.DistanceCalculated || (!v2.Final && vm.Distance + e.Length < v2.Distance))
                        {
                            v2.Distance = vm.Distance + e.Length;
                            v2.DistanceCalculated = true;
                            v2.Previous = vm;
                        }
                    }
                }
            }
            while (vm != null);
        }
    }
}
