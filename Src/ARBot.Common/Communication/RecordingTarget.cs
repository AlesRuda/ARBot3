using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Cil, ktery zapisuje proud zprav do datoveho souboru (<see cref="MessageWriter"/>)
    /// a soucasne plni sidecar index (<see cref="MessageIndexWriter"/>). Vlastni streamy
    /// NEzavira (vlastni je volajici).
    ///
    /// <para><b>Rezimy:</b> bez konfigurovanych limitu (default) je zaznam BEZZTRATOVY
    /// (offline). S per-typ limity je <b>best-effort</b>: pri nestihani se prebytecne
    /// zpravy zahazuji uz v <see cref="Post"/> (na vlakne producenta), aby fronta
    /// nebrzdila <c>Stream.Emit</c> ani ridici smycku. Typ bez limitu = neomezeny;
    /// limit 0 = zahodit vzdy.</para>
    ///
    /// <para><b>Dva casy:</b> <c>T_in</c> = cas porizeni (<see cref="IHasCaptureTime.CaptureTime"/>),
    /// <c>T_out</c> = cas prichodu na Stream (stampuje se v <see cref="Post"/> a protéká
    /// frontou v obalce <see cref="Envelope"/>), zapisuje se do indexu jako
    /// <see cref="IndexEntry.ArrivalTicks"/>. Do indexu jde i <see cref="IndexEntry.Name"/>
    /// z <see cref="INamedMessage"/>.</para>
    /// </summary>
    public sealed class RecordingTarget : MessageTarget
    {
        private readonly Stream data;
        private readonly MessageWriter writer;
        private readonly MessageIndexWriter index;   // muze byt null
        private long seq;

        // Per-typ retence: limits = konfigurovane limity (typ mimo mapu = neomezeny),
        // inflight = aktualni pocet zprav daneho typu ve fronte + prave zpracovavanych.
        private readonly Dictionary<string, int> limits;
        private readonly Dictionary<string, int> inflight;
        private readonly object countsLock = new object();

        /// <param name="dataStream">Datovy soubor (proud zprav).</param>
        /// <param name="indexStream">Sidecar index; null = bez indexu.</param>
        /// <param name="encoding">Kodovani (typicky UTF-8).</param>
        /// <param name="policy">Politika vstupni fronty; Block = bezztratove.</param>
        /// <param name="perTypeLimits">
        /// Volitelne per-typ limity (MsgName -&gt; max zprav v obehu). null / prazdne =
        /// bezztratovy rezim (zadne zahazovani). Best-effort: napr. Blob=2, ostatni vysoke.
        /// </param>
        public RecordingTarget(Stream dataStream, Stream indexStream, Encoding encoding,
                               OverflowPolicy policy = OverflowPolicy.Block,
                               IReadOnlyDictionary<string, int> perTypeLimits = null)
            : base(policy)
        {
            data = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
            var enc = encoding ?? Encoding.UTF8;
            writer = new MessageWriter(dataStream, enc);
            index = indexStream != null ? new MessageIndexWriter(indexStream, enc) : null;

            if (perTypeLimits != null && perTypeLimits.Count > 0)
            {
                limits = new Dictionary<string, int>(perTypeLimits.Count);
                foreach (var kv in perTypeLimits)
                    limits[kv.Key] = kv.Value;
                inflight = new Dictionary<string, int>();
            }
        }

        /// <summary>Pocet dosud zapsanych zprav.</summary>
        public long Count => seq;

        /// <summary>
        /// Prijeti zpravy na vlakne producenta. Stampuje <c>T_out</c> a pri konfigurovanych
        /// limitech rozhoduje o zahozeni (drop) driv, nez se zprava dostane do fronty.
        /// </summary>
        public override void Post(Message msg)
        {
            if (msg == null) return;

            long arrival = TimeBase.Now.Ticks;   // T_out (cas prichodu)

            // Per-typ retence (jen kdyz jsou limity nakonfigurovane). Typ mimo mapu = neomezeny.
            if (limits != null && limits.TryGetValue(msg.MsgName, out int limit))
            {
                lock (countsLock)
                {
                    inflight.TryGetValue(msg.MsgName, out int c);
                    if (c >= limit)
                        return;                  // zahozeni pri nestihani
                    inflight[msg.MsgName] = c + 1;
                }
            }

            base.Post(new Envelope(msg, arrival));
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            Message inner = msg;
            long arrival = 0L;
            if (msg is Envelope env)
            {
                inner = env.Inner;
                arrival = env.ArrivalTicks;
            }

            // Dekrement poctu v obehu (jen sledovane typy).
            if (limits != null && limits.ContainsKey(inner.MsgName))
            {
                lock (countsLock)
                {
                    if (inflight.TryGetValue(inner.MsgName, out int c) && c > 0)
                        inflight[inner.MsgName] = c - 1;
                }
            }

            long offset = data.Position;
            writer.Write(inner);
            long len = data.Position - offset;
            if (index != null)
            {
                long capture = (inner is IHasCaptureTime h) ? h.CaptureTime.Ticks : 0L;   // T_in
                string name = (inner is INamedMessage nm) ? (nm.Name ?? string.Empty) : string.Empty;
                index.Write(new IndexEntry
                {
                    Seq = seq,
                    Offset = offset,
                    Length = (int)len,
                    CaptureTicks = capture,
                    ArrivalTicks = arrival,
                    MsgName = inner.MsgName,
                    Name = name
                });
            }
            seq++;
        }

        /// <inheritdoc/>
        protected override void OnFlush()
        {
            writer.Flush();
            index?.Flush();
        }

        /// <inheritdoc/>
        protected override void OnStopped()
        {
            writer.Flush();
            index?.Flush();
        }

        /// <summary>
        /// Interni obalka nesouci zpravu spolu s casem prichodu (<c>T_out</c>) frontou
        /// z <see cref="Post"/> do <see cref="Consume"/> (jine vlakno). Nikdy se
        /// (de)serializuje - zapisuje se vzdy vnitrni <see cref="Inner"/>.
        /// </summary>
        private sealed class Envelope : Message
        {
            public readonly Message Inner;
            public readonly long ArrivalTicks;

            public Envelope(Message inner, long arrivalTicks) : base("RecordEnvelope", 1)
            {
                Inner = inner;
                ArrivalTicks = arrivalTicks;
            }

            public override void ToData(BinaryWriter bw)
                => throw new NotSupportedException("Envelope se neserializuje.");
            public override void FromData(BinaryReader br)
                => throw new NotSupportedException("Envelope se nedeserializuje.");
            public override Message Build()
                => throw new NotSupportedException("Envelope se nevytvari z katalogu.");
        }
    }
}
