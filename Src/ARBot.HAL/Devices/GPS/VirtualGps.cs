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

            double? course = Course(n);

            var lla = origin.ToLLA(x, y);

            // GPSState drzi RADIANY, tedy tutez jednotku jako LLA - zadny prevod (od 26. 8. 2026,
            // viz GPSState.Latitude). Driv tady byl Rad2Deg a byla to past pro kazdeho konzumenta.
            return new GPSState
            {
                Latitude = lla.Latitude,
                Longitude = lla.Longitude,
                Altitude = lla.Altitude,
                Quality = GPSState.FixQuality.GpsFix,
                NumberOfSatellites = options.GpsSatellites,
                Hdop = options.GpsHdop,
                Speed = speed,
                DynamicOrientation = course,
                TimeStamp = ts,
            };
        }

        /// <summary>
        /// Kurz nad zemi (course over ground) [rad], nebo <c>null</c> pri male rychlosti.
        ///
        /// <para><b>Je to DRUHA absolutni reference kurzu</b>, nezavisla na magnetometru — a proto
        /// tady vubec je (doplneno 25. 8. 2026). Bez ni ma fuze jedinou absolutni referenci
        /// (<c>IMU/heading</c>), takze bias kompasu nema proti cemu zmerit: namereno, ze pri
        /// <c>imubias=3</c> zustane chyba kurzu na 3,0 stupne bez ohledu na to, co dela korelace
        /// s mapou. Skutecny prijimac to hlasi (<c>NmeaGps</c> z VTG, <c>uBloxGps</c> jako
        /// <c>atan2</c> z vektoru rychlosti), simulace to dosud NEhlasila.</para>
        ///
        /// <para><b>Model sumu je fyzikalni, ne dohodnuty:</b> kurz se pocita z vektoru rychlosti,
        /// takze se zasumi PRICNA slozka rychlosti a uhel z ni vyjde sam. Nejistota tedy klesa
        /// s rychlosti (<c>sigma ≈ sigma_v / v</c>) — presne jak se chova skutecny prijimac.
        /// Konstantni „sum kurzu ve stupnich" by tuhle zavislost zahodil, a prave ona rozhoduje,
        /// jestli je z toho pouzitelna reference.</para>
        ///
        /// <para><b>Bere se PRAVY kurz robotu</b> (ground truth), ne odhad — jinak by to byla
        /// kruhova reference, tatáž past jako <c>camerapose=fusion</c> (viz doc/virtual-hw.md).
        /// Podvozek je diferencialni, takze se pohybuje ve smeru sve orientace; kurz nad zemi je
        /// tedy pravy kurz. Pri jizde vzad by se lisil o 180 stupnu — to simulace nedela, a kdyby
        /// zacala, patri to sem.</para>
        /// </summary>
        private double? Course(int n)
        {
            robot.Read(out _, out _, out double theta, out _, out _, out _, out _);
            double v = robot.Speed;

            // Pod prahem je atan2 ze sumu rovnomerne rozdeleny uhel, tedy cista dezinformace.
            if (Math.Abs(v) < options.GpsCourseMinSpeedMps) return null;

            if (options.GpsCrossTrackNoiseMps <= 0) return theta;

            // Sum se prida do PRICNE slozky; uhlova chyba z nej vyjde jako atan(dv/v).
            double cross = DeterministicNoise.Gaussian(options.Seed, n, ChannelCourse)
                           * options.GpsCrossTrackNoiseMps;
            return theta + Math.Atan2(cross, Math.Abs(v));
        }

        private const int ChannelX = 0;
        private const int ChannelY = 1;
        private const int ChannelSpeed = 2;

        /// <summary>Kanal sumu pricne slozky rychlosti (kurz). Vlastni kanal, aby zapnuti kurzu
        /// nezmenilo sum polohy ani rychlosti — jinak by A/B proti starsim behum neslo.</summary>
        private const int ChannelCourse = 3;

        /// <summary>Pocka do casu dalsiho vzorku (drzi zadanou frekvenci).</summary>
        private void WaitForNextTick()
        {
            var now = TimeBase.Now;
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
