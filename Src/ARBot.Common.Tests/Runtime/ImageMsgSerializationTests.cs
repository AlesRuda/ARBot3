using System;
using System.IO;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// Round-trip <see cref="ImageMsg"/> přes plnou serializaci zprávy (MessageWriter/Reader)
    /// s kompresí (JPEG přes SkiaSharp / None / Deflate). Testováno na x64; native arm64 se
    /// ověří až na OrangePi.
    /// </summary>
    public class ImageMsgSerializationTests
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

        /// <summary>Serializuje ImageMsg přes MessageWriter a načte zpět přes katalog.</summary>
        private static (ImageMsg msg, int bytes) RoundTrip(ImageMsg msg)
        {
            var enc = Encoding.UTF8;
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(msg);
            w.Flush();
            var data = ms.ToArray();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new MemoryStream(data), enc, map);
            return (reader.Read() as ImageMsg, data.Length);
        }

        [Test]
        public void Bgr32_Jpeg_RoundTripsWithinTolerance()
        {
            var src = SolidBgr32(64, 64, r: 200, g: 120, b: 60);
            var (back, bytes) = RoundTrip(new ImageMsg(src, "cam", ImageMsg.Compression.Jpeg));

            Assert.That(back, Is.Not.Null);
            Assert.That(back.Image, Is.InstanceOf<Image<BGR32>>());
            Assert.That(back.Image.Width, Is.EqualTo(64));
            Assert.That(back.Image.Height, Is.EqualTo(64));
            // JPEG plné barvy je výrazně menší než raw 64*64*4
            Assert.That(bytes, Is.LessThan(64 * 64 * 4));

            var dec = (Image<BGR32>)back.Image;
            var p = dec[32, 32];
            Assert.That((int)p.R, Is.EqualTo(200).Within(20), "R");
            Assert.That((int)p.G, Is.EqualTo(120).Within(20), "G");
            Assert.That((int)p.B, Is.EqualTo(60).Within(20), "B");
        }

        [Test]
        public void Bgr32_None_RoundTripsExact()
        {
            var src = SolidBgr32(8, 8, r: 10, g: 20, b: 30);
            var (back, _) = RoundTrip(new ImageMsg(src, "cam"));   // None

            var dec = (Image<BGR32>)back.Image;
            var p = dec[4, 4];
            Assert.That((int)p.R, Is.EqualTo(10));
            Assert.That((int)p.G, Is.EqualTo(20));
            Assert.That((int)p.B, Is.EqualTo(30));
        }

        [Test]
        public void Rgb_Deflate_RoundTripsExact_PreservesChannelOrder()
        {
            // RGB (step 3) nepodporuje Jpeg/Png (jen 8bit step 1/4) -> bezztrátový Deflate.
            var src = SolidRgb(16, 16, r: 210, g: 100, b: 40);
            var (back, _) = RoundTrip(new ImageMsg(src, "cam", ImageMsg.Compression.Deflate));

            Assert.That(back.Image, Is.InstanceOf<Image<RGB>>());
            var dec = (Image<RGB>)back.Image;
            var p = dec[8, 8];
            Assert.That((int)p.R, Is.EqualTo(210), "R");
            Assert.That((int)p.G, Is.EqualTo(100), "G");
            Assert.That((int)p.B, Is.EqualTo(40), "B");
        }
    }
}
