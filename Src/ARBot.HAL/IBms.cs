using System;
using ARBot.Common.Devices;

namespace ARBot.HAL
{
    /// <summary>BMS baterie (doc/plan-bms-jbd.md) — jen čte stav, nic neřídí.</summary>
    public interface IBms : ISensor
    {
        /// <summary>Poslední měření; bez nového měření null.</summary>
        BmsState GetLastMeasurement();

        /// <summary>Vyvoláno po příchodu nového měření (i zprávy bez měření po chybě).</summary>
        event EventHandler<BmsState> MeasurementArived;
    }
}
