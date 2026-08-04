using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Simplification
{
    public class Points2Lines
    {
        int N = 20;
        double max_k =15;   //kritérium pro segmentaci úsečky
        int min_i=3;      //nejmenší počet bodů tvořících úsečku - 1 
        List<double> RegressionCoeff;


        public Points2Lines()
        {
            int x0 = N;
            int x1 = 1;
            int x2 = 1;
            int x3 = 1;
            int x4 = 1;
            int f;
            RegressionCoeff = new List<double>();
            for (int i = 2; i < N; i++) // x = 0,1,2 ... N-1
            {
                f = i;
                x1 += f;
                f = f * i;
                x2 += f;
                f = f * i;
                x3 += f;
                f = f * i;
                x4 += f;
            }
            double det = -(x4 * x1 * x1 - 2 * x1 * x2 * x3 + x2 * x2 * x2 - x0 * x4 * x2 + x0 * x3 * x3);
            RegressionCoeff.Add((-x1 * x1 + x0 * x2) / det);//0  yx2   -> a
            RegressionCoeff.Add(-(x0 * x3 - x1 * x2) / det);//1  yx    -> a    
            RegressionCoeff.Add((-x2 * x2 + x1 * x3) / det);//2  y     -> a
            RegressionCoeff.Add(-(x0 * x3 - x1 * x2) / det);//3  yx2   -> b
            RegressionCoeff.Add((-x2 * x2 + x0 * x4) / det);//4  yx    -> b
            RegressionCoeff.Add(-(x1 * x4 - x2 * x3) / det);//5  y     -> b
            RegressionCoeff.Add((-x2 * x2 + x1 * x3) / det);//6  yx2   -> c
            RegressionCoeff.Add(-(x1 * x4 - x2 * x3) / det);//7  yx    -> c
            RegressionCoeff.Add((-x3 * x3 + x2 * x4) / det);//8  y     -> c 

        }

        public List<Tuple<Point2D, Point2D>> Simplify(List<Point2D> points)
        {
            List<Tuple<Point2D, Point2D>> SegmentedLines = new List<Tuple<Point2D, Point2D>>();

            int i_h, i_l = 0, i;
            int s = 0;
            double k;
            while (s < points.Count - 1)
            {
                i_l = min_i;
                i_h = points.Count - 1 - s;
                i = i_l;

                while ((i_h - i_l) >= 2)
                {
                    k = RatePossibleLine(points, s, i);
                    if (k > max_k)
                    {
                        i_h = i;
                        i = (int)Math.Round((i_h + i_l) / 2.0);
                    }
                    else
                    {
                        i_l = i;
                        i = (int)Math.Round((i_h + i_l) / 2.0);
                    }
                }

                if (i_l > min_i)
                {
                    SegmentedLines.Add(new Tuple<Point2D, Point2D>(points[s], points[Math.Min(s + i_l, points.Count - 1)]));
                    s = s + i_l + 1;
                }
                else
                    s = s + 1;
            }
            return SegmentedLines;
        }
        private double RatePossibleLine(List<Point2D> points, int s, int i)
        {

            for (int k = s; k < i; k++)
                if (points[s + i].Distance == 0)
                    return (max_k + 1);

            var tl = new Line2D(points[s], points[s + i]);
            Point2D prusecik;
            Point2D pp;
            double l = tl.Length;

            double[] dist = new double[Math.Min(i, N)];
            for (int k = 0; k < dist.Length; k++)
            {
                int ii = Convert.ToInt16((k + 1.0) * i / (dist.Length + 1.0));
                pp = points[s + ii];
                prusecik = tl.ProjectOntoLine(pp);

                dist[k] = Math.Sqrt((pp.X - prusecik.X) * (pp.X - prusecik.X) + (pp.Y - prusecik.Y) * (pp.Y - prusecik.Y));
                if (Math.Abs(dist[k]) / l > 0.3)
                    return (max_k + 1);
            }

            double yx2 = 0;
            double yx = 0;
            double y = 0;
            double f;
            for (int k = 0; k < dist.Length; k++)
            {
                f = dist[k];
                y += f;
                f = f * k;
                yx += f;
                f = f * k;
                yx2 += f;
            }
            double a = yx2 * RegressionCoeff[0] + yx * RegressionCoeff[1] + y * RegressionCoeff[2];
            double b = yx2 * RegressionCoeff[3] + yx * RegressionCoeff[4] + y * RegressionCoeff[5];
            double c = yx2 * RegressionCoeff[6] + yx * RegressionCoeff[7] + y * RegressionCoeff[8];
            double dis = b * b - 4 * a * c;
            List<double> roots = new List<double>();
            roots.Add(0);
            roots.Add(N - 1);
            double x;
            if (dis > 0)
            {
                x = ((-b + Math.Sqrt(dis)) / (2 * a));
                if ((x > 0) && (x < N - 1))
                    roots.Add(x);
                x = ((-b - Math.Sqrt(dis)) / (2 * a));
                if ((x > 0) && (x < N - 1))
                    roots.Add(x);
                roots.Sort();
            }
            double zn;
            f = 0;
            for (int k = 0; k < roots.Count - 1; k++)
            {
                x = (roots[k + 1] + roots[k]) / 2;
                zn = a * x * x + b * x + c;
                f = f + Math.Sign(zn) * (a * (Math.Pow(roots[k + 1], 3) - Math.Pow(roots[k], 3)) / 3 + b * (Math.Pow(roots[k + 1], 2) - Math.Pow(roots[k], 2)) / 2 + c * (roots[k + 1] - roots[k]));
            }
            return f / N;
        }
    }
}
