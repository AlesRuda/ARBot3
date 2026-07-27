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
    /// Testy best-effort zaznamu <see cref="RecordingTarget"/> (per-typ retence, drop v Post)
    /// a zapisu T_out (<see cref="IndexEntry.ArrivalTicks"/>) + <see cref="IndexEntry.Name"/>.
    /// </summary>
    public class RecordingTargetTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ImageMsg MakeBlob(string name)
            => new ImageMsg(new ARBot.Common.Common.Image<ARBot.Common.Common.RGB>(1, 1) { Data = new byte[] { 1, 2, 3 } }, name);

        [Test]
        public void PerTypeLimit_DropsExcessBlobs_KeepsUntrackedTypes()
        {
            // Blob ma limit 2; IMUState neni v mape -> neomezeny (bezztratovy).
            var limits = new Dictionary<string, int> { ["ImageMsg"] = 2 };

            byte[] dataBytes, idxBytes;
            using (var dataMs = new MemoryStream())
            using (var idxMs = new MemoryStream())
            {
                // Konzumaci zamerne spustime az po Post - dokud nebezi, inflight neklesa,
                // takze prebytecne bloby se deterministicky zahodi uz v Post.
                var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc,
                                              OverflowPolicy.Block, limits);
                for (int i = 0; i < 5; i++)
                    rec.Post(MakeBlob($"cam{i}"));                 // jen prvni 2 projdou, 3 se zahodi
                for (int i = 0; i < 10; i++)
                    rec.Post(TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: i * 0.01, omega: 0.1));

                rec.Start();
                rec.Stop();

                dataBytes = dataMs.ToArray();
                idxBytes = idxMs.ToArray();
            }

            var entries = MessageIndex.Read(new MemoryStream(idxBytes), TestHelpers.Enc);
            int blobCount = entries.Count(e => e.MsgName == "ImageMsg");
            int imuCount = entries.Count(e => e.MsgName == "IMUState");

            Assert.That(blobCount, Is.LessThanOrEqualTo(2), "bloby prekrocily limit retence");
            Assert.That(imuCount, Is.EqualTo(10), "neomezeny typ (IMU) se nesmi zahazovat");

            // T_out (ArrivalTicks) se zapisuje pro vsechny zaznamy.
            Assert.That(entries.All(e => e.ArrivalTicks > 0), Is.True, "chybi T_out (ArrivalTicks)");

            // Name se plni z INamedMessage (Blob), u IMU je prazdne.
            var blobEntries = entries.Where(e => e.MsgName == "ImageMsg").ToList();
            Assert.That(blobEntries.Select(e => e.Name), Is.EqualTo(new[] { "cam0", "cam1" }));
            Assert.That(entries.Where(e => e.MsgName == "IMUState").All(e => e.Name == ""), Is.True);
        }

        [Test]
        public void NoLimits_IsLossless_AndWritesArrivalTicks()
        {
            // Bez limitu = bezztratovy rezim (jako dosud), ale T_in i T_out se zapisou.
            byte[] idxBytes;
            using (var dataMs = new MemoryStream())
            using (var idxMs = new MemoryStream())
            {
                var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc);
                rec.Start();
                for (int i = 0; i < 8; i++)
                    rec.Post(TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: i * 0.01, omega: 0.1));
                rec.Stop();
                Assert.That(rec.Count, Is.EqualTo(8));
                idxBytes = idxMs.ToArray();
            }

            var entries = MessageIndex.Read(new MemoryStream(idxBytes), TestHelpers.Enc);
            Assert.That(entries.Count, Is.EqualTo(8));
            for (int i = 0; i < entries.Count; i++)
            {
                Assert.That(entries[i].CaptureTime, Is.EqualTo(T0.AddMilliseconds(i * 20)), $"T_in[{i}]");
                Assert.That(entries[i].ArrivalTicks, Is.GreaterThan(0), $"T_out[{i}]");
            }
        }
    }
}
