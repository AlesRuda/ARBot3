using System;
using System.IO;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// JPEG komprese/dekomprese v Blob (SkiaSharp). Testovano na x64; native arm64 se
    /// overi az na OrangePi.
    /// </summary>
    public class BlobJpegTests
    {
        private static Image<BGR32> SolidBgr32(int w, int h, byte r, byte g, byte b)
        {
            var img = new Image<BGR32>(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = img[x, y];
                    p.R = r; p.G = g; p.B = b;
                }
            return img;
        }

        private static Image<RGB> SolidRgb(int w, int h, byte r, byte g, byte b)
        {
            var img = new Image<RGB>(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = img[x, y];
                    p.R = r; p.G = g; p.B = b;
                }
            return img;
        }

        [Test]
        public void Bgr32_Compress_RoundTripsWithinTolerance()
        {
            var src = SolidBgr32(64, 64, r: 200, g: 120, b: 60);
            var blob = Blob.FromImage("cam", src, compress: true);

            Assert.That(blob.Type, Is.EqualTo(Blob.BlobType.Jpeg));
            Assert.That(blob.Width, Is.EqualTo(64));
            Assert.That(blob.Height, Is.EqualTo(64));
            // JPEG plne barvy je vyrazne mensi nez raw 64*64*4
            Assert.That(blob.Data.Length, Is.LessThan(64 * 64 * 4));

            var dec = blob.ToBGR32Image();
            Assert.That(dec.Width, Is.EqualTo(64));
            Assert.That(dec.Height, Is.EqualTo(64));

            var p = dec[32, 32];
            Assert.That((int)p.R, Is.EqualTo(200).Within(20), "R");
            Assert.That((int)p.G, Is.EqualTo(120).Within(20), "G");
            Assert.That((int)p.B, Is.EqualTo(60).Within(20), "B");
        }

        [Test]
        public void Bgr32_NoCompress_RoundTripsExact()
        {
            var src = SolidBgr32(8, 8, r: 10, g: 20, b: 30);
            var blob = Blob.FromImage("cam", src, compress: false);

            Assert.That(blob.Type, Is.EqualTo(Blob.BlobType.BGR32));

            var dec = blob.ToBGR32Image();
            var p = dec[4, 4];
            Assert.That((int)p.R, Is.EqualTo(10));
            Assert.That((int)p.G, Is.EqualTo(20));
            Assert.That((int)p.B, Is.EqualTo(30));
        }

        [Test]
        public void Rgb_Compress_PreservesChannelOrder()
        {
            var src = SolidRgb(64, 64, r: 210, g: 100, b: 40);
            var blob = Blob.FromImage("cam", src, compress: true);
            Assert.That(blob.Type, Is.EqualTo(Blob.BlobType.Jpeg));

            var dec = blob.ToBGR32Image();
            var p = dec[32, 32];
            Assert.That((int)p.R, Is.EqualTo(210).Within(20), "R");
            Assert.That((int)p.G, Is.EqualTo(100).Within(20), "G");
            Assert.That((int)p.B, Is.EqualTo(40).Within(20), "B");
        }

        [Test]
        public void Jpeg_SurvivesMessageSerialization()
        {
            var enc = Encoding.UTF8;
            var src = SolidBgr32(48, 32, r: 180, g: 90, b: 30);
            var blob = Blob.FromImage("cam", src, compress: true);

            // serializace pres MessageWriter (spusti lazy kompresi v ToData)
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(blob);
            w.Flush();
            var bytes = ms.ToArray();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new MemoryStream(bytes), enc, map);
            var back = reader.Read() as Blob;

            Assert.That(back, Is.Not.Null);
            Assert.That(back.Type, Is.EqualTo(Blob.BlobType.Jpeg));
            Assert.That(back.Width, Is.EqualTo(48));
            Assert.That(back.Height, Is.EqualTo(32));

            var dec = back.ToBGR32Image();
            Assert.That(dec.Width, Is.EqualTo(48));
            Assert.That(dec.Height, Is.EqualTo(32));
            var p = dec[24, 16];
            Assert.That((int)p.R, Is.EqualTo(180).Within(20), "R");
            Assert.That((int)p.G, Is.EqualTo(90).Within(20), "G");
            Assert.That((int)p.B, Is.EqualTo(30).Within(20), "B");
        }
    }
}
