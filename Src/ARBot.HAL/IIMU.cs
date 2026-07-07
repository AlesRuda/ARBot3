using ARBot.Common.Devices;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    /// <summary>
    /// Rozhrani pro inercialni jednotky
    /// </summary>
    public interface IIMU:ISensor
    {
        /// <summary>
        /// Vraci posledni zmerene hodnoty. Bez noveho mereni vraci null.
        /// </summary>
        IMUState GetLastMeasurement();

        /// <summary>
        /// Vyvolano po prichodu noveho mereni (v ramci zpracovani na pozadi).
        /// </summary>
        event EventHandler<IMUState> MeasurementArived;
    }
}
