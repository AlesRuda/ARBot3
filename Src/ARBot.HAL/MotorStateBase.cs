using ARBot.Common.Devices;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.HAL
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
        /// <param name="leftWheelSpeed"></param>
        /// <param name="rightWheelSpeed"></param>
        public MotorStateBase(bool emergencyStop, double leftEncoder, double rightEncoder, double voltage, double leftMotorCurrent, double rightMotorCurrent)
        {
            this.emergencyStop = emergencyStop;
            this.leftEncoder=leftEncoder;
            this.rightEncoder=rightEncoder;
            this.voltage=voltage;
            this.leftMotorCurrent=leftMotorCurrent;
            this.rightMotorCurrent = rightMotorCurrent;
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
    }
}
