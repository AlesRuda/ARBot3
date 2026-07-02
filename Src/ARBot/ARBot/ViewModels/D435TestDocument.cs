using ARBot.Common.Common;
using ARBot.HAL;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using HALWindows;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci dokument zobrazujici RGB stream z kamery D435.
    /// Po vytvoreni se pripoji k D435Camera (sn=null) a na kazdy prichozi snimek
    /// aktualizuje obraz (na UI vlakne).
    /// </summary>
    public partial class D435TestDocument : Document, IDisposable
    {
        private readonly D435Camera camera;

        /// <summary>Aktualni RGB snimek pro zobrazeni.</summary>
        [ObservableProperty]
        private WriteableBitmap? image;

        public D435TestDocument()
        {
            Id = "D435Test";
            Title = "D435 Test";

            try
            {
                camera = new D435Camera();          // sn = null -> prvni dostupna kamera
                camera.MeasurementArived += OnMeasurement;
            }
            catch (Exception ex)
            {
                Title = "D435 Test (chyba: " + ex.Message + ")";
            }
        }

        private void OnMeasurement(object? sender, CameraFrame frame)
        {
            var rgb = frame?.ImageRGB;
            if (rgb == null)
                return;

            // Aktualizace bitmapy musi probehnout na UI vlakne.
            Dispatcher.UIThread.Post(() => UpdateImage(rgb));
        }

        private void UpdateImage(Image<BGR32> rgb)
        {
            int w = rgb.Width;
            int h = rgb.Height;
            if (w <= 0 || h <= 0)
                return;

            // Novy bitmap na kazdy snimek -> binding spolehlive prekresli.
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                int rowBytes = w * 4;       // BGR32 = 4 bajty na pixel
                var data = rgb.Data;
                for (int y = 0; y < h; y++)
                    Marshal.Copy(data, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
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
            if (camera != null)
            {
                camera.MeasurementArived -= OnMeasurement;
                camera.Dispose();
            }
        }
    }
}
