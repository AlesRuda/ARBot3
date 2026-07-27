using System;
using System.Collections.Generic;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Tests.Runtime;   // TestHelpers, DelegateTarget

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Round-trip (de)serializace <see cref="CameraFrame"/> přes záznam a replay
    /// (<see cref="RecordingTarget"/> → <see cref="FileMessageSource"/> s katalogem). Ověřuje verzní
    /// rámování i komprese vrstev: RGB = Jpeg (ztrátové), Probability = Png a Depth = Deflate
    /// (bezztrátové) — viz <see cref="CameraFrame.ToData"/>.
    /// </summary>
    public class CameraFrameSerializationTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Image<T> MakeImage<T>(int w, int h, int seed) where T : IPixel, new()
        {
            var img = new Image<T>(w, h);
            var d = img.Data;
            for (int i = 0; i < d.Length; i++)
                d[i] = (byte)((i + seed) & 0xFF);
            return img;
        }

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

        [Test]
        public void CameraFrame_RoundTrips_ViaRecordReplay()
        {
            var frame = new CameraFrame
            {
                Name = "Left 740112071040",
                FrameNum = 7,
                TimeStamp = T0,
                RGBTimeStamp = T0.AddMilliseconds(1),
                DepthTimeStamp = T0.AddMilliseconds(2),
                ImageRGB = SolidBgr32(16, 16, r: 200, g: 120, b: 60),   // None (bezztrátové)
                ImageProbability = MakeImage<Gray>(8, 6, 3),            // None (bezztrátové)
                ImageDepth = MakeImage<Gray16>(4, 4, 100),             // None (bezztrátové)
            };

            // záznam
            byte[] dataBytes;
            using (var ms = new MemoryStream())
            {
                var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
                rec.Start();
                rec.Post(frame);
                rec.Stop();
                Assert.That(rec.Count, Is.EqualTo(1));
                dataBytes = ms.ToArray();
            }

            // replay
            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            var got = new List<CameraFrame>();
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) got.Add(c); });
            sink.Start();
            using (var ms = new MemoryStream(dataBytes))
            {
                var src = new FileMessageSource(ms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(got.Count, Is.EqualTo(1));
            var r = got[0];

            Assert.That(r.Verze, Is.EqualTo(CameraFrame.FormatVersion));
            Assert.That(r.Name, Is.EqualTo(frame.Name));
            Assert.That(r.FrameNum, Is.EqualTo(frame.FrameNum));
            Assert.That(r.TimeStamp, Is.EqualTo(frame.TimeStamp));
            Assert.That(r.RGBTimeStamp, Is.EqualTo(frame.RGBTimeStamp));
            Assert.That(r.DepthTimeStamp, Is.EqualTo(frame.DepthTimeStamp));

            // Vsechny vrstvy None (bezztratove) -> presna shoda dat.
            Assert.That(r.ImageRGB, Is.Not.Null);
            Assert.That((r.ImageRGB.Width, r.ImageRGB.Height), Is.EqualTo((16, 16)));
            Assert.That(r.ImageRGB.Data, Is.EqualTo(frame.ImageRGB.Data));

            Assert.That(r.ImageProbability, Is.Not.Null);
            Assert.That((r.ImageProbability.Width, r.ImageProbability.Height), Is.EqualTo((8, 6)));
            Assert.That(r.ImageProbability.Data, Is.EqualTo(frame.ImageProbability.Data));

            Assert.That(r.ImageDepth, Is.Not.Null);
            Assert.That((r.ImageDepth.Width, r.ImageDepth.Height), Is.EqualTo((4, 4)));
            Assert.That(r.ImageDepth.Data, Is.EqualTo(frame.ImageDepth.Data));
        }

        [Test]
        public void CameraFrame_NullLayer_RoundTripsToNull()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                TimeStamp = T0,
                ImageRGB = null,
                ImageProbability = MakeImage<Gray>(4, 4, 1),
                ImageDepth = null,
            };

            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(frame); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults().Register(new CameraFrame());
            CameraFrame r = null;
            var sink = new DelegateTarget(m => { if (m is CameraFrame c) r = c; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(r, Is.Not.Null);
            Assert.That(r.ImageRGB, Is.Null);
            Assert.That(r.ImageDepth, Is.Null);
            Assert.That(r.ImageProbability, Is.Not.Null);
        }

        [Test]
        public void CameraFrame_UnknownVersion_Throws()
        {
            // FromData musí odmítnout neznámou (budoucí) verzi místo tichého špatného čtení.
            var f = new CameraFrame { Verze = 999 };
            using var ms = new MemoryStream(new byte[64]);
            using var br = new BinaryReader(ms, TestHelpers.Enc);
            Assert.That(() => f.FromData(br), Throws.TypeOf<NotSupportedException>());
        }
    }
}
