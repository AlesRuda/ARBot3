using System;
using System.Collections.Generic;

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
                    while (now >= r.NextTick)
                    {
                        due.Add((r.OnTick, r.NextTick));
                        r.NextTick = r.NextTick + r.Interval;
                    }
                }
                regs.RemoveAll(x => x.Disposed);
            }

            foreach (var d in due)
                d.cb(d.t);
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
