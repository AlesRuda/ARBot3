using System;
using System.IO;
using System.Text;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Cil, ktery bezztratove zapisuje proud zprav do datoveho souboru
    /// (<see cref="MessageWriter"/>) a soucasne plni sidecar index
    /// (<see cref="MessageIndexWriter"/>). Nejde pres ztratovou <see cref="MessageQueue"/>.
    /// Vlastni streamy NEzavira (vlastni je volajici).
    /// </summary>
    public sealed class RecordingTarget : MessageTarget
    {
        private readonly Stream data;
        private readonly MessageWriter writer;
        private readonly MessageIndexWriter index;   // muze byt null
        private long seq;

        /// <param name="dataStream">Datovy soubor (proud zprav).</param>
        /// <param name="indexStream">Sidecar index; null = bez indexu.</param>
        /// <param name="encoding">Kodovani (typicky UTF-8).</param>
        /// <param name="policy">Politika fronty; Block = bezztratove.</param>
        public RecordingTarget(Stream dataStream, Stream indexStream, Encoding encoding,
                               OverflowPolicy policy = OverflowPolicy.Block)
            : base(policy)
        {
            data = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
            var enc = encoding ?? Encoding.UTF8;
            writer = new MessageWriter(dataStream, enc);
            index = indexStream != null ? new MessageIndexWriter(indexStream, enc) : null;
        }

        /// <summary>Pocet dosud zapsanych zprav.</summary>
        public long Count => seq;

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            long offset = data.Position;
            writer.Write(msg);
            long len = data.Position - offset;
            if (index != null)
            {
                long capture = (msg is IHasCaptureTime h) ? h.CaptureTime.Ticks : 0L;
                index.Write(new IndexEntry
                {
                    Seq = seq,
                    Offset = offset,
                    Length = (int)len,
                    CaptureTicks = capture,
                    MsgName = msg.MsgName
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
    }
}
