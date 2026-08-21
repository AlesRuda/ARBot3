namespace ARBot.Common.Devices
{
    /// <summary>
    /// Senzor, ktery ma vlastni smycku mereni, a da se tedy spustit a zastavit.
    ///
    /// <para><b>Proc to neni soucast <see cref="ISensor"/></b> (21. 8. 2026): ne kazdy senzor
    /// smycku ma. <c>MD23</c> (motory po I2C) i <c>DummyMotors</c> jsou <see cref="ISensor"/>, ale
    /// nic na pozadi nebezi — mereni se u nich cte az na dotaz. No-op <c>Start</c>/<c>Stop</c> by
    /// u nich lhaly a panel senzoru by nabizel tlacitko, ktere nic nedela. Kdo umi Start/Stop,
    /// prizna se timhle rozhranim; ostatni se v UI ukazou bez ovladani.</para>
    ///
    /// <para>Implementuje <see cref="SensorBase{TState}"/>, tedy vsechny senzory s vlastnim
    /// vlaknem (kamery, GPS, IMU, motory po UART).</para>
    /// </summary>
    public interface IControllableSensor
    {
        /// <summary>Bezi smycka mereni?</summary>
        bool IsRunning { get; }

        /// <summary>Spusti smycku mereni (kdyz uz bezi, nedela nic).</summary>
        void Start();

        /// <summary>Zastavi smycku mereni a pocka na jeji ukonceni.</summary>
        void Stop();
    }
}
