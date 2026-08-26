namespace ARBot.Common.Missions
{
    /// <summary>
    /// Faze stavoveho automatu mise Robotour. Viz doc/robotour-mission.md.
    ///
    /// <para>Tri zastaveni (depo, nakladka, vykladka) maji <b>totozny prubeh</b>, takze se
    /// <c>AwaitingEStop</c> → <c>Servicing</c> → <c>AwaitingEStopRelease</c> pouziva opakovane jako
    /// jeden podautomat „servisni okno"; ktere zastaveni to je, rika
    /// <see cref="RobotourMission.CurrentStop"/>.</para>
    ///
    /// <para>Cisla jsou soucasti formatu zpravy — <b>existujici hodnoty neprecislovat</b>, nove se
    /// pridavaji na konec.</para>
    /// </summary>
    public enum RobotourPhase
    {
        /// <summary>Ceka na „Start mise" z UI.</summary>
        Idle = 0,

        /// <summary>Ceka na kvalitni fix, inicializuje jim fuzi a zapamatuje depo.</summary>
        ArmingAtDepot = 1,

        /// <summary>Robot stoji a je pod napetim; ceka, az obsluha zmackne nouzove zastaveni.</summary>
        AwaitingEStop = 2,

        /// <summary>Nouzove zastaveni drzi → obsluha nakalda/vyklada, pripadne se cte kod.</summary>
        Servicing = 3,

        /// <summary>Vse potvrzeno; ceka na UVOLNENI nouzoveho zastaveni.</summary>
        AwaitingEStopRelease = 4,

        /// <summary>Jede na misto nakladky.</summary>
        DrivingToPickup = 5,

        /// <summary>Jede na misto vykladky.</summary>
        DrivingToDrop = 6,

        /// <summary>Jede zpet do depa.</summary>
        DrivingToDepot = 7,

        /// <summary>Stoji, mise hotova.</summary>
        Finished = 8,

        /// <summary>Okamzite zastaveni; duvod je v <see cref="Logs.MissionMsg.AbortReason"/>.</summary>
        Aborted = 9,
    }

    /// <summary>
    /// Ktere ze tri zastaveni je prave obsluhovane. Rika, jestli se v servisnim okne <b>ceka kod</b>
    /// a kam se pojede dal.
    /// </summary>
    public enum RobotourStop
    {
        /// <summary>Depo pri startu — cte se kod s mistem nakladky.</summary>
        Depot = 0,

        /// <summary>Misto nakladky — nakladka + cte se kod s mistem vykladky.</summary>
        Pickup = 1,

        /// <summary>Misto vykladky — jen vykladka, kod se necte (dal se jede na zapamatovane depo).</summary>
        Drop = 2,
    }
}
