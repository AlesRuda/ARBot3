using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Round-trip <see cref="PerfMsg"/> pres plnou serializaci. Zprava jde do zaznamu, takze
    /// rozbor po jizde stoji na tom, ze prezije zapis na disk. Viz doc/perf-monitoring.md.
    /// </summary>
    public class PerfMsgSerializationTests
    {
        private static PerfMsg RoundTrip(PerfMsg msg)
        {
            var enc = Encoding.UTF8;
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(msg);
            w.Flush();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new MemoryStream(ms.ToArray()), enc, map);
            return reader.Read() as PerfMsg;
        }

        [Test]
        public void RoundTrip_ZachovaVsechnaPole()
        {
            var od = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            var src = new PerfMsg
            {
                From = od,
                To = od.AddSeconds(1),
                TickCount = 10,
                MissedTicks = 2,
                OccupancyAvgPct = 31.5,
                OccupancyMaxPct = 92.0,
                DelayAvgMs = 1.5,
                DelayMaxMs = 12.0,
                WorstTickTime = od.AddMilliseconds(400),
                WorstProcessorId = 5,
                ProcessCpuPct = 42.0,
                MachineCpuPct = -1,
                Verdict = PerfVerdict.Warning,
                Cores = new List<PerfMsg.CoreEntry>
                {
                    new PerfMsg.CoreEntry { ProcessorId = 1, TickCount = 4, AvgMs = 80 },
                    new PerfMsg.CoreEntry { ProcessorId = 5, TickCount = 6, AvgMs = 25 },
                },
                Stages = new List<PerfMsg.StageEntry>
                {
                    new PerfMsg.StageEntry { Name = "FusionProcessor", QueueLength = 3,
                                             Processed = 120, Dropped = 4, AvgMs = 1.2, MaxMs = 9.9 },
                },
            };

            var back = RoundTrip(src);

            Assert.That(back, Is.Not.Null);
            Assert.That(back.From, Is.EqualTo(src.From));
            Assert.That(back.To, Is.EqualTo(src.To));
            Assert.That(back.TickCount, Is.EqualTo(10));
            Assert.That(back.MissedTicks, Is.EqualTo(2));
            Assert.That(back.OccupancyMaxPct, Is.EqualTo(92.0).Within(1e-9));
            Assert.That(back.DelayMaxMs, Is.EqualTo(12.0).Within(1e-9));
            Assert.That(back.WorstTickTime, Is.EqualTo(src.WorstTickTime));
            Assert.That(back.WorstProcessorId, Is.EqualTo(5));
            Assert.That(back.ProcessCpuPct, Is.EqualTo(42.0).Within(1e-9));
            Assert.That(back.MachineCpuPct, Is.EqualTo(-1).Within(1e-9), "-1 = neznamo");
            Assert.That(back.Verdict, Is.EqualTo(PerfVerdict.Warning));

            Assert.That(back.Cores, Has.Count.EqualTo(2));
            Assert.That(back.Cores[1].ProcessorId, Is.EqualTo(5));
            Assert.That(back.Cores[1].AvgMs, Is.EqualTo(25).Within(1e-9));

            Assert.That(back.Stages, Has.Count.EqualTo(1));
            Assert.That(back.Stages[0].Name, Is.EqualTo("FusionProcessor"));
            Assert.That(back.Stages[0].Dropped, Is.EqualTo(4));
        }

        [Test]
        public void RoundTrip_PrazdneSeznamy()
        {
            var back = RoundTrip(new PerfMsg { From = DateTime.UtcNow, To = DateTime.UtcNow });
            Assert.That(back.Cores, Is.Empty);
            Assert.That(back.Stages, Is.Empty);
        }
    }
}
