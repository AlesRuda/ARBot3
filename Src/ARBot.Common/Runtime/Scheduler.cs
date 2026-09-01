using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Bezvlaknova implementace <see cref="IScheduler"/>. Takty vydava az volajici pres
    /// <see cref="PumpDue"/>. Mrizka je kotvena na cas prvniho <see cref="PumpDue"/> po
    /// registraci (<c>t0</c>); dalsi takty jsou <c>t0 + k*interval</c>. Thread-safe.
    /// </summary>
    public sealed class Scheduler : IScheduler
    {
        private sealed class Registration
        {
            public TimeSpan Interval;
            public Action<DateTime> OnTick;
            public bool Anchored;      // uz videla prvni PumpDue (t0 nastaven)
            public DateTime NextTick;  // cas nasledujiciho taktu na mrizce
            public bool Disposed;
        }

        private readonly object sync = new object();
        private readonly List<Registration> regs = new List<Registration>();

        /// <summary>
        /// Volitelny odberatel metrik taktu; <c>null</c> = nemeri se. Kdyz neni nastaven, stoji
        /// mereni jeden test na null za takt - viz doc/perf-monitoring.md, "Rizika".
        /// </summary>
        public ARBot.Common.Diagnostics.ISchedulerMetrics Metrics { get; set; }

        /// <summary>Prevod tiku Stopwatch na ms. NESMI to byt new TimeSpan(ticks) - viz Performance.</summary>
        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

        /// <inheritdoc/>
        public IDisposable Register(TimeSpan interval, Action<DateTime> onTick)
        {
            if (onTick == null) throw new ArgumentNullException(nameof(onTick));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

            var r = new Registration { Interval = interval, OnTick = onTick };
            lock (sync)
                regs.Add(r);
            return new Handle(this, r);
        }

        /// <inheritdoc/>
        public void PumpDue(DateTime now)
        {
            // Pod zamkem jen posbirame due takty; callbacky volame az mimo zamek,
            // aby onTick nemohl zpusobit reentrantni deadlock.
            var due = new List<(Action<DateTime> cb, DateTime t)>();
            // Davky vydanych taktu jedne registrace - hlasi se odberateli metrik az mimo zamek.
            var davky = new List<(DateTime first, int count)>();
            lock (sync)
            {
                foreach (var r in regs)
                {
                    if (r.Disposed) continue;
                    if (!r.Anchored)
                    {
                        // t0 = cas prvniho PumpDue; prvni takt padne rovnou na t0.
                        r.Anchored = true;
                        r.NextTick = now;
                    }
                    int vydano = 0;
                    DateTime prvni = r.NextTick;
                    while (now >= r.NextTick)
                    {
                        due.Add((r.OnTick, r.NextTick));
                        r.NextTick = r.NextTick + r.Interval;
                        vydano++;
                    }
                    if (vydano > 0)
                        davky.Add((prvni, vydano));
                }
                regs.RemoveAll(x => x.Disposed);
            }

            // Hlaseni az MIMO zamek - odberatel metrik nesmi drzet zamek scheduleru.
            var m = Metrics;
            if (m != null)
                foreach (var d in davky)
                    m.OnTicksDue(d.first, now, d.count);

            foreach (var d in due)
            {
                if (m == null) { d.cb(d.t); continue; }

                int cpu = Thread.GetCurrentProcessorId();
                long t0 = Stopwatch.GetTimestamp();
                try { d.cb(d.t); }
                finally
                {
                    m.OnTickCompleted(d.t, (Stopwatch.GetTimestamp() - t0) * TickToMs, cpu);
                }
            }
        }

        private void Remove(Registration r)
        {
            lock (sync)
            {
                r.Disposed = true;
                regs.Remove(r);
            }
        }

        private sealed class Handle : IDisposable
        {
            private Scheduler owner;
            private Registration reg;
            public Handle(Scheduler owner, Registration reg) { this.owner = owner; this.reg = reg; }
            public void Dispose()
            {
                var o = owner; var r = reg;
                owner = null; reg = null;
                if (o != null && r != null) o.Remove(r);
            }
        }
    }
}
