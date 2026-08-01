using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    public class MessageWriter:IDisposable
    {
        bool disposed = false;

        Encoding encoding;
        BinaryWriter bw;

        // Znovupouzity scratch buffer pro serializaci zpravy (proti GC pauzam): puvodne se pro KAZDOU
        // zpravu alokovala nova MemoryStream + ms.ToArray() - u CameraFrame (~1,8 MB nekomprimovane)
        // to bylo nekolik LOH alokaci na snimek (~90 MB/s pri 16 fps) => periodicka blokujici gen2 GC
        // (200-455 ms pauzy). Ted serializujeme do JEDNE znovupouzite MemoryStream a zapisujeme primo
        // z jejiho interniho bufferu (GetBuffer, bez kopie). MessageWriter je pouzivan jednim vlaknem
        // (napr. Consume vlakno RecordingTargetu), takze scratch nepotrebuje zamek.
        readonly MemoryStream scratch = new MemoryStream(1 << 16);
        readonly BinaryWriter scratchBw;

        public MessageWriter(Stream s, Encoding encoding)
        {
            this.encoding = encoding;
            bw = new BinaryWriter(s, encoding);
            scratchBw = new BinaryWriter(scratch, encoding);
        }
        public void Write(Message msg)
        {
            // Serializace do znovupouziteho bufferu (0 alokaci/zpravu v ustalenem stavu).
            scratch.Position = 0;
            scratch.SetLength(0);
            msg.ToData(scratchBw);
            scratchBw.Flush();

            int len = (int)scratch.Length;
            bw.Write(string.Format("{0}:{1}:{2}", msg.MsgName, len, msg.Verze));
            bw.Write(scratch.GetBuffer(), 0, len);   // GetBuffer = interni pole bez kopie
        }
        public void Flush()
        {
            bw.Flush();
        }
        MessageWriter()
        {
            Dispose(false);
        }
        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (bw != null)
                    {
                        bw.Dispose();
                        bw = null;
                    }
                    try { scratchBw?.Dispose(); } catch { }
                }
            }
            disposed = true;
        }


    }
}
