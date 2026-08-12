using System;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Vision.Synthetic
{
    /// <summary>
    /// Vnitrni parametry simulovane kamery. Ciste dirkova komora (pinhole) bez zkresleni -
    /// simulace nema duvod predstirat zkresleni realne optiky. Viz doc/virtual-hw.md.
    /// </summary>
    public static class SyntheticIntrinsics
    {
        /// <summary>
        /// Intrinsics ze zadaneho rozliseni a horizontalniho zorneho pole.
        /// Ohnisko je spolecne pro obe osy (ctvercove pixely), stred je uprostred obrazu.
        /// </summary>
        /// <param name="width">Sirka obrazu [px].</param>
        /// <param name="height">Vyska obrazu [px].</param>
        /// <param name="horizontalFovDeg">Horizontalni zorne pole [stupne].</param>
        public static Intrinsics Pinhole(int width, int height, double horizontalFovDeg)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (horizontalFovDeg <= 0 || horizontalFovDeg >= 180)
                throw new ArgumentOutOfRangeException(nameof(horizontalFovDeg));

            float f = (float)(width / 2.0 / Math.Tan(horizontalFovDeg * Math.PI / 360.0));

            return new Intrinsics
            {
                Width = width,
                Height = height,
                Fx = f,
                Fy = f,
                PPx = width / 2f,
                PPy = height / 2f,
                Model = Intrinsics.Distortion.None,
                Coeffs = new float[5],
            };
        }
    }
}
