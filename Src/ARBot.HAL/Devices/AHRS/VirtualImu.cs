using System;
using System.Numerics;
using MnMatrix = MathNet.Numerics.LinearAlgebra.Matrix<double>;
using MnVector = MathNet.Numerics.LinearAlgebra.Vector<double>;
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

            // Bias je SYSTEMATICKA chyba - na rozdil od sumu se neprumeruje pryc. Kurz posunuty
            // o konstantu je spatna kalibrace magnetometru, bias gyra se navic integruje do
            // rostouci chyby kurzu. Prave to ma hranova lokalizace lecit. Viz doc/virtual-hw.md.
            heading += options.ImuHeadingBiasRad;
            omega += options.ImuGyroBiasRadPerSec;

            var ypr = new YawPitchRoll((float)heading, 0f, 0f);

            // Pole, gravitace a naklon se odvozuji z JEDNE rotace (svet -> teleso). Dva nezavisle
            // vzorce jsou tu prokazatelne past: rozesly se a fit pak spravne hlasil rozptyl sklonu
            // 18,7 stupne u perfektni kalibrace (8. 9. 2026, viz doc/plan-vn100-kalibrace.md).
            var (poleIdeal, gravitace) = PoleAGravitace(heading, n);
            var poleRaw = VnutZelezo(poleIdeal, n);

            return new IMUState
            {
                Name = Name,   // puvodce mereni - v robotovi muze byt IMU vic (viz IMUState.Name)
                Rotation = ypr.ToQuaternion(YawPitchRoll.Euler.zxy),
                // Gyro je v BODY framu; u planarniho robotu je yaw rate slozka Z. Otaceni RUKOU
                // (SimulatedRobot.HandSpinRadPerSec) je uz soucasti robot.AngularSpeed - bez toho
                // by kolektor kalibrace, ktery pokryti pocita z integrovaneho gyra, stal.
                AngularVelocity = new Vector3(0f, 0f, (float)omega),
                Acceleration = gravitace,
                // Kompenzovane pole = surove: virtualni senzor zadnou palubni kompenzaci nedela.
                // Obe pole se plni schvalne, aby slo zkouset i cestu pro zaznamy formatu < 4.
                Magnetometer = poleRaw,
                MagnetometerRaw = poleRaw,
                OrientationUncertainty = new Vector3((float)options.ImuHeadingNoiseRad, 0f, 0f),
                TimeStamp = ts,
            };
        }

        /// <summary>
        /// Idealni pole a gravitace v ramci telesa pro dany kurz a nastaveny naklon.
        ///
        /// <para><see cref="ARBot.Common.Simulation.SimulatedRobot"/> je rovinny, takze naklon
        /// se nesimuluje — bere se <b>zadany</b> z <see cref="VirtualSensorOptions.TiltRad"/>
        /// (pri kalibraci se robot podklada, viz doc/plan-vn100-kalibrace.md).</para>
        /// </summary>
        private (Vector3 Pole, Vector3 Gravitace) PoleAGravitace(double heading, int n)
        {
            double b = options.MagFieldG, sklon = options.MagInclinationRad;
            var poleSvet = new Vector3((float)(b * Math.Cos(sklon)), 0f, (float)(-b * Math.Sin(sklon)));
            var gravitaceSvet = new Vector3(0f, 0f, -9.81f);

            var qKurz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-heading);
            var osa = new Vector3((float)Math.Cos(options.TiltDirRad + Math.PI / 2),
                                  (float)Math.Sin(options.TiltDirRad + Math.PI / 2), 0f);
            var qNaklon = Quaternion.CreateFromAxisAngle(osa, (float)-options.TiltRad);

            Vector3 DoTelesa(Vector3 v)
                => Vector3.Transform(Vector3.Transform(v, qKurz), qNaklon);

            return (DoTelesa(poleSvet), DoTelesa(gravitaceSvet));
        }

        /// <summary>
        /// Aplikuje VNUCENE zelezo: <c>m_raw = C⁻¹ · m_ideal + b</c> — tedy inverzi toho, co ma
        /// kalibrace najit. Bez zadaneho zeleza vrati pole nezmenene (jen se sumem).
        /// </summary>
        private Vector3 VnutZelezo(Vector3 ideal, int n)
        {
            var m = ideal;

            var s = options.MagSoftIron;
            if (s != null && (s.Length == 3 || s.Length == 6))
            {
                // Poradi xx, yy, zz, xy, xz, yz; 3 cisla = jen diagonala.
                double xx = s[0], yy = s[1], zz = s[2];
                double xy = s.Length == 6 ? s[3] : 0, xz = s.Length == 6 ? s[4] : 0,
                       yz = s.Length == 6 ? s[5] : 0;
                var C = MnMatrix.Build.DenseOfArray(new[,] {
                    { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });
                var x = MnVector.Build.DenseOfArray(new double[] { m.X, m.Y, m.Z });
                var r = C.Inverse() * x;
                m = new Vector3((float)r[0], (float)r[1], (float)r[2]);
            }

            m += options.MagHardIronG;

            if (options.MagNoiseG > 0)
                m += new Vector3(
                    (float)(DeterministicNoise.Gaussian(options.Seed, n, ChannelMagX) * options.MagNoiseG),
                    (float)(DeterministicNoise.Gaussian(options.Seed, n, ChannelMagY) * options.MagNoiseG),
                    (float)(DeterministicNoise.Gaussian(options.Seed, n, ChannelMagZ) * options.MagNoiseG));

            return m;
        }

        private const int ChannelMagX = 2;
        private const int ChannelMagY = 3;
        private const int ChannelMagZ = 4;

        private const int ChannelHeading = 0;
        private const int ChannelGyro = 1;

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
