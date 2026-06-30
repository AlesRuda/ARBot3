using ARBot.Common.Common;
using ARBot.Common.Navigations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.Lidar
{
    public class RPLidarA2 : ILidar
    {
        public IEnumerable<BlindRegion> BlindRegions { get { return new List<BlindRegion>(); } }

        public event EventHandler<ScanReceivedEventArgs> ScanReceived;
        public List<Ray> Samples;
        public DateTime Time;
        public List<Ray> tempSamples;
        private Task processTask;
        private CancellationToken cancellationToken;
        private CancellationTokenSource cancellationTokenSource;
        private double off=0;

        public enum SendMode
        {
            SingleResponse = 0,
            MultipleResponse = 1,
            Reserve1 = 2,
            Reserve2 = 3
        }
        public enum HealthStatus
        {
            Good = 0,
            Warning = 1,
            Error = 2
        }
        public struct HealthResponse
        {
            public HealthStatus Status;
            public int ErrorCode;
        }
        public struct Cabin
        {
            public int Distance;
            public double dFi;
        }
        public struct SampleRateResponse
        {
            public double Standard;
            public double Express;
        }
        public struct InfoResponse
        {
            public byte Model;
            public byte FirmwaMinor;
            public byte FirmwaMajor;
            public byte Hardware;
            public string SerialNumber;
        }
        public struct ResponseDescriptor
        {
            public int Length;
            public SendMode Mode;
            public byte DateType;
        }

        const byte StopCmd = 0x25;
        const byte ResetCmd = 0x40;
        const byte ScanCmd = 0x20;
        const byte PWMCmd = 0xF0;
        const byte ExpressScanCmd = 0x82;
        const byte ForceScanCmd = 0x21;
        const byte GetInfoCmd = 0x50;
        const byte GetHealth = 0x52;
        const byte GetSmapleRateCmd = 0x59;

        IUart uart;

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="uart">Uart</param>
        /// <param name="off">Lidar nativne ma 0 stupnu pred sebou (kdyz das lidar privodem k sobe). Tato hodnota bude prictena k uhlu lidaru.
        /// Hodnta je v radianech a matematickem smyslu.
        /// </param>
        public RPLidarA2(IUart uart, double off)
        {
            this.uart = uart;
            this.off = off;
        }

        public byte CheckSum(byte[] data)
        {
            byte sum = 0;
            foreach (byte b in data)
                sum ^= b;
            return sum;
        }

        private void SendRequest(byte req, byte[] payload)
        {
            byte[] data = new byte[payload.Length == 0 ? 3 : 4 + payload.Length];
            data[0] = 0xA5;
            data[1] = req;
            data[2] = (byte)payload.Length;
            if (payload.Length > 0)
                payload.CopyTo(data, 3);
            data[data.Length - 1] = CheckSum(data);
            uart.Write(data);
        }

        public void Stop()
        {
            Debug.WriteLine("Lidar stop");
            SendRequest(StopCmd, new byte[0]);
            SetPWM(0);
        }

        public void Reset()
        {
            SendRequest(ResetCmd, new byte[0]);
        }
/*
        public void Scan()
        {
            SendRequest(ScanCmd, new byte[0]);
        }
        */
        public void Scan()
        {
            SendRequest(ExpressScanCmd, new byte[5]);
            Process();
        }

        public void SetPWM(int pwm)
        {
            byte[] b = new byte[2];
            b[0] = (byte)(pwm & 0xff);
            b[1] = (byte)(pwm>>8);
            SendRequest(PWMCmd, b);
        }

        public void ForceScan()
        {
            SendRequest(ForceScanCmd, new byte[0]);
        }

        public HealthResponse Health()
        {
            SendRequest(GetHealth, new byte[0]);
            var r = ReadResponseDescriptor();
            if (r.DateType != 0x06)
                throw new Exception("Neocekavany response.");
            return GetHealthResponse();
        }

        public SampleRateResponse SampleRate()
        {
            SendRequest(GetSmapleRateCmd, new byte[0]);
            var r = ReadResponseDescriptor();
            if (r.DateType != 0x15)
                throw new Exception("Neocekavany response.");
            return GetSampleRateResponse();
        }

        public InfoResponse Info()
        {
            SendRequest(GetInfoCmd, new byte[0]);
            var r = ReadResponseDescriptor();
            if (r.DateType != 0x04)
                throw new Exception("Neocekavany response.");
            return GetInfoResponse();
        }

        private void Sync()
        {
            byte[] b = new byte[1];
            do
            {
                while (b.Length > 0 && b[0] != 0xa5)
                {
                    b = uart.Read(1);
                }
                b = uart.Read(1);
            }
            while (b.Length > 0 && b[0] != 0x5a);
        }

        private ResponseDescriptor ReadResponseDescriptor()
        {
            Sync();
            byte[] b;
            b = uart.Read(5);
            int l = b[3] & 0x3f;
            l >>= 8;
            l += b[2];
            l >>= 8;
            l += b[1];
            l >>= 8;
            l += b[0];
            return new ResponseDescriptor() { Length = l, Mode = (SendMode)(b[3] >> 6), DateType = b[4] };
        }

        private HealthResponse GetHealthResponse()
        {
            byte[] b;
            b = uart.Read(3);
            return new HealthResponse() { Status = (HealthStatus)b[0], ErrorCode = ((int)b[0]) + (((int)b[1]) << 8) };
        }

        private SampleRateResponse GetSampleRateResponse()
        {
            byte[] b;
            b = uart.Read(4);
            return new SampleRateResponse() { Standard = (double)(((int)b[0]) + (((int)b[1]) << 8))/1000000.0, Express = (double)(((int)b[2]) + (((int)b[3]) << 8)) / 1000000.0 };
        }

        private InfoResponse GetInfoResponse()
        {
            byte[] b;
            b = uart.Read(20);
            string sn = "";
            for (int i = 0; i < 16; i++)
                sn += string.Format("{0:x02}", b[i]);

            return new InfoResponse() { Model = b[0], FirmwaMinor=b[1], FirmwaMajor=b[2], Hardware=b[3], SerialNumber=sn};
        }

        private void DecodeCabins(Cabin[] c, int cIdx,  byte[] data, int idx)
        {
            int distance1 = (((int)data[idx + 1]) << 6) + (data[idx] >> 2);
            double dFi1 = ((double)((((data[idx] & 3) << 4) + data[idx + 4] & 0xf))) / 8.0;

            int distance2 = (((int)data[idx + 3]) << 6) + (data[idx+2] >> 2);
            double dFi2 = ((double)((((data[idx+2] & 3) << 4) + data[idx + 4] >>4))) / 8.0;

            c[cIdx]=new Cabin() { Distance = distance1, dFi = dFi1 };
            c[cIdx+1] =new Cabin() { Distance = distance2, dFi = dFi2 };
        }

        private void OnScanReceived(List<Ray> l, DateTime startTime, DateTime endTime)
        {
            if (ScanReceived != null)
                ScanReceived(this, new ScanReceivedEventArgs() { Samples=l, StartTime=startTime, EndTime=endTime });
        }

        private void CancelInternal()
        {
            if (processTask != null)
            {
                cancellationTokenSource.Cancel();

                try
                {
                    processTask.Wait();
                }
                finally
                {
                    cancellationTokenSource.Dispose();
                }
                cancellationTokenSource = null;
                processTask = null;
            }

        }

        public void Cancel()
        {
            lock (this)
            {
                CancelInternal();
            }
        }
        public void Process()
        {
            lock (this)
            {
                CancelInternal();
                cancellationTokenSource = new CancellationTokenSource();
                cancellationToken = cancellationTokenSource.Token;
                processTask = new Task(() =>
                  {
                      ProcessResponses();
                  }, cancellationToken);
                processTask.Start();
            }
        }
        private void ProcessResponses()
        {
            while(!cancellationToken.IsCancellationRequested)
            {
                ResponseDescriptor r=ReadResponseDescriptor();
                if (r.DateType==0x82)
                {
                    DateTime endTime = TimeBase.Now;
                    float? lastAngle = null;
                    float lastAlfa =0;
                    tempSamples = new List<Ray>();
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        byte[] b = uart.Read(1);
                        if ((b[0] & 0xf0) == 0xA0)
                        {
                            b = uart.Read(1);
                            if ((b[0] & 0xf0) == 0x50)
                            {
                                b = uart.Read(r.Length - 2);
                                Cabin[] cabins = new Cabin[32];

                                byte checkSum = CheckSum(b);

                                float startAngle = (float)Conversions.Deg2Rad((double)(((int)b[0]) + ((((int)b[1] & 0x7f)) << 8)) / 64.0);
                                if (lastAngle != null)
                                {
                                    float fi = (startAngle >= lastAngle ? (startAngle - lastAngle.Value) : startAngle - lastAngle.Value + MathF.PI * 2) / 32.0f;
                                    for (int i = 0; i < 16; i++)
                                        DecodeCabins(cabins, i * 2, b, i * 5 + 2);

                                    for (int i = 0; i < 32; i++)
                                    {
                                        float alfa = startAngle + fi * i - (float)Conversions.Deg2Rad(cabins[i].dFi);

                                        if (alfa > MathF.PI * 2 || lastAngle>startAngle)
                                        {
                                            if (alfa > MathF.PI * 2)
                                            {
                                                startAngle -= MathF.PI * 2;
                                                lastAngle-= MathF.PI * 2;
                                            }
                                            else
                                                lastAngle = startAngle;

                                            alfa -= MathF.PI * 2;

                                            DateTime dt = TimeBase.Now;

                                            DateTime dt1 = endTime;

                                            TimeSpan ts = new TimeSpan((dt - endTime).Ticks / (tempSamples.Count + 32 - i));

                                            foreach (Ray ray in tempSamples)
                                            {
                                                ray.TimeStamp = dt1;
                                                dt1 += ts;
                                            }

                                            OnScanReceived(tempSamples, endTime, dt1);
                                            endTime = dt1;
                                            tempSamples = new List<Ray>();
                                        }

                                        lastAlfa = alfa;
                                        tempSamples.Add(new Ray() { Angle = (float)Conversions.NormalizeOrientation( - alfa+off), Distance = cabins[i].Distance != 0 ? cabins[i].Distance / 1000.0f : (float?)null });

                                    }
                                }

                                lastAngle = startAngle;
                            }
                        }
                    }
                }
            }
        }
    }
}
