using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Controls motors
    /// </summary>
    public interface IMotorControl: ISensor
    {
        /// <summary>
        /// Sets motors speed
        /// </summary>
        /// <param name="forvardSpeed">Forvard speed (left and right motor common speed) in m/s.</param>
        /// <param name="difSpeed">Diferencial speed in m/s. Positive value - right rotation, left motor is faster.
        /// Je to OFFSET NA KOLO (driver ho k jednomu kolu prictе a od druheho odecte), takze pro
        /// uhlovou rychlost omega plati <c>difSpeed = omega * rozchod / 2</c>. Viz ControlLoop.OnTick.</param>
        void Drive(double forvardSpeed, double difSpeed);

        /// <summary>
        /// Sets motor driver acceleration/deceleration
        /// </summary>
        /// <param name="acceleration"></param>
        void SetAcceleration(double acceleration);
/*
        /// <summary>
        /// Emergency stop
        /// </summary>
        void EmergencyStop();

        /// <summary>
        /// Recover from emergency stop
        /// </summary>
        void Release();
        */
        /// <summary>
        /// Returns state of motor control unit.
        /// </summary>
        /// <returns></returns>
        IMotorState GetLastMeasurement();

        /// <summary>
        /// Vyvolano po prichodu noveho mereni (v ramci zpracovani na pozadi).
        /// </summary>
        event EventHandler<IMotorState> MeasurementArived;
    }
}
