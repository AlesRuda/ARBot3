using System;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Models;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Typovany pohled na stav filtru v danem casovem okamziku.
    /// Poloha a orientace ve svetovych souradnicich (X na vychod, Y na sever,
    /// orientace matematicky s 0 na vychod a rustem proti smeru hodinovych rucicek).
    ///
    /// Implementuje <see cref="IModelState"/>, aby ho mohl primo pouzit regulator.
    /// Mapovani: <see cref="Orientation"/> = <see cref="Theta"/>, <see cref="Velocity"/> = <see cref="V"/>,
    /// <see cref="OrientationVelocity"/> = <see cref="Omega"/>. <see cref="Roll"/>/<see cref="Pitch"/>
    /// se doplnuji z posledniho IMU (EKF je nedrzi).
    /// </summary>
    public class RobotState : IModelState
    {
        /// <summary>Poloha na vychod od pocatku [m]</summary>
        public double X { get; set; }
        /// <summary>Poloha na sever od pocatku [m]</summary>
        public double Y { get; set; }
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

        /// <summary>Naklon vlevo/vpravo [rad] (z posledniho IMU, ne z EKF).</summary>
        public double Roll { get; set; }
        /// <summary>Predozadni naklon [rad] (z posledniho IMU, ne z EKF).</summary>
        public double Pitch { get; set; }

        /// <summary>Svetova orientace [rad], matematicky. Namapovana na <see cref="Theta"/>.</summary>
        public double Orientation { get => Theta; set => Theta = value; }

        /// <summary>Rychlost otaceni v rad/s v matematickem smyslu. Namapovana na <see cref="Omega"/>.</summary>
        public double OrientationVelocity => Omega;

        /// <summary>Rychlost v m/s. Namapovana na <see cref="V"/>.</summary>
        public double Velocity => V;

        /// <summary>
        /// Rotacni matice ve svetovych souradnicich ENU z (<see cref="Orientation"/>,
        /// <see cref="Pitch"/>, <see cref="Roll"/>). Pouziva projektovou konvenci
        /// <see cref="Conversions.WordToWordTransform"/> (yaw kolem svisle osy Z).
        /// </summary>
        public Matrix4x4 Rotation
            => Conversions.WordToWordTransform(Orientation, Pitch, Roll, Vector3.Zero);

        /// <summary>
        /// Kompletni transformace = <see cref="Rotation"/> + posun (<see cref="X"/>, <see cref="Y"/>).
        /// </summary>
        public Matrix4x4 Trasnformation
            => Conversions.WordToWordTransform(Orientation, Pitch, Roll, new Vector3((float)X, (float)Y, 0f));

        public override string ToString()
        {
            return string.Format("X={0:F2} Y={1:F2} theta={2:F3} v={3:F2} omega={4:F3}", X, Y, Theta, V, Omega);
        }
    }
}
