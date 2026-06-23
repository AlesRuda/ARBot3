using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms
{
    /// <summary>
    /// Spocte Rotaci, Zvetseni=1 a Translaci tak aby se B promitlo na A
    /// B'=Scale*Rotation*B+Translation
    /// sum((A-B')^2) je minimalni
    /// </summary>
    public class Kabsch : PointMatchBase
    {
        public override void Process(IEnumerable<Pair> items)
        {
            var CA = Avg(items.Select(i => i.A));
            var CB = Avg(items.Select(i => i.B));

            Matrix<double> h = Matrix<double>.Build.Dense(CA.RowCount, CA.RowCount, (r, c) => 0);
            foreach (var i in items)
                h += (i.A - CA) * (i.B - CB).Transpose();

            var svd = h.Svd(true);

            var VT = svd.VT;
            var V = VT.Transpose();
            //            var S = svd.S;
            var U = svd.U;

            var d = U.Determinant() * VT.Determinant(); //tady je mozna problem v transpozici V, v SVD neni jasne zda vraci V ci VT
            if (d < 0)
                //                U[:, -1] = -U[:, -1]
                U.SetColumn(U.ColumnCount - 1, -U.Column(U.ColumnCount - 1));

            Rotation = U * VT; //U*V;
            Scale = 1;
            Translation = CA - Rotation * CB;

        }
    }
}
