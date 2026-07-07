using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using ARBot.HAL.Devices.Spis;

namespace ARBot.HAL.Devices.NeoPixel
{
    /// <summary>
    /// Ridi pasek neopixel pomoci SPI sbernice. Vyuzivat FTCSPI.DLL
    /// </summary>
    public class FTDISpiNeoPixelDriver : SpiNeoPixelDriver
    {
        private static Dictionary<uint, PulseConfig> divisor2configs = new Dictionary<uint, PulseConfig>();

        static FTDISpiNeoPixelDriver()
        {
            divisor2configs.Add(0, new PulseConfig() { T0H = 12, T1H = 24, T0L = 26, T1L = 14 });
            divisor2configs.Add(1, new PulseConfig() { T0H = 6, T1H = 12, T0L = 13, T1L = 7 });
            divisor2configs.Add(2, new PulseConfig() { T0H = 4, T1H = 8, T0L = 9, T1L = 5 });
            divisor2configs.Add(3, new PulseConfig() { T0H = 3, T1H = 6, T0L = 6, T1L = 3 });
            divisor2configs.Add(5, new PulseConfig() { T0H = 2, T1H = 4, T0L = 4, T1L = 2 });
            divisor2configs.Add(7, new PulseConfig() { T0H = 2, T1H = 3, T0L = 3, T1L = 2 });
            divisor2configs.Add(10, new PulseConfig() { T0H = 1, T1H = 2, T0L = 2, T1L = 1 });
            divisor2configs.Add(11, new PulseConfig() { T0H = 1, T1H = 2, T0L = 2, T1L = 1 });
            divisor2configs.Add(12, new PulseConfig() { T0H = 1, T1H = 2, T0L = 2, T1L = 1 });
        }


        FTDISpi spi;
        FTDISpi.ChipSelectPin cs;
        uint clockDivisor;

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="spi"></param>
        /// <param name="clockDivisor"></param>
        /// <param name="config"></param>
        /// <param name="cs"></param>
        public FTDISpiNeoPixelDriver(FTDISpi spi, uint clockDivisor, PulseConfig config, FTDISpi.ChipSelectPin cs) : base(config)
        {
            if (spi == null)
                throw new ArgumentNullException("spi");
            this.spi = spi;
            this.clockDivisor = clockDivisor;
            this.cs = cs;
        }
        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="spi"></param>
        /// <param name="cs"></param>
        public FTDISpiNeoPixelDriver(FTDISpi spi, FTDISpi.ChipSelectPin cs) : base(divisor2configs[spi.ClockDivisor])
        {
            if (spi == null)
                throw new ArgumentNullException("spi");
            this.spi = spi;
            this.cs = cs;
            this.clockDivisor = spi.ClockDivisor;
        }

        protected override void WriteData(List<byte> values)
        {

            int br = 240;
            byte[] control = values.Take(br).ToArray();
            byte[] data = values.Skip(br).ToArray();
            
  /*          byte[] control = new byte[] { 0, 0 };
            byte[] data = values.ToArray();
    */        
            using (FTDISpiToken t = spi.GetToken(true, clockDivisor))
            {
                spi.Write(t, new FTDISpi.InitCondition() { ChipSelectPin = cs, ChipSelectPinState = false, ClockPinState = false, DataOutPinState = false },
                    true, false, (uint)(8 * control.Length), control, (uint)(8 * data.Length), data,
                    new FTDISpi.WaitDataWrite() { WaitDataWriteComplete = false, DataWriteTimeoutmSecs = 100000 });
            }
        }
    }
}
