using System;
using System.IO;
using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena (mezivysledkova) zprava: fuzovany <see cref="RobotState"/> v danem case.
    /// Role "odvozene" - pri replay se regeneruje a diffuje, neni replay-vstup.
    /// </summary>
    [Serializable()]
    public class RobotStateMsg : Message, IHasCaptureTime
    {
        /// <summary>
        /// Format verze 2 (4. 10. 2026): pridana odometricka poza <see cref="OdomX"/>,
        /// <see cref="OdomY"/>, <see cref="OdomTheta"/> (lp-grid-odometricka-soustava).
        /// </summary>
        public const int FormatVersion = 2;

        /// <summary>Poloha na vychod [m].</summary>
        public double X;
        /// <summary>Poloha na sever [m].</summary>
        public double Y;
        /// <summary>Orientace [rad], matematicky.</summary>
        public double Theta;
        /// <summary>Rychlost ve smeru orientace [m/s].</summary>
        public double V;
        /// <summary>Uhlova rychlost [rad/s].</summary>
        public double Omega;
        /// <summary>Cas, ke kteremu stav plati.</summary>
        public DateTime TimeStamp;
        /// <summary>Kovariance stavu (5x5), muze byt null.</summary>
        public Matrix<double> Covariance;
        /// <summary>Poloha v odometricke soustave na vychod [m] (viz <see cref="OdomPose"/>).</summary>
        public double OdomX;
        /// <summary>Poloha v odometricke soustave na sever [m].</summary>
        public double OdomY;
        /// <summary>Orientace v odometricke soustave [rad], matematicky.</summary>
        public double OdomTheta;

        /// <summary>
        /// Nese zprava odometrickou pozu? Ve verzi 1 neni — tam jsou <see cref="OdomX"/> atd. nuly
        /// a rozbor zaznamu je nesmi brat jako „robot stal v pocatku".
        /// </summary>
        public bool HasOdom => Verze >= 2;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public RobotStateMsg() : base("RobotStateMsg", FormatVersion)
        {
        }

        public RobotStateMsg(RobotState s) : this()
        {
            X = s.X;
            Y = s.Y;
            Theta = s.Theta;
            V = s.V;
            Omega = s.Omega;
            TimeStamp = s.TimeStamp;
            Covariance = s.Covariance;
            OdomX = s.OdomX;
            OdomY = s.OdomY;
            OdomTheta = s.OdomTheta;
        }

        /// <summary>Typovany pohled na obsah zpravy.</summary>
        public RobotState ToRobotState() => new RobotState
        {
            X = X,
            Y = Y,
            Theta = Theta,
            V = V,
            Omega = Omega,
            TimeStamp = TimeStamp,
            Covariance = Covariance,
            OdomX = OdomX,
            OdomY = OdomY,
            OdomTheta = OdomTheta
        };

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(X);
            bw.Write(Y);
            bw.Write(Theta);
            bw.Write(V);
            bw.Write(Omega);
            Write(bw, TimeStamp);
            bw.Write(Covariance != null);
            if (Covariance != null)
                Write(bw, Covariance);
            if (Verze >= 2)
            {
                bw.Write(OdomX);
                bw.Write(OdomY);
                bw.Write(OdomTheta);
            }
        }

        public override void FromData(BinaryReader br)
        {
            X = br.ReadDouble();
            Y = br.ReadDouble();
            Theta = br.ReadDouble();
            V = br.ReadDouble();
            Omega = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
            if (br.ReadBoolean())
                Covariance = ReadMatrixDouble(br);
            // Verze 1 odometrii nenese - zustava 0 (a HasOdom = false).
            if (Verze >= 2)
            {
                OdomX = br.ReadDouble();
                OdomY = br.ReadDouble();
                OdomTheta = br.ReadDouble();
            }
        }

        public override Message Build() => new RobotStateMsg();

        public override string ToString()
            => string.Format("RobotStateMsg X={0:F2} Y={1:F2} th={2:F3} v={3:F2} w={4:F3}", X, Y, Theta, V, Omega);
    }
}
