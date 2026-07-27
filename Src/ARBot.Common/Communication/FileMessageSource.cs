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
    /// Rezimy tempa: <see cref="ReplayPacing.AsFastAsPossible"/> (batch/regrese) a
    /// <see cref="ReplayPacing.RealTime"/> (vizualizace, skalovatelna rychlost).
    /// Type-filtr umoznuje definovat "rez" (tap) - emitovat jen vybrane typy.
    /// Datovy stream NEzavira (vlastni je volajici).
    ///
    /// <para><b>Navigace (View):</b> je-li predan <see cref="IndexEntry"/> index, zdroj se
    /// chova jako stavovy automat <see cref="ReplayState.Playing"/> / <see cref="ReplayState.Paused"/>.
    /// Kurzor je poradove cislo <c>Seq</c>. <see cref="SeekTo"/> (jen v Paused) rekonstruuje
    /// stav zpetnym pruchodem indexu od kurzoru (posledni &le; pozice pro kazdou dvojici
    /// <c>(MsgName, Name)</c>) a emituje nalezene ramce nactene <b>nahodne z Offsetu</b>
    /// samostatnym ctenarem. <see cref="Play"/> pokracuje z ulozene <c>Seq</c>.</para>
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

        /// <summary>Stav prehravani (navigace).</summary>
        public enum ReplayState
        {
            /// <summary>Prehrava sekvenceni od kurzoru.</summary>
            Playing,
            /// <summary>Pozastaveno (lze <see cref="SeekTo"/> / krokovat).</summary>
            Paused
        }

        private readonly Stream data;
        private readonly Encoding enc;
        private readonly Dictionary<string, Message> proto;
        private readonly ReplayPacing pacing;
        private readonly double rate;

        // Navigace (jen kdyz je predan index).
        private readonly List<IndexEntry> index;              // muze byt null (jen sekvencni Play)
        private readonly HashSet<(string, string)> allKeys;   // vsechny (MsgName, Name) z indexu
        private readonly object navLock = new object();
        private long cursor;                                   // Seq nasledujici zpravy k prehrani

        private HashSet<string> typeFilter;
        private Task task;
        private CancellationTokenSource cts;

        /// <param name="data">Datovy soubor se zaznamem.</param>
        /// <param name="encoding">Kodovani (typicky UTF-8).</param>
        /// <param name="catalog">Katalog prototypu zprav.</param>
        /// <param name="pacing">Tempo prehravani.</param>
        /// <param name="rate">Nasobek rychlosti pro RealTime (2.0 = 2x rychleji).</param>
        /// <param name="index">Volitelny index (sidecar) pro navigaci/seek; null = jen sekvencni Play.</param>
        public FileMessageSource(Stream data, Encoding encoding, MessageCatalog catalog,
                                 ReplayPacing pacing = ReplayPacing.AsFastAsPossible, double rate = 1.0,
                                 IReadOnlyList<IndexEntry> index = null)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            enc = encoding ?? Encoding.UTF8;
            proto = (catalog ?? throw new ArgumentNullException(nameof(catalog))).ToPrototypeMap();
            this.pacing = pacing;
            this.rate = rate <= 0 ? 1.0 : rate;

            if (index != null)
            {
                this.index = new List<IndexEntry>(index);
                allKeys = new HashSet<(string, string)>();
                foreach (var e in this.index)
                    allKeys.Add((e.MsgName ?? string.Empty, e.Name ?? string.Empty));
            }
        }

        /// <summary>Filtr typu k emitovani (null = vse). Definuje spolu s grafem "rez".</summary>
        public void SetTypeFilter(IEnumerable<string> msgNames)
            => typeFilter = msgNames == null ? null : new HashSet<string>(msgNames);

        /// <summary>Vyvolano po dosazeni konce zaznamu (jen v Playing).</summary>
        public event EventHandler Completed;

        /// <summary>Aktualni stav prehravani (Playing hned po <see cref="Start"/> / <see cref="Play"/>).</summary>
        public ReplayState State { get; private set; } = ReplayState.Playing;

        /// <summary>Aktualni pozice kurzoru (Seq nasledujici zpravy). Vyzaduje index.</summary>
        public long Cursor { get { lock (navLock) return cursor; } }

        /// <summary>Pocet zaznamu v indexu (0 kdyz index neni k dispozici).</summary>
        public int Count => index?.Count ?? 0;

        /// <summary>Snapshot indexu (pro navigacni nastroj); null bez indexu.</summary>
        public IReadOnlyList<IndexEntry> Index => index;

        /// <inheritdoc/>
        public override void Start()
        {
            Play();
        }

        /// <summary>Spusti/obnovi sekvencni prehravani od aktualniho kurzoru.</summary>
        public void Play()
        {
            lock (navLock)
            {
                if (task != null) return;
                State = ReplayState.Playing;
                cts = new CancellationTokenSource();
                var token = cts.Token;
                task = Task.Factory.StartNew(() => ReplayLoop(token), TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>Pozastavi prehravani (dokonci probihajici zpravu, pak stoji).</summary>
        public void Pause()
        {
            CancellationTokenSource c;
            Task t;
            lock (navLock)
            {
                State = ReplayState.Paused;
                c = cts; t = task;
                cts = null; task = null;
            }
            c?.Cancel();
            try { t?.Wait(); }
            catch (Exception ex) { Debug.WriteLine(ex.ToString()); }
            c?.Dispose();
        }

        /// <inheritdoc/>
        public override void Stop() => Pause();

        /// <summary>Synchronne prehraje cely zaznam a vrati se (vhodne pro batch/testy).</summary>
        public void RunToEnd() => ReplayLoop(CancellationToken.None);

        /// <summary>
        /// Rekonstruuje stav v pozici <paramref name="seq"/> (jen index; jen v Paused): zpetny
        /// pruchod indexem od pozice, pro kazdou dosud nevidenou dvojici <c>(MsgName, Name)</c>
        /// vezme prvni vyskyt (= posledni &le; pozice), precte ramec nahodne z Offsetu a emituje
        /// na vystup. Konci, kdyz ma vsechny klice, nebo dojde na zacatek. Kurzor se nastavi za
        /// pozici (dalsi Play pokracuje od <paramref name="seq"/>+1).
        /// </summary>
        public void SeekTo(long seq)
        {
            if (index == null)
                throw new InvalidOperationException("SeekTo vyzaduje index.");
            if (State != ReplayState.Paused)
                throw new InvalidOperationException("SeekTo je povolen jen v Paused (nejdriv Pause).");

            // Ohranici pozici do platneho rozsahu [0, Count-1].
            long pos = seq;
            if (pos < 0) pos = 0;
            if (pos > index.Count - 1) pos = index.Count - 1;
            if (index.Count == 0) return;

            // Zpetny pruchod: sber posledni <= pos pro kazdy klic. Emit v poradi rostouciho Seq.
            var seen = new HashSet<(string, string)>();
            var picks = new List<IndexEntry>();
            for (long i = pos; i >= 0; i--)
            {
                var e = index[(int)i];
                var key = (e.MsgName ?? string.Empty, e.Name ?? string.Empty);
                if (seen.Add(key))
                    picks.Add(e);
                if (seen.Count >= allKeys.Count)
                    break;
            }

            // Emit chronologicky (rostouci Seq) - stav se posklada od nejstarsiho k pozici.
            picks.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            foreach (var e in picks)
            {
                var msg = ReadFrameAt(e.Offset);
                if (msg == null) continue;
                if (typeFilter != null && !typeFilter.Contains(msg.MsgName)) continue;
                Emit(msg);
            }

            lock (navLock)
                cursor = pos + 1;
        }

        /// <summary>Nahodne precte jeden ramec z daneho <paramref name="offset"/> (samostatny ctenar).</summary>
        private Message ReadFrameAt(long offset)
        {
            // Bufferovany sekvenci reader nelze preseekovat -> vlastni docasny ctenar z Offsetu.
            // SeekTo je povoleno jen v Paused, kdy neni aktivni Play smycka (zadny soubeh na Position).
            lock (navLock)
            {
                long saved = data.Position;
                try
                {
                    data.Position = offset;
                    var reader = new MessageReader(data, enc, proto);
                    return reader.Read();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    return null;
                }
                finally
                {
                    data.Position = saved;
                }
            }
        }

        private void ReplayLoop(CancellationToken token)
        {
            // Kdyz je index k dispozici, zacneme od Offsetu odpovidajiciho kurzoru (po Seek),
            // jinak od aktualni pozice streamu (kompatibilita s puvodnim chovanim).
            if (index != null)
            {
                long start;
                lock (navLock) start = cursor;
                if (start < index.Count)
                    data.Position = index[(int)start].Offset;
                else
                    data.Position = data.Length;   // za koncem -> nic k prehrani
            }

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

                // Posun kurzoru (i pri preskoceni neznameho typu jde o jednu zpravu v souboru).
                if (index != null)
                    lock (navLock) cursor++;

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

            if (!token.IsCancellationRequested)
                Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
