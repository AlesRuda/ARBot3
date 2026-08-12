namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Parametry obrazu virtualni kamery (rozliseni, zorne pole, takt).
    /// Vychozi hodnoty odpovidaji tomu, jak je v teto aplikaci provozovana D435
    /// (RGB 640x480, hloubka 480x270). Viz doc/virtual-hw.md.
    /// </summary>
    public sealed class VirtualCameraOptions
    {
        /// <summary>Rozliseni barevneho streamu.</summary>
        public CameraSettings RGB = new CameraSettings(640, 480);

        /// <summary>Rozliseni hloubkoveho streamu.</summary>
        public CameraSettings Depth = new CameraSettings(480, 270);

        /// <summary>Horizontalni zorne pole barevneho streamu [stupne] (D435: 69,4).</summary>
        public double RgbHorizontalFovDeg = 69.4;

        /// <summary>Horizontalni zorne pole hloubkoveho streamu [stupne] (D435: 87).</summary>
        public double DepthHorizontalFovDeg = 87.0;

        /// <summary>Snimkova frekvence [Hz].</summary>
        public int FrameRateHz = 30;
    }
}
