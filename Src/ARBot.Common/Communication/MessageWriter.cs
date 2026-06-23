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
        public MessageWriter(Stream s, Encoding encoding)
        {
            this.encoding = encoding;
            bw = new BinaryWriter(s, encoding);
        }
        public void Write(Message msg)
        {
            byte[] data = msg.ToData(encoding);
            bw.Write(string.Format("{0}:{1}:{2}", msg.MsgName, data.Length, msg.Verze));
            bw.Write(data);
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
                }
            }
            disposed = true;
        }


    }
}
