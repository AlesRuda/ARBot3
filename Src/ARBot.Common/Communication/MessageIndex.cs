using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Jeden zaznam indexu (sidecar) - popisuje jednu zpravu v datovem souboru.
    /// </summary>
    public struct IndexEntry
    {
        /// <summary>Poradove cislo zpravy (od 0).</summary>
        public long Seq { get; set; }
        /// <summary>Offset zacatku ramce v datovem souboru.</summary>
        public long Offset { get; set; }
        /// <summary>Delka celeho ramce (hlavicka + payload) v bajtech.</summary>
        public int Length { get; set; }
        /// <summary>Cas porizeni (T_in) v tickach (0 = neznamy).</summary>
        public long CaptureTicks { get; set; }
        /// <summary>Cas prichodu na Stream (T_out) v tickach (0 = neznamy). Stampuje <see cref="RecordingTarget"/>.</summary>
        public long ArrivalTicks { get; set; }
        /// <summary>Nazev typu zpravy.</summary>
        public string MsgName { get; set; }
        /// <summary>Jmeno instance zpravy z <see cref="ARBot.Common.Logs.INamedMessage"/> (jinak prazdne).</summary>
        public string Name { get; set; }

        /// <summary>Cas porizeni (T_in) jako <see cref="DateTime"/>. K tomuto okamziku se data uplatnuji.</summary>
        public DateTime CaptureTime => new DateTime(CaptureTicks);
        /// <summary>Cas prichodu (T_out) jako <see cref="DateTime"/>. Okamzik kdy se data dostanou do streamu, ale jejich platnost je zpetne k CaptureTime.</summary>
        public DateTime ArrivalTime => new DateTime(ArrivalTicks);
    }

    /// <summary>
    /// Zapisovac indexu (sidecar <c>*.idx</c>). Append-only, po jednom zaznamu na zpravu.
    /// </summary>
    public sealed class MessageIndexWriter : IDisposable
    {
        private BinaryWriter bw;
        private bool disposed;

        public MessageIndexWriter(Stream s, Encoding encoding)
        {
            bw = new BinaryWriter(s, encoding);
        }

        /// <summary>Zapise jeden zaznam indexu.</summary>
        public void Write(in IndexEntry e)
        {
            bw.Write(e.Seq);
            bw.Write(e.Offset);
            bw.Write(e.Length);
            bw.Write(e.CaptureTicks);
            bw.Write(e.ArrivalTicks);
            bw.Write(e.MsgName ?? string.Empty);
            bw.Write(e.Name ?? string.Empty);
        }

        public void Flush() => bw.Flush();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bw?.Dispose();
            bw = null;
        }
    }

    /// <summary>
    /// Cteni indexu do pameti (pro seek podle casu/typu).
    /// </summary>
    public static class MessageIndex
    {
        /// <summary>Nacte vsechny zaznamy indexu ze streamu.</summary>
        public static List<IndexEntry> Read(Stream s, Encoding encoding)
        {
            var list = new List<IndexEntry>();
            using (var br = new BinaryReader(s, encoding, leaveOpen: true))
            {
                while (s.Position < s.Length)
                {
                    var e = new IndexEntry
                    {
                        Seq = br.ReadInt64(),
                        Offset = br.ReadInt64(),
                        Length = br.ReadInt32(),
                        CaptureTicks = br.ReadInt64(),
                        ArrivalTicks = br.ReadInt64(),
                        MsgName = br.ReadString(),
                        Name = br.ReadString()
                    };
                    list.Add(e);
                }
            }
            return list;
        }
    }
}
