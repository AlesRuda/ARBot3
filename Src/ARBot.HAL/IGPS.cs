using ARBot.Common.Devices;
using ARBot.HAL.NMEA;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    public interface IGPS:ISensor
    {
        /// <summary>
        /// Vraci posledni zmerene hodnoty. Bez noveho mereni vraci null.
        /// </summary>
        GPSState GetLastMeasurement();

        /// <summary>
        /// Vyvolano po prichodu noveho mereni (v ramci zpracovani na pozadi).
        /// </summary>
        event EventHandler<GPSState> MeasurementArived;
    }
}
