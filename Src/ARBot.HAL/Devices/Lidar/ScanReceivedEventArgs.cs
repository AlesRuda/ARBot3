using ARBot.Common.Navigations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Lidar
{
    public class ScanReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// Jednotliva mereni
        /// </summary>
        public IList<Ray> Samples;
        /// <summary>
        /// Casovy okamzik konce mereni
        /// </summary>
        public DateTime EndTime;
        /// <summary>
        /// Casovy okamzik zacatku mereni
        /// </summary>
        public DateTime StartTime;
    }
}
