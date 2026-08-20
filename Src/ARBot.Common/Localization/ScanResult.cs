namespace ARBot.Common.Localization
{
    /// <summary>
    /// Vysledek hrube-jemneho skenovani korelace. Viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class ScanResult
    {
        /// <summary>Nalezeny posun na vychod [m].</summary>
        public double Dx;

        /// <summary>Nalezeny posun na sever [m].</summary>
        public double Dy;

        /// <summary>Nalezena chyba kurzu [rad].</summary>
        public double Phi;

        /// <summary>Skore v maximu (z NEJJEMNEJSI urovne).</summary>
        public double Score;

        /// <summary>
        /// DIAGNOSTIKA: skore maxima nalezeneho na NEJHRUBSI urovni, tedy v bode kvantizovanem na
        /// jeji krok (0,4 m). Uz se proti nemu NEPOROVNAVA konkurent - viz
        /// <see cref="CoarseStrideScoreAtPeak"/>. Zustava jako diagnostika, o kolik jemne urovne
        /// maximum jeste zvedly.
        /// </summary>
        public double CoarsePeakScore;

        /// <summary>
        /// Skore v NALEZENEM (jemnem) maximu vyhodnocene se stride nejhrubsi urovne. Proti TOMUTO
        /// cislu se porovnava konkurent (<see cref="CorrelationScorer.BestRivalAlongAxis"/>).
        ///
        /// <para><b>Proc ne <see cref="CoarsePeakScore"/>:</b> oba operandy prahu nejednoznacnosti
        /// musi byt merene ve STEJNEM bode a se STEJNYM podvzorkovanim. Stride si odpovidal, ale
        /// pozice ne: konkurent se vyhodnocuje kolem JEMNEHO maxima, zatimco
        /// <see cref="CoarsePeakScore"/> je z bodu kvantizovaneho na 0,4 m, a ten skoruje NIZ. Prah
        /// tim byl systematicky mirnejsi, nez se navrhoval (cca 0,85 misto zamyslenych 0,90), coz
        /// oslabovalo hlavni obranu proti prilepeni na soubeznou cestu.</para>
        /// </summary>
        public double CoarseStrideScoreAtPeak;

        /// <summary>Kolik kandidatu se celkem vyhodnotilo (diagnostika ceny).</summary>
        public int Candidates;
    }
}
