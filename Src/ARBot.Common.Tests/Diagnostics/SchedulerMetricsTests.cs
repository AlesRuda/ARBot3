using System;
using System.Collections.Generic;
using ARBot.Common.Diagnostics;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Scheduler hlasi takty odberateli metrik. Je to jedine misto, ktere zna planovany
    /// i skutecny cas taktu. Viz doc/perf-monitoring.md.
    /// </summary>
    public class SchedulerMetricsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private sealed class Zaznamnik : ISchedulerMetrics
        {
            public readonly List<(DateTime first, DateTime now, int count)> Due = new();
            public readonly List<(DateTime planned, double ms, int cpu)> Completed = new();

            public void OnTicksDue(DateTime firstPlanned, DateTime now, int count)
                => Due.Add((firstPlanned, now, count));

            public void OnTickCompleted(DateTime planned, double durationMs, int processorId)
                => Completed.Add((planned, durationMs, processorId));
        }

        [Test]
        public void VcasnyTakt_HlasiJedenTakt()
        {
            var s = new Scheduler();
            var z = new Zaznamnik();
            s.Metrics = z;
            s.Register(TimeSpan.FromMilliseconds(100), _ => { });

            s.PumpDue(T0);                       // t0 = kotva, prvni takt
            s.PumpDue(T0.AddMilliseconds(100));  // druhy takt presne na mrizce

            Assert.That(z.Due, Has.Count.EqualTo(2));
            Assert.That(z.Due[1].count, Is.EqualTo(1), "vcas = jeden takt, zadny zameskany");
            Assert.That(z.Completed, Has.Count.EqualTo(2));
        }

        [Test]
        public void OpozdenyPump_HlasiVICE_TAKTU_NAJEDNOU()
        {
            // Tohle je jadro veci: pri zpozdeni o 300 ms vyda Scheduler tri takty za sebou
            // (viz Scheduler.cs, `while (now >= r.NextTick)`). Bez teto metriky se to nepozna.
            var s = new Scheduler();
            var z = new Zaznamnik();
            s.Metrics = z;
            s.Register(TimeSpan.FromMilliseconds(100), _ => { });

            s.PumpDue(T0);
            s.PumpDue(T0.AddMilliseconds(350));

            Assert.That(z.Due[1].count, Is.EqualTo(3), "100, 200 a 300 ms");
            Assert.That(z.Due[1].first, Is.EqualTo(T0.AddMilliseconds(100)));
            Assert.That(z.Due[1].now, Is.EqualTo(T0.AddMilliseconds(350)));
        }

        [Test]
        public void DobaTaktu_SeMeri()
        {
            var s = new Scheduler();
            var z = new Zaznamnik();
            s.Metrics = z;
            s.Register(TimeSpan.FromMilliseconds(100), _ => System.Threading.Thread.Sleep(20));

            s.PumpDue(T0);

            Assert.That(z.Completed, Has.Count.EqualTo(1));
            Assert.That(z.Completed[0].ms, Is.GreaterThan(10),
                        "Sleep(20) se musi projevit; volny prah kvuli rozliseni casovace");
            Assert.That(z.Completed[0].planned, Is.EqualTo(T0));
        }

        [Test]
        public void BezOdberatele_SchedulerFunguje()
        {
            var s = new Scheduler();
            int volani = 0;
            s.Register(TimeSpan.FromMilliseconds(100), _ => volani++);

            s.PumpDue(T0);
            s.PumpDue(T0.AddMilliseconds(100));

            Assert.That(volani, Is.EqualTo(2));
        }
    }
}
