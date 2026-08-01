using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ARBot.Diagnostics
{
    /// <summary>
    /// Zachycení snímku UI (screenshot) do PNG - pro self-test (bezobslužné pořízení obrázků do deníčku).
    /// Používá Avalonia <see cref="RenderTargetBitmap"/>; MUSÍ běžet na UI vlákně. Žádná externí závislost.
    /// </summary>
    public static class ScreenCapture
    {
        /// <summary>Vyrenderuje vizuál (typicky hlavní okno) do PNG na zadané cestě. Vrací true při úspěchu.</summary>
        public static bool SavePng(Visual visual, string path)
        {
            try
            {
                using var rtb = RenderToBitmap(visual);
                if (rtb == null) return false;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var fs = File.Create(path);
                rtb.Save(fs);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ScreenCapture.SavePng: " + ex.Message);
                return false;
            }
        }

        /// <summary>Vyrenderuje vizuál do <see cref="RenderTargetBitmap"/> (RGBA); null při nulovém rozměru/chybě.</summary>
        public static RenderTargetBitmap RenderToBitmap(Visual visual)
        {
            if (visual == null) return null;
            var size = visual.Bounds.Size;
            int w = (int)Math.Ceiling(size.Width);
            int h = (int)Math.Ceiling(size.Height);
            if (w <= 0 || h <= 0) return null;

            var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            rtb.Render(visual);
            return rtb;
        }

        /// <summary>
        /// Zachytí vizuál jako RGB snímek (volitelně zmenšený faktorem <paramref name="downscale"/>).
        /// Vrací pole R,G,B (3 bajty/pixel) + rozměry; null při chybě. Avalonia RTB je Bgra8888 -
        /// převádíme na RGB. MUSÍ běžet na UI vlákně.
        /// </summary>
        public static byte[] CaptureRgb(Visual visual, int downscale, out int outW, out int outH)
        {
            outW = outH = 0;
            using var rtb = RenderToBitmap(visual);
            if (rtb == null) return null;

            int w = rtb.PixelSize.Width, h = rtb.PixelSize.Height;
            int stride = w * 4;
            var bgra = new byte[stride * h];
            var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
            try { rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), bgra.Length, stride); }
            finally { handle.Free(); }

            if (downscale < 1) downscale = 1;
            int dw = w / downscale, dh = h / downscale;
            if (dw <= 0 || dh <= 0) return null;
            var rgb = new byte[dw * dh * 3];
            int o = 0;
            for (int y = 0; y < dh; y++)
            {
                int sy = y * downscale;
                for (int x = 0; x < dw; x++)
                {
                    int si = sy * stride + x * downscale * 4;   // Bgra
                    rgb[o++] = bgra[si + 2];   // R
                    rgb[o++] = bgra[si + 1];   // G
                    rgb[o++] = bgra[si + 0];   // B
                }
            }
            outW = dw; outH = dh;
            return rgb;
        }
    }
}
