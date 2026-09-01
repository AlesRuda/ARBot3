using System;
using System.Collections.Generic;

namespace ARBot.Common.Diagnostics
{
    /// <summary>Statistika taktu za jeden interval sberu.</summary>
    public readonly struct TickSnapshot
    {
        public int TickCount { get; init; }
        public int MissedTicks { get; init; }

        /// <summary>Prumerna obsazenost periody [%] - doba taktu deleno period.</summary>
        public double OccupancyAvgPct { get; init; }
        /// <summary>Nejvetsi obsazenost periody [%] v intervalu.</summary>
        public double OccupancyMaxPct { get; init; }

        public double DelayAvgMs { get; init; }
        public double DelayMaxMs { get; init; }

        /// <summary>Cas taktu, ktery trval nejdele - kotva pro dohledani v ostatnich datech.</summary>
        public DateTime WorstTickTime { get; init; }
        /// <summary>Jadro, na kterem ten takt bezel.</summary>
        public int WorstProcessorId { get; init; }

        /// <summary>Rozpad po jadrech; prazdne, kdyz v intervalu nebyl zadny takt.</summary>
        public IReadOnlyList<CoreSnapshot> Cores { get; init; }
    }

    /// <summary>Kolik taktu a jak dlouhych probehlo na jednom jadru.</summary>
    public readonly struct CoreSnapshot
    {
        public int ProcessorId { get; init; }
        public int TickCount { get; init; }
        public double AvgMs { get; init; }
    }

    /// <summary>
    /// Akumuluje statistiku taktu ridici smycky mezi dvema odecty.
    ///
    /// <para><b>Rozpad po jadrech</b> je tu kvuli tomu, ze cilove zarizeni (RK3588) ma ctyri
    /// vykonna a ctyri usporna jadra: tataz prace tam trva ruzne dlouho podle toho, kde bezi,
    /// a vlakno se mezi nimi stehuje volne. Ze samotneho prumeru by to neslo poznat. Ktera jadra
    /// jsou vykonna se ZAMERNE nikam nezapisuje - vyjde to z dat. Viz doc/perf-monitoring.md.</para>
    ///
    /// <para>Zapisuje vlakno scheduleru, cte sberac - proto zamek. Pri 10 Hz je jeho cena
    /// zanedbatelna.</para>
    /// </summary>
    public sealed class TickStats
    {
        private readonly object sync = new object();
        private readonly double periodMs;
        private readonly Dictionary<int, (int Count, double SumMs)> cores
            = new Dictionary<int, (int, double)>();

        private int tickCount;
        private int missed;
        private double sumDurationMs;
        private double maxDurationMs;
        private double sumDelayMs;
        private double maxDelayMs;
        private DateTime worstTime;
        private int worstCore;

        public TickStats(TimeSpan period)
        {
            periodMs = period.TotalMilliseconds;
            if (periodMs <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        }

        /// <summary>Zaznamena jeden probehly takt.</summary>
        public void AddTick(DateTime planned, double delayMs, double durationMs, int processorId)
        {
            lock (sync)
            {
                tickCount++;
                sumDurationMs += durationMs;
                sumDelayMs += delayMs;
                if (delayMs > maxDelayMs) maxDelayMs = delayMs;
                if (tickCount == 1 || durationMs > maxDurationMs)
                {
                    maxDurationMs = durationMs;
                    worstTime = planned;
                    worstCore = processorId;
                }

                cores.TryGetValue(processorId, out var c);
                cores[processorId] = (c.Count + 1, c.SumMs + durationMs);
            }
        }

        /// <summary>Zaznamena takty, ktere se nestihly vydat vcas.</summary>
        public void AddMissed(int count)
        {
            if (count <= 0) return;
            lock (sync) { missed += count; }
        }

        /// <summary>
        /// Vrati statistiku za dosud nasbirany interval a VYNULUJE ji. Kazdy snimek tak pokryva
        /// jen svuj interval - jinak by se prumer pocital pres cely beh a spicka by se v nem
        /// utopila.
        /// </summary>
        public TickSnapshot TakeSnapshot()
        {
            lock (sync)
            {
                var list = new List<CoreSnapshot>(cores.Count);
                foreach (var kv in cores)
                    list.Add(new CoreSnapshot
                    {
                        ProcessorId = kv.Key,
                        TickCount = kv.Value.Count,
                        AvgMs = kv.Value.Count == 0 ? 0 : kv.Value.SumMs / kv.Value.Count,
                    });

                var snap = new TickSnapshot
                {
                    TickCount = tickCount,
                    MissedTicks = missed,
                    OccupancyAvgPct = tickCount == 0 ? 0 : 100.0 * (sumDurationMs / tickCount) / periodMs,
                    OccupancyMaxPct = tickCount == 0 ? 0 : 100.0 * maxDurationMs / periodMs,
                    DelayAvgMs = tickCount == 0 ? 0 : sumDelayMs / tickCount,
                    DelayMaxMs = maxDelayMs,
                    WorstTickTime = worstTime,
                    WorstProcessorId = worstCore,
                    Cores = list,
                };

                tickCount = 0; missed = 0;
                sumDurationMs = 0; maxDurationMs = 0;
                sumDelayMs = 0; maxDelayMs = 0;
                worstTime = default; worstCore = 0;
                cores.Clear();
                return snap;
            }
        }
    }
}
