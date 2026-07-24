using System;
using ARBot.Common.Devices;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Zdroj zprav napojeny na udalost mereni senzoru. Kazde prichozi mereni (ktere je nyni
    /// samo <see cref="ARBot.Common.Logs.Message"/>) rozesle beze zmeny - zadna konverze.
    ///
    /// Lze pouzit dvema zpusoby:
    /// - nad konkretnim <see cref="SensorBase{TState}"/> (volitelne rizeni jeho Start/Stop),
    /// - nebo nad libovolnou udalosti pres subscribe/unsubscribe delegaty (napr. rozhrani
    ///   <c>ICamera</c>/<c>IIMU</c>, ktere neni <see cref="SensorBase{TState}"/>).
    /// </summary>
    public sealed class SensorMessageSource<TState> : MessageSource where TState : SensorStateBase
    {
        private readonly Action<EventHandler<TState>> subscribe;
        private readonly Action<EventHandler<TState>> unsubscribe;
        private readonly SensorBase<TState> sensor;   // volitelny - jen pro rizeni Start/Stop
        private readonly bool controlSensor;
        private EventHandler<TState> handler;

        /// <param name="sensor">Zdrojovy senzor.</param>
        /// <param name="controlSensor">Zda Start/Stop tohoto zdroje take spusti/zastavi senzor.</param>
        public SensorMessageSource(SensorBase<TState> sensor, bool controlSensor = true)
        {
            this.sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
            this.controlSensor = controlSensor;
            subscribe = h => sensor.MeasurementArived += h;
            unsubscribe = h => sensor.MeasurementArived -= h;
        }

        /// <param name="subscribe">Prihlaseni k udalosti mereni.</param>
        /// <param name="unsubscribe">Odhlaseni od udalosti mereni.</param>
        public SensorMessageSource(Action<EventHandler<TState>> subscribe, Action<EventHandler<TState>> unsubscribe)
        {
            this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            this.unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        /// <inheritdoc/>
        public override void Start()
        {
            if (handler != null) return;
            handler = (s, state) => { if (state != null) Emit(state); };
            subscribe(handler);
            if (controlSensor) sensor?.Start();
        }

        /// <inheritdoc/>
        public override void Stop()
        {
            if (handler == null) return;
            unsubscribe(handler);
            handler = null;
            if (controlSensor) sensor?.Stop();
        }
    }
}
