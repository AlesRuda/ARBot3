using System;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Typovany pohled na stav filtru v danem casovem okamziku.
    /// Poloha a orientace ve svetovych souradnicich (X na vychod, Y na sever,
    /// orientace matematicky s 0 na vychod a rustem proti smeru hodinovych rucicek).
    /// </summary>
    public class RobotState
    {
        /// <summary>Poloha na vychod od pocatku [m]</summary>
        public double X;
        /// <summary>Poloha na sever od pocatku [m]</summary>
        public double Y;
        /// <summary>Orientace [rad], matematicky (0 = vychod, roste proti smeru hod. rucicek)</summary>
        public double Theta;
        /// <summary>Rychlost ve smeru orientace [m/s]</summary>
        public double V;
        /// <summary>Uhlova rychlost [rad/s]</summary>
        public double Omega;
        /// <summary>Casovy okamzik, ke kteremu stav plati</summary>
        public DateTime TimeStamp;
        /// <summary>Kovariance stavu (5x5), muze byt null.</summary>
        public Matrix<double> Covariance;

        public override string ToString()
        {
            return string.Format("X={0:F2} Y={1:F2} theta={2:F3} v={3:F2} omega={4:F3}", X, Y, Theta, V, Omega);
        }
    }
}
