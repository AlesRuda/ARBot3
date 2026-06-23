using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using ARBot.Common.Logs;
using System.Diagnostics;
using ARBot.Common.LocalMaps;

namespace ARBot.Common.SLAM
{
    /// <summary>
    /// Implementace algoritmu Iterative closest point
    /// </summary>
    public class ICP:GraphMapBase
    {
        #region Declaration and constructor

        public IList<ICPMatchPoint> Matches; //Namapovani vstupni mereni na stavy, obsahuje vsechny pozorovani, ale nemusi byt prirazen stav
        public float Tx { get; private set; }// vypočtené posuntí, stav - mereni, tj. zaporna hodnota udava, ze stav mam posunot o -Tx 
        public float Ty { get; private set; }
        private double alfa;
        uint N; // počet iterací
        bool EndingCondition;
        public ARBot.Common.KDTree.KDTree<ICPStatePoint> Tree { get; private set; }
        double end;
        double maxP = 0.3;
        int maxStateCount = 1000;

        MathNet.Numerics.LinearAlgebra.Matrix<double> TR;

        public ICP()
        {
            States = new List<ICPStatePoint>();
            Reset();
            N = 4;
        }
        #endregion 
        #region Properties
        public Point2D Translation
        {
            get
            {
                Point2D Q;
                Q.X = Tx;
                Q.Y = Ty;
                return Q;
            }
        }

        /// <summary>
        /// Kovariance mereni
        /// </summary>
        public ARBot.Common.Common.Matrix P { get; private set; }

        /// <summary>
        /// Rotace ve stupnich
        /// </summary>
        public double Rotation
        {
            get
            {
                return alfa;
            }
        }

        public uint Iterations
        {
            get { return N; }
            set { N = value; }
        }

        #endregion

        public void Reset()
        {
            alfa = 0;
            Tx = 0;
            Ty = 0;
            double[] row0 = { 1, 0 };
            double[] row1 = { 0, 1 };
            TR = Matrix<double>.Build.DenseOfRows(new[] { Vector<double>.Build.DenseOfArray(row0), Vector<double>.Build.DenseOfArray(row1) });
        }

        public override void Solve(List<ICPObservationPoint> data)
        {
            if(Tree==null)
                Tree = new ARBot.Common.KDTree.KDTree<ICPStatePoint>(2);
            Reset();

            EndingCondition = false;
            int i = 0;
            end = 0.0000004;

            IList<ICPMatchPoint> fit = null;

            while (!EndingCondition && i < N)
            {
                foreach (ICPStatePoint s in States)
                    s.Match = null;

                fit = data.Select(ii => new ICPMatchPoint() { Observation=ii, Point = new Point2D(ii.Point.X * TR.At(0, 0) + ii.Point.Y * TR.At(0, 1) + Tx, ii.Point.X * TR.At(1, 0) + ii.Point.Y * TR.At(1, 1) + Ty )}).ToList();
                foreach(var f in fit)
                {
                    var nb = Tree.NearestNeighbors(new double[] { f.Point.X, f.Point.Y }, States.Count);

                    //                    var xxx=nb.Select(ii => Math.Sqrt(Math.Pow((ii.Point - f.Point).X, 2)+Math.Pow((ii.Point-f.Point).Y, 2)));

                    //                    foreach (var s in nb)
                    var s = nb.FirstOrDefault();
                    {
                        if (s != null)
                        {
                            f.NearestState = s;
                            double d = Math.Sqrt(Math.Pow(f.Point.X - s.Point.X, 2) + Math.Pow(f.Point.Y - s.Point.Y, 2));
                            if (s.Match == null || s.Match.Distance>d)
                            {
                                if (s.Match != null)
                                    s.Match.State = null;
                                s.Match = f;
                                f.State = s;
                                f.Distance = d;
                            }
                        }
                    }
                }

                i++;

                eq_point(fit);
                end = 0.00000001;
            }

            Matches = fit;

            foreach(var m in Matches)
            {
                if (m.State != null)
                {
                    m.State.Generace++;
                    m.State.LastMatch = 0;
                }
                else
                {
                    var s = Tree.NearestNeighbors(new double[] { m.Point.X, m.Point.Y }, 1).FirstOrDefault();
                    if (s != null)
                    {
                        var diff = (m.Point - s.Point);
                        if (diff.Length > Math.Sqrt(s.Rozptyl))
                            m.Add = true;
                    }
                    else
                        m.Add = true;
                }
            }
            alfa = Math.Atan2(TR.At(1, 0), TR.At(0, 0));
        }

        private void eq_point(IList<ICPMatchPoint> fit)
        {
            Point2D Centrum_A=new Point2D();
            Point2D Centrum_B = new Point2D();
            int cnt = 0;
            foreach(var f in fit)
            {
                if (f.NearestState!=null)
                {
                    Centrum_A += f.NearestState.Point;
                    cnt++;
                }
                Centrum_B += f.Point;
            }
            if(cnt!=0)
                Centrum_A = Centrum_A / cnt;
            Centrum_B = Centrum_B / fit.Count;

            double[] row0 = { 0, 0 };
            double[] row1 = { 0, 0 };

            ARBot.Common.Common.Matrix p =P= new Common.Matrix(2, 2);

            foreach (var f in fit)
            {
                if (f.NearestState != null)
                {
                    double dax = f.NearestState.Point.X - Centrum_A.X;
                    double day = f.NearestState.Point.Y - Centrum_A.Y;
                    double dfx = f.Point.X - Centrum_B.X;
                    double dfy = f.Point.Y - Centrum_B.Y;

                    row0[0] += dax * dfx;
                    row0[1] += dax * dfy;
                    row1[0] += day * dfx;
                    row1[1] += day * dfy;

                    if (f.Observation.R!=null)
                        p += f.Observation.R;
                }
            }
            if (cnt != 0)
            {
                row0[0] = row0[0] / cnt;
                row0[1] = row0[1] / cnt;
                row1[0] = row1[0] / cnt;
                row1[1] = row1[1] / cnt;
                P = p * (1.0 / cnt);
            }
            var A = Matrix<double>.Build.DenseOfColumns(new[] { Vector<double>.Build.DenseOfArray(row0), Vector<double>.Build.DenseOfArray(row1) });
            var Decomposition = A.Svd(true);
            var U = Decomposition.U;
            var V = Decomposition.VT.Transpose();
            var R = V * U.Transpose();
            double x = Centrum_A.X - R.At(0, 0) * Centrum_B.X - R.At(0, 1) * Centrum_B.Y;
            double y = Centrum_A.Y - R.At(1, 0) * Centrum_B.X - R.At(1, 1) * Centrum_B.Y;
            //alfa = Math.Acos(R.At(1, 1))*180/Math.PI;
            double v = Math.Abs(R.At(0, 1) * R.At(1, 0));
            if (v > end)
            {
                TR = R * TR;
                x = R.At(0, 0) * Tx + R.At(0, 1) * Ty + x;
                Ty = (float)( R.At(1, 0) * Tx + R.At(1, 1) * Ty + y);
                Tx = (float)x;
            }
            else
                EndingCondition = true;
        }
        public override string ToString()
        {
            return alfa.ToString("0.00") + "rad, x=" + Tx.ToString("0.0") + ", y=" + Ty.ToString("0.0");
        }

        public override ICPMsg ToLogMessage()
        {
            ICPMsg m = new ICPMsg("ICP", 
                States.Select(i=>new ICPMsg.ICPPoint()
                {
                    X = i.Point.X,
                    Y = i.Point.Y,
                    Generace = i.Generace,
                    Iterace=i.Iterace,
                    LastMatch=i.LastMatch,
                    IsMain=i.IsMain,
                    P=i.P
                }).ToList(),
                Translation.X, Translation.Y, Rotation);
            return m;
        }

        /// <summary>
        /// Reprezentuje sum systemu
        /// </summary>
        public ARBot.Common.Common.Matrix Q;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dx">O kolik se maji posunot stavy</param>
        /// <param name="dy">O kolik se maji posunot stavy</param>
        /// <param name="alfa">O kolik se maji pootocit mereni pred pripocitani ke stavum</param>
        /// <param name="Q"></param>
        public override void Update(double dx, double dy, double alfa)
        {
            Point2D d = new Point2D(dx, dy);
            var rot = new ARBot.Common.Common.Matrix(new double[,] { { Math.Cos(alfa), -Math.Sin(alfa) }, { Math.Sin(alfa), Math.Cos(alfa) } });
            foreach (var s in States)
            {
                // posunuto do dalsiho kroku
                s.Point += d;
                s.P += Q;
            }
            foreach(var m in Matches)
            {
                if(m.State!=null)
                {
                    ICPStatePoint s = m.State;

                    var diff = new Point2D(rot*new ARBot.Common.Common.Matrix(m.Observation.Point)) - s.Point; // rozdil pozorovane a predikovane polohy
                    ARBot.Common.Common.Matrix dd = new Common.Matrix(diff);
                    ARBot.Common.Common.Matrix p = s.P;
                    ARBot.Common.Common.Matrix k;
                    if(m.Observation.R!=null)
                        k= p * ARBot.Common.Common.Matrix.Inverse(p + m.Observation.R);
                    else
                        k = p * ARBot.Common.Common.Matrix.Inverse(p);
                    ARBot.Common.Common.Matrix ddx = k*dd;
                    s.Point +=new Point2D(ddx);
                    s.P = p - k * p;
                }
                else
                {
//                    if (m.Add)
                    {
                        ICPStatePoint s;
                        if (m.Observation.R!=null)
                            s = new ICPStatePoint() { Generace = 0, Point = m.Observation.Point, P = (m.Observation.R + Q) };
                        else
                            s = new ICPStatePoint() { Generace = 0, Point = m.Observation.Point, P = Q };
                        States.Add(s);
                    }
                }
            }

            var ss = States
                .Select(s => new { State = s, r = s.Rozptyl, v= s.Iterace!=0?(double)s.Generace/ (double)s.Iterace:1 }).ToList();
            States = ss
                .Where(s => s.r< maxP || s.State.Iterace<3)
                .OrderBy(s => s.State.IsMain)
                .ThenByDescending(s=>s.v)
                .Take(maxStateCount)
                .Select(s=>s.State).ToList();
/*            ss = ss.Where(s => s.r >= maxP).ToList();
                        foreach(var s in ss)
                        {
                            Debug.WriteLine(string.Format("{0}, {1}", s.State.Iterace, s.State.Generace));
                        }*/
            List<ICPStatePoint> states = new List<ICPStatePoint>();
            Tree = new ARBot.Common.KDTree.KDTree<ICPStatePoint>(2);
            foreach (ICPStatePoint s in States.OrderBy(s=>s.Rozptyl))
            {
                var ss1 = Tree.NearestNeighbors(new double[] { s.Point.X, s.Point.Y }, 1).FirstOrDefault();
                if (ss1 == null || (s.Point - ss1.Point).Length > Math.Sqrt(s.Rozptyl + ss1.Rozptyl) / 2)
                {
                    states.Add(s);
                    s.Match = null;
                    Tree.AddPoint(new double[] { s.Point.X, s.Point.Y }, s);
                    s.Iterace++;
                    s.LastMatch++;
                }
            }

            States = states;
        }
    }
}
