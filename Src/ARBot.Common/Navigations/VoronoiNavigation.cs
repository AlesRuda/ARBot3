using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Algorithms.Graphs;
using ARBot.Common.KDTree;
using ARBot.Common.Logs;

namespace ARBot.Common.Navigations
{
    public class VoronoiNavigation : GraphNavigationBase
    {
        private double safeZoneAdd = 0.2;
        public IEnumerable<GraphStateBase> states;
        public override IEnumerable<GraphStateBase> States => states;

        private KDTree<Point2D> obstaclesTree = new KDTree<Point2D>(2);
        public KDTree<Point2D> ObstaclesTree => obstaclesTree;

        private List<Point2D> obstacles;
        public override List<Point2D> Obstacles
        {
            get
            {
                return obstacles;
            }
            set
            {
                if (obstacles != value)
                {
                    obstacles = value;
                    obstaclesTree = new KDTree<Point2D>(2);

                    foreach (var i in obstacles)
                    {
                        obstaclesTree.AddPoint(new double[] { i.X, i.Y }, i);
                    }
                }
            }
        }

        BenTools.Mathematics.VoronoiGraph voronoi;
        GraphPoint2D graph;
        public override GridNavigationResult Process(GraphStateBase target)
        {
            if (!(target is GraphState2D))
                throw new Exception("Nepodporovany target");

            Target = target;
            Ret = null;
            Result = null;
            states = null;
            Vertexes = null;
            graph = null;

            GraphStateBase sStart = target.Clone() as GraphStateBase;
            sStart.FromX = sStart.X = Start.X;
            sStart.FromY = sStart.Y = Start.Y;

            if (!target.Collision(sStart, safeZoneAdd))
            {
                Ret = new List<GraphStateBase>();
                Ret.Add(target);
                Ret[0].FromX = sStart.X;
                Ret[0].FromY = sStart.Y;
                Ret.Insert(0, sStart);

                return Result = FindFree(Ret);
            }
            else
            {
                if (Obstacles.Count > 2)
                {
                    var points = Obstacles.Select(i => new Point2D() { X = i.X, Y = i.Y }).ToList();
                    var b = points.Border();
                    b = b.Offset(b.Width, b.Height);

                    points.Add(b.LeftTop);
                    points.Add(b.RightTop);
                    points.Add(b.LeftBottom);
                    points.Add(b.RightBottom);

                    Point2DComparer cmp = new Point2DComparer(target.SafeZone / 10);

                    points = points.Distinct(cmp).ToList();

                    var v = BenTools.Mathematics.Fortune.ComputeVoronoiGraph(points.Select(i => new BenTools.Mathematics.Vector(i.X, i.Y)));
                    this.voronoi = v;
                    v = BenTools.Mathematics.Fortune.FilterVG(v, target.SafeZone * target.SafeZone * 4);

                    var edges = v.Edges.Where(e => e.VVertexA != BenTools.Mathematics.Fortune.VVInfinite && e.VVertexB != BenTools.Mathematics.Fortune.VVInfinite).ToList();

                    var statesDic = edges.ToDictionary(e => e, e => new GraphState2D(this) { X = e.VVertexA[0], Y = e.VVertexA[1], FromX = e.VVertexB[0], FromY = e.VVertexB[1] });

                    this.states = statesDic.Values.Concat(new[] { sStart }).ToList();

                    var vertexes = v.Vertizes.Where(i => i != BenTools.Mathematics.Fortune.VVInfinite).ToDictionary(i => i, i =>
                    {
                        GraphStateBase state = target.Clone();
                        state.X = i[0];
                        state.Y = i[1];
                        Vertex vv = new Vertex2D() { X = i[0], Y = i[1], Tag = state };
                        return vv;
                    });

                    GraphPoint2D g = new GraphPoint2D();
                    graph = g;
                    g.Vertexes = vertexes.Select(i => i.Value).ToList();
                    Vertexes = g.Vertexes;
                    g.Edges = edges.Select(i =>
                    {
                        Vertex from = vertexes[i.VVertexA];
                        Vertex to = vertexes[i.VVertexB];
                        return new Algorithms.Graphs.Edge() { From = from, To = to, Length = g.Distance(from, to), Tag = statesDic[i] };
                    }).ToList();

                    g.Init();

                    double dist;
                    Vertex vStart = new Vertex2D() { X = sStart.FromX, Y = sStart.FromY, Tag = sStart, Final = false, DistanceCalculated = true, Distance = 0 };

                    g.Vertexes.Add(vStart);

                    g.Edges.AddRange(g.Vertexes.Where(i => !((GraphStateBase)i.Tag).Collision(sStart, 0.0)).Select(i => new Algorithms.Graphs.Edge() { From = vStart, To = i, Length = g.Distance(vStart, i) *(((GraphStateBase)i.Tag).Collision(sStart, safeZoneAdd)?1.3:1) }));

                    g.CalculateDistances();

                    Vertex vTargetNearest = g.GetNearestVertex(new Vertex2D() { X = target.X, Y = target.Y }, 1, 1, out dist, (i) => i.Final && !target.Collision(i.Tag as GraphStateBase, 0));
                    if (vTargetNearest != null)
                        vTargetNearest = g.GetNearestVertex(new Vertex2D() { X = target.X, Y = target.Y }, 1, 1, out dist, (i) => i.Final);
                    if (vTargetNearest != null)
                    {
                        Ret = new List<GraphStateBase>();
                        Ret.Add(target);

                        while (vTargetNearest != null)
                        {
                            GraphState2D v2 = vTargetNearest.Tag as GraphState2D;
                            Ret[0].FromX = v2.X;
                            Ret[0].FromY = v2.Y;
                            Ret.Insert(0, v2);
                            vTargetNearest = g.GetPreviousVertex(vTargetNearest);
                        }

                        return Result = FindFree(Ret);
                    }
                }
                return null;
            }
        }

        public override GraphNavigationMsg ToLogMessage()
        {
            Point2DComparer cmp = new Point2DComparer(0.001);

            List<Tuple<Vertex2D, GraphNavigationMsg.Vertex>> vertexes = graph==null?new List<Tuple<Vertex2D, GraphNavigationMsg.Vertex>>():graph.Vertexes.Select(i =>new Tuple<Vertex2D, GraphNavigationMsg.Vertex>(i as Vertex2D,
                  new GraphNavigationMsg.Vertex()
                  {
                      X = ((Vertex2D)i).X,
                      Y = ((Vertex2D)i).Y,
                      Distance = ((Vertex2D)i).Distance,
                      DistanceCalculated = ((Vertex2D)i).DistanceCalculated,
                      Final = ((Vertex2D)i).Final
                  })).ToList();


            var vertexesDic = new Dictionary<Vertex2D, int>();
            for (int j = 0; j < vertexes.Count; j++)
                vertexesDic.Add(vertexes[j].Item1, vertexesDic.Count);

            List<GraphNavigationMsg.Edge> edges = graph==null?new List<GraphNavigationMsg.Edge>():graph.Edges.Select(e => new GraphNavigationMsg.Edge(null)
            {
                From = vertexesDic[e.From as Vertex2D],
                To = vertexesDic[e.To as Vertex2D],
                Collision = (e.Tag as GraphState2D)?.IsCollision??false,
                Length = e.Length,
                Graph = true,
                Path = false
            }).ToList();

            if (Ret != null)
            {
                foreach (var s in Ret)
                {
                    Vertex2D f = vertexesDic.Keys.FirstOrDefault(i => ((Vertex2D)i).X == s.FromX && ((Vertex2D)i).Y == s.FromY);
                    if (f == null)
                    {
                        f = new Vertex2D() { X = s.FromX, Y = s.FromY };
                        vertexes.Add(new Tuple<Vertex2D, GraphNavigationMsg.Vertex>(f,
                              new GraphNavigationMsg.Vertex()
                              {
                                  X = f.X,
                                  Y = f.Y,
                                  Distance = f.Distance,
                                  DistanceCalculated = f.DistanceCalculated,
                                  Final = f.Final
                              }
                            ));
                        vertexesDic.Add(f, vertexesDic.Count);
                    }

                    Vertex2D t = vertexesDic.Keys.FirstOrDefault(i => ((Vertex2D)i).X == s.X && ((Vertex2D)i).Y == s.Y);
                    if (t == null)
                    {
                        t = new Vertex2D() { X = s.X, Y = s.Y };
                        vertexes.Add(new Tuple<Vertex2D, GraphNavigationMsg.Vertex>(f,
                              new GraphNavigationMsg.Vertex()
                              {
                                  X = t.X,
                                  Y = t.Y,
                                  Distance = t.Distance,
                                  DistanceCalculated = t.DistanceCalculated,
                                  Final = t.Final
                              }
                            ));
                        vertexesDic.Add(t, vertexesDic.Count);
                    }

                    edges.Add(new GraphNavigationMsg.Edge(null)
                    {
                        From = vertexesDic[f],
                        To = vertexesDic[t],
                        Collision = s.IsCollision,
                        Length = s.Length,
                        Graph = false,
                        Path = true
                    });
                }
            }

            GraphNavigationMsg m = new GraphNavigationMsg(Start?.X??0, Start?.Y ?? 0, Target?.X ?? 0, Target?.Y ?? 0, Result?.X, Result?.Y, vertexes.Select(i=>i.Item2).ToList(), edges);
            return m;
        }

    }
}
