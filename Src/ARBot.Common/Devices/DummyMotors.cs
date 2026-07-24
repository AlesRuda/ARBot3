using ARBot.Common.Models;
using System;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Fiktivni motory (no-op). Pouziti: rezim Simulate (prepocet ze zaznamu) a testy,
    /// kde se nema fyzicky nic ridit. <see cref="Drive"/> a <see cref="SetAcceleration"/>
    /// nedelaji nic, <see cref="GetLastMeasurement"/> vraci nulove mereni a udalost
    /// <see cref="MeasurementArived"/> se nikdy nevyvola.
    /// </summary>
    public sealed class DummyMotors : IMotorControl
    {
        /// <inheritdoc/>
        public string Name => "Dummy";

        /// <inheritdoc/>
        public bool IsError => false;

        /// <inheritdoc/>
        public void Drive(double forvardSpeed, double difSpeed) { /* no-op */ }

        /// <inheritdoc/>
        public void SetAcceleration(double acceleration) { /* no-op */ }

        /// <inheritdoc/>
        public IMotorState GetLastMeasurement() => new MotorStateBase(false, 0, 0, 0, 0, 0);

        /// <inheritdoc/>
        /// <remarks>Nikdy se nevyvola - fiktivni motory neprodukuji zadna mereni.</remarks>
        public event EventHandler<IMotorState> MeasurementArived
        {
            add { /* no-op */ }
            remove { /* no-op */ }
        }
    }
}
