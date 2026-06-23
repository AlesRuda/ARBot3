using ARBot.Common.Common;
using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms
{
    /// <summary>
    /// Spocte Rotaci, Zvetseni a Translaci tak aby se B promitlo na A
    /// B'=Scale*Rotation*B+Translation
    /// sum((A-B')^2) je minimalni, to je otazkou?
    /// </summary>
    public abstract class PointMatchBase
    {
        public class Pair
        {
            // sloupcovy vektor - vzor
            public Matrix<double> A;
            // sloupcovy vektor - transformovany bod
            public Matrix<double> B;
        }

        /// <summary>
        /// Jak musim otocit B aby se promitlo na A, nebere v uvahu zvetseni
        /// </summary>
        public Matrix<double> Rotation { get; protected set; }
        /// <summary>
        /// JAk musim posunout B aby se promitlo na A
        /// </summary>
        public Matrix<double> Translation { get; protected set; }
        /// <summary>
        /// Kolikrat musim zvetsit B aby se promitlo na A
        /// </summary>
        public double Scale { get; protected set; }

        protected Matrix<double> Avg(IEnumerable<Matrix<double>> items)
        {
            var f = items.FirstOrDefault();
            double d = 1.0/ items.Count();
            Matrix<double> sum = Matrix<double>.Build.Dense(f.RowCount, f.ColumnCount, (r, c)=>0);
            foreach (var i in items)
                sum += i;
            return sum * d;
        }

        protected double Length2(Matrix<double> m)
        {
            var n = m.L2Norm();
            return n*n;
        }

        protected double Trace(Matrix<double> m)
        {
            return m.Trace();
        }


        public abstract void Process(IEnumerable<Pair> items);
    }
}
