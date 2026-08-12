using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena (debug) zprava: pozadavek ridici smycky na motory v danem case.
    /// Role "odvozena" - pri replay se regeneruje, neni replay-vstup.
    /// <see cref="Speed"/>/<see cref="RotationSpeed"/> je rychlost, kterou smycka skutecne
    /// prikazala (tedy vystup regulatoru PO upravach smycky - dobrzdeni zastarale drahy,
    /// nouzove zastaveni), <see cref="Forvard"/>/<see cref="Dif"/> jsou primo argumenty
    /// <c>IMotorControl.Drive(forvard, dif)</c> (dif = RotationSpeed * Rozchod).
    /// <see cref="EmergencyStop"/> rika, ze zasah zkratilo nouzove zastaveni - bez nej by v
    /// zaznamu byly nuly bez vysvetleni.
    /// </summary>
    [Serializable()]
    public class DriveCommandMsg : Message, IHasCaptureTime
    {
        /// <summary>Format verze 2: pridan <see cref="EmergencyStop"/>.</summary>
        public const int FormatVersion = 2;

        /// <summary>Prikazana dopredna rychlost [m/s].</summary>
        public double Speed;
        /// <summary>Prikazana rotacni rychlost [rad/s], matematicky (+CCW).</summary>
        public double RotationSpeed;
        /// <summary>Argument Drive: dopredna rychlost [m/s] (= <see cref="Speed"/>).</summary>
        public double Forvard;
        /// <summary>Argument Drive: diferencialni rychlost [m/s] (= RotationSpeed * Rozchod).</summary>
        public double Dif;
        /// <summary>
        /// Bylo v case taktu aktivni nouzove zastaveni (<see cref="Models.IMotorState.IsEmergencyStop"/>)?
        /// Pak je dopredna rychlost nulovana smyckou - viz doc/robotour-mission.md.
        /// </summary>
        public bool EmergencyStop;
        /// <summary>Cas, ke kteremu prikaz plati (cas taktu ridici smycky).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public DriveCommandMsg() : base("DriveCommandMsg", FormatVersion)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Speed);
            bw.Write(RotationSpeed);
            bw.Write(Forvard);
            bw.Write(Dif);
            Write(bw, TimeStamp);
            if (Verze >= 2)
                bw.Write(EmergencyStop);
        }

        public override void FromData(BinaryReader br)
        {
            Speed = br.ReadDouble();
            RotationSpeed = br.ReadDouble();
            Forvard = br.ReadDouble();
            Dif = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
            // Starsi zaznamy (v1) priznak nemaji - zustava false.
            EmergencyStop = Verze >= 2 && br.ReadBoolean();
        }

        public override Message Build() => new DriveCommandMsg();

        public override string ToString()
            => string.Format("DriveCommandMsg fwd={0:F2} dif={1:F2} (v={2:F2} w={3:F3}){4}",
                             Forvard, Dif, Speed, RotationSpeed, EmergencyStop ? " ESTOP" : string.Empty);
    }
}
