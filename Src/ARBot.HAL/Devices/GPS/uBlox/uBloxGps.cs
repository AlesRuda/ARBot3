using ARBot.Common.Common;
using ARBot.Common.Devices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.GPSs.uBlox
{
    public class uBloxGps : UartSensorBase<GPSState>, IGPS
    {
        /// <summary>
        /// Constructor for the Device class.
        /// </summary>
        public uBloxGps(IUart uart):base(uart)
        {
            Start();
        }

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => "uBloxGps";

        protected override GPSState GetMeasurement()
        {
            PVTMessage pos = null;
            while (pos == null)
            {
                var msg = Read();
                if (msg is PVTMessage)
                {
                    pos = msg as PVTMessage;
                }
            }
            TimeSpan ts = new TimeSpan(0, 0, 0, 0, 0);
            int d = 0, h = 0, m = 0, s = 0, ms = 0;
            d = (int)pos.ITOW / (1000 * 60 * 60 * 24);
            h = (int)pos.ITOW / (1000 * 60 * 60) - d * 24;
            m = (int)pos.ITOW / (1000 * 60) - (d * 24 + h) * 60;
            s = (int)pos.ITOW / 1000 - ((d * 24 + h) * 60 + m * 60);
            ms = (int)pos.ITOW - (((d * 24 + h) * 60 + m * 60) + s) * 1000;
            return new GPSState()
            {
                FixTime = new TimeSpan(d, h, m, s, ms),
                Latitude = pos.Latitude,
                Longitude = pos.Longitude,
                Quality = (GPSState.FixQuality)pos.FixType,
                NumberOfSatellites = pos.NumSV,
                Hdop = pos.PDOP,
                Altitude = pos.HeightMSL,
                DynamicOrientation = Math.Atan2(pos.VelocityN, pos.VelocityE),
                Orientation= pos.HeadVehValid?Conversions.Azimut2Orientation(Conversions.Deg2Rad(pos.HeadVeh)):(double?)null,
                Speed = pos.GroundSpeed
            };
        }


        private UBXMessage Read()
        {
            UBXMessage m = null;

            try
            {
                m = UBXMessage.Parse(uart);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            return m;
        }
    }
}

