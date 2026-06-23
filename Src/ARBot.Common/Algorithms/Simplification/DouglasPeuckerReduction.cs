using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Simplification
{
    public class DouglasPeuckerReduction
    {
        private double tolerance;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="tolerance">Pomer vzdalenosti bodu od usecky, kdy je pod klasifikovan jako dalsi pridany do vysledku</param>
        public DouglasPeuckerReduction(double tolerance)
        {
            this.tolerance = tolerance;
        }
        /// <summary>
        /// Uses the Douglas Peucker algorithm to reduce the number of points.
        /// </summary>
         /// <param name="Points">The points.</param>
        /// <param name="Tolerance">The tolerance.</param>
        /// <returns></returns>
        public List<Point2D> Simplify(List<Point2D> Points)
        {
            if (Points == null || Points.Count < 3)
                return Points;

            Int32 firstPoint = 0;
            Int32 lastPoint = Points.Count - 1;
            List<Int32> pointIndexsToKeep = new List<Int32>();

            //Add the first and last index to the keepers
            pointIndexsToKeep.Add(firstPoint);
            pointIndexsToKeep.Add(lastPoint);

            //The first and the last point cannot be the same
            while (Points[firstPoint].Equals(Points[lastPoint]))
            {
                lastPoint--;
            }

            Simplify(Points, firstPoint, lastPoint, ref pointIndexsToKeep);

            var returnPoints = new List<Point2D>();
            pointIndexsToKeep.Sort();
            foreach (Int32 index in pointIndexsToKeep)
            {
                returnPoints.Add(Points[index]);
            }

            return returnPoints;
        }


        /// <summary>
        /// Douglases the peucker reduction.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="firstPoint">The first point.</param>
        /// <param name="lastPoint">The last point.</param>
        /// <param name="tolerance">The tolerance.</param>
        /// <param name="pointIndexsToKeep">The point index to keep.</param>
        private void Simplify(List<Point2D> points, 
            Int32 firstPoint, Int32 lastPoint, 
            ref List<Int32> pointIndexsToKeep)
        {
            Double maxDistance = 0;
            Int32 indexFarthest = 0;

            var l = new Line2D(points[firstPoint], points[lastPoint]);
            double len = l.Length;

            for (Int32 index = firstPoint; index < lastPoint; index++)
            {
                Double distance = l.Distance(points[index]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    indexFarthest = index;
                }
            }

            if (maxDistance/len > tolerance && indexFarthest != 0)
            {
                //Add the largest point that exceeds the tolerance
                pointIndexsToKeep.Add(indexFarthest);

                Simplify(points, firstPoint, indexFarthest, ref pointIndexsToKeep);
                Simplify(points, indexFarthest, lastPoint, ref pointIndexsToKeep);
            }
        }
    }
}
