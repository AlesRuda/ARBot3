using System;
using ARBot.Common.Common;

namespace ARBot.Common.Vision
{
    /// <summary>Druh vrstvy - urcuje, jak se renderuje.</summary>
    public enum LayerKind
    {
        /// <summary>Barevny obraz (BGR32).</summary>
        Color,
        /// <summary>Pravdepodobnostni / sedy obraz (1 bajt/pixel).</summary>
        Probability,
        /// <summary>Hloubka (16 bit).</summary>
        Depth
    }

    /// <summary>
    /// Pojmenovana obrazova vrstva - spolecny model pro <see cref="ARBot.Common.Logs.Blob"/> i
    /// <see cref="ARBot.Common.Devices.CameraFrame"/>. Podle <see cref="Kind"/> je vyplneno prave
    /// jedno z pol <see cref="Color"/> / <see cref="Gray"/> / <see cref="Depth"/>.
    /// </summary>
    public sealed class ImageLayer
    {
        public string Name;
        public LayerKind Kind;
        public DateTime TimeStamp;

        public Image<BGR32> Color;
        public Image<Gray> Gray;
        public Image<Gray16> Depth;

        public int Width =>
            Color?.Width ?? Gray?.Width ?? Depth?.Width ?? 0;
        public int Height =>
            Color?.Height ?? Gray?.Height ?? Depth?.Height ?? 0;
    }
}
