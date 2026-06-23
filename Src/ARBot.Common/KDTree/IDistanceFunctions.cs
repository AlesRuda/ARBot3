
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.KDTree
{
    /// <summary>
    /// An interface which enables flexible distance functions.
    /// </summary>
    public interface IDistanceFunctions
    {
        /// <summary>
        /// Compute a distance between two n-dimensional points.
        /// </summary>
        /// <param name="p1">The first point.</param>
        /// <param name="p2">The second point.</param>
        /// <returns>The n-dimensional distance.</returns>
        double Distance(double[] p1, double[] p2);

        /// <summary>
        /// Find the shortest distance from a point to an axis aligned rectangle in n-dimensional space.
        /// </summary>
        /// <param name="point">The point of interest.</param>
        /// <param name="min">The minimum coordinate of the rectangle.</param>
        /// <param name="max">The maximum coorindate of the rectangle.</param>
        /// <returns>The shortest n-dimensional distance between the point and rectangle.</returns>
        double DistanceToRectangle(double[] point, double[] min, double[] max);
    }
}
