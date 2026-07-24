using System;
using ARBot.Common.Common;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Zivé hodiny - obaluji <see cref="TimeBase.Now"/> (monotonni stopwatch-based cas).
    /// </summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>Sdilena instance.</summary>
        public static readonly SystemClock Instance = new SystemClock();

        /// <inheritdoc/>
        public DateTime Now => TimeBase.Now;
    }
}
