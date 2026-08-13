using System;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Simulation;
using ARBot.HAL.NMEA;

namespace ARBot.HAL.Devices.GPSs
{
    /// <summary>
    /// Virtualni GPS - misto prijimace cte skutecnou polohu ze <see cref="SimulatedRobot"/>
    /// a hlasi ji zasumenou (viz doc/virtual-hw.md).
    /// </summary>
    public sealed class VirtualGps : SensorBase<GPSState>, IGPS
    {
        private readonly SimulatedRobot robot;
        private readonly GeoReference origin;
        private readonly VirtualSensorOptions options;
        private readonly int periodMs;

        /// <summary>Poradi vzorku - vstup do sumu (reprodukovatelnost).</summary>
        private int sample;

        private DateTime nextSampleAt = DateTime.MinValue;

        /// <inheritdoc/>
        public override string Name => "VirtualGPS";

        /// <param name="robot">Ground truth, ze ktereho se cte skutecna poloha.</param>
        /// <param name="origin">Pocatek lokalni ENU roviny - tentyz, se kterym pocita fuze.</param>
        /// <param name="options">Sum a frekvence; null = vychozi.</param>
        public VirtualGps(SimulatedRobot robot, GeoReference origin, VirtualSensorOptions options = null)
        {
            this.robot = robot ?? throw new ArgumentNullException(nameof(robot));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.options = options ?? new VirtualSensorOptions();

            periodMs = Math.Max(1, 1000 / Math.Max(1, this.options.GpsRateHz));
            Start();
        }

        /// <summary>
        /// Vzorek polohy. Sum se prida v METRECH v lokalni rovine a teprve pak se prevadi na LLA -
        /// je to prirozenejsi nez sumet ve stupnich (kde by mel jiny fyzicky vyznam v zemepisne
        /// sirce a delce).
        /// </summary>
        protected override GPSState GetMeasurement()
        {
            WaitForNextTick();

            var ts = TimeBase.Now;
            robot.Advance(ts);
            robot.Read(out double x, out double y, out _, out _, out _, out _, out _);

            int n = sample++;
            if (options.GpsPositionNoiseM > 0)
            {
                x += DeterministicNoise.Gaussian(options.Seed, n, ChannelX) * options.GpsPositionNoiseM;
                y += DeterministicNoise.Gaussian(options.Seed, n, ChannelY) * options.GpsPositionNoiseM;
            }

            double speed = robot.Speed;
            if (options.GpsSpeedNoiseMps > 0)
                speed += DeterministicNoise.Gaussian(options.Seed, n, ChannelSpeed) * options.GpsSpeedNoiseMps;

            var lla = origin.ToLLA(x, y);

            // POZOR NA JEDNOTKY: GPSState drzi STUPNE, LLA radiany (mapper na to ma varovani -
            // zamena znamena posun o stovky kilometru bez jedineho hlaseni).
            return new GPSState
            {
                Latitude = Conversions.Rad2Deg(lla.Latitude),
                Longitude = Conversions.Rad2Deg(lla.Longitude),
                Altitude = lla.Altitude,
                Quality = GPSState.FixQuality.GpsFix,
                NumberOfSatellites = options.GpsSatellites,
                Speed = speed,
                TimeStamp = ts,
            };
        }

        private const int ChannelX = 0;
        private const int ChannelY = 1;
        private const int ChannelSpeed = 2;

        /// <summary>Pocka do casu dalsiho vzorku (drzi zadanou frekvenci).</summary>
        private void WaitForNextTick()
        {
            var now = DateTime.UtcNow;
            if (nextSampleAt == DateTime.MinValue)
            {
                nextSampleAt = now;
                return;
            }

            nextSampleAt = nextSampleAt.AddMilliseconds(periodMs);
            var wait = nextSampleAt - now;
            if (wait > TimeSpan.Zero)
                Thread.Sleep(wait);
            else if (wait < TimeSpan.FromMilliseconds(-5 * periodMs))
                nextSampleAt = now;
        }
    }
}
