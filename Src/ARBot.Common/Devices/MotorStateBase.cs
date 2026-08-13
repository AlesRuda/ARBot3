using ARBot.Common.Logs;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// base motor state implementation
    /// </summary>
    public class MotorStateBase : SensorStateBase, IMotorState
    {
        /// <summary>
        /// Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).
        /// <para>Verze 2: enkodery jsou KUMULATIVNI a rychlosti kol se prenaseji jako vlastni pole.
        /// Do verze 1 byl enkoder prirustek od posledniho vyzvednuti a rychlost se z nej dopocitavala
        /// pres <c>FramePickupPeriod</c> - to davalo nulu, kdyz mereni nikdo nevyzvedaval (v runtime
        /// se motory odebiraji udalosti). Viz doc/virtual-hw.md.</para>
        /// </summary>
        public const int FormatVersion = 2;

        bool emergencyStop;
        double leftEncoder, rightEncoder, voltage, leftMotorCurrent, rightMotorCurrent;
        double leftWheelSpeed, rightWheelSpeed;

        /// <summary>
        /// Contructor
        /// </summary>
        /// <param name="emergencyStop">Aktivni nouzove zastaveni.</param>
        /// <param name="leftEncoder">KUMULATIVNI ujeta draha leveho kola [m].</param>
        /// <param name="rightEncoder">KUMULATIVNI ujeta draha praveho kola [m].</param>
        /// <param name="voltage">Napeti baterie [V].</param>
        /// <param name="leftMotorCurrent">Proud leveho motoru [A].</param>
        /// <param name="rightMotorCurrent">Proud praveho motoru [A].</param>
        /// <param name="leftWheelSpeed">Rychlost leveho kola [m/s] - meri ji driver ze SVEHO
        /// vzorkovaciho intervalu, aby nezavisela na tom, kdo a kdy mereni cte.</param>
        /// <param name="rightWheelSpeed">Rychlost praveho kola [m/s].</param>
        public MotorStateBase(bool emergencyStop, double leftEncoder, double rightEncoder, double voltage,
                              double leftMotorCurrent, double rightMotorCurrent,
                              double leftWheelSpeed, double rightWheelSpeed)
            : base(FormatVersion)
        {
            this.emergencyStop = emergencyStop;
            this.leftEncoder=leftEncoder;
            this.rightEncoder=rightEncoder;
            this.voltage=voltage;
            this.leftMotorCurrent=leftMotorCurrent;
            this.rightMotorCurrent = rightMotorCurrent;
            this.leftWheelSpeed = leftWheelSpeed;
            this.rightWheelSpeed = rightWheelSpeed;
        }

        /// <summary>Bezparametrický ctor (nutný pro Build/reflexi prototypů zpráv).</summary>
        public MotorStateBase() : this(false, 0, 0, 0, 0, 0, 0, 0)
        {
        }

        /// <summary>
        /// Emergency stop
        /// </summary>
        public bool IsEmergencyStop
        {
            get
            {
                return emergencyStop;
            }
        }
        /// <summary>
        /// Left encoder distance in m
        /// </summary>
        public double LeftEncoder
        {
            get
            {
                return leftEncoder;
            }
        }
        /// <summary>
        /// Right encoder distance in m
        /// </summary>
        public double RightEncoder
        {
            get
            {
                return rightEncoder;
            }
        }
        /// <summary>
        /// Voltage
        /// </summary>
        public double Voltage
        {
            get
            {
                return voltage;
            }
        }
        /// <summary>
        /// Left motor current
        /// </summary>
        public double LeftMotorCurrent
        {
            get
            {
                return leftMotorCurrent;
            }
        }
        /// <summary>
        /// Right motor current
        /// </summary>
        public double RightMotorCurrent
        {
            get
            {
                return rightMotorCurrent;
            }
        }
        /// <summary>
        /// Left wheel speed in m/s
        /// </summary>
        public double LeftWheelSpeed => leftWheelSpeed;
        /// <summary>
        /// Right wheel speed in m/s
        /// </summary>
        public double RightWheelSpeed => rightWheelSpeed;

        public override string ToString()
        {
            return string.Format("MotorStateBase: IsEmergencyStop={0}, LeftEncoder={1}, RightEncoder={2}, Voltage={3}, LeftMotorCurrent={4}, RightMotorCurrent={5}, LeftWheelSpeed={6}, RightWheelSpeed={7}", IsEmergencyStop, LeftEncoder, RightEncoder, Voltage, LeftMotorCurrent, RightMotorCurrent, LeftWheelSpeed, RightWheelSpeed);
        }

        /// <inheritdoc/>
        public override Message Build() => new MotorStateBase();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            WriteMeta(bw);
            bw.Write(emergencyStop);
            bw.Write(leftEncoder);
            bw.Write(rightEncoder);
            bw.Write(voltage);
            bw.Write(leftMotorCurrent);
            bw.Write(rightMotorCurrent);
            bw.Write(leftWheelSpeed);
            bw.Write(rightWheelSpeed);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            ReadMeta(br);
            emergencyStop = br.ReadBoolean();
            leftEncoder = br.ReadDouble();
            rightEncoder = br.ReadDouble();
            voltage = br.ReadDouble();
            leftMotorCurrent = br.ReadDouble();
            rightMotorCurrent = br.ReadDouble();

            if (Verze >= 2)
            {
                leftWheelSpeed = br.ReadDouble();
                rightWheelSpeed = br.ReadDouble();
            }
            else
            {
                // Verze 1 rychlosti neukladala (dopocitavala se z prirustku enkoderu a doby od
                // vyzvednuti). Zpetne to nejde rekonstruovat - enkoder je tam prirustek, ne
                // kumulativni hodnota, a doba vyzvednuti se neserializovala.
                leftWheelSpeed = 0;
                rightWheelSpeed = 0;
            }
        }
    }
}
