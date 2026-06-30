using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.HAL
{
    /// <summary>
    /// Rozhrani pro NeoPixel
    /// </summary>
    public interface INeoPixelDriver
    {
        /// <summary>
        /// Zobraz
        /// </summary>
        /// <param name="values"></param>
        void Send(Color[] values);
    }
}
