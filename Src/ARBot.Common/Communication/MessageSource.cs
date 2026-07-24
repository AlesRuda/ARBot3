using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Producent zprav. Drzi seznam odberatelu (<see cref="IMessageSink"/>) a rozesila
    /// jim zpravy (fan-out). Kdo je zdroj zprav (hardwarovy senzor, soubor, vypocetni
    /// stupen) rozhoduje konkretni potomek.
    /// </summary>
    public abstract class MessageSource : IDisposable
    {
        private readonly List<IMessageSink> sinks = new List<IMessageSink>();
        private readonly object sinksLock = new object();

        /// <summary>Pripoji odberatele. Vraceny handle jej pri Dispose odpoji.</summary>
        public IDisposable Connect(IMessageSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            lock (sinksLock) sinks.Add(sink);
            return new Subscription(this, sink);
        }

        /// <summary>Odpoji odberatele.</summary>
        public void Disconnect(IMessageSink sink)
        {
            lock (sinksLock) sinks.Remove(sink);
        }

        /// <summary>Rozesle zpravu vsem aktualnim odberatelum (snapshot pod zamkem).</summary>
        protected void Emit(Message msg)
        {
            if (msg == null) return;
            IMessageSink[] snap;
            lock (sinksLock) snap = sinks.ToArray();
            for (int i = 0; i < snap.Length; i++)
                snap[i].Post(msg);
        }

        /// <summary>Spusti produkci zprav.</summary>
        public abstract void Start();

        /// <summary>Zastavi produkci zprav.</summary>
        public abstract void Stop();

        /// <inheritdoc/>
        public virtual void Dispose() => Stop();

        private sealed class Subscription : IDisposable
        {
            private MessageSource src;
            private readonly IMessageSink sink;
            public Subscription(MessageSource src, IMessageSink sink) { this.src = src; this.sink = sink; }
            public void Dispose()
            {
                var s = Interlocked.Exchange(ref src, null);
                s?.Disconnect(sink);
            }
        }
    }
}
