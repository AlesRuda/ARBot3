using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.KDTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.SLAM;
using ARBot.Common.Logs;
using System.Diagnostics;

namespace ARBot.Common.Navigations
{
    public class RRT: GraphNavigationBase
    {
        Random rnd = new Random();
        int maxN = 10000;
        public bool SingleTree = true;


        public KDTree<TreeStateBase> stromZ;
        public KDTree<TreeStateBase> stromDo;

        public override IEnumerable<GraphStateBase> States => stromZ.NearestNeighbors(new double[] { 0, 0 }, stromZ.Size).Concat(stromDo.NearestNeighbors(new double[] { 0, 0 }, stromDo.Size));

        private TreeStateBase start;
        public override GraphStateBase Start
        {
            get
            {
                return start;
            }
            set
            {
                start = value as TreeStateBase;
                stromZ = new KDTree<TreeStateBase>(2);
                Add(stromZ, start);
            }
        }

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

                    RemoveKolize(start.Children);

                    stromZ = new KDTree<TreeStateBase>(2);
                    RebuildTree(stromZ, start);
                }
            }
        }

        public RRT()
        {
            Start = new RRTStateSimple(this) { X = 0, Y = 0 };
            stromDo = new KDTree<TreeStateBase>(2);

            GenObstacles(0, 0);
            var m = new RRTModel(this, 1, 1) { X = 0, Y = 0};
            Add(stromZ, m);

            m = new RRTModel(this, 1, 1) { X = 10, Y = 5, Parent = m};
            Add(stromZ, m);

            m = new RRTModel(this, 1, 1) { X = 0, Y = 15, Parent = m};
            Add(stromZ, m);
        }


        void Add(KDTree<TreeStateBase> strom, TreeStateBase m)
        {
            strom.AddPoint(new double[] { m.X, m.Y }, m);
        }

        public Tuple<TreeStateBase, double> GetNearest(KDTree<TreeStateBase> strom, double x, double y)
        {
            var e = strom.NearestNeighbors(new double[] { x, y }, 1);
            e.MoveNext();
            return new Tuple<TreeStateBase, double>(e.Current, e.CurrentDistance);
        }

        void RemoveKolize(IEnumerable<TreeStateBase> items)
        {
            foreach(TreeStateBase i in items.ToList())
            {
                if (i.IsCollision)
                    i.Parent = null;
                else
                    RemoveKolize(i.Children);
            }
        }

        void RebuildTree(KDTree<TreeStateBase> strom, TreeStateBase item)
        {
            Add(strom, item);
            foreach (TreeStateBase i in item.Children.ToList())
            {
                RebuildTree(strom, i);
            }
        }

        public override GridNavigationResult Process(GraphStateBase target)
        {
            FindTarget(target as TreeStateBase);
            return Result;
        }

        public Tuple<List<GraphStateBase>, TimeSpan> FindTarget(TreeStateBase target)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Target = target;
            Random rnd = new Random();
            double pravdepodobnostKCili = 0.1;
            double a = 0.5;

            var os = Obstacles.Concat(new[] {new Point2D(target.X, target.Y), new Point2D(Start.X, Start.Y) }).ToList();

            double minX = os.Any()? os.Min(p => p.X):0;
            double minY = os.Any() ? os.Min(p => p.Y):0;
            double maxX = os.Any() ? os.Max(p => p.X):0;
            double maxY = os.Any() ? os.Max(p => p.Y):0;

            bool k = false;

            double x;
            double y;

            minX -= (maxX - minX) * a;
            minY -= (maxY - minY) * a;
            maxX += (maxX - minX) * a;
            maxY += (maxY - minY) * a;

            var cil = start.Clone() as TreeStateBase;
            cil.X = target.X;
            cil.Y = target.Y;

            stromDo = new KDTree<TreeStateBase>(2);
            Add(stromDo, cil );
/*
                x = minX + (maxX - minX) * rnd.NextDouble();
                y = minY + (maxY - minY) * rnd.NextDouble();

                var tm1 = GetNearest(stromZ, x, y);

                x = minX + (maxX - minX) * rnd.NextDouble();
                y = minY + (maxY - minY) * rnd.NextDouble();

                var tm2 = GetNearest(stromZ, x, y);

                if(tm1.Item1!=tm2.Item1)
                {
                    TreeStateBase mf, mt;
                    if (tm1.Item1.Distance < tm2.Item1.Distance)
                    {
                        mf = tm1.Item1;
                        mt = tm2.Item1;
                    }
                    else
                    {
                        mf = tm2.Item1;
                        mt = tm1.Item1;
                    }

                    var p = mt.Parent;
                    mt.Parent = mf;

                    while(p!=null && p!=mf)
                    {
                        mt = p;
                        p = p.Parent;
                        mt.Parent = null;
                    }
                }
                */


            var strom1 = stromZ;
            var strom2 = stromDo;
            int N = 0;
            bool first = true;
            while (N<maxN)
            {
                if (first || rnd.NextDouble() < pravdepodobnostKCili)
                {
                    first = false;
                    if (strom1 == stromZ)
                    {
                        x = cil.X;
                        y = cil.Y;
                    }
                    else
                    {
                        x = start.X;
                        y = start.Y;
                    }
                }
                else
                {
                    x = minX + (maxX - minX) * rnd.NextDouble();
                    y = minY + (maxY - minY) * rnd.NextDouble();
                }

                var tm = GetNearest(strom1, x, y);
                var m = tm.Item1;
                do
                {
                    N++;
                    var newM = m.NewState(x, y) as TreeStateBase;
                    newM.Parent = m;
                    k = newM.IsCollision;
                    if (!k)
                    {
                        m = newM;
                        Add(strom1, m);
                        var tm2 = GetNearest(strom2, m.X, m.Y);
                        if (tm2.Item2 < m.MinDist2)
                        {
                            var ret = new List<GraphStateBase>();

                            if (strom1 == stromZ)
                            {
                                while (m != null)
                                {
                                    ret.Insert(0, m);
                                    m = m.Parent;
                                }

                                m = tm2.Item1;
                                while (m != null)
                                {
                                    ret.Add(m);
                                    m = m.Parent;
                                }
                            }
                            else
                            {
                                while (m != null)
                                {
                                    ret.Add(m);
                                    m = m.Parent;
                                }

                                m = tm2.Item1;
                                while (m != null)
                                {
                                    ret.Insert(0, m);
                                    m = m.Parent;
                                }
                            }

                            Ret = ret;
                            Result = FindFree(ret);
                            Notify(null);
                            return new Tuple<List<GraphStateBase>, TimeSpan>(ret, sw.Elapsed);
                        }
                    }
                }
                while (!k && maxN > 0 && Math.Pow(m.X - x, 2) + Math.Pow(m.Y - y, 2) > m.MinDist2);

                if (!SingleTree)
                {
                    if (strom1 == stromZ)
                    {
                        strom1 = stromDo;
                        strom2 = stromZ;
                    }
                    else
                    {
                        strom1 = stromZ;
                        strom2 = stromDo;
                    }
                }            
            }
            Notify(null);
            return null;
        }

        public override GraphNavigationMsg ToLogMessage()
        {
            return null;
        }
    }
}
