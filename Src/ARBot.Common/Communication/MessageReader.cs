using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    public class MessageReader:IDisposable
    {
        Dictionary<string, Message> msgs;
        bool disposed = false;

        Encoding encoding;
        BinaryReader br;
        public MessageReader(Stream s, Encoding encoding, Dictionary<string, Message> msgs)
        {
            this.encoding = encoding;
            this.msgs = msgs;
            br = new BinaryReader(s, encoding);
        }
//        [DebuggerStepThrough]
        public Message Read()
        {
            string s;
            try
            {
                s = br.ReadString(); 
            }
            catch (EndOfStreamException ex)
            {
                Debug.WriteLine(ex.ToString());
                throw;
            }
            catch (IOException ex)
            {
                Debug.WriteLine(ex.ToString());
                throw;
            }
            catch
            {
                s = "";
            }
            string[] ss = s.Split(new string[] { ":" }, StringSplitOptions.RemoveEmptyEntries);
            if (ss.Length >= 2)
            {
                int len;
                if (int.TryParse(ss[1], out len))
                {
                    byte[] data = br.ReadBytes(len);
                    if (msgs.ContainsKey(ss[0]))
                    {
                        Message msg = msgs[ss[0]].Build();
                        if(ss.Length > 2)
                        {
                            int v = 1;
                            if (int.TryParse(ss[2], out v))
                                msg.Verze = v;
                        }
                        else
                            msg.Verze = 1;

                        try
                        {
                            msg.FromData(encoding, data);
                        }
                        catch
                        {
                            return null;
                        }
                        return msg;
                    }
                    else
                        Debug.WriteLine("Neznama zprava: " + ss[0]);
                }
            }
            return null;
        }
        ~MessageReader()
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
                    if (br != null)
                    {
                        br.Dispose();
                        br = null;
                    }
                }
            }
            disposed = true;
        }
    }
}
