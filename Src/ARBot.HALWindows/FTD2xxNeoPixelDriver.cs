using FTD2XX_NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ARBot.HAL.Devices.NeoPixel
{
    public class FTD2xxNeoPixelDriver : SpiNeoPixelDriver
    {
        public static FTDI.FT_DEVICE_INFO_NODE[] GetDeviceList(FTDI f)
        {
            uint cnt = 0;
            var ret = f.GetNumberOfDevices(ref cnt);
            if (ret != FTDI.FT_STATUS.FT_OK)
                return null;

            FTDI.FT_DEVICE_INFO_NODE[] devList = new FTDI.FT_DEVICE_INFO_NODE[cnt];
            ret = f.GetDeviceList(devList);
            if (ret != FTDI.FT_STATUS.FT_OK)
                return null;

            return devList;
        }
        static byte[] ReadAll(FTDI f)
        {
            uint len = 0;
            var status = f.GetRxBytesAvailable(ref len);
            byte[] inputBuffer = new byte[len];

            if (len > 0)
            {
                uint rLen = 0;
                f.Read(inputBuffer, len, ref rLen);
            }
            return inputBuffer;
        }

        static void SendSimpleCommand(FTDI f, byte cmd)
        {
            var b = new byte[1];
            b[0] = cmd;
            uint bytesWriten = 0;
            var ret = f.Write(b, 1, ref bytesWriten);
        }
        static void Send2ParCommand(FTDI f, byte cmd, byte par1, byte par2)
        {
            var b = new byte[3];
            b[0] = cmd;
            b[1] = par1;
            b[2] = par2;
            uint bytesWriten = 0;
            var ret = f.Write(b, 3, ref bytesWriten);
        }
        static void SetClock(FTDI f, int divisor)
        {
            Send2ParCommand(f, 0x86, (byte)(divisor & 0xff), (byte)(divisor >> 8));
        }
        static void SetDataBitsLow(FTDI f, byte val, byte dir)
        {
            Send2ParCommand(f, 0x80, val, dir);
        }

        static void SetHiSppedClock(FTDI f)
        {
            SendSimpleCommand(f, 0x8A);
        }

        static void DisableLoopBack(FTDI f)
        {
            SendSimpleCommand(f, 0x85);
        }

        static void EnableLoopBack(FTDI f)
        {
            SendSimpleCommand(f, 0x84);
        }


        static bool Synchronize(FTDI f)
        {
            byte[] inputBuffer = ReadAll(f);

            EnableLoopBack(f);

            inputBuffer = ReadAll(f);

            if (inputBuffer.Length > 0)
                return false;

            SendSimpleCommand(f, 0xAB);

            Thread.Sleep(1);

            inputBuffer = ReadAll(f);

            DisableLoopBack(f);
            if (inputBuffer.Length == 2 && inputBuffer[0] == 0xfa && inputBuffer[1] == 0xab)
            {
                inputBuffer = ReadAll(f);

                if (inputBuffer.Length == 0)
                    return true;
            }

            return false;
        }


        FTDI f;
        public FTD2xxNeoPixelDriver(FTDI f, FTDI.FT_DEVICE_INFO_NODE node) : base(new PulseConfig() { T0H = 1, T1H = 2, T0L = 2, T1L = 1 })
        {
            this.f = f;
            var ret = f.OpenByLocation(node.LocId);
            if (ret != FTDI.FT_STATUS.FT_OK)
                return;

            //SPI_InitDevice
            ret = f.ResetDevice();
            ret = f.InTransferSize(65536);
            ret = f.SetCharacters(0, false, 0, false);
            ret = f.SetTimeouts(0, 5000);
            ret = f.SetLatency(32);
            ret = f.SetBitMode(0, 0);
            ret = f.SetBitMode(0, 2);

            Synchronize(f);

            DisableLoopBack(f);

            SetDataBitsLow(f, 0, 0xfb);

            //SPI_TurnOffDivideByFiveClockingHiSpeedDevice
            SetHiSppedClock(f);

            //SPI_SetClock
            SetClock(f, 12);
        }

        protected override void WriteData(List<byte> values)
        {
            byte[] rd;
            do
            {
                rd = ReadAll(f);
            } while (rd.Length > 0);

            var len = values.Count - 1;
            if (len >= 0)
            {
                var l = new List<byte>(values.Count + 3);
                l.Add(0x10);
                l.Add((byte)(len & 0xff));
                l.Add((byte)((len >> 8) & 0xff));
                l.AddRange(values);

                uint bytesWriten = 0;
                var ret = f.Write(l.ToArray(), l.Count, ref bytesWriten);
            }
        }
    }
}
