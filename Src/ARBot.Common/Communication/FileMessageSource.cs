using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Zdroj zprav ctouci ze zaznamu (replay). Zpravy emituje v poradi souboru.
    /// Rezimy: <see cref="ReplayPacing.AsFastAsPossible"/> (batch/regrese) a
    /// <see cref="ReplayPacing.RealTime"/> (vizualizace, skalovatelna rychlost).
    /// Type-filtr umoznuje definovat "rez" (tap) - emitovat jen vybrane typy.
    /// Datovy stream NEzavira (vlastni je volajici).
    /// </summary>
    public sealed class FileMessageSource : MessageSource
    {
        /// <summary>Rezim tempa prehravani.</summary>
        public enum ReplayPacing
        {
            /// <summary>Co nejrychleji (batch/regrese).</summary>
            AsFastAsPossible,
            /// <summary>V realnem case podle casu porizeni (vizualizace).</summary>
            RealTime
        }

        private readonly Stream data;
        private readonly Encoding enc;
        private readonly Dictionary<string, Message> proto;
        private readonly ReplayPacing pacing;
        private readonly double rate;
        private HashSet<string> typeFilter;
        private Task task;
        private CancellationTokenSource cts;

        /// <param name="data">Datovy soubor se zaznamem.</param>
        /// <param name="encoding">Kodovani (typicky UTF-8).</param>
        /// <param name="catalog">Katalog prototypu zprav.</param>
        /// <param name="pacing">Tempo prehravani.</param>
        /// <param name="rate">Nasobek rychlosti pro RealTime (2.0 = 2x rychleji).</param>
        public FileMessageSource(Stream data, Encoding encoding, MessageCatalog catalog,
                                 ReplayPacing pacing = ReplayPacing.AsFastAsPossible, double rate = 1.0)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            enc = encoding ?? Encoding.UTF8;
            proto = (catalog ?? throw new ArgumentNullException(nameof(catalog))).ToPrototypeMap();
            this.pacing = pacing;
            this.rate = rate <= 0 ? 1.0 : rate;
        }

        /// <summary>Filtr typu k emitovani (null = vse). Definuje spolu s grafem "rez".</summary>
        public void SetTypeFilter(IEnumerable<string> msgNames)
            => typeFilter = msgNames == null ? null : new HashSet<string>(msgNames);

        /// <summary>Vyvolano po dosazeni konce zaznamu.</summary>
        public event EventHandler Completed;

        /// <inheritdoc/>
        public override void Start()
        {
            if (task != null) return;
            cts = new CancellationTokenSource();
            var token = cts.Token;
            task = Task.Factory.StartNew(() => ReplayLoop(token), TaskCreationOptions.LongRunning);
        }

        /// <inheritdoc/>
        public override void Stop()
        {
            cts?.Cancel();
            try { task?.Wait(); }
            catch (Exception ex) { Debug.WriteLine(ex.ToString()); }
            task = null;
            cts?.Dispose();
            cts = null;
        }

        /// <summary>Synchronne prehraje cely zaznam a vrati se (vhodne pro batch/testy).</summary>
        public void RunToEnd() => ReplayLoop(CancellationToken.None);

        private void ReplayLoop(CancellationToken token)
        {
            // MessageReader zamerne nedisposujeme - nechceme zavrit stream volajiciho.
            var reader = new MessageReader(data, enc, proto);
            var sw = Stopwatch.StartNew();
            DateTime firstCapture = default;
            bool haveFirst = false;

            while (!token.IsCancellationRequested && data.Position < data.Length)
            {
                Message msg;
                try { msg = reader.Read(); }
                catch (EndOfStreamException) { break; }
                if (msg == null) continue;   // neznamy typ / chyba dekodovani -> preskoc

                if (typeFilter != null && !typeFilter.Contains(msg.MsgName)) continue;

                if (pacing == ReplayPacing.RealTime && msg is IHasCaptureTime h)
                {
                    if (!haveFirst) { firstCapture = h.CaptureTime; haveFirst = true; }
                    double targetMs = (h.CaptureTime - firstCapture).TotalMilliseconds / rate;
                    double waitMs = targetMs - sw.Elapsed.TotalMilliseconds;
                    if (waitMs > 1)
                    {
                        try { Task.Delay((int)waitMs, token).Wait(token); }
                        catch (OperationCanceledException) { break; }
                    }
                }

                Emit(msg);
            }

            Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
