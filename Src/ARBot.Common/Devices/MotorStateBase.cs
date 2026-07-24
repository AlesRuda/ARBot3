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
        bool emergencyStop;
        double leftEncoder, rightEncoder, voltage, leftMotorCurrent, rightMotorCurrent;

        /// <summary>
        /// Contructor
        /// </summary>
        /// <param name="emergencyStop"></param>
        /// <param name="leftEncoder"></param>
        /// <param name="rightEncoder"></param>
        /// <param name="voltage"></param>
        /// <param name="leftMotorCurrent"></param>
        /// <param name="rightMotorCurrent"></param>
        public MotorStateBase(bool emergencyStop, double leftEncoder, double rightEncoder, double voltage, double leftMotorCurrent, double rightMotorCurrent)
        {
            this.emergencyStop = emergencyStop;
            this.leftEncoder=leftEncoder;
            this.rightEncoder=rightEncoder;
            this.voltage=voltage;
            this.leftMotorCurrent=leftMotorCurrent;
            this.rightMotorCurrent = rightMotorCurrent;
        }

        /// <summary>Bezparametrický ctor (nutný pro Build/reflexi prototypů zpráv).</summary>
        public MotorStateBase() : this(false, 0, 0, 0, 0, 0)
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
        public double LeftWheelSpeed
        {
            get
            {
                var t = FramePickupPeriod.TotalSeconds;
                return t < 0.001 ? 0 : LeftEncoder /t;
            }
        }
        /// <summary>
        /// Right wheel speed in m/s
        /// </summary>
        public double RightWheelSpeed
        {
            get
            {
                var t = FramePickupPeriod.TotalSeconds;
                return t<0.001?0:RightEncoder / t;
            }
        }

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
        }
    }
}
