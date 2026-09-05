using System;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Na co mise ceka.</b> Vycet, ne text: cislo prezije v zaznamu a da se podle nej filtrovat,
    /// text pro cloveka dela <see cref="MissionStatusText"/> na jednom miste.
    ///
    /// <para>Znamena „bez tohohle se mise nepohne" — tedy neco, na co se ceka <b>zvenci</b>
    /// (clovek, senzor, dojezd). Stavy, ktere nikam necekaji (mise dojela, byla prerusena, prave
    /// jede a rozhoduje sama), maji <see cref="None"/>. „Neceka se na nic" je taky odpoved.</para>
    ///
    /// <para>Cisla jsou soucasti diagnostiky i pripadneho formatu zpravy — <b>neprecislovat</b>,
    /// nove hodnoty pridavat na konec.</para>
    /// </summary>
    public enum MissionWait
    {
        /// <summary>Neceka se na nic (jede, dojela, nebo byla prerusena).</summary>
        None = 0,

        /// <summary>Ceka na pokyn ke startu mise.</summary>
        MissionStart = 1,

        /// <summary>Ceka na kvalitni fix GPS, aby mohla ukotvit depo a inicializovat fuzi.</summary>
        GpsFix = 2,

        /// <summary>Ceka, az obsluha <b>stisne</b> nouzove zastaveni (servisni okno se otevira).</summary>
        EmergencyStopPressed = 3,

        /// <summary>Ceka na QR kod z kamery (pod drzenym nouzovym zastavenim).</summary>
        QrCode = 4,

        /// <summary>Ceka, az obsluha nouzove zastaveni <b>uvolni</b> — to je signal „hotovo, jed".</summary>
        EmergencyStopReleased = 5,

        /// <summary>Jede k cili a ceka na dojezd.</summary>
        Arrival = 6,
    }

    /// <summary>
    /// <b>Hlaseni stavu mise.</b> Jednotne „jaka mise, v jake fazi, na co ceka a jak dlouho uz jede"
    /// — pro webovy nahled headless a pro UI.
    ///
    /// <para><b>Proc rozhrani, a ne spolecny predek:</b> obe mise uz dedi z <c>MessageProcessor</c>
    /// (vlakno stupne), takze bazova trida by jim musela stat mezi — a hlavne by svazovala i to, co
    /// spolecne nemaji. Spolecne je jen hlaseni stavu; <b>ridici</b> osa spolecna neni a zamerne
    /// nebude (viz <see cref="RobotourMission"/>): FreeRun produkuje mrkev pro lokalni planovac,
    /// Robotour LLA cil pro globalni navigaci.</para>
    ///
    /// <para><b>Cas z hodin DAT, ne stroje</b> — <see cref="Elapsed"/> se pocita z razitek zprav,
    /// takze pri prehravani zaznamu a v testech znamena totez jako za behu.</para>
    /// </summary>
    public interface IMissionStatus
    {
        /// <summary>Jmeno mise ve tvaru parametru <c>mission=</c> (<c>freerun</c>, <c>robotour</c>).</summary>
        string MissionName { get; }

        /// <summary>Kratky nazev fáze pro cloveka (co mise prave dela).</summary>
        string PhaseText { get; }

        /// <summary>Na co se ceka; <see cref="MissionWait.None"/> = na nic.</summary>
        MissionWait WaitingFor { get; }

        /// <summary>Jak dlouho mise bezi (podle razitek zprav); nula, dokud nezacala.</summary>
        TimeSpan Elapsed { get; }
    }
}
