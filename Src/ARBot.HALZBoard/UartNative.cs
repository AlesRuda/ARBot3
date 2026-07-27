using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.HAL;

namespace ARBot.HALLinux
{
    public class UartNative : IUart, IDisposable
    {
        Encoding encoding = Encoding.ASCII;
        bool disposed = false;
        // Kooperativni zruseni blokujiciho cteni (viz CancelRead).
        private volatile bool readCancel;
        string device;
        FileStream stream;
        StreamReader sr;
        StreamWriter sw;
        string newLine;


        /// <summary>
        /// konstruktor
        /// </summary>
        /// <param name="device">Zarizeni napr. /dev/ttyS0</param>
        /// <param name="baudRate">prenosova rychlost</param>
        /// <param name="newLine">Odradkovani</param>
        public UartNative(string device, int baudRate, string newLine = "\r\n")
        {
            this.newLine = newLine;
            this.device = device;
            Process.Start(new ProcessStartInfo("/bin/stty", string.Format("-F {0} {1} -echo -inlcr -icrnl", device, baudRate)));
            stream = new FileStream(device, FileMode.Open, FileAccess.ReadWrite);
            sr = new StreamReader(stream);
            sw = new StreamWriter(stream);
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~UartNative()
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
                    readCancel = true;   // odblokuj pripadne visici cteni
                    if (sr != null)
                        sr.Dispose();
                    if (sw != null)
                        sw.Dispose();
                    if (stream != null)
                        stream.Dispose();
                }
            }
            disposed = true;
        }

        /// <summary>
        /// Zda je uart otevren
        /// </summary>
        public bool IsOpen => true;

        /// <summary>
        /// Read timeout in ms
        /// </summary>
        public int ReadTimeout
        {
            get
            {
                return 0;
            }
            set
            {
                
            }
        }

        /// <summary>
        /// Reads data from uart
        /// </summary>
        /// <param name="buffer">Buffer to store data</param>
        /// <param name="offset">Offset to the buffer</param>
        /// <param name="count">Max read bytes</param>
        /// <returns>Readed bytes</returns>
        public int Read(byte[] buffer, int offset, int count)
        {
            return stream.Read(buffer, offset, count);
        }

        /// <summary>
        /// Read
        /// </summary>
        /// <param name="count">Bytes to read</param>
        /// <returns></returns>
        public byte[] Read(int count)
        {
            byte[] bytes = new byte[count];
            int idx = 0;
            readCancel = false;   // novy pozadavek na cteni
            while (idx < count)
            {
                if (readCancel)
                    throw new OperationCanceledException("UART read cancelled.");
                int len = Read(bytes, idx, Math.Min(count - idx, count));
                if (len > 0)
                    idx += len;
                else
                    Thread.Sleep(10);
            }
            return bytes;
        }

        /// <summary>
        /// Read async
        /// </summary>
        /// <param name="count">Bytes to read</param>
        /// <returns></returns>
        public async Task<byte[]> ReadAsync(int count)
        {
            return await Task.Run(() => Read(count));
        }

        /// <summary>
        /// Read line
        /// </summary>
        /// <returns></returns>
        public string ReadLine()
        {
            return ReadTo(newLine);
        }

        /// <summary>
        /// Read line async
        /// </summary>
        /// <returns></returns>
        public async Task<string> ReadLineAsync()
        {
            return await Task.Run(() => ReadLine());
        }


        /// <summary>
        /// Writes bytes to uart
        /// </summary>
        /// <param name="buffer"></param>
        public void Write(byte[] buffer)
        {
            stream.Write(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Writes line
        /// </summary>
        /// <param name="txt"></param>
        public void WriteLine(string txt)
        {
            sw.WriteLine(txt);
        }

        /// <summary>Kooperativne zrusi probihajici blokujici cteni (viz <see cref="IUart.CancelRead"/>).</summary>
        public void CancelRead()
        {
            readCancel = true;
        }

        public int ReadByte()
 		{ 
 			byte [] buff = new byte [1]; 
 			if (Read(buff, 0, 1) > 0) 
 				return buff [0]; 
 
 			return -1; 
 		} 

        /// <summary>
        /// Read all
        /// </summary>
        /// <returns></returns>
        public string ReadAll()
        {
            List<byte> vals = new List<byte>();
            int v;

            while((v=ReadByte())!=-1)
                vals.Add((byte)v);

            return encoding.GetString(vals.ToArray());
        }

        /// <summary>
        /// funguje pro max dvouznakove value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private string ReadTo (string value) 
 		{ 
 			if (value == null) 
 				throw new ArgumentNullException ("value"); 
 			if (value.Length == 0) 
 				throw new ArgumentException ("value"); 
 

 			// Turn into byte array, so we can compare 
 			byte [] byte_value = encoding.GetBytes(value); 
 			int current = 0; 
 			List<byte> seen = new List<byte> (); 

 			while (true)
            {
                if (readCancel)
                    throw new OperationCanceledException("UART read cancelled.");
 				int n = ReadByte();
// 				if (n == -1) 
 //					break; 
                if (n != -1)
                {
                    seen.Add((byte)n);
                    if (n == byte_value[current])
                    {
                        current++;
                        if (current == byte_value.Length)
                            return encoding.GetString(seen.ToArray(), 0, seen.Count - byte_value.Length);
                    }
                    else
                    {
                        current = (byte_value[0] == n) ? 1 : 0;
                    }
                }
                else
                    Thread.Sleep(10);
 			} 
// 			return encoding.GetString (seen.ToArray ()); 
 		} 
    }
}
