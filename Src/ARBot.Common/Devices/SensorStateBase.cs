using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Predek pro mereni senzoru
    /// </summary>
    public abstract class SensorStateBase
    {
        /// <summary>
        /// Poradi vrozku
        /// </summary>
        public uint FrameNum;

        /// <summary>
        /// Pocet preskocenych vzorku pred timto a predchozim vyzvednutym
        /// </summary>
        public uint DropedOutNum;

        /// <summary>
        /// Doba od prichodu predchoziho frejmu v s
        /// </summary>
        public TimeSpan FrameReceivePeriod;
        /// <summary>
        /// Doba od vyzvednuti predchoziho frejmu v s
        /// </summary>
        public TimeSpan FramePickupPeriod;

        /// <summary>
        /// okamzik vzorku
        /// </summary>
        public DateTime TimeStamp;

    }
}
