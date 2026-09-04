using System;
using System.Runtime.InteropServices;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using SkiaSharp;

namespace ARBot.Common.Rendering
{
    /// <summary>
    /// Occupancy grid jako PNG: neprujezdne cervene, potvrzene volne zelene, nezname pruhledne.
    /// <b>Radek 0 obrazu je SEVER</b> (nejvyssi <c>j</c>) - rastr se kresli shora dolu.
    ///
    /// <para>Presunuto 4. 9. 2026 z <c>WorldViewDocument</c> (UI) sem, aby na nej videl i webovy
    /// nahled headless runtime (doc/headless.md) a <c>ARBot.Analyze</c>; UI to vola odtud. Kod je
    /// jinak tentyz, jen <c>GCHandle</c> se uvolnuje ve <c>finally</c> - driv pri vyjimce
    /// v kodovani unikal.</para>
    ///
    /// <para><b>Pozn. k ladeni:</b> <see cref="CellState.Unknown"/> je pruhledne, takze v mape nejde
    /// odlisit od plochy, o ktere grid nic nevi. Pri otazce „proc robot leze" to muze svest - brzdna
    /// obalka (<c>VBrake</c>) jede jen pres bunky <see cref="CellState.Free"/>, takze souvisle
    /// vypadajici plocha jeste neznamena potvrzenou. Cisla jsou v Debug outputu
    /// (<c>LocalNavigator</c>: <c>koridor: free=… unknown=…</c>).</para>
    /// </summary>
    public static class OccupancyPng
    {
        /// <summary>
        /// Zakoduje grid do PNG (BGRA premultiplied). Vraci <c>null</c> pri chybe nebo prazdnem gridu -
        /// volajici z toho udela „nemam co ukazat", ne pad.
        /// </summary>
        public static byte[] Encode(OccupancyGridMsg og)
        {
            if (og == null || og.Occ == null || og.Size <= 0) return null;

            int n = og.Size;
            GCHandle handle = default;
            try
            {
                using var bmp = new SKBitmap(new SKImageInfo(n, n, SKColorType.Bgra8888, SKAlphaType.Premul));
                var pixels = new uint[n * n];
                for (int j = 0; j < n; j++)
                {
                    int row = (n - 1 - j) * n;   // otoceni: sever nahoru
                    for (int i = 0; i < n; i++)
                    {
                        pixels[row + i] = og.State(i, j) switch
                        {
                            CellState.Blocked => BlockedBgra,
                            CellState.Free => FreeBgra,
                            _ => 0u,             // Unknown = pruhledne
                        };
                    }
                }

                handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                bmp.InstallPixels(bmp.Info, handle.AddrOfPinnedObject(), bmp.Info.RowBytes);

                using var image = SKImage.FromBitmap(bmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex)
            {
                // Trace, ne Debug: v Release na zarizeni by Debug mlcel (viz CLAUDE.md).
                System.Diagnostics.Trace.WriteLine($"OccupancyPng: kodovani selhalo: {ex.Message}");
                return null;
            }
            finally
            {
                // MUSI byt tady: kdyz Encode vyhodi, driv unikal pripnuty GCHandle.
                if (handle.IsAllocated) handle.Free();
            }
        }

        // Barvy rastru (BGRA premultiplied, jako u SKColorType.Bgra8888).
        private static readonly uint BlockedBgra = PremulBgra(0xE5, 0x39, 0x35, 0xB0);
        // Free ma vyssi alfu nez puvodnich 0x50: pri prekryvu se zelenym podkladem OSM se slaba
        // zelena od nej nedala rozeznat. Takto je potvrzena plocha citelna i bez zvyrazneni Unknown.
        private static readonly uint FreeBgra = PremulBgra(0x4C, 0xAF, 0x50, 0x80);

        private static uint PremulBgra(byte r, byte g, byte b, byte a)
        {
            uint rr = (uint)(r * a / 255), gg = (uint)(g * a / 255), bb = (uint)(b * a / 255);
            return ((uint)a << 24) | (rr << 16) | (gg << 8) | bb;
        }
    }
}
