using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Pocitadla stupne: kolik zprav proslo, kolik se ZAHODILO a jak dlouho trvalo zpracovani.
    /// Zahozene se dosud nepocitaly vubec - stupen mohl tise ztracet data.
    /// Viz doc/perf-monitoring.md.
    /// </summary>
    public class StageStatsTests
    {
        private sealed class Pomaly : MessageTarget
        {
            private readonly int spanimMs;
            public Pomaly(int spanimMs, OverflowPolicy policy, int capacity)
                : base(policy, capacity) { this.spanimMs = spanimMs; }

            protected override void Consume(Message msg) => Thread.Sleep(spanimMs);
        }

        [Test]
        public void ZpracovaneZpravy_SePocitaji()
        {
            using var t = new Pomaly(0, OverflowPolicy.Block, 0);
            t.Start();
            t.Post(new Info("a"));
            t.Post(new Info("b"));
            t.Stop();

            var snap = t.TakeStageSnapshot();
            Assert.That(snap.Processed, Is.EqualTo(2));
            Assert.That(snap.Dropped, Is.Zero);
        }

        [Test]
        public void ZahozeneZpravy_SePocitaji()
        {
            // DropNewest s kapacitou 1: konzument spi, takze dalsi zpravy nemaji kam.
            using var t = new Pomaly(200, OverflowPolicy.DropNewest, capacity: 1);
            t.Start();
            for (int i = 0; i < 20; i++) t.Post(new Info("x"));

            Assert.That(t.TakeStageSnapshot().Dropped, Is.GreaterThan(0),
                        "pri plne fronte a DropNewest se musi neco zahodit");
            t.Stop();
        }

        [Test]
        public void DobaZpracovani_SeMeri()
        {
            using var t = new Pomaly(20, OverflowPolicy.Block, 0);
            t.Start();
            t.Post(new Info("a"));
            t.Stop();

            var snap = t.TakeStageSnapshot();
            Assert.That(snap.MaxMs, Is.GreaterThan(10));
        }

        [Test]
        public void TakeStageSnapshot_NulujeJenPrirustkoveUdaje()
        {
            // Fronta je STAV (musi zustat), zpracovane a zahozene jsou PRIRUSTKY za interval.
            using var t = new Pomaly(0, OverflowPolicy.Block, 0);
            t.Start();
            t.Post(new Info("a"));
            t.Stop();
            t.TakeStageSnapshot();

            var druhy = t.TakeStageSnapshot();
            Assert.That(druhy.Processed, Is.Zero);
            Assert.That(druhy.MaxMs, Is.Zero);
        }
    }
}
