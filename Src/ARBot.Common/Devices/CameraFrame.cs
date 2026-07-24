using ARBot.Common.Common;
using ARBot.Common.Logs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Snimek kamery
    /// </summary>
    public class CameraFrame: SensorStateBase, INamedMessage
    {
        /// <summary>Jmeno zdroje (napr. kamera Left/Right) - pro rozliseni v pipeline a vizualizaci.</summary>
        public string Name { get; set; }

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
