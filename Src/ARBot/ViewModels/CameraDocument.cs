using System;
using System.Globalization;
using System.Runtime.InteropServices;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.HAL;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovací dokument zobrazující RGB stream z kamery (<see cref="ICamera"/>).
    /// Obnova je řízená událostí <see cref="ICamera.MeasurementArived"/> (každý snímek),
    /// obraz se překresluje na UI vlákně. Kamera je předána jako parametr a dokument ji
    /// NEvlastní (jen se odhlásí z události, NEzavírá ji – na rozdíl od D435TestDocument,
    /// který si vlastní kameru vytváří sám).
    /// </summary>
    public partial class CameraDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.CameraDocumentView);

        private readonly ICamera? camera;

        /// <summary>Rozsah hloubky pro normalizaci do grayscale [mm] (nad = černá).</summary>
        private const double DepthMaxMm = 6000.0;

        /// <summary>Poslední přijatý snímek – drží se kvůli překreslení při přepnutí RGB/hloubka.</summary>
        private CameraFrame? lastFrame;

        /// <summary>Aktuální obraz pro zobrazení (RGB nebo hloubka dle <see cref="ShowDepth"/>).</summary>
        [ObservableProperty] private WriteableBitmap? image;

        /// <summary>false = RGB stream, true = hloubkový stream.</summary>
        [ObservableProperty] private bool showDepth;

        [ObservableProperty] private string resolutionText = "-";
        [ObservableProperty] private string frameText = "-";

        /// <summary>Podkladový senzor pro indikátor stavu (SensorStatusControl).</summary>
        public ISensor? Sensor { get; }

        /// <summary>Konstruktor pro design-time / návrhář.</summary>
        public CameraDocument()
        {
            Id = "Camera";
            Title = "Kamera";
        }

        public CameraDocument(ICamera camera)
        {
            this.camera = camera;
            Sensor = camera;
            string name = camera.Name ?? "Camera";
            Id = "Camera:" + name;
            Title = "Kamera — " + name;

            camera.MeasurementArived += OnMeasurement;

            // úvodní vykreslení z posledního známého snímku (pokud je)
            Apply(camera.GetLastMeasurement());
        }

        private void OnMeasurement(object? sender, CameraFrame frame)
        {
            if (frame == null)
                return;
            Dispatcher.UIThread.Post(() => Apply(frame));
        }

        /// <summary>Promítne snímek do vlastností (musí běžet na UI vlákně).</summary>
        private void Apply(CameraFrame? f)
        {
            if (f == null)
                return;

            lastFrame = f;
            Render(f);

            // Hz z periody příjmu (doplňuje SensorBase); neplatná perioda = prázdné.
            string hzText = f.FrameReceivePeriod.TotalSeconds > 0
                ? (1.0 / f.FrameReceivePeriod.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)
                : "";
            FrameText = string.Format(CultureInfo.InvariantCulture,
                "#{0}   {1,5} Hz   {2:HH:mm:ss.fff}", f.FrameNum, hzText, f.TimeStamp);
        }

        /// <summary>Přepnutí RGB/hloubka – překreslí poslední snímek do nového režimu.</summary>
        partial void OnShowDepthChanged(bool value)
        {
            if (lastFrame != null)
                Render(lastFrame);
        }

        /// <summary>Vykreslí zvolený stream (RGB nebo hloubka) z daného snímku.</summary>
        private void Render(CameraFrame f)
        {
            if (ShowDepth)
            {
                var depth = f.ImageDepth;
                if (depth != null)
                {
                    UpdateDepthImage(depth);
                    ResolutionText = string.Format(CultureInfo.InvariantCulture,
                        "{0} × {1}   (hloubka)", depth.Width, depth.Height);
                }
                else
                {
                    Image = null;
                    ResolutionText = "hloubka není k dispozici";
                }
            }
            else
            {
                var rgb = f.ImageRGB;
                if (rgb != null)
                {
                    UpdateImage(rgb);
                    ResolutionText = string.Format(CultureInfo.InvariantCulture,
                        "{0} × {1}   (RGB)", rgb.Width, rgb.Height);
                }
                else
                {
                    Image = null;
                    ResolutionText = "RGB není k dispozici";
                }
            }
        }

        /// <summary>Zkopíruje BGR32 data snímku do nové WriteableBitmap (Bgra8888).</summary>
        private void UpdateImage(Image<BGR32> rgb)
        {
            int w = rgb.Width, h = rgb.Height;
            if (w <= 0 || h <= 0)
                return;

            // Nový bitmap na každý snímek -> binding spolehlivě překreslí.
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                int rowBytes = w * 4;   // BGR32 = 4 bajty na pixel
                var data = rgb.Data;
                for (int y = 0; y < h; y++)
                    Marshal.Copy(data, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
            }
            Image = bmp;
        }

        /// <summary>
        /// Převede 16bitovou hloubku (Gray16, ~mm) na grayscale WriteableBitmap:
        /// blízko = světlé, daleko = tmavé, 0 (neplatné) = černé. Normalizace fixním
        /// rozsahem <see cref="DepthMaxMm"/>.
        /// </summary>
        private void UpdateDepthImage(Image<Gray16> depth)
        {
            int w = depth.Width, h = depth.Height;
            if (w <= 0 || h <= 0)
                return;

            var src = depth.Data;   // 2 bajty/pixel, little-endian
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            byte[] row = new byte[w * 4];
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w * 2;
                    for (int x = 0; x < w; x++)
                    {
                        int v = src[srcRow + x * 2] + (src[srcRow + x * 2 + 1] << 8);
                        byte g;
                        if (v <= 0)
                            g = 0;   // neplatná hloubka
                        else
                        {
                            double t = v / DepthMaxMm;
                            if (t > 1.0) t = 1.0;
                            g = (byte)(255.0 * (1.0 - t));   // blízko světlé, daleko tmavé
                        }
                        int o = x * 4;
                        row[o] = g;        // B
                        row[o + 1] = g;    // G
                        row[o + 2] = g;    // R
                        row[o + 3] = 255;  // A
                    }
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w * 4);
                }
            }
            Image = bmp;
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            // Kameru NEvlastníme (je sdílená z ARBotHW.Sensors) -> jen se odhlásíme.
            if (camera != null)
                camera.MeasurementArived -= OnMeasurement;
        }
    }
}
