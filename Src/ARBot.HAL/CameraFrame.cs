using ARBot.Common.Common;
using ARBot.Common.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    /// <summary>
    /// Snimek kamery
    /// </summary>
    public class CameraFrame: SensorStateBase
    {
        /// <summary>
        /// Barevny obrazek
        /// </summary>
        public Image<BGR32> ImageRGB { get; set; }
        /// <summary>
        /// Sjizdnost
        /// </summary>
        public Image<Gray> ImageProbability { get; set; }
        /// <summary>
        /// 3D obraz
        /// </summary>
        public Image<Gray16> ImageDepth { get; set; }

        public DateTime RGBTimeStamp;
        public DateTime DepthTimeStamp;
    }
}
