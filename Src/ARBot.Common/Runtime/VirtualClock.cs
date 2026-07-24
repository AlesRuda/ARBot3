using System;
using System.Threading;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Virtualni hodiny pro replay/simulaci. Cas se posouva vyhradne explicitne
    /// (<see cref="AdvanceTo"/>) casy zprav a nikdy nejde zpet (monotonni).
    /// </summary>
    public sealed class VirtualClock : IClock
    {
        private long ticks;

        public VirtualClock(DateTime? start = null)
        {
            ticks = start?.Ticks ?? 0L;
        }

        /// <inheritdoc/>
        public DateTime Now => new DateTime(Volatile.Read(ref ticks));

        /// <summary>Posune cas na <paramref name="t"/>, pokud je novejsi (jinak no-op).</summary>
        public void AdvanceTo(DateTime t)
        {
            long v = t.Ticks;
            long cur = Volatile.Read(ref ticks);
            if (v > cur)
                Volatile.Write(ref ticks, v);
        }
    }
}
