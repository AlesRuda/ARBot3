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

        /// <summary>
        /// Poloha v <b>odometricke soustave</b> na vychod [m] (viz <see cref="OdomPose"/>):
        /// spojita, korekce GPS / koridoru / korelace do ni neskacou. Pocatek je tam, kde fuze
        /// dostala prvni merenie. Plni ji <see cref="AsyncFusionEngine.GetStateAt"/>; ve stavu
        /// odjinud (simulace, zprava starsi verze) je 0.
        /// </summary>
        public double OdomX;
        /// <summary>Poloha v odometricke soustave na sever [m] (viz <see cref="OdomX"/>).</summary>
        public double OdomY;
        /// <summary>Orientace v odometricke soustave [rad], matematicky (viz <see cref="OdomX"/>).</summary>
        public double OdomTheta;

        /// <summary>
        /// Transformace odometricka soustava → svet v okamziku tohoto stavu: svet =
        /// <c>R(dTheta)·odom + (dX, dY)</c>. Prevadi mezi soustavami vse, co se v nich potka
        /// (cil a mrkev do lokalni vrstvy, grid a plan do zobrazeni). Meni se jen korekcemi,
        /// mezi nimi stoji.
        /// </summary>
        public FrameTransform OdomToWorld()
        {
            double dTh = Conversions.NormalizeOrientation(Theta - OdomTheta);
            double c = Math.Cos(dTh), s = Math.Sin(dTh);
            return new FrameTransform(X - (c * OdomX - s * OdomY), Y - (s * OdomX + c * OdomY), dTh);
        }

        /// <summary>
        /// Transformace z dane lokalni soustavy do sveta v okamziku tohoto stavu
        /// (<see cref="LocalFrame.World"/> = identita).
        /// </summary>
        public FrameTransform ToWorldTransform(LocalFrame frame)
            => frame == LocalFrame.Odom ? OdomToWorld() : FrameTransform.Identity;

        /// <summary>
        /// Stav vyjadreny v dane soustave: u <see cref="LocalFrame.World"/> tentyz objekt, u
        /// <see cref="LocalFrame.Odom"/> KOPIE, ve ktere <see cref="X"/>/<see cref="Y"/>/<see cref="Theta"/>
        /// nesou odometrickou pozu (rychlosti, cas, naklony i odometricka pole zustavaji). Tak ji
        /// dostane lokalni vrstva i regulator, aniz by o soustave vedely.
        ///
        /// <para>Kovariance se kopiruje BEZE ZMENY — je to nejistota globalni pozy, odometricka
        /// vlastni nema. Lokalni vrstva ji nepouziva.</para>
        /// </summary>
        public RobotState InFrame(LocalFrame frame)
        {
            if (frame != LocalFrame.Odom) return this;
            return new RobotState
            {
                X = OdomX,
                Y = OdomY,
                Theta = OdomTheta,
                V = V,
                Omega = Omega,
                TimeStamp = TimeStamp,
                Covariance = Covariance,
                Roll = Roll,
                Pitch = Pitch,
                OdomX = OdomX,
                OdomY = OdomY,
                OdomTheta = OdomTheta,
            };
        }

        /// <summary>Naklon vlevo/vpravo [rad] (z posledniho IMU, NE z EKF - viz pozn. u <see cref="Pitch"/>).</summary>
        public double Roll { get; set; }
        /// <summary>
        /// Predozadni naklon [rad] (z posledniho IMU, NE z EKF).
        /// <para>OTEVRENY UKOL: na rozdil od ostatnich slozek nejsou Pitch/Roll fuzovane - plni je
        /// <c>ControlLoop</c> z posledniho dosleho <c>IMUState</c>, ktery navic nenese identitu zdroje
        /// (pri dvou IMU neni poznat od ktereho). Meli by byt ve stavu EKF. Viz
        /// doc/ekf-fusion.md → "Pitch/Roll patri do stavu EKF".</para>
        /// </summary>
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
        /// <see cref="Conversions.WorldToWorldTransform"/> (yaw kolem svisle osy Z).
        /// </summary>
        public Matrix4x4 Rotation
            => Conversions.WorldToWorldTransform(Orientation, Pitch, Roll, Vector3.Zero);

        /// <summary>
        /// Kompletni transformace = <see cref="Rotation"/> + posun (<see cref="X"/>, <see cref="Y"/>).
        /// </summary>
        public Matrix4x4 Transformation
            => Conversions.WorldToWorldTransform(Orientation, Pitch, Roll, new Vector3((float)X, (float)Y, 0f));

        public override string ToString()
        {
            return string.Format("X={0:F2} Y={1:F2} theta={2:F3} v={3:F2} omega={4:F3}", X, Y, Theta, V, Omega);
        }
    }
}
