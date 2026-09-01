using System;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Odberatel metrik periodickych taktu. Implementuje ho sberac, hlasi do nej
    /// <c>Scheduler</c>.
    ///
    /// <para><b>Proc zrovna Scheduler.</b> Jako jediny zna PLANOVANY cas taktu i SKUTECNY cas,
    /// kdy ho nekdo vyzvedl, takze zpozdeni spocte zadarmo; a protoze callback sam vola, zmeri
    /// na temze miste i dobu prace. Casovac, ktery ho pumpuje, o svem zpozdeni nevi nic.
    /// Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public interface ISchedulerMetrics
    {
        /// <summary>
        /// Ohlasi, kolik taktu jedne registrace se vydava najednou.
        /// <paramref name="count"/> &gt; 1 znamena, ze <paramref name="count"/>-1 taktu se
        /// nestihlo vcas a dohanime je.
        /// </summary>
        void OnTicksDue(DateTime firstPlanned, DateTime now, int count);

        /// <summary>Ohlasi dokonceny takt: jak dlouho trval a na kterem jadru bezel.</summary>
        void OnTickCompleted(DateTime planned, double durationMs, int processorId);
    }
}
