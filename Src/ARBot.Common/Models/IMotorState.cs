using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Motor control unit base information.
    /// </summary>
    public interface IMotorState
    {
        /// <summary>
        /// Emergency stop
        /// </summary>
        bool IsEmergencyStop { get; }
        /// <summary>
        /// Left encoder integral distance
        /// </summary>
        double LeftEncoder { get; }
        /// <summary>
        /// Right encoder integral distance
        /// </summary>
        double RightEncoder { get; }

        /// <summary>
        /// Left wheel speed in m/s
        /// </summary>
        double LeftWheelSpeed { get; }
        /// <summary>
        /// Right wheel speed in m/s
        /// </summary>
        double RightWheelSpeed { get; }
        
        
        /// <summary>
        /// Voltage
        /// </summary>
        double Voltage { get; }
        /// <summary>
        /// Left motor current
        /// </summary>
        double LeftMotorCurrent { get; }
        /// <summary>
        /// Right motor current
        /// </summary>
        double RightMotorCurrent { get; }
    }
}
