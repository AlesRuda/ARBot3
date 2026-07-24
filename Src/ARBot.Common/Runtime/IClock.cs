using System;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Zdroj casu "ted". V zivem rezimu skutecny cas (<see cref="SystemClock"/>),
    /// pri replay virtualni cas rizeny casy zprav (<see cref="VirtualClock"/>).
    /// Diky teto abstrakci nezavisi vypocet na wall-clocku a je deterministicky
    /// reprodukovatelny.
    /// </summary>
    public interface IClock
    {
        /// <summary>Aktualni cas.</summary>
        DateTime Now { get; }
    }
}
