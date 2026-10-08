using System;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Devices;

namespace ARBot.HAL.Devices.Bms
{
    /// <summary>
    /// Simulovaná BMS: hlásí stav nabití a proud z <see cref="VirtualSensorOptions"/> (panel
    /// <i>Virtuální senzory</i>), aby šla stránka náhledu a varování proklikat bez hardwaru.
    /// Napětí článků je jen ilustrační (lineární v procentech), ne model LiFePO4.
    /// </summary>
    public sealed class VirtualBms : SensorBase<BmsState>, IBms
    {
        private readonly VirtualSensorOptions options;
        private readonly int periodMs;

        /// <inheritdoc/>
        public override string Name => "VirtualBms";

        public VirtualBms(VirtualSensorOptions options = null, int periodMs = 1000)
        {
            this.options = options ?? new VirtualSensorOptions();
            this.periodMs = Math.Max(1, periodMs);
            Start();
        }

        /// <inheritdoc/>
        protected override BmsState GetMeasurement()
        {
            for (int cekano = 0; cekano < periodMs && !stopRequired; cekano += 10)
                Thread.Sleep(Math.Min(10, periodMs));
            if (stopRequired || !options.BmsPresent) return null;

            double soc = Math.Clamp(options.BmsSocPercent, 0, 100);
            double clanek = 3.0 + 0.0033 * soc;
            return new BmsState
            {
                TimeStamp = TimeBase.Now,
                PackVoltage = 4 * clanek,
                Current = options.BmsCurrentA,
                NominalAh = 15,
                RemainingAh = 15 * soc / 100,
                SocPercent = (int)Math.Round(soc),
                CellVoltages = new[] { clanek, clanek, clanek, clanek },
                Temperatures = new[] { 22.0 },
                ChargeFetOn = true,
                DischargeFetOn = true,
            };
        }
    }
}
