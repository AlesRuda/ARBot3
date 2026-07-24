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

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public RobotStateMsg() : base("RobotStateMsg", 1)
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
            Covariance = Covariance
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
        }

        public override Message Build() => new RobotStateMsg();

        public override string ToString()
            => string.Format("RobotStateMsg X={0:F2} Y={1:F2} th={2:F3} v={3:F2} w={4:F3}", X, Y, Theta, V, Omega);
    }
}
