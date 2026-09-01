using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Sberac sestavi PerfMsg z metrik smycky a stupnu. Viz doc/perf-monitoring.md.
    /// </summary>
    public class PerfCollectorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private sealed class Jimka : IMessageSink
        {
            public readonly List<Message> Zpravy = new();
            public void Post(Message msg) => Zpravy.Add(msg);
        }

        private static PerfCollector Sberac(Jimka j, Func<DateTime> now)
            => new PerfCollector(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), j, now);

        [Test]
        public void Snimek_PrenaseMetrikySmycky()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 1);
            c.Metrics.OnTickCompleted(T0, durationMs: 50, processorId: 3);

            var msg = c.BuildSnapshot();
            Assert.That(msg.TickCount, Is.EqualTo(1));
            Assert.That(msg.OccupancyAvgPct, Is.EqualTo(50.0).Within(1e-9));
            Assert.That(msg.Cores, Has.Count.EqualTo(1));
            Assert.That(msg.Cores[0].ProcessorId, Is.EqualTo(3));
        }

        [Test]
        public void ZameskaneTakty_SeSpocitajiZDavky()
        {
            // OnTicksDue s count=3 znamena: jeden vcas, dva zameskane.
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0.AddMilliseconds(350), 3);

            Assert.That(c.BuildSnapshot().MissedTicks, Is.EqualTo(2));
        }

        [Test]
        public void Verdikt_ChybaPriZameskanemTaktu()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 2);

            Assert.That(c.BuildSnapshot().Verdict, Is.EqualTo(PerfVerdict.Error));
        }

        [Test]
        public void Verdikt_VarovaniPriVysokeObsazenosti()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 1);
            c.Metrics.OnTickCompleted(T0, durationMs: 95, processorId: 0);   // 95 % periody

            Assert.That(c.BuildSnapshot().Verdict, Is.EqualTo(PerfVerdict.Warning));
        }

        [Test]
        public void Verdikt_OkKdyzSeStiha()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 1);
            c.Metrics.OnTickCompleted(T0, durationMs: 10, processorId: 0);

            Assert.That(c.BuildSnapshot().Verdict, Is.EqualTo(PerfVerdict.Ok));
        }

        [Test]
        public void Interval_JeOdPoslednihoOdectu()
        {
            var cas = T0;
            var j = new Jimka();
            using var c = Sberac(j, () => cas);

            c.BuildSnapshot();
            cas = T0.AddSeconds(1);
            var msg = c.BuildSnapshot();

            Assert.That(msg.From, Is.EqualTo(T0));
            Assert.That(msg.To, Is.EqualTo(T0.AddSeconds(1)));
        }
    }
}
