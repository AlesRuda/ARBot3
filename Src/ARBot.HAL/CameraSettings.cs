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
        /// <summary>
        /// Snimkova frekvence, kdyz se nezada [sn/s]. Hodnota je v <see cref="Profile.CameraFps"/>,
        /// aby ji videl i registr parametru (<c>Common</c> na <c>HAL</c> nereferencuje).
        /// </summary>
        public static int DefaultFps => ARBot.Common.Configuration.Profile.CameraFps;

        public CameraSettings(int width, int height) : this(width, height, DefaultFps)
        {
        }

        /// <param name="width">Sirka snimku [px].</param>
        /// <param name="height">Vyska snimku [px].</param>
        /// <param name="fps">Snimkova frekvence [sn/s]; viz <see cref="Fps"/>.</param>
        public CameraSettings(int width, int height, int fps)
        {
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps), fps, "Fps musi byt kladne.");
            Width = width;
            Height = height;
            Fps = fps;
        }

        /// <summary>
        /// Sirka snimku kamery
        /// </summary>
        public int Width { get; private set; }
        /// <summary>
        /// Vyska snimku kamery
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Pozadovana snimkova frekvence [sn/s].
        ///
        /// <para><b>D435 zna jen nekolik hodnot</b> (6, 15, 30, 60) a na jinou pipeline vubec
        /// nenastartuje — proto to hlida uz <c>ParamRegistry.CameraFps</c> pri startu, aby se to
        /// nepoznalo az jako "kamera se nepripojila".</para>
        ///
        /// <para><b>Nac to je:</b> drazsi model segmentace nemusi stihat 30 sn/s (Model96.2 na NPU
        /// stoji ~44 ms na snimek, viz doc/semantic-segmentation.md). Snizit frekvenci uz na
        /// kamere je lepsi nez zahazovat hotove snimky - usetri to USB, dekodovani i zapis do
        /// zaznamu.</para>
        /// </summary>
        public int Fps { get; private set; } = DefaultFps;
    }
}
