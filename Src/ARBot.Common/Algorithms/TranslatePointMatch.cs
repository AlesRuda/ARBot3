using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms
{
    /// <summary>
    /// Spocte posunuti tak aby se B promitlo na A
    /// B'=Scale*Rotation*B+Translation
    /// sum((A-B')^2) je minimalni
    /// </summary>
    public class TranslatePointMatch : PointMatchBase
    {
        public override void Process(IEnumerable<Pair> items)
        {
            var EA = Avg(items.Select(i => i.A));
            var EB = Avg(items.Select(i => i.B));

            Rotation = Matrix<double>.Build.DenseIdentity(EA.RowCount);
            Scale = 1;
            Translation = EA - EB;
        }
    }
}
