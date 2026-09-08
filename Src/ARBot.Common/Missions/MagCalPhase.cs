namespace ARBot.Common.Missions
{
    /// <summary>
    /// Faze mise <c>magcal</c>. Cisla jdou do <c>MagCalMsg</c> — <b>neprecislovat</b>, nove
    /// hodnoty pridavat na konec. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public enum MagCalPhase
    {
        /// <summary>Jeste nezacala.</summary>
        Idle = 0,

        /// <summary>Sbira pole a ceka, az obsluha otacenim pokryje azimuty a naklony.</summary>
        Collecting = 1,

        /// <summary>Pokryti uplne a prolozeni pouzitelne — ceka se na pokyn k zapisu.</summary>
        Ready = 2,

        /// <summary>Kalibrace zapsana do registru 23 a ulozena do flash.</summary>
        Written = 3,

        /// <summary>
        /// Mise nezacala, protoze se nepodarilo precist referencni <c>|B|</c> (registr 21).
        /// Bez nej by se prokladalo proti dohadu — radeji stat a rict to.
        /// </summary>
        NoReference = 4,
    }
}
