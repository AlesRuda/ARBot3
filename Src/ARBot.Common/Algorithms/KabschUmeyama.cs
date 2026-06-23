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
    /// sum((A-B')^2) je minimalni
    /// </summary>
    public class KabschUmeyama : PointMatchBase
    {
        public override void Process(IEnumerable<Pair> items)
        {
            //myslim, ze A odpovida P v puvodnim algoritmu
            var CA = Avg(items.Select(i => i.A));
            var CB = Avg(items.Select(i => i.B));

            var VarA = items.Select(i => Length2(i.A - CA)).Average();

            Matrix<double> h = Matrix<double>.Build.Dense(CA.RowCount, CA.RowCount, (r, c)=>0);
            foreach (var i in items)
                h += (i.A - CA) * (i.B - CB).Transpose();

            h = h * (1.0 / items.Count());

            var svd=h.Svd(true);

            var VT = svd.VT;
            var V = VT.Transpose();
            var S = svd.S;
            var U = svd.U;

            var d = Math.Sign(U.Determinant() * VT.Determinant()); //tady je mozna problem v transpozici V, v SVD neni jasne zda vraci V ci VT
            var s = Matrix<double>.Build.Diagonal(new double[] { 1, d });

            Rotation = U*s*VT;
            var tr = (Matrix<double>.Build.Diagonal(S.AsArray()) * s).Trace();
            Scale =  VarA==0?1:VarA / tr;
            Translation = CA - Scale * Rotation * CB;
        }
    }

}
