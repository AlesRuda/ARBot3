using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    public class KDNode
    {
        double p, n;
        Point2D point;
        KDNode OK, NOK;
        int I;
        char xy;

        public KDNode(List<int> i, List<Point2D> data, char q)
        {
            if (i.Count == 1)
            {
                I = i[0];
                point = data[i[0]];
                return;
            }
            I = -1;
            xy = q;
            List<int> inx = i.ToList();
            List<int> inx_a = new List<int>();
            int c = inx.Count / 2;
            if (q == 'x')
            {
                double min_point = 0;
                int min_inx;
                while (c < inx.Count)
                {
                    min_point = data[inx[0]].X;
                    min_inx = 0;
                    for (int k = 1; k < inx.Count; k++)
                        if (data[inx[k]].X < min_point)
                        {
                            min_inx = k;
                            min_point = data[inx[k]].X;
                        }
                    inx_a.Add(inx[min_inx]);
                    inx.RemoveAt(min_inx);
                }
                double min_point_1 = data[inx[0]].X;
                int min_inx_1 = 0;
                for (int k = 1; k < inx.Count; k++)
                    if (data[inx[k]].X < min_point_1)
                    {
                        min_inx_1 = k;
                        min_point_1 = data[inx[k]].X;
                    }
                p = (min_point_1 + min_point) / 2;
                n = (min_point_1 - min_point) / 2;
                OK = new KDNode(inx_a, data, 'y');
                NOK = new KDNode(inx, data, 'y');
            }
            else
            {
                double min_point = 0;
                int min_inx;
                while (c < inx.Count)
                {
                    min_point = data[inx[0]].Y;
                    min_inx = 0;
                    for (int k = 1; k < inx.Count; k++)
                        if (data[inx[k]].Y < min_point)
                        {
                            min_inx = k;
                            min_point = data[inx[k]].Y;
                        }
                    inx_a.Add(inx[min_inx]);
                    inx.RemoveAt(min_inx);
                }
                double min_point_1 = data[inx[0]].Y;
                int min_inx_1 = 0;
                for (int k = 1; k < inx.Count; k++)
                    if (data[inx[k]].Y < min_point_1)
                    {
                        min_inx_1 = k;
                        min_point_1 = data[inx[k]].Y;
                    }
                p = (min_point_1 + min_point) / 2;
                n = (min_point_1 - min_point) / 2;
                OK = new KDNode(inx_a, data, 'x');
                NOK = new KDNode(inx, data, 'x');
            }

        }

        public int Search_aproximetly(Point2D X)
        {
            if (I != -1)
                return I;
            if (xy == 'x')
                if (X.X < p)
                    return OK.Search_aproximetly(X);
                else
                    return NOK.Search_aproximetly(X);
            else
                if (X.Y < p)
                return OK.Search_aproximetly(X);
            else
                return NOK.Search_aproximetly(X);
        }

        public KDRet Search(Point2D X)
        {
            if (I != -1)
            {
                KDRet W;
                W.inx = I;
                //W.q = Math.Sqrt(Math.Pow(X.X - point.X, 2) + Math.Pow(X.Y - point.Y, 2)); // L_2 metrika
                W.q = Math.Max(Math.Abs(X.X - point.X), Math.Abs(X.Y - point.Y)); //L_inf metrika
                return W;
            }
            double t;
            if (xy == 'x')
                t = X.X;
            else
                t = X.Y;

            if (t < p)
            {
                KDRet W = OK.Search(X);
                if ((t + W.q) < (p + n))
                    return W;
                else
                {
                    KDRet WW = NOK.Search(X);
                    if (WW.q < W.q)
                        return WW;
                    else
                        return W;
                }
            }
            else
            {
                KDRet W = NOK.Search(X);
                if ((t - W.q) > (p - n))
                    return W;
                else
                {
                    KDRet WW = OK.Search(X);
                    if (WW.q < W.q)
                        return WW;
                    else
                        return W;
                }
            }
        }
    }
}
