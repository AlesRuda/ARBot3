using System;
using System.Threading.Channels;
using System.Threading.Tasks;
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
            if (writer.TryWrite(msg)) return;     // rychla cesta (unbounded / je misto / drop politika)
            if (policy == OverflowPolicy.Block)
            {
                try { writer.WriteAsync(msg).AsTask().GetAwaiter().GetResult(); }   // backpressure
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
                    try { Consume(msg); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
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

        /// <summary>Zpracuje jednu zpravu (volano serializovane jednim vláknem).</summary>
        protected abstract void Consume(Message msg);

        /// <summary>Volano po vyprazdneni davky zprav (periodicky flush).</summary>
        protected virtual void OnFlush() { }

        /// <summary>Volano jednou po uplnem zastaveni a dokonceni fronty.</summary>
        protected virtual void OnStopped() { }
    }
}
