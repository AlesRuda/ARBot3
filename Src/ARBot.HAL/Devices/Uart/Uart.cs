using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace ARBot.HAL.Devices.Uart
{
    /// <summary>
    /// Implementuje pristup k Uartum
    /// </summary>
    public class Uart : IUart, IDisposable
    {
        SerialPort sp;

        bool disposed = false;
        string device;
        string name;

        // Neblokujici backoff pro znovuotevreni portu: pokus o sp.Open() se dela nejvyse jednou
        // za ReopenBackoffMs. Diky tomu ReOpen() nikdy neblokuje volajici vlakno (drive ridici
        // smycku, zapis) - drive to delal Thread.Sleep(1000) primo v ReOpen a pri nedostupnem
        // portu zahlcoval threadpool (viz doc/record-replay.md).
        const int ReopenBackoffMs = 1000;
        DateTime lastOpenAttempt = DateTime.MinValue;

        // Kooperativni zruseni blokujiciho cteni (viz CancelRead). Volatile - nastavuje jine
        // vlakno (Stop senzoru) nez cte cteci smycka.
        private volatile bool readCancel;


        /// <summary>
        /// konstruktor
        /// </summary>
        /// <param name="device">Zarizeni napr. /dev/ttyS0</param>
        /// <param name="baudRate">prenosova rychlost</param>
        /// <param name="newLine">Odradkovani</param>
        public Uart(string name, string device, int? baudRate = null, string newLine = "\r\n")
        {
            this.name = name;
            try
            {
                Encoding encoding = Encoding.ASCII;
                this.device = device;
                //            Process.Start(new ProcessStartInfo("/bin/stty", string.Format("-F {0} {1} -echo -inlcr -icrnl", device, baudRate)));

                if (baudRate.HasValue)
                    sp = new SerialPort(device, baudRate.Value);
                else
                    sp = new SerialPort(device);

                sp.DataBits = 8;
                sp.Parity = Parity.None;
                sp.StopBits = StopBits.One;
                sp.Handshake = Handshake.None;
                sp.NewLine = newLine;
                ReOpen();
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Could not open UART {0} ({1}).", name, device), ex);
            }
        }
        /// <summary>
        /// Zda je uart otevren
        /// </summary>
        public bool IsOpen
        {
            get
            {
                return sp.IsOpen;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~Uart()
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
                    if (sp != null)
                    {
                        if (sp.IsOpen)
                            sp.Close();
                        sp.Dispose();
                    }
                }
            }
            disposed = true;
        }

        /// <summary>
        /// Read timeout in ms
        /// </summary>
        public int ReadTimeout
        {
            get
            {
                return sp.ReadTimeout;
            }
            set
            {
                sp.ReadTimeout = value;
            }
        }

        /// <summary>
        /// Number of bytes in imput buffer
        /// </summary>
        public int BytesToRead
        {
            get
            {
                return sp.BytesToRead;
            }
        }

        private bool ReOpen()
        {
            if (sp.IsOpen)
                return true;

            // Neblokujici backoff: mezi neuspesnymi pokusy o otevreni nedelame nic (hned
            // vracime false), misto abychom spali na volajicim vlakne. Throtlovani cteci
            // smycky resi volajici (Read/Process idle-sleep).
            DateTime now = DateTime.UtcNow;
            if ((now - lastOpenAttempt).TotalMilliseconds < ReopenBackoffMs)
                return false;
            lastOpenAttempt = now;

            try
            {
                sp.Open();
            }
            catch (Exception ex)
            {
                ReportEx(ex);
            }
            return sp.IsOpen;
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
            try
            {
                if(ReOpen())
                    return sp.Read(buffer, offset, count);
                return 0;
            }
            catch (Exception ex)
            {
                ReportEx(ex);
            }
            return 0;
        }

        /// <summary>
        /// Read async
        /// </summary>
        /// <param name="count">Bytes to read</param>
        /// <returns></returns>
        public async Task<byte[]> ReadAsync(int count)
        {
            byte[] bytes = new byte[count];
            int idx = 0;
            readCancel = false;   // novy pozadavek na cteni
            //Logger.WriteLine(count);
            while (idx < count)
            {
                // Kooperativni zruseni (viz CancelRead) - odblokuje visici cteni pri Stop().
                if (readCancel)
                    throw new OperationCanceledException("UART read cancelled.");

                int len = sp.BytesToRead;
                if (len > 0)
                {
                    len = Read(bytes, idx, Math.Min(count, len));
                    if (len > 0)
                        idx += len;
                    //                  Logger.WriteLine(idx);
                }
                else
                    await Task.Delay(10);
            }
            return bytes;
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
            //            Logger.WriteLine("Read");
            //          Logger.WriteLine(count);
            while (idx < count)
            {
                // Kooperativni zruseni: umoznuje Stop() senzoru odblokovat visici cteni na
                // nedostupnem portu (jinak by se tato smycka tocila donekonecna).
                if (readCancel)
                    throw new OperationCanceledException("UART read cancelled.");

                if (ReOpen())
                {
                    int len = sp.BytesToRead;
                    if (len > 0)
                    {
                        len = Read(bytes, idx, Math.Min(count - idx, len));
                        if (len > 0)
                            idx += len;
                        //                    Logger.WriteLine(idx);
                    }
                    else
                        // Port otevren, ale zadna data - kratky spanek misto busy-waitu.
                        Thread.Sleep(10);
                }
                else
                    // Port neni dostupny: throtlovani, aby cteci vlakno nebusy-spinovalo
                    // (ReOpen uz sam nespi). Otevreni se stejne zkusi az za ReopenBackoffMs.
                    Thread.Sleep(ReopenBackoffMs);
            }
            return bytes;
        }

        /// <summary>
        /// Read line
        /// </summary>
        /// <returns></returns>
        public string ReadLine()
        {
            try
            {
                if (ReOpen())
                    return sp.ReadLine().Replace("\x00", "");
            }
            catch (Exception ex)
            {
                ReportEx(ex);
            }
            return null;
        }

        /// <summary>
        /// Read all
        /// </summary>
        /// <returns></returns>
        public string ReadAll()
        {
            try
            {
                if (ReOpen())
                    return sp.ReadExisting();
            }
            catch (Exception ex)
            {
                ReportEx(ex);
            }
            return null;
        }

        /// <summary>
        /// Read line async
        /// </summary>
        /// <returns></returns>
        public async Task<string> ReadLineAsync()
        {
            return await Task.Run(() =>
            {
                return ReadLine();
            });
            /*
                        StringBuilder sb = new StringBuilder();
                        char[] ch = new char[1];

                        while (!sb.ToString().EndsWith(sp.NewLine))
                        {
                            int len = sp.BytesToRead;
                            if (len > 0)
                            {
                                if (sp.Read(ch, 0, 1) > 0)
                                    sb.Append(ch[0]);
                            }
                            else
                                await Task.Delay(10);
                        }
                        return sb.ToString();*/
        }

        /// <summary>
        /// Writes bytes to uart
        /// </summary>
        /// <param name="buffer"></param>
        public void Write(byte[] buffer)
        {
            try
            {
                if (ReOpen())
                    sp.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                ReportEx(ex);
            }
        }

        /// <summary>
        /// Writes line
        /// </summary>
        /// <param name="txt"></param>
        public void WriteLine(string txt)
        {
            try
            {
                if (ReOpen())
                    sp.WriteLine(txt);
            }
            catch (Exception ex)
            {
                ReportEx(ex);
            }
        }

        /// <summary>
        /// Kooperativne zrusi probihajici blokujici cteni (viz <see cref="IUart.CancelRead"/>).
        /// </summary>
        public void CancelRead()
        {
            readCancel = true;
        }

        private void ReportEx(Exception ex)
        {
            Debug.WriteLine(string.Format("{0} ({1}): {2}", name, sp.PortName, ex.ToString()));
        }
    }
}
