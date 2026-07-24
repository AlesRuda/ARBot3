using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Pipeline BackProject (bez HW): synteticky CameraFrame -> BackProjectProcessor -> Blob
    /// -> zaznam do souboru -> nacteni. Overuje, ze pravdepodobnostni obraz sedi na
    /// BackProject.Process a ze projde zaznamem/replayem beze zmeny.
    /// </summary>
    public class BackProjectPipelineTest
    {
        private static readonly Encoding Enc = Encoding.UTF8;

        private sealed class Collector : MessageTarget
        {
            public readonly List<Message> Items = new List<Message>();
            public Collector() : base(OverflowPolicy.Block) { }
            protected override void Consume(Message msg) { Items.Add(msg); }
        }

        private static CameraFrame MakeFrame(int w, int h, DateTime t)
        {
            var rgb = new Image<BGR32>(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = rgb[x, y];
                    p.R = (byte)((x * 4) & 0xff);
                    p.G = (byte)((y * 5) & 0xff);
                    p.B = (byte)((x + y) & 0xff);
                }
            return new CameraFrame { ImageRGB = rgb, TimeStamp = t };
        }

        private static byte[] ExpectedProbability(Image<BGR32> rgb, IBackProject bp)
        {
            var size = bp.Size(rgb.Width, rgb.Height);
            var prob = new Image<Gray>(size.Width, size.Height);
            bp.Process(rgb, prob);
            return prob.Data;
        }

        [Test]
        public void BackProject_RecordReplay_ReproducesProbability()
        {
            const int W = 64, H = 48;
            var bp = new BackProject(BackProject.RoadProbability);
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var frames = new List<CameraFrame>();
            for (int i = 0; i < 3; i++)
                frames.Add(MakeFrame(W, H, t0.AddMilliseconds(i * 100)));

            // --- LIVE: CameraFrame -> BackProjectProcessor -> Blob(y) -> zaznam ---
            byte[] dataBytes, idxBytes;
            using (var dataMs = new MemoryStream())
            using (var idxMs = new MemoryStream())
            {
                var rec = new RecordingTarget(dataMs, idxMs, Enc);
                var proc = new BackProjectProcessor(bp, includeSourceRgb: true);
                rec.Start();
                proc.Start();
                using (proc.Output.Connect(rec))
                {
                    foreach (var f in frames) proc.Post(f);
                    proc.Stop();   // dozpracuje frontu -> vsechny bloby do rec
                }
                rec.Stop();
                dataBytes = dataMs.ToArray();
                idxBytes = idxMs.ToArray();
            }

            // 2 bloby na snimek (rgb + backproject)
            var index = MessageIndex.Read(new MemoryStream(idxBytes), Enc);
            Assert.That(index.Count, Is.EqualTo(frames.Count * 2));

            // --- REPLAY: nacteni jen "backproject" blobu a porovnani s ocekavanim ---
            var catalog = MessageCatalog.CommonDefaults();
            var collector = new Collector();
            collector.Start();
            using (var readMs = new MemoryStream(dataBytes))
            {
                var src = new FileMessageSource(readMs, Enc, catalog);
                src.SetTypeFilter(new[] { "Blob" });
                src.Connect(collector);
                src.RunToEnd();
            }
            collector.Stop();

            Assert.That(collector.Items.Count, Is.EqualTo(frames.Count * 2));

            // najdi backproject bloby (Probability) a porovnej s BackProject.Process
            int probIdx = 0;
            foreach (var m in collector.Items)
            {
                var blob = (Blob)m;
                if (blob.Name != "backproject") continue;

                Assert.That(blob.Type, Is.EqualTo(Blob.BlobType.Probability), "typ vysledku");
                Assert.That(blob.Width, Is.EqualTo(W));
                Assert.That(blob.Height, Is.EqualTo(H));
                Assert.That(blob.TimeStamp, Is.EqualTo(frames[probIdx].TimeStamp), "capture time");

                var expected = ExpectedProbability(frames[probIdx].ImageRGB, bp);
                Assert.That(blob.Data, Is.EqualTo(expected), $"probability data snimku {probIdx}");
                probIdx++;
            }
            Assert.That(probIdx, Is.EqualTo(frames.Count), "pocet backproject blobu");
        }

        [Test]
        public void BackProject_SourceRgb_DecodesFromJpeg()
        {
            const int W = 64, H = 48;
            var bp = new BackProject(BackProject.RoadProbability);
            var frame = MakeFrame(W, H, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var collector = new Collector();
            var proc = new BackProjectProcessor(bp, includeSourceRgb: true);
            collector.Start();
            proc.Start();
            using (proc.Output.Connect(collector))
            {
                proc.Post(frame);
                proc.Stop();
            }
            collector.Stop();

            Blob rgbBlob = null;
            foreach (var m in collector.Items)
                if (m is Blob b && b.Name == "rgb") rgbBlob = b;

            Assert.That(rgbBlob, Is.Not.Null, "rgb blob");
            Assert.That(rgbBlob.Type, Is.EqualTo(Blob.BlobType.Jpeg));
            var dec = rgbBlob.ToBGR32Image();
            Assert.That(dec.Width, Is.EqualTo(W));
            Assert.That(dec.Height, Is.EqualTo(H));
        }
    }
}
