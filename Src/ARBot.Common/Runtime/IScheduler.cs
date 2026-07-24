using System;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Scheduler periodickych uzlu (napr. ridici smycka) nad <see cref="IClock"/>.
    /// Nema vlastni vlakno - takty vydava az volani <see cref="PumpDue"/> (pumpuje
    /// volajici: v Run casovac s <c>clock.Now</c>, pri replay virtualni cas).
    ///
    /// Takty jsou kotveny na pevnou mrizku <c>t0 + k*interval</c>, kde <c>t0</c> je cas
    /// prvniho <see cref="PumpDue"/> videneho danou registraci. Jitter casu predaneho
    /// do <see cref="PumpDue"/> mrizku nemeni - <c>onTick</c> vzdy dostane presny cas taktu.
    /// </summary>
    public interface IScheduler
    {
        /// <summary>
        /// Zaregistruje periodicky callback. <c>onTick</c> je volan pro kazdy bod mrizky
        /// <c>t0 + k*interval</c>, ktery nastal (&lt;= naposledy napumpovany cas), s presnym
        /// casem daneho taktu. Vraceny <see cref="IDisposable"/> registraci zrusi.
        /// </summary>
        IDisposable Register(TimeSpan interval, Action<DateTime> onTick);

        /// <summary>Vyda vsechny takty vsech registraci, ktere nastaly do casu <paramref name="now"/> (vcetne).</summary>
        void PumpDue(DateTime now);
    }
}
