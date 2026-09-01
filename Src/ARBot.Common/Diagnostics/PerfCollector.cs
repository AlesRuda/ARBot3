using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Jednou za interval sestavi <see cref="PerfMsg"/> z metrik ridici smycky a stupnu a posle ji
    /// do streamu - tim jde soucasne do UI i do zaznamu.
    ///
    /// <para>⚠️ <b>Ma VLASTNI casovac, ne ridici mrizku.</b> Kdyby visel na scheduleru, prestal by
    /// posilat prave ve chvili, kdy se nestiha - tedy kdyz je nejvic potreba. Nezavisly casovac
    /// navic zachyti i pripad, kdy rizeni stoji uplne. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public sealed class PerfCollector : IDisposable
    {
        private sealed class Metriky : ISchedulerMetrics
        {
            private readonly PerfCollector owner;
            public Metriky(PerfCollector owner) { this.owner = owner; }

            public void OnTicksDue(DateTime firstPlanned, DateTime now, int count)
            {
                // Prvni takt je vcasny, zbytek jsou zameskane a dohanene.
                if (count > 1) owner.ticks.AddMissed(count - 1);
                owner.lastDelayMs = Math.Max(0, (now - firstPlanned).TotalMilliseconds);
            }

            public void OnTickCompleted(DateTime planned, double durationMs, int processorId)
                => owner.ticks.AddTick(planned, owner.lastDelayMs, durationMs, processorId);
        }

        private readonly TickStats ticks;
        private readonly TimeSpan interval;
        private readonly IMessageSink sink;
        private readonly Func<DateTime> now;
        private readonly List<MessageTarget> stages = new List<MessageTarget>();
        private readonly Process process = Process.GetCurrentProcess();
        private readonly double warnPct;

        private Timer timer;
        private DateTime lastTake;
        private double lastDelayMs;
        private TimeSpan lastCpu;
        private DateTime lastCpuAt;

        /// <param name="period">Perioda ridici smycky - jmenovatel obsazenosti.</param>
        /// <param name="interval">Jak casto se posila zprava (~1 s).</param>
        /// <param name="sink">Stream, do ktereho zprava odchazi.</param>
        /// <param name="now">Zdroj casu (kvuli testum).</param>
        /// <param name="warnPct">
        /// Obsazenost periody [%], od ktere je verdikt „varovani". Predava se ZVENCI (z parametru
        /// <c>perfwarn</c>, ktery cte <c>ARBot</c>) - <c>ARBot.Common</c> zamerne nesaha na
        /// <c>ParamStore</c>: konfigurace se v tomhle projektu cte vyhradne pres
        /// <c>Program.GetParam*</c> a straz <c>ParamRegistryGuardTests</c> hleda jen tam.
        /// </param>
        public PerfCollector(TimeSpan period, TimeSpan interval, IMessageSink sink, Func<DateTime> now,
                             double warnPct = 70)
        {
            this.interval = interval;
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.now = now ?? throw new ArgumentNullException(nameof(now));
            this.warnPct = warnPct;
            ticks = new TickStats(period);
            Metrics = new Metriky(this);

            lastTake = now();
            lastCpu = process.TotalProcessorTime;
            lastCpuAt = lastTake;
        }

        /// <summary>Odberatel, ktery se predava do <c>Scheduler.Metrics</c>.</summary>
        public ISchedulerMetrics Metrics { get; }

        /// <summary>Zaradi stupen do mereni. Vola se pred <see cref="Start"/>.</summary>
        public void AddStage(MessageTarget stage)
        {
            if (stage != null) stages.Add(stage);
        }

        /// <summary>Spusti vlastni casovac.</summary>
        public void Start()
        {
            if (timer != null) return;
            int ms = Math.Max(100, (int)interval.TotalMilliseconds);
            timer = new Timer(_ =>
            {
                try { sink.Post(BuildSnapshot()); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }, null, ms, ms);
        }

        /// <summary>
        /// Sestavi zpravu za interval od posledniho odectu a metriky vynuluje.
        /// Verejne kvuli testum - v behu ji vola casovac.
        /// </summary>
        public PerfMsg BuildSnapshot()
        {
            DateTime t = now();
            var snap = ticks.TakeSnapshot();

            var msg = new PerfMsg
            {
                From = lastTake,
                To = t,
                TickCount = snap.TickCount,
                MissedTicks = snap.MissedTicks,
                OccupancyAvgPct = snap.OccupancyAvgPct,
                OccupancyMaxPct = snap.OccupancyMaxPct,
                DelayAvgMs = snap.DelayAvgMs,
                DelayMaxMs = snap.DelayMaxMs,
                WorstTickTime = snap.WorstTickTime,
                WorstProcessorId = snap.WorstProcessorId,
                ProcessCpuPct = ZmerCpuProcesu(t),
                MachineCpuPct = -1,          // faze 3 (HAL)
            };

            foreach (var c in snap.Cores)
                msg.Cores.Add(new PerfMsg.CoreEntry
                {
                    ProcessorId = c.ProcessorId, TickCount = c.TickCount, AvgMs = c.AvgMs,
                });

            foreach (var s in stages)
            {
                var st = s.TakeStageSnapshot();
                msg.Stages.Add(new PerfMsg.StageEntry
                {
                    Name = st.Name, QueueLength = st.QueueLength, Processed = st.Processed,
                    Dropped = st.Dropped, AvgMs = st.AvgMs, MaxMs = st.MaxMs,
                });
            }

            msg.Verdict = Verdikt(msg);
            lastTake = t;
            return msg;
        }

        /// <summary>
        /// Zameskany takt je chyba VZDY (prah nema): znamena, ze se rizeni uz nestiha na mrizce.
        /// Obsazenost nad prahem je varovani - jeste se stiha, ale rezerva dochazi.
        /// </summary>
        private PerfVerdict Verdikt(PerfMsg m)
        {
            if (m.MissedTicks > 0) return PerfVerdict.Error;
            if (m.OccupancyMaxPct >= warnPct) return PerfVerdict.Warning;
            return PerfVerdict.Ok;
        }

        /// <summary>
        /// Vytizeni procesu v procentech CELEHO stroje. TotalProcessorTime je soucet pres vsechna
        /// jadra, proto se deli poctem jader - jinak by na 8 jadrech vychazelo az 800 %
        /// (linuxovy zvyk z `top`). Viz doc/perf-monitoring.md.
        /// </summary>
        private double ZmerCpuProcesu(DateTime t)
        {
            try
            {
                var cpu = process.TotalProcessorTime;
                double wallMs = (t - lastCpuAt).TotalMilliseconds;
                double cpuMs = (cpu - lastCpu).TotalMilliseconds;
                lastCpu = cpu;
                lastCpuAt = t;

                if (wallMs <= 0) return -1;
                return 100.0 * cpuMs / (wallMs * Math.Max(1, Environment.ProcessorCount));
            }
            catch { return -1; }
        }

        public void Dispose()
        {
            timer?.Dispose();
            timer = null;
            process?.Dispose();
        }
    }
}
