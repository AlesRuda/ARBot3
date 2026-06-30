using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ARBot.Common.Devices;
using ARBot.Common.Common;
using ARBot.HAL.NMEA;

namespace ARBot.HAL.Devices.GPSs
{
    public class NmeaGps:SensorBase<GPSState>, IGPS
    {
        IUart uart;

        /// <summary>
        /// Constructor for the Device class.
        /// </summary>
        public NmeaGps(IUart uart)
        {
            if (uart == null)
                throw new ArgumentNullException("uart");
            this.uart = uart;
            Start();
        }
        /// <summary>
        /// Provides a mechanism to send an NMEA sentence to the GPS Device.
        /// </summary>
        public void Send(NmeaMessage sentence)
        {
            uart.WriteLine(sentence.ToString());
        }

        protected override GPSState GetMeasurement()
        {
            GgaMessage gga=null;
            VtgMessage vtg = null;
            bool end = false;
            while (!end)
            {
                var s = Read();
                if (s is GgaMessage)
                {
                    gga = s as GgaMessage;
                    vtg = null;
                }
                if (s is VtgMessage)
                {
                    if (gga != null)
                        end = true;
                    vtg = s as VtgMessage;
                }
            }
            return new GPSState()
            {
                FixTime = gga.FixTime,
                Latitude=gga.Latitude,
                Longitude=gga.Longitude,
                Quality=(GPSState.FixQuality)gga.Quality,
                NumberOfSatellites=gga.NumberOfSatellites,
                Hdop=gga.Hdop,
                Altitude = gga.Altitude,
                DynamicOrientation= vtg!=null?Conversions.Azimut2Orientation(Conversions.Deg2Rad(vtg.CourseTrue)):(double?)null,
                Speed= vtg != null ? vtg.SpeedKph/3.6 : (double?)null
            };
        }


        private NmeaMessage Read()
        {
            string strMessage = "";
            NmeaMessage retSentence = null;

            try
            {
                strMessage = uart.ReadLine();
                if (strMessage != null)
                {
                    retSentence = NmeaMessage.Parse(strMessage);
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine(ex.ToString());
            }
            return retSentence;
        }
    }
}

