using System;
using System.Diagnostics;
using System.Threading;
using ARBot.HAL;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Integracni HW test D435 kamery na ARM/Armbian (Orange Pi).
    /// Vyzaduje fyzicky pripojenou kameru D435 (USB3) a native librealsense2.so.
    /// Bez kamery se test gracefully preskoci (Assert.Ignore).
    /// Spusteni: dotnet test --filter Category=Hardware   (na Pi pres SSH)
    /// </summary>
    [Category("Hardware")]
    public class D435CameraTest
    {
        private const int RgbW = 640, RgbH = 480;
        private const int DepthW = 480, DepthH = 270;

        [Test]
        public void D435_GrabsFrame_WithExpectedResolution()
        {
            D435Camera camera;
            try
            {
                // Konstruktor spusti pipeline (rs2). Bez kamery/SDK vyhodi vyjimku.
                camera = new D435Camera();
            }
            catch (Exception ex)
            {
                Assert.Ignore($"D435 nedostupna (kamera nepripojena nebo librealsense chyba): {ex.Message}");
                return;
            }

            try
            {
                ICamera cam = camera;

                // Pockat na prvni kompletni snimek (background task ho doda pres SensorBase).
                CameraFrame? frame = null;
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed < TimeSpan.FromSeconds(5))
                {
                    var f = cam.GetLastMeasurement();
                    if (f?.ImageRGB != null && f.ImageDepth != null)
                    {
                        frame = f;
                        break;
                    }
                    Thread.Sleep(50);
                }

                Assert.That(frame, Is.Not.Null, "Do 5 s neprisel zadny snimek z D435.");
                Assert.Multiple(() =>
                {
                    Assert.That(frame!.ImageRGB, Is.Not.Null, "ImageRGB");
                    Assert.That(frame!.ImageRGB!.Width, Is.EqualTo(RgbW), "RGB sirka");
                    Assert.That(frame!.ImageRGB!.Height, Is.EqualTo(RgbH), "RGB vyska");
                    Assert.That(frame!.ImageDepth, Is.Not.Null, "ImageDepth");
                    Assert.That(frame!.ImageDepth!.Width, Is.EqualTo(DepthW), "Depth sirka");
                    Assert.That(frame!.ImageDepth!.Height, Is.EqualTo(DepthH), "Depth vyska");
                });

                TestContext.Out.WriteLine(
                    $"D435 OK: RGB {frame!.ImageRGB!.Width}x{frame.ImageRGB.Height}, " +
                    $"Depth {frame.ImageDepth!.Width}x{frame.ImageDepth.Height}");
            }
            finally
            {
                (camera as IDisposable)?.Dispose();
            }
        }
    }
}
