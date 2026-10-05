using ARBot.Common.Configuration;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Rampy motorove jednotky [m/s²]: bezna jizda a nouzove zastaveni.
    ///
    /// <para><b>Proc dve.</b> Bezna jizda ma byt plynula a odpovidat modelu planovace
    /// (<see cref="Profile.MaxAcceleration"/> — <b>jedna</b> rampa pro rozjezd i brzdeni), nouzove
    /// zastaveni (vstup <c>DI3</c> ve skriptu jednotky, i watchdog pri mlcicim hostiteli) ma zastavit
    /// co nejdriv, co dovoli trakce a naklad. Jedna hodnota pro obe je kompromis spatny na obe
    /// strany. Rozhodnuti autora 5. 10. 2026, registr <c>hw-motor-rampa-jednotky</c>,
    /// doc/plan-drive-hold.md.</para>
    ///
    /// <para><b>Proc ne zvlast rozjezd a brzdeni</b> (autor 6. 10. 2026): zmena rychlosti je
    /// symetricka jen s jednou rampou — s ruznou by pri zmenach, kdy jedno kolo zrychluje a druhe
    /// zpomaluje, nedosla kola cile soucasne. Do 6. 10. tu byla i samostatna <c>Deceleration</c>
    /// (<c>VAR 8</c> skriptu), zrusena.</para>
    ///
    /// <para><b>Nouzova rampa zije ve SKRIPTU jednotky</b> (<c>RizeniDiffPodvozku.mbs</c> 2.2,
    /// <c>VAR 9</c>), protoze nouzove zastaveni obsluhuje jednotka sama i bez hostitele; hostitel ji
    /// jen nastavuje. Dokud ji nenastavi, plati ve skriptu vychozi hodnota (1,0 m/s²).</para>
    /// </summary>
    /// <param name="Acceleration">Bezna jizda — rozjezd i brzdeni [m/s²].</param>
    /// <param name="EmergencyDeceleration">Brzdeni pod nouzovym zastavenim a watchdogem [m/s²].</param>
    public readonly record struct MotorRamps(double Acceleration, double EmergencyDeceleration)
    {
        /// <summary>Rampy z <see cref="Profile"/>: tak, jak je runtime nastavuje motorum.</summary>
        public static MotorRamps FromProfile()
            => new MotorRamps(Profile.MaxAcceleration, Profile.EmergencyDeceleration);
    }
}
