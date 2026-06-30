using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.HAL
{
    /// <summary>
    /// Parametry kamery
    /// </summary>
    public class CameraSettings
    {
        public CameraSettings(int width, int height)
        {
            Width = width;
            Height = height;
        }
        /// <summary>
        /// Sirka snimku kamery
        /// </summary>
        public int Width { get; private set; }
        /// <summary>
        /// Vyska snimku kamery
        /// </summary>
        public int Height { get; private set; }
    }
}
