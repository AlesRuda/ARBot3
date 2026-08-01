using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci dokument s robot-centrickym (ptacim) pohledem na robot-centricka mereni. Zatim
    /// zobrazuje polarni grid(y) sjizdnosti (<see cref="PolarTraversabilityGrid"/> z <see cref="CameraFrame"/>); vyhledove
    /// pribudou dalsi vrstvy (sjizdnost z RGB, okraje vozovky, ...). Zpravy prijima jako
    /// <see cref="IMessageSink"/> ze <see cref="ARBot.Robot.ARBotRuntime.Stream"/> (Run i View -
    /// ve View se prehrava zaznamenany grid) a drzi nejnovejsi grid per kamera. Vykresleni resi
    /// <see cref="ARBot.Views.Controls.RobotCentricControl"/>.
    ///
    /// Zdroj se pripoji zvenci pres <see cref="AttachFeed"/>; dokument ho pri zavreni zastavi.
    /// </summary>
    public partial class RobotCentricDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.RobotCentricDocumentView);

        private readonly List<IDisposable> feeds = new List<IDisposable>();

        // Backpressure (viz Views/README.md): nejnovejsi grid per kamera, koalescovany flush na UI.
        // Grid je nyni soucasti CameraFrame - jmeno kamery i cas porizeni nese ramec (drzime je vedle gridu).
        private readonly object gate = new object();
        private readonly Dictionary<string, (DateTime ts, PolarTraversabilityGrid grid)> registry
            = new Dictionary<string, (DateTime, PolarTraversabilityGrid)>();
        private readonly Dictionary<string, (DateTime ts, PolarTraversabilityGrid grid)> pending
            = new Dictionary<string, (DateTime, PolarTraversabilityGrid)>();
        private volatile bool updateQueued;

        /// <summary>Aktualni gridy sjizdnosti (per kamera) pro control (snapshot).</summary>
        [ObservableProperty] private IReadOnlyList<PolarTraversabilityGrid> grids
            = Array.Empty<PolarTraversabilityGrid>();

        [ObservableProperty] private string info = "-";

        /// <summary>Konstruktor pro design-time i runtime (bez vedlejsich efektu).</summary>
        public RobotCentricDocument()
        {
            Id = "RobotCentric";
            Title = "Robot-centric";
        }

        /// <summary>Pripoji zdroj/e zprav; dokument je pri zavreni zastavi (Dispose).</summary>
        public void AttachFeed(params IDisposable[] disposables)
        {
            if (disposables != null)
                feeds.AddRange(disposables);
        }

        // --- IMessageSink (bezi na vlakne producenta - musi byt neblokujici) ---
        public void Post(Message msg)
        {
            // Grid je soucasti CameraFrame; ramce bez gridu (kamera zatim nepripojena) ignorujeme.
            if (msg is not CameraFrame f || f.Grid == null) return;

            lock (gate)
                pending[f.Name ?? string.Empty] = (f.TimeStamp, f.Grid);   // nejnovejsi vyhrava (drop stale)

            if (updateQueued) return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        private void Flush()
        {
            updateQueued = false;

            lock (gate)
            {
                if (pending.Count == 0) return;
                foreach (var kv in pending)
                    registry[kv.Key] = kv.Value;
                pending.Clear();
            }

            var keys = new List<string>(registry.Keys);
            keys.Sort(StringComparer.Ordinal);

            var snapshot = new List<PolarTraversabilityGrid>(keys.Count);

            // Per kamera: cas porizeni + STARI zpravy (Δ = ted - TimeStamp). Rostouci Δ = backlog
            // (prepocet/zobrazeni nestiha realny cas). TimeStamp je cas porizeni snimku (T_in).
            var now = DateTime.Now;
            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                var (ts, grid) = registry[k];
                snapshot.Add(grid);
                int cells = grid.Cells?.Length ?? 0;
                double ageMs = (now - ts).TotalMilliseconds;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:HH:mm:ss.fff}  Δ{2:F0} ms  (výpočet {3:F0} ms, {4} b.)",
                    k, ts, ageMs, grid.ComputeMs, cells));
            }

            Grids = snapshot;
            Info = sb.ToString().TrimEnd();
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            foreach (var d in feeds)
            {
                try { d.Dispose(); } catch { }
            }
            feeds.Clear();
        }
    }
}
