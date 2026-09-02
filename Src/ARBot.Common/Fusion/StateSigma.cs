using System;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Smerodatne odchylky stavu EKF spoctene z kovariancni matice <c>P</c>
    /// (rozvrzeni podle <see cref="EKFModel"/>: <c>IX=0, IY=1, ITh=2, IV=3, IW=4</c>).
    ///
    /// <para><b>Nac to je.</b> Kovariance tekla v <c>RobotStateMsg</c> na Stream i do zaznamu uz
    /// davno, ale <b>nikdo ji nezobrazoval</b> - o vyvoji nejistoty filtru tedy nebylo videt nic.
    /// Prevod na sigmu je doménovy vypocet nad kovarianci, ne formatovani do tabulky, takze patri
    /// sem k fuzi; telemetrie ho jen vola. Druhy duvod je testovatelnost: sloupce telemetrie zijou
    /// v projektu <c>ARBot</c>, na ktery <c>ARBot.Common.Tests</c> nevidi.</para>
    ///
    /// <para><b>Vsechno vraci <c>double?</c> a pri necem podezrelem <c>null</c>, ne nulu.</b>
    /// Nula by lhala („filtr si je jisty") a NaN by se v grafu tvaril jako platna hodnota;
    /// prazdno je jedina poctiva odpoved na „tohle nevim". Viz doc/ekf-fusion.md.</para>
    /// </summary>
    public static class StateSigma
    {
        /// <summary>Smerodatna odchylka polohy v ose X (world ENU, vychod) [m].</summary>
        public static double? X(Matrix<double> p) => Diag(p, EKFModel.IX);

        /// <summary>Smerodatna odchylka polohy v ose Y (world ENU, sever) [m].</summary>
        public static double? Y(Matrix<double> p) => Diag(p, EKFModel.IY);

        /// <summary>Smerodatna odchylka kurzu [rad]. Prevod na stupne patri az na okraj (UI).</summary>
        public static double? Theta(Matrix<double> p) => Diag(p, EKFModel.ITh);

        /// <summary>Smerodatna odchylka dopredne rychlosti [m/s].</summary>
        public static double? V(Matrix<double> p) => Diag(p, EKFModel.IV);

        /// <summary>Smerodatna odchylka uhlove rychlosti [rad/s].</summary>
        public static double? Omega(Matrix<double> p) => Diag(p, EKFModel.IW);

        /// <summary>
        /// Souhrnna nejistota polohy [m]: <c>sqrt(Pxx + Pyy)</c>.
        ///
        /// <para>Je to <b>jedno cislo do grafu</b> na otazku „roste mi nejistota?"; kterym smerem
        /// pak reknou <see cref="X"/> a <see cref="Y"/>. <b>ZAMERNE to neni poloosa elipsy</b> -
        /// ta by brala v uvahu i korelaci <c>P[0,1]</c> a patri do panelu, ne do tabulky.</para>
        /// </summary>
        public static double? Position(Matrix<double> p)
        {
            double? vx = Var(p, EKFModel.IX), vy = Var(p, EKFModel.IY);
            if (vx == null || vy == null) return null;
            return Math.Sqrt(vx.Value + vy.Value);
        }

        /// <summary>Odmocnina prvku na diagonale, nebo <c>null</c>, kdyz ho nelze poctive urcit.</summary>
        private static double? Diag(Matrix<double> p, int i)
        {
            double? v = Var(p, i);
            return v == null ? null : Math.Sqrt(v.Value);
        }

        /// <summary>
        /// Rozptyl z diagonaly. <c>null</c> pri chybejici nebo prilis male matici (stara zprava,
        /// zmena stavoveho vektoru) a pri zapornem rozptylu (numericky rozpadla kovariance).
        /// </summary>
        private static double? Var(Matrix<double> p, int i)
        {
            if (p == null || p.RowCount <= i || p.ColumnCount <= i) return null;
            double v = p[i, i];
            if (double.IsNaN(v) || v < 0) return null;
            return v;
        }
    }
}
