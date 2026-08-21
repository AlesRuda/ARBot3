using System;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Reaktivni fuzni stupen pipeline. Konzumuje surova senzorova mereni, prevadi je
    /// <see cref="IMeasurementMapper"/>em na <see cref="IMeasurement"/> a vklada do
    /// <see cref="AsyncFusionEngine"/>. <b>Netikuje, neemituje, nevydava rizeni</b> -
    /// jen agreguje a udrzuje dotazovatelny odhad (dotaz resi <see cref="ControlLoop"/>
    /// pres <see cref="AsyncFusionEngine.GetStateAt"/>).
    ///
    /// Pokud je predan <see cref="VirtualClock"/>, posouva se na cas porizeni mereni
    /// (zdroj virtualniho casu pri replay).
    /// </summary>
    public sealed class FusionProcessor : MessageProcessor
    {
        private readonly AsyncFusionEngine engine;
        private readonly IMeasurementMapper mapper;
        private readonly IClock clock;

        // Diagnostika merenii (zapina EnableMeasurementDiagnostics). Motor hlasi verdikty pod svym
        // vnitrnim zamkem, takze se tady jen odlozi do fronty a emituje az z Consume.
        private readonly System.Collections.Concurrent.ConcurrentQueue<AsyncFusionEngine.MeasurementInfo>
            diagQueue = new System.Collections.Concurrent.ConcurrentQueue<AsyncFusionEngine.MeasurementInfo>();
        private Func<string, bool> diagFilter;

        /// <param name="engine">Fuzni engine.</param>
        /// <param name="mapper">Prevod senzor -&gt; IMeasurement.</param>
        /// <param name="controlPeriod">Zachovano kvuli kompatibilite volajicich; nepouziva se
        /// (takty prevzala <see cref="ControlLoop"/> pres <see cref="IScheduler"/>).</param>
        /// <param name="clock">Volitelne hodiny; je-li <see cref="VirtualClock"/>, posouvaji se.</param>
        /// <param name="policy">Politika vstupni fronty.</param>
        public FusionProcessor(AsyncFusionEngine engine, IMeasurementMapper mapper,
                               TimeSpan controlPeriod = default, IClock clock = null,
                               OverflowPolicy policy = OverflowPolicy.Block)
            : base(policy)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.clock = clock;
        }

        /// <summary>
        /// Zapne publikovani <see cref="MeasurementDiagMsg"/> - u kazdeho merenia verdikt fuze
        /// (NIS, prijeti gatingem, nebo „prislo pozde"). <b>Vychozi stav je vypnuto</b>: merenii
        /// chodi stovky za sekundu (IMU 100 Hz + odometrie 50 Hz + GPS), takze by to zaplavilo
        /// stream i zaznam, a za normalniho behu to nikdo nepotrebuje.
        ///
        /// <para><paramref name="sourceFilter"/> = predikat nad <c>IMeasurement.Source</c>
        /// (null = vse). Typicke pouziti je vyzobat jen korekce z korelace s mapou.</para>
        ///
        /// <para>Verdikt chodi az ve chvili, kdy merenie vypadne z okna historie (viz
        /// <see cref="AsyncFusionEngine.OnMeasurement"/>) - tedy opozdene o okno. Pro rozbor
        /// zaznamu to nevadi, k rizeni se to nepouziva.</para>
        /// </summary>
        public void EnableMeasurementDiagnostics(Func<string, bool> sourceFilter = null)
        {
            diagFilter = sourceFilter;
            engine.OnMeasurement = info =>
            {
                var f = diagFilter;
                if (f != null && !f(info.Source)) return;
                diagQueue.Enqueue(info);              // jen odlozit - jsme pod zamkem motoru
            };
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Odlozene verdikty (viz EnableMeasurementDiagnostics). Nejdriv, at je poradi v proudu
            // co nejblizsi poradi vzniku.
            while (diagQueue.TryDequeue(out var info))
                EmitDerived(info.ToLogMessage());

            // Runtime reaguje jen na surova senzorova mereni.
            if (msg is not SensorStateBase s) return;

            (clock as VirtualClock)?.AdvanceTo(s.TimeStamp);

            foreach (var m in mapper.ToMeasurements(msg))
                engine.Enqueue(m);
        }

        // --- ODSTRANENO (krok 6 record/replay): fuze uz netikuje ani neemituje RobotStateMsg.
        //     Periodicke vzorkovani + rizeni prevzala ControlLoop (IScheduler nad IClock).
        //     Puvodni implementace ponechana zakomentovana, dokud novy graf neni plne overen.
        //
        // private DateTime nextTick;
        // private DateTime currentTime;
        // private bool gridInit;
        // private readonly long tsTicks;
        //
        // private void PumpTicks(DateTime now)
        // {
        //     while (gridInit && now >= nextTick)
        //     {
        //         RunControlStep(nextTick);
        //         nextTick = nextTick.AddTicks(tsTicks);
        //     }
        // }
        //
        // private void RunControlStep(DateTime t)
        // {
        //     RobotState rs = engine.GetStateAt(t);
        //     EmitDerived(new RobotStateMsg(rs));
        // }
        //
        // private DateTime AlignToGrid(DateTime t) => new DateTime(t.Ticks - (t.Ticks % tsTicks));
    }
}
