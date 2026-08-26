using System;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.Common.Simulation;

namespace ARBot.HAL.Devices.MotorDrivers
{
    /// <summary>
    /// Virtualni motory - misto realneho driveru posouvaji <see cref="SimulatedRobot"/>
    /// a hlasi z nej odometrii (viz doc/virtual-hw.md).
    /// </summary>
    public sealed class VirtualMotors : SensorBase<IMotorState>, IMotorControl
    {
        private readonly SimulatedRobot robot;
        private readonly int periodMs;

        /// <summary>
        /// Nastaveni simulace — cte se z nej <b>nouzove zastaveni</b> (viz
        /// <see cref="VirtualSensorOptions.EmergencyStop"/>). Drzi se TATAZ instance jako v panelu,
        /// takze prepnuti plati hned a motory se nemusi zakladat znovu.
        /// </summary>
        private readonly VirtualSensorOptions options;


        private DateTime nextSampleAt = DateTime.MinValue;

        /// <inheritdoc/>
        public override string Name => "VirtualMotors";

        /// <param name="robot">Ground truth, ktery se ridi a ze ktereho se cte odometrie.</param>
        /// <param name="rateHz">Frekvence hlaseni odometrie [Hz].</param>
        /// <param name="options">Nastaveni simulace (nouzove zastaveni); null = vlastni vychozi.</param>
        public VirtualMotors(SimulatedRobot robot, int rateHz = 50, VirtualSensorOptions options = null)
        {
            this.robot = robot ?? throw new ArgumentNullException(nameof(robot));
            this.options = options ?? new VirtualSensorOptions();
            periodMs = Math.Max(1, 1000 / Math.Max(1, rateHz));

            Start();
        }

        /// <inheritdoc/>
        public void Drive(double forvardSpeed, double difSpeed) => robot.Drive(forvardSpeed, difSpeed);

        /// <inheritdoc/>
        public void SetAcceleration(double acceleration) => robot.SetAcceleration(acceleration);

        /// <summary>
        /// Posune simulaci na aktualni cas a vrati odometrii: <b>kumulativni</b> enkodery
        /// a rychlosti kol primo ze simulace. Nic nezavisi na tom, kdo a kdy mereni cte.
        /// </summary>
        protected override IMotorState GetMeasurement()
        {
            WaitForNextTick();

            var ts = TimeBase.Now;
            robot.Advance(ts);
            robot.Read(out _, out _, out _,
                       out double leftSpeed, out double rightSpeed,
                       out double left, out double right);

            // Nouzove zastaveni je jen HLASENY priznak - kola zastavuje ControlLoop tim, ze pod nim
            // posila Drive(0, ...), takze simulovany robot dobrzdi svou rampou jako na zeleze.
            return new MotorStateBase(options.EmergencyStop, left, right,
                                      voltage: 24.0, leftMotorCurrent: 0, rightMotorCurrent: 0,
                                      leftWheelSpeed: leftSpeed, rightWheelSpeed: rightSpeed)
            {
                TimeStamp = ts,
            };
        }

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
                nextSampleAt = now;   // vyrazne zpozdeni: nedohanet davku, jen se srovnat
        }
    }
}
