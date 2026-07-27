using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// Testy navigace <see cref="FileMessageSource.SeekTo"/> (bez UI). Synteticky zaznam +
    /// index podle prikladu v doc/record-replay.md: Blob "X" @Seq 0,5; IMU @Seq 1,2,3,4,6.
    /// Seek na pozici vrati posledni &le; pozice pro kazdy stream (MsgName, Name).
    /// </summary>
    public class FileSourceSeekTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Poradi zprav v zaznamu (odpovida prikladu: Blob X @80,100 ; IMU @81,85,91,95,101).
        // Zde reprezentovano jako Seq 0..6 s ruznymi casy porizeni.
        private static List<Message> BuildStream()
        {
            var list = new List<Message>();
            // Seq 0: Blob X (starsi)
            list.Add(MakeBlob("X", T0.AddMilliseconds(80)));
            // Seq 1..4: IMU
            list.Add(TestHelpers.MakeImu(T0.AddMilliseconds(81), 0.01, 0.1));
            list.Add(TestHelpers.MakeImu(T0.AddMilliseconds(85), 0.02, 0.1));
            list.Add(TestHelpers.MakeImu(T0.AddMilliseconds(91), 0.03, 0.1));
            list.Add(TestHelpers.MakeImu(T0.AddMilliseconds(95), 0.04, 0.1));
            // Seq 5: Blob X (novejsi)
            list.Add(MakeBlob("X", T0.AddMilliseconds(100)));
            // Seq 6: IMU
            list.Add(TestHelpers.MakeImu(T0.AddMilliseconds(101), 0.05, 0.1));
            return list;
        }

        private static ImageMsg MakeBlob(string name, DateTime t)
        {
            // Maly grayscale obrazek jako nositel dat; klic streamu je (MsgName="ImageMsg", Name).
            var img = new ARBot.Common.Common.Image<ARBot.Common.Common.Gray>(2, 2);
            return new ImageMsg(img, name) { TimeStamp = t };
        }

        private static (byte[] data, List<IndexEntry> index) Record(List<Message> msgs)
        {
            using var dataMs = new MemoryStream();
            using var idxMs = new MemoryStream();
            var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc);
            rec.Start();
            foreach (var m in msgs) rec.Post(m);
            rec.Stop();
            var idx = MessageIndex.Read(new MemoryStream(idxMs.ToArray()), TestHelpers.Enc);
            return (dataMs.ToArray(), idx);
        }

        private static MessageCatalog Catalog() => MessageCatalog.CommonDefaults();

        [Test]
        public void SeekTo_EmitsLastPerStream_UpToPosition()
        {
            var msgs = BuildStream();
            var (data, index) = Record(msgs);

            var collected = new List<Message>();
            var sink = new DelegateTarget(m => { lock (collected) collected.Add(m); });
            sink.Start();

            using var readMs = new MemoryStream(data);
            var src = new FileMessageSource(readMs, TestHelpers.Enc, Catalog(),
                                            FileMessageSource.ReplayPacing.AsFastAsPossible, index: index);
            using (src.Connect(sink))
            {
                src.Pause();          // do stavu Paused (Start nevolame)
                src.SeekTo(4);        // pozice 4 -> Blob X@Seq0, IMU@Seq4
            }
            sink.Stop();

            var blobs = collected.OfType<ImageMsg>().ToList();
            var imus = collected.OfType<IMUState>().ToList();

            Assert.That(blobs, Has.Count.EqualTo(1), "prave jeden Blob (posledni <= pozice)");
            Assert.That(blobs[0].TimeStamp, Is.EqualTo(T0.AddMilliseconds(80)), "Blob X @Seq0 (Seq5 je az za pozici)");

            Assert.That(imus, Has.Count.EqualTo(1), "prave jedno IMU (posledni <= pozice)");
            Assert.That(imus[0].TimeStamp, Is.EqualTo(T0.AddMilliseconds(95)), "IMU @Seq4");
        }

        [Test]
        public void SeekTo_PastNewerBlob_PicksNewerBlob()
        {
            var msgs = BuildStream();
            var (data, index) = Record(msgs);

            var collected = new List<Message>();
            var sink = new DelegateTarget(m => { lock (collected) collected.Add(m); });
            sink.Start();

            using var readMs = new MemoryStream(data);
            var src = new FileMessageSource(readMs, TestHelpers.Enc, Catalog(),
                                            FileMessageSource.ReplayPacing.AsFastAsPossible, index: index);
            using (src.Connect(sink))
            {
                src.Pause();
                src.SeekTo(6);        // cela mnozina -> Blob X@Seq5, IMU@Seq6
            }
            sink.Stop();

            var blob = collected.OfType<ImageMsg>().Single();
            var imu = collected.OfType<IMUState>().Single();
            Assert.That(blob.TimeStamp, Is.EqualTo(T0.AddMilliseconds(100)), "novejsi Blob X @Seq5");
            Assert.That(imu.TimeStamp, Is.EqualTo(T0.AddMilliseconds(101)), "IMU @Seq6");
        }

        [Test]
        public void SeekTo_BeforeAnyBlob_EmitsOnlyAvailableStreams()
        {
            var msgs = BuildStream();
            var (data, index) = Record(msgs);

            var collected = new List<Message>();
            var sink = new DelegateTarget(m => { lock (collected) collected.Add(m); });
            sink.Start();

            using var readMs = new MemoryStream(data);
            var src = new FileMessageSource(readMs, TestHelpers.Enc, Catalog(),
                                            FileMessageSource.ReplayPacing.AsFastAsPossible, index: index);
            using (src.Connect(sink))
            {
                src.Pause();
                src.SeekTo(2);        // pozice 2 -> Blob X@Seq0, IMU@Seq2
            }
            sink.Stop();

            var blob = collected.OfType<ImageMsg>().Single();
            var imu = collected.OfType<IMUState>().Single();
            Assert.That(blob.TimeStamp, Is.EqualTo(T0.AddMilliseconds(80)));
            Assert.That(imu.TimeStamp, Is.EqualTo(T0.AddMilliseconds(85)), "IMU @Seq2");
        }

        [Test]
        public void Cursor_AdvancesToPositionPlusOne_AfterSeek()
        {
            var msgs = BuildStream();
            var (data, index) = Record(msgs);

            using var readMs = new MemoryStream(data);
            var src = new FileMessageSource(readMs, TestHelpers.Enc, Catalog(),
                                            FileMessageSource.ReplayPacing.AsFastAsPossible, index: index);
            src.Pause();
            src.SeekTo(4);
            Assert.That(src.Cursor, Is.EqualTo(5), "kurzor za pozici (dalsi Play pokracuje od Seq 5)");
        }
    }
}
