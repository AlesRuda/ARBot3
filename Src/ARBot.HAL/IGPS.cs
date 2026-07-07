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
        GPSState GetLastMeasurement();
    }
}
