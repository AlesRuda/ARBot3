using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Nastaveni hledani koridoru. Vychozi hodnoty vzesly z merení nad zaznamem
    /// <c>records/20260821-095328.rec</c> (21. 8. 2026) — viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class CorridorConfig
    {
        /// <summary>Prah inlieru pro RANSAC prolozeni hranice primkou [m].</summary>
        public double InlierThresholdM = 0.10;

        /// <summary>
        /// Minimalni pocet inlieru na kazde strane.
        ///
        /// <para><b>Toto je ten podstatny gate.</b> Nad zaznamem zahodil 282 z 559 snimku a bez nej
        /// se do statistiky michaly primky prolozene 3–6 body, ktere vyjdou kolmo na cestu
        /// (sirka az 10 m, smer −88°). S nim: sirka sd 0,45 m misto 3,3 m.</para>
        /// </summary>
        public int MinInliers = 25;

        /// <summary>Minimalni pocet vstupnich bodu, aby se prokladani vubec zkousilo.</summary>
        public int MinPoints = 6;

        /// <summary>Nejvetsi pripustna odchylka smeru obou hranic [rad]; nad tim to neni koridor.</summary>
        public double MaxParallelErrorRad = 10 * Math.PI / 180;

        /// <summary>Rozumny rozsah sirky cesty [m] - mimo nej se koridor zahodi.</summary>
        public double MinWidthM = 0.5;

        /// <summary>Rozumny rozsah sirky cesty [m].</summary>
        public double MaxWidthM = 8.0;

        /// <summary>
        /// Dolni hranice sigma pricne polohy [m]. Odpovida <b>namerene opakovatelnosti</b> nad
        /// zaznamem (sd 3 cm, 21. 8. 2026) — tedy systematice, kterou rezidua prolozeni nevidi
        /// vubec (kalibrace kamer, rozliseni hloubky, definice "kde presne cesta konci").
        /// Bez podlahy by u cistych dat vysla milimetrova jistota, kterou si merenie nezaslouzi.
        /// </summary>
        public double SigmaFloorM = 0.03;

        /// <summary>Dolni hranice sigma smeru [rad].</summary>
        public double SigmaFloorRad = 0.5 * Math.PI / 180;
    }
}
