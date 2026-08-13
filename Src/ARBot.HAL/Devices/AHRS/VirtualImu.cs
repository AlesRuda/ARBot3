using System;
using System.Numerics;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.Common.Simulation;

namespace ARBot.HAL.Devices.AHRS
{
    /// <summary>
    /// Virtualni IMU - misto jednotky cte skutecny kurz a uhlovou rychlost ze
    /// <see cref="SimulatedRobot"/> a hlasi je zasumene (viz doc/virtual-hw.md).
    /// </summary>
    public sealed class VirtualImu : SensorBase<IMUState>, IIMU
    {
        private readonly SimulatedRobot robot;
        private readonly VirtualSensorOptions options;
        private readonly int periodMs;

        /// <summary>Poradi vzorku - vstup do sumu (reprodukovatelnost).</summary>
        private int sample;

        private DateTime nextSampleAt = DateTime.MinValue;

        /// <inheritdoc/>
        public override string Name => "VirtualIMU";

        /// <param name="robot">Ground truth, ze ktereho se cte skutecna orientace.</param>
        /// <param name="options">Sum a frekvence; null = vychozi.</param>
        public VirtualImu(SimulatedRobot robot, VirtualSensorOptions options = null)
        {
            this.robot = robot ?? throw new ArgumentNullException(nameof(robot));
            this.options = options ?? new VirtualSensorOptions();

            periodMs = Math.Max(1, 1000 / Math.Max(1, this.options.ImuRateHz));
            Start();
        }

        /// <summary>
        /// Vzorek orientace. Kvaternion se sklada pres <see cref="YawPitchRoll.ToQuaternion"/>
        /// se stejnou Euler konvenci (<c>zxy</c>), jakou pouziva <see cref="IMUState.YPR"/> pri
        /// zpetnem prevodu - jinak by kurz z kvaternionu vysel jiny, nez jaky do nej vstoupil.
        /// </summary>
        protected override IMUState GetMeasurement()
        {
            WaitForNextTick();

            var ts = TimeBase.Now;
            robot.Advance(ts);

            double heading = robot.Theta;
            double omega = robot.AngularSpeed;

            int n = sample++;
            if (options.ImuHeadingNoiseRad > 0)
                heading += DeterministicNoise.Gaussian(options.Seed, n, ChannelHeading) * options.ImuHeadingNoiseRad;
            if (options.ImuGyroNoiseRad > 0)
                omega += DeterministicNoise.Gaussian(options.Seed, n, ChannelGyro) * options.ImuGyroNoiseRad;

            var ypr = new YawPitchRoll((float)heading, 0f, 0f);

            return new IMUState
            {
                Rotation = ypr.ToQuaternion(YawPitchRoll.Euler.zxy),
                // Gyro je v BODY framu; u planarniho robotu je yaw rate slozka Z.
                AngularVelocity = new Vector3(0f, 0f, (float)omega),
                OrientationUncertainty = new Vector3((float)options.ImuHeadingNoiseRad, 0f, 0f),
                TimeStamp = ts,
            };
        }

        private const int ChannelHeading = 0;
        private const int ChannelGyro = 1;

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
