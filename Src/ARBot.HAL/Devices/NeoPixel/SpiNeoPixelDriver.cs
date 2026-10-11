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

            /// <summary>
            /// Nulove bajty PRED daty snimku (MOSI v nule). Chyba na zacatku prenosu SPI pak padne do
            /// nul, kde zadny puls neni, a pred daty je zaruceny reset WS2812 (&gt; 280 us) i kdyby
            /// linka mezi prenosy nestala v nule. 0 = data hned od prvniho bajtu (puvodni chovani).
            /// </summary>
            public int LeadingZeroBytes;

            /// <summary>
            /// Casovani pro Orange Pi (SPI4, <see cref="OrangePiClockHz"/>): 4 sub-bity na bit WS2812,
            /// tedy DVA bity v jednom bajtu. Rockchip dela z 3,2 MHz 3,125 MHz (200 MHz / 64), sub-bit
            /// 320 ns: nula 1+3 (320 ns vysoko / 960 nizko), jednicka 2+2 (640 / 640), bit 1,28 us.
            ///
            /// <para>⚠️ Do 11. 10. 2026 to bylo 6,4 MHz a 8 sub-bitu (nula 2+6, jednicka 5+3) a pasek
            /// obcas problikl jinou barvou - vzdy od PRVNI LED a pokazde na jinem poctu LED. To je
            /// podpis mezery uprostred snimku: SPI na chvili zastavi hodiny, WS2812 to vezme jako
            /// reset a zbytek snimku zapise znovu od zacatku, posunuty o par bitu. Jednicka 4+4 pri
            /// 6,4 MHz chybu jen presunula (casovani to tedy neni), polovicni datovy tok ji odstranil
            /// - vykladame to tak, ze DMA pri 6,25 MHz nestihalo plnit FIFO radice (osciloskopem
            /// mezera chycena neni). Zmereno na robotu, viz OrangePi5Ultra/POSTUP.md.</para>
            ///
            /// <para>⚠️ Bez <see cref="LeadingZeroBytes"/> (data hned od prvniho bajtu) PRVNI LED obcas
            /// „zustala cervena" - pri animaci KnightRider svitila jasne, kdyz uz mela dohasinat.
            /// Sedi to na ztraceny prvni bit snimku: prvni LED pak cte sva data posunuta o bit, tedy
            /// dvojnasobny jas cervene (0x7F -&gt; 0xFE), kdezto u ostatnich (vetsinou zhasnutych) je
            /// tentyz posun neviditelny. 120 bajtu = 307 us pri 3,125 MHz.</para>
            /// </summary>
            public static PulseConfig OrangePi => new PulseConfig { T0H = 1, T0L = 3, T1H = 2, T1L = 2, LeadingZeroBytes = 120 };
        }

        /// <summary>Hodinovy kmitocet SPI k <see cref="PulseConfig.OrangePi"/> (realne 3,125 MHz).</summary>
        public const int OrangePiClockHz = 3_200_000;

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
            for (int i = 0; i < config.LeadingZeroBytes; i++)
                bytes.Add(0);
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
