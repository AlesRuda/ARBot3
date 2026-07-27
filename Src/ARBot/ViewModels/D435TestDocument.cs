using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.HAL;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using System;
using System.Linq;
using System.Runtime.InteropServices;
// D435Camera je platformove dedikovana (HALArmbian/HALWindows), ale v obou
// vrstvach ji najdeme ve stejnem namespace ARBot.HAL.Devices.Camera.
using ARBot.HAL.Devices.Camera;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci dokument zobrazujici RGB stream z kamery D435.
    /// Po vytvoreni se pripoji k D435Camera (sn=null) a na kazdy prichozi snimek
    /// aktualizuje obraz (na UI vlakne).
    /// </summary>
    public partial class D435TestDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.D435TestDocumentView);

        private readonly D435Camera camera;

        // Diagnostika (zapnuta env ARBOT_DIAG) - zapisuje do d435-diag.log vedle appky.
        private static readonly string? DiagLog =
            Environment.GetEnvironmentVariable("ARBOT_DIAG") != null
                ? System.IO.Path.Combine(AppContext.BaseDirectory, "d435-diag.log")
                : null;
        private static void Diag(string msg)
        {
            if (DiagLog == null) return;
            try { System.IO.File.AppendAllText(DiagLog, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n"); } catch { }
        }
        private int measCount;
        private int updCount;

        // Backpressure: nejnovější nezpracovaný snímek (starší se zahazují), aby se při zaostání
        // UI nehromadila dispatcher fronta. Diagnostika (measCount/Diag) bezi dal per-frame.
        private readonly object pendingGate = new object();
        private CameraFrame? pendingFrame;
        private int pendingN;
        private long pendingSum;
        private volatile bool updateQueued;

        /// <summary>Aktualni RGB snimek pro zobrazeni.</summary>
        [ObservableProperty]
        private WriteableBitmap? image;

        /// <summary>Stavovy text (pocet snimku, rozmery, kontrolni soucet) - overlay v UI.</summary>
        [ObservableProperty]
        private string status = "cekam na snimek z D435...";

        public D435TestDocument()
        {
            Id = "D435Test";
            Title = "D435 Test";

            // V design-time nahledu nezakladat kameru (jen prazdny nahled).
            if (Avalonia.Controls.Design.IsDesignMode)
                return;

            try
            {
                camera = new D435Camera();          // sn = null -> prvni dostupna kamera
                camera.MeasurementArived += OnMeasurement;
                Diag("ctor: camera created OK");
            }
            catch (Exception ex)
            {
                Title = "D435 Test (chyba: " + ex.Message + ")";
                Diag("ctor EX: " + ex);
            }
        }

        private void OnMeasurement(object? sender, CameraFrame frame)
        {
            var rgb = frame?.ImageRGB;
            int n = System.Threading.Interlocked.Increment(ref measCount);

            // Kontrolni soucet zacatku dat (odhali cerny/prazdny obraz).
            byte[]? dd = rgb?.Data;
            long sum = 0;
            if (dd != null) for (int i = 0; i < Math.Min(dd.Length, 4096); i++) sum += dd[i];
            if (n <= 3 || n % 30 == 0)
                Diag($"OnMeasurement #{n}: rgb={(rgb == null ? "null" : rgb.Width + "x" + rgb.Height)} dataLen={(dd?.Length ?? -1)} sum4k={sum}");

            // Uloz nejnovejsi snimek a koalescovane naplanuj jednu UI aktualizaci (starsi zahod).
            lock (pendingGate)
            {
                pendingFrame = frame;
                pendingN = n;
                pendingSum = sum;
            }

            if (updateQueued)
                return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        /// <summary>Zpracuje posledni nasbirany snimek na UI vlakne (starsi mezitim zahozene).</summary>
        private void Flush()
        {
            updateQueued = false;

            CameraFrame? frame; int n; long sum;
            lock (pendingGate)
            {
                frame = pendingFrame;
                pendingFrame = null;
                n = pendingN;
                sum = pendingSum;
            }
            if (frame == null)
                return;

            var rgb = frame.ImageRGB;
            if (rgb == null)
            {
                Status = $"snimek #{n}: ImageRGB == null";
                return;
            }

            UpdateImage(rgb);
            Status = $"snimek #{n}  {rgb.Width}x{rgb.Height}  sum4k={sum}  upd={updCount}";
        }

        private void UpdateImage(Image<BGR32> rgb)
        {
            try
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
                int u = ++updCount;
                if (u <= 3 || u % 30 == 0)
                    Diag($"UpdateImage #{u}: set Image {w}x{h}, rowBytes={w * 4}");
            }
            catch (Exception ex)
            {
                Diag("UpdateImage EX: " + ex);
            }
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
