using System;
using System.Collections.Generic;
using ARBot.Common.Common;

namespace ARBot.Common.Vision.Qr
{
    /// <summary>
    /// Dekoder QR kodu postaveny na <b>ZXing.Net</b> (ciste managed). Viz doc/robotour-mission.md.
    ///
    /// <para><b>Proc ZXing a ne ZBar,</b> se kterym pocital puvodni navrh: ZBar se v predchozi
    /// generaci robotu osvedcil, ale jeho C# binding (<c>zbar-sharp</c> z ARBot2) nebyl k dispozici
    /// — a hlavne by za sebou tahl nativni <c>libzbar</c> na obou platformach, tedy <c>libzbar.dll</c>
    /// pro x64 a rezolver pro <c>libzbar.so.0</c> na Armbianu. ZXing.Net je ciste managed, takze na
    /// ARM64 neni co resit. Vymena zpet je za <see cref="IQrDecoder"/> lokalni zmena.</para>
    ///
    /// <para><b>Povoleny je jen QR.</b> Ostatni symbologie jen zdrzuji a mohou plodit falesne nalezy
    /// — tatáž konfigurace, jakou mel puvodni kod (vypnout vse, povolit <c>QRCODE</c>).</para>
    ///
    /// <para><b>Bez <c>System.Drawing</c>:</b> dekoderu se predavaji surova Y800 data
    /// (<see cref="ZXing.RGBLuminanceSource.BitmapFormat.Gray8"/>), zadny bitmapovy mezikrok.</para>
    /// </summary>
    public sealed class ZXingQrDecoder : IQrDecoder
    {
        private readonly ZXing.BarcodeReaderGeneric reader;

        /// <param name="tryHarder">
        /// Vic vypocetniho casu za vyssi uspesnost cteni. <b>Vychozi je <c>true</c></b>: skenuje se
        /// vyhradne pod drzenym nouzovym zastavenim, kdy robot stoji a vypocetni cas je zdarma —
        /// tam je spravne zaplatit za uspesnost.
        /// </param>
        public ZXingQrDecoder(bool tryHarder = true)
        {
            reader = new ZXing.BarcodeReaderGeneric
            {
                Options = new ZXing.Common.DecodingOptions
                {
                    PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE },
                    TryHarder = tryHarder,
                },
            };
        }

        /// <inheritdoc/>
        public QrResult[] Decode(Image<Gray> img)
        {
            if (img == null) return Array.Empty<QrResult>();

            // Y800 = 1 bajt na pixel; Image<Gray>.Data je presne to.
            var source = new ZXing.RGBLuminanceSource(img.Data, img.Width, img.Height,
                                                      ZXing.RGBLuminanceSource.BitmapFormat.Gray8);

            // Snimek bez kodu je normalni, ocekavany stav - ne chyba. ZXing v takovem pripade vraci
            // null, coz se prevadi na prazdne pole.
            ZXing.Result[] results;
            try { results = reader.DecodeMultiple(source); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ZXingQrDecoder: {ex.Message}");
                return Array.Empty<QrResult>();
            }

            if (results == null || results.Length == 0) return Array.Empty<QrResult>();

            var found = new List<QrResult>(results.Length);
            foreach (var r in results)
            {
                if (r == null || r.BarcodeFormat != ZXing.BarcodeFormat.QR_CODE) continue;
                found.Add(new QrResult(r.Text, ToCorners(r.ResultPoints)));
            }

            return found.ToArray();
        }

        /// <summary>
        /// Polohove znacky kodu v obraze. QR jich ma <b>tri nebo ctyri</b> (ctvrta je zarovnavaci
        /// znacka, kterou male verze kodu nemaji) — proto se pocet nekontroluje.
        /// </summary>
        private static Point2D[] ToCorners(ZXing.ResultPoint[] points)
        {
            if (points == null) return Array.Empty<Point2D>();

            var corners = new Point2D[points.Length];
            for (int i = 0; i < points.Length; i++)
                corners[i] = points[i] == null ? new Point2D(0, 0)
                                               : new Point2D(points[i].X, points[i].Y);
            return corners;
        }
    }
}
