using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Cil zprav s vlastni frontou (<see cref="Channel{T}"/>) a jednim konzumnim vláknem.
    /// Konzumace je tim serializovana (jedno vlakno) - i kdyz producenti pisou z vice
    /// vlaken. Politika <see cref="OverflowPolicy.Block"/> prenasi zpetny tlak na
    /// producenta a je BEZZTRATOVA (pro zaznam).
    /// </summary>
    public abstract class MessageTarget : IMessageSink, IDisposable
    {
        private readonly Channel<Message> channel;
        private readonly OverflowPolicy policy;
        private readonly object startLock = new object();
        private Task consumer;
        private bool started;

        /// <param name="policy">Chovani pri zaplneni fronty.</param>
        /// <param name="capacity">Kapacita fronty; &lt;=0 = neomezena (Block je pak vzdy bezztratovy).</param>
        protected MessageTarget(OverflowPolicy policy = OverflowPolicy.Block, int capacity = 0)
        {
            this.policy = policy;
            if (capacity <= 0)
            {
                channel = Channel.CreateUnbounded<Message>(
                    new UnboundedChannelOptions { SingleReader = true });
            }
            else
            {
                var mode = policy switch
                {
                    OverflowPolicy.DropOldest => BoundedChannelFullMode.DropOldest,
                    OverflowPolicy.DropNewest => BoundedChannelFullMode.DropNewest,
                    _ => BoundedChannelFullMode.Wait
                };
                channel = Channel.CreateBounded<Message>(
                    new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = mode });
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// <c>virtual</c> kvuli best-effort odberatelum (napr. <see cref="RecordingTarget"/>),
        /// ktere potrebuji na vlakne producenta rozhodnout o zahozeni (drop) drive, nez se
        /// zprava dostane do fronty.
        /// </remarks>
        public virtual void Post(Message msg)
        {
            if (msg == null) return;
            var writer = channel.Writer;
            if (writer.TryWrite(msg))
            {
                Interlocked.Increment(ref written);
                return;                           // rychla cesta (unbounded / je misto / drop politika)
            }
            if (policy == OverflowPolicy.Block)
            {
                try
                {
                    writer.WriteAsync(msg).AsTask().GetAwaiter().GetResult();       // backpressure
                    Interlocked.Increment(ref written);
                }
                catch (ChannelClosedException) { /* cil zastaven */ }
            }
        }

        /// <summary>Spusti konzumni vlakno (idempotentni).</summary>
        public virtual void Start()
        {
            lock (startLock)
            {
                if (started) return;
                started = true;
                consumer = Task.Factory
                    .StartNew(ConsumeLoop, TaskCreationOptions.LongRunning)
                    .Unwrap();
            }
        }

        private async Task ConsumeLoop()
        {
            var reader = channel.Reader;
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var msg))
                {
                    long t0 = Stopwatch.GetTimestamp();
                    try { Consume(msg); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
                    finally
                    {
                        long dt = Stopwatch.GetTimestamp() - t0;
                        Interlocked.Add(ref durationTicks, dt);
                        Interlocked.Increment(ref processed);
                        Interlocked.Increment(ref processedForAvg);
                        Interlocked.Increment(ref consumed);

                        // Maximum bez zamku: opakovany CAS, dokud nas nekdo nepredbehne vyssi
                        // hodnotou. Bezi jen jedno konzumni vlakno, takze smycka je fakticky
                        // jednoprubezna - je tu kvuli soubehu s nulovanim v TakeStageSnapshot.
                        long max = Volatile.Read(ref maxDurationTicks);
                        while (dt > max)
                        {
                            long puvodni = Interlocked.CompareExchange(ref maxDurationTicks, dt, max);
                            if (puvodni == max) break;
                            max = puvodni;
                        }
                    }
                }
                try { OnFlush(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
            }
        }

        /// <summary>Zastavi cil: dokonci frontu, dopočte zbytek a flushne (idempotentni).</summary>
        public virtual void Stop()
        {
            lock (startLock)
            {
                if (!started) return;
                channel.Writer.TryComplete();
                try { consumer?.Wait(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
                started = false;
            }
            OnStopped();
        }

        /// <inheritdoc/>
        public void Dispose() => Stop();

        // --- Pocitadla vykonu (diagnostika) -------------------------------------------------
        // Interlocked, ne zamek: zapisuje vlakno konzumenta i vlakna producentu a mereni nesmi
        // stat znatelny cas. Viz doc/perf-monitoring.md.
        private long written;            // celkem zapsanych do kanalu za cely beh
        private long consumed;           // celkem vyzvednutych z kanalu za cely beh
        private long droppedReported;    // kolik zahozeni uz vratil nektery snimek
        private long processed;          // prirustek za interval
        private long processedForAvg;    // prirustek za interval (parovan s durationTicks)
        private long durationTicks;
        private long maxDurationTicks;

        private static readonly double StageTickToMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>Jmeno stupne pro diagnostiku; vychozi je nazev typu.</summary>
        public virtual string StageName => GetType().Name;

        /// <summary>
        /// Vrati statistiku od posledniho odectu a prirustkove udaje VYNULUJE. Delka fronty je
        /// stav, ta se nenuluje.
        /// </summary>
        public StageSnapshot TakeStageSnapshot()
        {
            long dropped = ZmerZahozene(out int queue);
            return new StageSnapshot
            {
                Name = StageName,
                QueueLength = queue,
                Processed = Interlocked.Exchange(ref processed, 0),
                Dropped = dropped,
                AvgMs = ZmerPrumer(),
                MaxMs = Interlocked.Exchange(ref maxDurationTicks, 0) * StageTickToMs,
            };
        }

        /// <summary>
        /// Kolik zprav se od posledniho odectu ztratilo, a jak je fronta dlouha.
        ///
        /// <para><b>Proc se to dopocitava a necita primo pri zahozeni.</b> U politik
        /// <see cref="OverflowPolicy.DropOldest"/> a <see cref="OverflowPolicy.DropNewest"/> vraci
        /// <c>TryWrite</c> <b>true i tehdy, kdyz se neco zahodilo</b> - kanal zahodi JINOU zpravu,
        /// ne tuhle, a volajicimu o tom nerekne. Pocet zahozenych proto z navratove hodnoty zjistit
        /// nejde a musi se odvodit z bilance: <c>zapsane - vyzvednute - delka fronty</c>.</para>
        ///
        /// <para>Poradi odectu je zamerne (zapsane, pak vyzvednute, pak fronta): pri soubehu tak
        /// muze rozdil vyjit jen MENSI, nikdy vetsi - mereni tedy zahozeni nikdy nevymysli.</para>
        /// </summary>
        private long ZmerZahozene(out int queue)
        {
            long w = Interlocked.Read(ref written);
            long c = Interlocked.Read(ref consumed);
            var reader = channel.Reader;
            queue = reader.CanCount ? reader.Count : (int)Math.Max(0, Math.Min(int.MaxValue, w - c));

            long celkem = Math.Max(0, w - c - queue);
            long prirustek = celkem - Interlocked.Exchange(ref droppedReported, celkem);
            return Math.Max(0, prirustek);
        }

        private double ZmerPrumer()
        {
            long ticks = Interlocked.Exchange(ref durationTicks, 0);
            long n = Interlocked.Exchange(ref processedForAvg, 0);
            return n == 0 ? 0 : ticks * StageTickToMs / n;
        }

        /// <summary>Zpracuje jednu zpravu (volano serializovane jednim vláknem).</summary>
        protected abstract void Consume(Message msg);

        /// <summary>Volano po vyprazdneni davky zprav (periodicky flush).</summary>
        protected virtual void OnFlush() { }

        /// <summary>Volano jednou po uplnem zastaveni a dokonceni fronty.</summary>
        protected virtual void OnStopped() { }
    }
}
