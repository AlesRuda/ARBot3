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
        public long Seq;
        /// <summary>Offset zacatku ramce v datovem souboru.</summary>
        public long Offset;
        /// <summary>Delka celeho ramce (hlavicka + payload) v bajtech.</summary>
        public int Length;
        /// <summary>Cas porizeni v tickach (0 = neznamy).</summary>
        public long CaptureTicks;
        /// <summary>Nazev typu zpravy.</summary>
        public string MsgName;

        /// <summary>Cas porizeni jako <see cref="DateTime"/>.</summary>
        public DateTime CaptureTime => new DateTime(CaptureTicks);
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
            bw.Write(e.MsgName ?? string.Empty);
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
                        MsgName = br.ReadString()
                    };
                    list.Add(e);
                }
            }
            return list;
        }
    }
}
