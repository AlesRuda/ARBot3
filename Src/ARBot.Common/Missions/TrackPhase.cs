namespace ARBot.Common.Missions
{
    /// <summary>
    /// Faze stavoveho automatu mise <see cref="TrackMission"/>. Viz doc/track-mission.md.
    ///
    /// <para>Prubeh je proti Robotouru primy: cekani na cloveka je <b>jen jednou, na zacatku</b>
    /// (stisk a uvolneni nouzoveho zastaveni), pak uz se jen prepinaji cile a mezi body se
    /// <b>nezastavuje</b>.</para>
    ///
    /// <para>Cisla jsou soucasti formatu zpravy — <b>existujici hodnoty neprecislovat</b>, nove
    /// se pridavaji na konec.</para>
    /// </summary>
    public enum TrackPhase
    {
        /// <summary>Ceka na „Start mise".</summary>
        Idle = 0,

        /// <summary>
        /// Robot stoji pod napetim; ceka, az obsluha <b>zmackne</b> nouzove zastaveni.
        ///
        /// <para>Tenhle stav je tu jako <b>fyzicka pojistka</b>: volba mise sama robota rozjet
        /// nesmi (tatáz zasada jako u Robotouru a u webove volby mise, viz CLAUDE.md).</para>
        /// </summary>
        AwaitingEStop = 1,

        /// <summary>Nouzove zastaveni drzi; ceka se na jeho UVOLNENI = pokyn „jed".</summary>
        AwaitingEStopRelease = 2,

        /// <summary>Jede k dalsimu mistu ze seznamu.</summary>
        Driving = 3,

        /// <summary>Objela vsechna mista a stoji (bez <c>repeat</c>).</summary>
        Finished = 4,

        /// <summary>Okamzite zastaveni; duvod je ve zprave.</summary>
        Aborted = 5,
    }
}
