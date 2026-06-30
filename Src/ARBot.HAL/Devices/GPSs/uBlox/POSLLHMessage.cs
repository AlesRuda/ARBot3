using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.GPSs.uBlox
{
    public class POSLLHMessage:UBXMessage
    {
        public POSLLHMessage(byte[] payload):base(1, 2, payload)
        {
        }

        public UInt32 ITOW
        {
            get
            {
                return GetUInt32(0);
            }
        }

        public double Longitude
        {
            get
            {
                return GetInt32(4) * 1e-7;
            }
        }
        public double Latitude
        {
            get
            {
                return GetInt32(8) * 1e-7;
            }
        }
        /// <summary>
        /// Vyska nad elipsoidem v m
        /// </summary>
        public double Height
        {
            get
            {
                return GetInt32(12) * 1e-3;
            }
        }
        /// <summary>
        /// Vyska na hladinou more v m
        /// </summary>
        public double HeightMSL
        {
            get
            {
                return GetInt32(16)*1e-3;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public double HorizontalAccuracy
        {
            get
            {
                return GetUInt32(20) * 1e-3;
            }
        }
        public double VerticalAccuracy
        {
            get
            {
                return GetUInt32(24) * 1e-3;
            }
        }
    }
}
