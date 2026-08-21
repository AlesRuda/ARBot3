namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Jak fuze s merenim naloadila. Doplneno 21. 8. 2026: samo <c>Accepted = false</c> nerozlisi
    /// dva zcela jine problemy — merenie prislo <b>pozde</b> (nedostane ani sanci), nebo prislo
    /// vcas a <b>gating</b> ho zamitl jako odlehle. U korekci z korelace s mapou je to rozdil mezi
    /// „zkratit vypocet" a „opravit sigma / model". Viz doc/map-correlation-localization.md.
    /// </summary>
    public enum MeasurementVerdict : byte
    {
        /// <summary>Merenie se aplikovalo (gating ho pustil).</summary>
        Accepted = 0,

        /// <summary>Merenie proslo do bufferu, ale gating ho zamitl (NIS nad prahem).</summary>
        GatedOut = 1,

        /// <summary>
        /// Merenie prislo starsi nez okno historie a do bufferu vubec nevstoupilo
        /// (viz <see cref="AsyncFusionEngine.DroppedTooOld"/>).
        /// </summary>
        TooOld = 2,
    }
}
