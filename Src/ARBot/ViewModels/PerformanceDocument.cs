using System;
using System.Collections.ObjectModel;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Robot;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ARBot.ViewModels
{
    /// <summary>Radek tabulky stupnu v panelu vykonu.</summary>
    public sealed class StageRow
    {
        public string Name { get; init; }
        public int Queue { get; init; }
        public long Processed { get; init; }
        public long Dropped { get; init; }
        public double AvgMs { get; init; }
        public double MaxMs { get; init; }
    }

    /// <summary>
    /// Dokument "Vykon": stiha ridici smycka svou periodu? Cte <see cref="PerfMsg"/> ze streamu,
    /// takze funguje i pri prehravani zaznamu (ve View se zpravy z behu prehraji).
    ///
    /// <para>Ukazuje POSLEDNI sekundu. Rozdeleni pres cely beh je uloha rozboru zaznamu
    /// (faze 4), ne panelu. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public partial class PerformanceDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.PerformanceDocumentView);

        private IDisposable feed;

        [ObservableProperty] private string occupancy = "—";
        [ObservableProperty] private string delay = "—";
        [ObservableProperty] private string missed = "—";
        [ObservableProperty] private string worst = "—";
        [ObservableProperty] private string cpu = "—";
        [ObservableProperty] private string verdict = "—";

        /// <summary>Barva verdiktu: zelena / oranzova / cervena.</summary>
        [ObservableProperty] private string verdictColor = "#4CAF50";

        public ObservableCollection<StageRow> Stages { get; } = new ObservableCollection<StageRow>();
        public ObservableCollection<string> Cores { get; } = new ObservableCollection<string>();

        public PerformanceDocument()
        {
            Id = "Performance";
            Title = "Výkon";

            if (Avalonia.Controls.Design.IsDesignMode)
                return;

            // Tentyz zpusob pripojeni jako VirtualSensorsDocument - stream muze byt null,
            // kdyz runtime jeste nebezi.
            try { feed = ARBotRuntime.Current?.Stream?.Connect(this); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        void IMessageSink.Post(Message msg)
        {
            if (msg is not PerfMsg p) return;
            Dispatcher.UIThread.Post(() => Zobraz(p));
        }

        private void Zobraz(PerfMsg p)
        {
            Occupancy = $"{p.OccupancyAvgPct:F0} % (max {p.OccupancyMaxPct:F0} %)";
            Delay = $"{p.DelayAvgMs:F1} ms (max {p.DelayMaxMs:F1} ms)";
            Missed = p.MissedTicks.ToString();
            Worst = p.TickCount == 0
                    ? "—"
                    : $"{p.WorstTickTime:HH:mm:ss.fff} na jádru {p.WorstProcessorId}";
            Cpu = p.ProcessCpuPct < 0 ? "—" : $"{p.ProcessCpuPct:F0} %";

            Verdict = p.Verdict switch
            {
                PerfVerdict.Error => "NESTÍHÁ",
                PerfVerdict.Warning => "dochází rezerva",
                _ => "OK",
            };
            VerdictColor = p.Verdict switch
            {
                PerfVerdict.Error => "#E05252",
                PerfVerdict.Warning => "#E0A052",
                _ => "#4CAF50",
            };

            Cores.Clear();
            foreach (var c in p.Cores)
                Cores.Add($"jádro {c.ProcessorId}: {c.TickCount}×, průměr {c.AvgMs:F1} ms");

            Stages.Clear();
            foreach (var s in p.Stages)
                Stages.Add(new StageRow
                {
                    Name = s.Name, Queue = s.QueueLength, Processed = s.Processed,
                    Dropped = s.Dropped, AvgMs = s.AvgMs, MaxMs = s.MaxMs,
                });
        }

        public void Dispose()
        {
            feed?.Dispose();
            feed = null;
        }
    }
}
