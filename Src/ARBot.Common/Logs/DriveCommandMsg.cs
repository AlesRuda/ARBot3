using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena (debug) zprava: pozadavek ridici smycky na motory v danem case.
    /// Role "odvozena" - pri replay se regeneruje, neni replay-vstup.
    /// <see cref="Speed"/>/<see cref="RotationSpeed"/> jsou vystup regulatoru,
    /// <see cref="Forvard"/>/<see cref="Dif"/> jsou primo argumenty
    /// <c>IMotorControl.Drive(forvard, dif)</c> (dif = RotationSpeed * Rozchod).
    /// </summary>
    [Serializable()]
    public class DriveCommandMsg : Message, IHasCaptureTime
    {
        /// <summary>Dopredna rychlost z regulatoru [m/s].</summary>
        public double Speed;
        /// <summary>Rotacni rychlost z regulatoru [rad/s], matematicky (+CCW).</summary>
        public double RotationSpeed;
        /// <summary>Argument Drive: dopredna rychlost [m/s] (= <see cref="Speed"/>).</summary>
        public double Forvard;
        /// <summary>Argument Drive: diferencialni rychlost [m/s] (= RotationSpeed * Rozchod).</summary>
        public double Dif;
        /// <summary>Cas, ke kteremu prikaz plati (cas taktu ridici smycky).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public DriveCommandMsg() : base("DriveCommandMsg", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Speed);
            bw.Write(RotationSpeed);
            bw.Write(Forvard);
            bw.Write(Dif);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Speed = br.ReadDouble();
            RotationSpeed = br.ReadDouble();
            Forvard = br.ReadDouble();
            Dif = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new DriveCommandMsg();

        public override string ToString()
            => string.Format("DriveCommandMsg fwd={0:F2} dif={1:F2} (v={2:F2} w={3:F3})", Forvard, Dif, Speed, RotationSpeed);
    }
}
