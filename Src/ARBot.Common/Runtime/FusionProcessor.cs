using System;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Runtime fuzni cesty jako vypocetni stupen pipeline. Konzumuje surova senzorova
    /// mereni, prevadi je <see cref="IMeasurementMapper"/>em na <see cref="IMeasurement"/>
    /// a vklada do <see cref="AsyncFusionEngine"/>. Na pevne casove mrizce <c>Ts</c> ziska
    /// <see cref="AsyncFusionEngine.GetStateAt"/> odhad stavu a vysle
    /// <see cref="RobotStateMsg"/> pres <see cref="MessageProcessor.Output"/>.
    ///
    /// Cas taktu je rizen casem porizeni prichozich mereni (deterministicke i pri
    /// AsFastAsPossible replay). Pripadny <see cref="VirtualClock"/> se soucasne posouva.
    /// </summary>
    public sealed class FusionProcessor : MessageProcessor
    {
        private readonly AsyncFusionEngine engine;
        private readonly IMeasurementMapper mapper;
        private readonly IClock clock;
        private readonly long tsTicks;

        private DateTime nextTick;
        private DateTime currentTime;
        private bool gridInit;

        /// <param name="engine">Fuzni engine.</param>
        /// <param name="mapper">Prevod senzor -&gt; IMeasurement.</param>
        /// <param name="controlPeriod">Perioda mrizky Ts (napr. 20 ms).</param>
        /// <param name="clock">Volitelne hodiny; je-li <see cref="VirtualClock"/>, posouvaji se.</param>
        /// <param name="policy">Politika vstupni fronty.</param>
        public FusionProcessor(AsyncFusionEngine engine, IMeasurementMapper mapper,
                               TimeSpan controlPeriod, IClock clock = null,
                               OverflowPolicy policy = OverflowPolicy.Block)
            : base(policy)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.clock = clock;
            tsTicks = controlPeriod.Ticks > 0 ? controlPeriod.Ticks : TimeSpan.FromMilliseconds(20).Ticks;
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Runtime reaguje jen na surova senzorova mereni.
            if (msg is not SensorStateBase s) return;

            DateTime t = s.TimeStamp;
            (clock as VirtualClock)?.AdvanceTo(t);

            if (!gridInit)
            {
                nextTick = AlignToGrid(t);
                gridInit = true;
            }
            if (t > currentTime) currentTime = t;

            foreach (var m in mapper.ToMeasurements(msg))
                engine.Enqueue(m);

            PumpTicks(currentTime);
        }

        /// <summary>Vysle takty na mrizce az do <paramref name="now"/> (vcetne).</summary>
        private void PumpTicks(DateTime now)
        {
            while (gridInit && now >= nextTick)
            {
                RunControlStep(nextTick);
                nextTick = nextTick.AddTicks(tsTicks);
            }
        }

        private void RunControlStep(DateTime t)
        {
            RobotState rs = engine.GetStateAt(t);
            EmitDerived(new RobotStateMsg(rs));
            // (M2: regulator -> IMotorControl.Drive, MeasurementDiagMsg, ...)
        }

        /// <summary>Zarovna cas na reprodukovatelnou mrizku (nasobky Ts).</summary>
        private DateTime AlignToGrid(DateTime t) => new DateTime(t.Ticks - (t.Ticks % tsTicks));
    }
}
