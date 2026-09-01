using System;
using System.Linq;
using ARBot.Common.Diagnostics;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Akumulator statistik taktu ridici smycky. Viz doc/perf-monitoring.md.
    /// </summary>
    public class TickStatsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private static TickStats Stats() => new TickStats(TimeSpan.FromMilliseconds(100));

        [Test]
        public void Obsazenost_JePomerDobyKPeriode()
        {
            var s = Stats();
            s.AddTick(T0, delayMs: 0, durationMs: 20, processorId: 0);
            s.AddTick(T0, delayMs: 0, durationMs: 40, processorId: 0);

            var snap = s.TakeSnapshot();
            Assert.That(snap.TickCount, Is.EqualTo(2));
            Assert.That(snap.OccupancyAvgPct, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(snap.OccupancyMaxPct, Is.EqualTo(40.0).Within(1e-9));
        }

        [Test]
        public void NejhorsiTakt_NeseCasIJadro()
        {
            var s = Stats();
            s.AddTick(T0, delayMs: 1, durationMs: 20, processorId: 3);
            s.AddTick(T0.AddSeconds(1), delayMs: 2, durationMs: 90, processorId: 5);
            s.AddTick(T0.AddSeconds(2), delayMs: 3, durationMs: 30, processorId: 3);

            var snap = s.TakeSnapshot();
            Assert.That(snap.WorstTickTime, Is.EqualTo(T0.AddSeconds(1)));
            Assert.That(snap.WorstProcessorId, Is.EqualTo(5));
        }

        [Test]
        public void Zpozdeni_PrumerIMaximum()
        {
            var s = Stats();
            s.AddTick(T0, delayMs: 2, durationMs: 10, processorId: 0);
            s.AddTick(T0, delayMs: 8, durationMs: 10, processorId: 0);

            var snap = s.TakeSnapshot();
            Assert.That(snap.DelayAvgMs, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(snap.DelayMaxMs, Is.EqualTo(8.0).Within(1e-9));
        }

        [Test]
        public void RozpadPoJadrech_SeparujeJadraAPocitaPrumer()
        {
            // Kvuli big.LITTLE na RK3588: ze samotneho prumeru nejde poznat, ze cast taktu
            // bezela na uspornem jadru. Viz doc/perf-monitoring.md, "Nestejna jadra".
            var s = Stats();
            s.AddTick(T0, delayMs: 0, durationMs: 20, processorId: 4);
            s.AddTick(T0, delayMs: 0, durationMs: 30, processorId: 4);
            s.AddTick(T0, delayMs: 0, durationMs: 80, processorId: 1);

            var cores = s.TakeSnapshot().Cores.OrderBy(c => c.ProcessorId).ToList();
            Assert.That(cores, Has.Count.EqualTo(2));
            Assert.That(cores[0].ProcessorId, Is.EqualTo(1));
            Assert.That(cores[0].TickCount, Is.EqualTo(1));
            Assert.That(cores[0].AvgMs, Is.EqualTo(80.0).Within(1e-9));
            Assert.That(cores[1].ProcessorId, Is.EqualTo(4));
            Assert.That(cores[1].TickCount, Is.EqualTo(2));
            Assert.That(cores[1].AvgMs, Is.EqualTo(25.0).Within(1e-9));
        }

        [Test]
        public void ZameskaneTakty_SeScitaji()
        {
            var s = Stats();
            s.AddMissed(2);
            s.AddMissed(1);
            Assert.That(s.TakeSnapshot().MissedTicks, Is.EqualTo(3));
        }

        [Test]
        public void TakeSnapshot_Vynuluje()
        {
            // Sberac cte jednou za sekundu a kazdy snimek ma pokryvat POUZE svuj interval -
            // jinak by se prumer pocital pres cely beh a spicka by se v nem utopila.
            var s = Stats();
            s.AddTick(T0, delayMs: 0, durationMs: 50, processorId: 0);
            s.AddMissed(1);
            s.TakeSnapshot();

            var druhy = s.TakeSnapshot();
            Assert.That(druhy.TickCount, Is.Zero);
            Assert.That(druhy.MissedTicks, Is.Zero);
            Assert.That(druhy.Cores, Is.Empty);
        }

        [Test]
        public void PrazdnySnimek_NeniDeleniNulou()
        {
            var snap = Stats().TakeSnapshot();
            Assert.That(snap.TickCount, Is.Zero);
            Assert.That(snap.OccupancyAvgPct, Is.Zero);
            Assert.That(snap.DelayAvgMs, Is.Zero);
        }
    }
}
