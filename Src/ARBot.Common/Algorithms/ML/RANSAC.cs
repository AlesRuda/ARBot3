using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Algorithms.ML
{
    public class RANSAC<TModel> where TModel : class
    {
        // RANSAC parameters
        private int minSamples;    // number of samples
        private double t; // inlier threshold
        private int maxSamplings = 100;
        private int maxEvaluations = 1000;
        private double probability = 0.99;

        // RANSAC functions
        private Func<int[], TModel> fitting;
        private Func<TModel, double, int[]> distances;
        private Func<int[], bool> degenerate;

        /// <summary>
        ///   Model fitting function.
        /// </summary>
        public Func<int[], TModel> Fitting
        {
            get { return fitting; }
            set { fitting = value; }
        }

        /// <summary>
        ///   Degenerative set detection function.
        /// </summary>
        public Func<int[], bool> Degenerate
        {
            get { return degenerate; }
            set { degenerate = value; }
        }

        /// <summary>
        ///   Distance function.
        /// </summary>
        public Func<TModel, double, int[]> Distances
        {
            get { return distances; }
            set { distances = value; }
        }

        /// <summary>
        ///   Gets or sets the minimum distance between a data point and
        ///   the model used to decide whether the point is an inlier or not.
        /// </summary>
        public double Threshold
        {
            get { return t; }
            set { t = value; }
        }

        /// <summary>
        ///   Gets or sets the minimum number of samples from the data
        ///   required by the fitting function to fit a model.
        /// </summary>
        public int MinSamples
        {
            get { return minSamples; }
            set { minSamples = value; }
        }

        /// <summary>
        ///   Maximum number of attempts to select a 
        ///   non-degenerate data set. Default is 100.
        /// </summary>
        public int MaxSamplings
        {
            get { return maxSamplings; }
            set { maxSamplings = value; }
        }

        /// <summary>
        ///   Maximum number of trials. Default is 1000.
        /// </summary>
        public int MaxEvaluations
        {
            get { return maxEvaluations; }
            set { maxEvaluations = value; }
        }

        /// <summary>
        /// Gets the current estimate of trials needed.
        /// </summary>
        public int TrialsNeeded { get; private set; }

        /// <summary>
        /// Gets the current number of trials performed.
        /// </summary>
        public int TrialsPerformed { get; private set; }

        /// <summary>
        ///   Gets or sets the probability of obtaining a random
        ///   sample of the input points that contains no outliers.
        ///   Default is 0.99.
        /// </summary>
        public double Probability
        {
            get { return probability; }
            set { probability = value; }
        }

        /// <summary>
        ///   Constructs a new RANSAC algorithm.
        /// </summary>
        /// 
        /// <param name="minSamples">
        ///   The minimum number of samples from the data
        ///   required by the fitting function to fit a model.
        /// </param>
        public RANSAC(int minSamples)
        {
            this.minSamples = minSamples;
        }

        /// <summary>
        ///   Constructs a new RANSAC algorithm.
        /// </summary>
        /// <param name="minSamples">
        ///   The minimum number of samples from the data
        ///   required by the fitting function to fit a model.
        /// </param>
        /// <param name="threshold">
        ///   The minimum distance between a data point and
        ///   the model used to decide whether the point is
        ///   an inlier or not.
        /// </param>
        public RANSAC(int minSamples, double threshold)
        {
            this.minSamples = minSamples;
            this.t = threshold;
        }

        /// <summary>
        ///   Constructs a new RANSAC algorithm.
        /// </summary>
        /// <param name="minSamples">
        ///   The minimum number of samples from the data
        ///   required by the fitting function to fit a model.
        /// </param>
        /// <param name="threshold">
        ///   The minimum distance between a data point and
        ///   the model used to decide whether the point is
        ///   an inlier or not.
        /// </param>
        /// <param name="probability">
        ///   The probability of obtaining a random sample of
        ///   the input points that contains no outliers.
        /// </param>

        public RANSAC(int minSamples, double threshold, double probability)
        {
            if (minSamples < 0)
                throw new ArgumentOutOfRangeException("minSamples");

            if (threshold < 0)
                throw new ArgumentOutOfRangeException("threshold");

            if (probability > 1.0 || probability < 0.0)
                throw new ArgumentException("Probability should be a value between 0 and 1", "probability");

            this.minSamples = minSamples;
            this.t = threshold;
            this.probability = probability;
        }

        /// <summary>
        ///   Computes the model using the RANSAC algorithm.
        /// </summary>
        /// <param name="size">The total number of points in the data set.</param>
        public TModel Compute(int size)
        {
            int[] inliers;
            return Compute(size, out inliers);
        }

        /// <summary>
        ///   Computes the model using the RANSAC algorithm.
        /// </summary>
        /// <param name="size">The total number of points in the data set.</param>
        /// <param name="inliers">The indexes of the outlier points in the data set.</param>
        public TModel Compute(int size, out int[] inliers)
        {
            if (size < minSamples)
            {
                inliers = new int[0];
                return default(TModel);
            }

            Dictionary<int, int> dic = new Dictionary<int, int>();
            List<int> l = new List<int>();
            Random rnd = new Random();

            // We are going to find the best model (which fits
            //  the maximum number of inlier points as possible).
            TModel bestModel = null;
            int[] bestInliers = null;
            int maxInliers = 0;

            int r = Math.Min(size, minSamples);

            // For this we are going to search for random samples
            //  of the original points which contains no outliers.

            TrialsPerformed = 0;              // Total number of trials performed
            TrialsNeeded = maxEvaluations;  // Estimative of number of trials needed.

            // While the number of trials is less than our estimative,
            //   and we have not surpassed the maximum number of trials
            while (TrialsPerformed < TrialsNeeded && TrialsPerformed < maxEvaluations)
            {
                TModel model = null;
                int[] sample = null;
                int samplings = 0;

                // While the number of samples attempted is less
                //   than the maximum limit of attempts
                while (samplings < maxSamplings)
                {
                    // Select at random r data points to form a trial model.
                    dic.Clear();
                    l.Clear();
                    for (int i = 0; i < r; i++)
                    {
                        int idx = rnd.Next(size);
                        while (dic.ContainsKey(idx))
                        {
                            idx++;
                            if (idx == size)
                                idx = 0;
                        }
                        dic.Add(idx, idx);
                        l.Add(idx);
                    }
                    sample = l.ToArray();

                    // If the sampled points are not in a degenerate configuration,
                    if (degenerate == null || !degenerate(sample))
                    {
                        // Fit model using the random selection of points
                        model = fitting(sample);
                        if(model!=null)
                            break; // Exit the while loop.
                    }

                    samplings++; // Increase the samplings counter
                }

                if (model != null)
                {
                    // Now, evaluate the distances between total points and the model returning the
                    //  indices of the points that are inliers (according to a distance threshold t).
                    inliers = distances(model, t);

                    // Check if the model was the model which highest number of inliers:
                    if (bestInliers == null || inliers.Length > maxInliers)
                    {
                        // Yes, this model has the highest number of inliers.
                        maxInliers = inliers.Length;  // Set the new maximum,
                        bestModel = model;            // This is the best model found so far,
                        bestInliers = inliers;        // Store the indices of the current inliers.

                        // Update estimate of N, the number of trials to ensure we pick, 
                        //   with probability p, a data set with no outliers.
                        double pInlier = (double)inliers.Length / (double)size;
                        double pNoOutliers = 1.0 - System.Math.Pow(pInlier, minSamples);

                        double num = System.Math.Log(1.0 - probability);
                        double den = System.Math.Log(pNoOutliers);
                        if (den == 0)
                            TrialsNeeded = num == 0 ? 0 : MaxEvaluations;
                        else
                            TrialsNeeded = (int)(num / den);
                    }
                }

                TrialsPerformed++; // Increase the trial counter.
            }
            inliers = bestInliers;
            return bestModel;
        }
    }
    public class RANSAC
    { 
        /// <summary>
        /// Linearni regrese
        /// </summary>
        /// <param name="points">Prokladane body</param>
        /// <param name="minCount">Minimalne potrebny pocet bodu</param>
        /// <param name="treshold">Hranice pro zarazeni do inliner</param>
        /// <param name="probability">Pravdepodobnost, ze zadny z vybranych bodu neni outliner</param>
        /// <returns></returns>
        public static Line2D LinearRegresion(List<Point2D> points, int minCount, double treshold, double probability)
        {
            if (points.Count < minCount)
                return null;
            var r=new RANSAC<Line2D>(minCount, treshold, probability);
            r.Fitting = (samples) => samples.Select(i => points[i]).ToList().LinearRegesion();
            r.Distances = (m, t) =>
              {
                  List<int> idx = new List<int>();
                  for (int i = 0; i < points.Count; i++)
                  {
                      if (m.Distance(points[i]) < t)
                          idx.Add(i);
                  }
                  return idx.ToArray();
              };
            return r.Compute(points.Count);
        }
        /// <summary>
        /// Linearni regrese
        /// </summary>
        /// <param name="points">Prokladane body</param>
        /// <param name="minCount">Minimalne potrebny pocet bodu</param>
        /// <param name="treshold">Hranice pro zarazeni do inlier</param>
        /// <param name="probability">Pravdepodobnost, ze zadny z vybranych bodu neni outliner</param>
        /// <param name="getter">Ze vstupniho pole ziskava Point@d reprezentujici prokladany bod</param>
        /// <param name="marker">Ve vstupnim poli oznaci inliery</param>
        /// <returns></returns>
        public static Tuple<Line2D, RANSAC<Line2D>> LinearRegresion2<T>(List<T> points, int minCount, double treshold, double probability, Func<T, Point2D> getter, Action<T> marker)
        {
            if (getter == null)
                throw new ArgumentNullException(nameof(getter));
            if (points.Count < minCount)
                return null;
            var r = new RANSAC<Line2D>(minCount, treshold, probability);
            r.Fitting = (samples) => samples.Select(i => getter(points[i])).ToList().LinearRegesion();
            r.Distances = (m, t) =>
            {
                List<int> idx = new List<int>();
                for (int i = 0; i < points.Count; i++)
                {
                    if (m.Distance(getter(points[i])) < t)
                        idx.Add(i);
                }
                return idx.ToArray();
            };
            int[] inliers;
            var line=r.Compute(points.Count, out inliers);

            if (marker != null && inliers!=null)
            {
                foreach (int i in inliers)
                    marker(points[i]);
            }

            return new Tuple<Line2D, RANSAC<Line2D>>(line, r);
        }
        /// <summary>
        /// Linearni regrese
        /// </summary>
        /// <param name="points">Prokladane body</param>
        /// <param name="minCount">Minimalne potrebny pocet bodu</param>
        /// <param name="treshold">Hranice pro zarazeni do inlier</param>
        /// <param name="probability">Pravdepodobnost, ze zadny z vybranych bodu neni outliner</param>
        /// <param name="getter">Ze vstupniho pole ziskava Point@d reprezentujici prokladany bod</param>
        /// <param name="marker">Ve vstupnim poli oznaci inliery</param>
        /// <returns></returns>
        public static Line2D LinearRegresion<T>(List<T> points, int minCount, double treshold, double probability, Func<T, Point2D> getter, Action<T> marker)
        {
            var r = LinearRegresion2<T>(points, minCount, treshold, probability, getter, marker);
            return r?.Item1;
        }
    }
}
