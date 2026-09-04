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

        // ---------------------------------------------------------------------------------
        // Vnitrni buffer prijmu pro Read(int).
        //
        // PROC: UBX parser cte po JEDNOM bajtu (UBXMessage.Parse vola Read(1) na kazdy bajt
        // hlavicky a preskakuje tak i vsechny NMEA vety). Puvodne kazdy takovy Read sahal na
        // port a kdyz zrovna nic nebylo, spal 10 ms - takze se za jedno probuzeni zpracoval
        // JEDEN bajt. Na u-bloxu, ktery posila 13 kB/s (200 NMEA vet/s + NAV-PVT 10 Hz),
        // se tim ztracelo ~92 % mereni: zmereno na zarizeni 31. 8. 2026, driver vytahl
        // 0,88 NAV-PVT/s misto 9,9/s. S timto bufferem 10,09/s (viz doc/decisions.md).
        //
        // Smycka a spanek zustaly stejne - zmenilo se jen to, ze se pri probuzeni vezme
        // VSECHNO, co je v portu, misto jednoho bajtu.
        //
        // POZOR: co lezi tady, uz v portu neni. Vsechny ostatni cteci metody proto musi
        // nejdriv vybrat tenhle buffer (viz TakeFromRx) - jinak by ReadLine() prectl radek,
        // kteremu chybi zacatek. Dnes zadny senzor styly nemicha (u-blox = Read(int),
        // VN100 = Read(buf,off,len), motor = ReadLine), ale tise by se to rozejit nemelo.
        private readonly byte[] rx = new byte[8192];
        private int rxHead, rxTail;
        private int RxCount => rxTail - rxHead;

        /// <summary>
        /// Vybere z vnitrniho bufferu az <paramref name="count"/> bajtu do
        /// <paramref name="buffer"/>. Vraci, kolik jich skutecne vzal (0 = buffer je prazdny).
        /// </summary>
        private int TakeFromRx(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(count, RxCount);
            if (n <= 0)
                return 0;
            Array.Copy(rx, rxHead, buffer, offset, n);
            rxHead += n;
            return n;
        }

        /// <summary>Natahne do vnitrniho bufferu vse, co je prave v portu. Vraci pocet bajtu.</summary>
        private int FillRx(int available)
        {
            rxHead = 0;
            rxTail = 0;
            int n = Read(rx, 0, Math.Min(rx.Length, available));
            if (n > 0)
                rxTail = n;
            return rxTail;
        }


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
            DateTime now = ARBot.Common.Common.TimeBase.Now;
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
            // Co uz je ve vnitrnim bufferu, v portu neni - musi se vydat drive nez novy zapis
            // z portu, jinak by se poradi bajtu prohodilo. Pri beznem pouziti (VN100) je
            // buffer trvale prazdny a tahle vetev se nikdy netrefi.
            int taken = TakeFromRx(buffer, offset, count);
            if (taken > 0)
                return taken;

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

                // Nejdriv z vnitrniho bufferu - tohle je cela pointa: pri volani Read(1)
                // v tesne smycce se uz na port nesaha, dokud se buffer nevyprazdni.
                int taken = TakeFromRx(bytes, idx, count - idx);
                if (taken > 0)
                {
                    idx += taken;
                    continue;
                }

                if (ReOpen())
                {
                    int len = sp.BytesToRead;
                    if (len > 0)
                        // Vezmi VSECHNO, co v portu je (drive se bralo jen count-idx bajtu,
                        // tj. pri Read(1) jediny bajt na jedno probuzeni).
                        FillRx(len);
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
                // Zbytek ve vnitrnim bufferu ma prednost (viz komentar u rx). Bezne je prazdny
                // - motor ani VN100 ASCII Read(int) nepouzivaji - a pak se chova jako drive.
                string pending = DrainRxAsText();
                if (pending != null)
                {
                    int nl = pending.IndexOf(sp.NewLine, StringComparison.Ordinal);
                    if (nl >= 0)
                    {
                        // radek cely v bufferu; zbytek vrat zpet pro dalsi cteni
                        string line = pending.Substring(0, nl);
                        PushBackRx(pending.Substring(nl + sp.NewLine.Length));
                        return line.Replace("\x00", "");
                    }
                    if (ReOpen())
                        return (pending + sp.ReadLine()).Replace("\x00", "");
                    return null;
                }

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
        /// Vybere cely vnitrni buffer jako ASCII text; <c>null</c>, kdyz je prazdny.
        /// Slouzi jen k tomu, aby se textova cteni nerozesla s <see cref="Read(int)"/>.
        /// </summary>
        private string DrainRxAsText()
        {
            int n = RxCount;
            if (n <= 0)
                return null;
            string s = Encoding.ASCII.GetString(rx, rxHead, n);
            rxHead = rxTail = 0;
            return s;
        }

        /// <summary>Vrati text zpet do vnitrniho bufferu (zbytek za koncem radku).</summary>
        private void PushBackRx(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            byte[] b = Encoding.ASCII.GetBytes(text);
            int n = Math.Min(b.Length, rx.Length);
            Array.Copy(b, 0, rx, 0, n);
            rxHead = 0;
            rxTail = n;
        }

        /// <summary>
        /// Read all
        /// </summary>
        /// <returns></returns>
        public string ReadAll()
        {
            try
            {
                // Vnitrni buffer patri pred to, co je jeste v portu (viz komentar u rx).
                string pending = DrainRxAsText();
                if (ReOpen())
                    return (pending ?? "") + sp.ReadExisting();
                return pending;
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
