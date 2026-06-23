using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    public class KDTree
    {
        /*
        KDNode Tree;

        public KDTree(List<Point2D> data)
        {
            List<int> inx = new List<int>();
            for (int i = 0; i < data.Count; i++) { inx.Add(i); }
            Tree = new KDNode(inx, data, 'x');
        }



        public KDRet Search(Point2D X)
        {
            return Tree.Search(X);
        }
        */
        ARBot.Common.KDTree.KDTree<int> Tree;
        List<ICPFitPoint> data;

        public KDTree(List<ICPFitPoint> data)
        {
            Tree = new ARBot.Common.KDTree.KDTree<int>(2);
            this.data = data;
            for (int i = 0; i < data.Count; i++)
                Tree.AddPoint(new double[] { data[i].Point.X, data[i].Point.Y }, i);
        }


        public KDRet Search(Point2D X)
        {
            int idx = Tree.NearestNeighbors(new double[] { X.X, X.Y }, 1).First();
            return new KDRet() { inx = idx, q=Math.Sqrt(Math.Pow(data[idx].Point.X-X.X, 2)+ Math.Pow(data[idx].Point.Y - X.Y, 2))  };
        }


    }
}
