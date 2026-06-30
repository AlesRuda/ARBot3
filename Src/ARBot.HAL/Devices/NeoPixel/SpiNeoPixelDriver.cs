using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using ARBot.Common.Common;

namespace ARBot.HAL.Devices.NeoPixel
{
    public abstract class SpiNeoPixelDriver : INeoPixelDriver
    {
        public class PulseConfig
        {
            public int T0H;
            public int T1H;
            public int T0L;
            public int T1L;
        }

        PulseConfig config;

        List<byte> bytes = new List<byte>();
        byte b = 0;
        int idx = 7;

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="spi"></param>
        /// <param name="clockDivisor"></param>
        /// <param name="config"></param>
        /// <param name="cs"></param>
        public SpiNeoPixelDriver(PulseConfig config)
        {
            this.config = config;

            if (config.T0H + config.T0L != config.T1H + config.T1L)
                throw new Exception("Musi platit t0h+t0l==t1h+t1l.");
        }

        private void Reset()
        {
            bytes.Clear();
            b = 0;
            idx = 7;
        }

        private void Flush()
        {
            bytes.Add(b);
            idx = 7;
            b = 0;
        }

        private void WriteBit(bool bit)
        {
            b |= (byte)(bit ? (1 << idx) : 0);
            if (idx == 0)
                Flush();
            else
                idx--;
        }

        private void WriteBit(int h, int l)
        {
            for (int i = 0; i < h; i++)
                WriteBit(true);
            for (int i = 0; i < l; i++)
                WriteBit(false);
        }

        public void Send(Color[] values)
        {
            List<int> l = new List<int>();
            for (int i = 0; i < values.Length; i++)
            {
                Color c = values[i];
                int val = (((int)c.G) << 16) + (((int)c.R) << 8) + c.B;
                l.Add(val);
            }
            Send(l.ToArray());
        }

        /// <summary>
        /// Odesila GRB data do ledek
        /// </summary>
        /// <param name="values"></param>
        public void Send(int[] values)
        {
            Reset();
            foreach (int v in values)
            {
                for (int i = 23; i >= 0; i--)
                {
                    bool bit = (v & (1 << i)) != 0;

                    int l = bit ? config.T1L : config.T0L;
                    int h = bit ? config.T1H : config.T0H;

                    WriteBit(h, l);
                }
            }

            WriteData(bytes);
        }
        protected abstract void WriteData(List<byte> values);
    }
}
