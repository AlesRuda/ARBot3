using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Nastaveni prevodu koridoru na merenia do fuze. Viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class CorridorLocalizerConfig
    {
        /// <summary>Posilat merenia do fuze? <c>false</c> = jen pocitat a hlasit zpravou (A/B).</summary>
        public bool SendCorrections = true;

        /// <summary>Nastaveni hledani koridoru z hranicnich bodu.</summary>
        public CorridorConfig Corridor = new CorridorConfig();

        /// <summary>
        /// Nejvetsi casovy rozestup snimku obou kamer, ktere jde spojit do jednoho koridoru [ms].
        /// Kazda kamera vidi jen jednu stranu cesty, takze koridor vznika z dvojice.
        /// </summary>
        public double MaxCameraSkewMs = 60;

        /// <summary>
        /// Strop na nesouhlas s mapou [m]. Kdyz se merena pricna poloha lisi od mapove o vic,
        /// merenie se <b>nepusti</b> - nejspis se koreluje na jinou cestu nebo je hranice falesna.
        /// Nahrada za chybejici nezavislou kontrolu (viz decisions.md, tri podminky).
        /// </summary>
        public double MaxLateralDisagreementM = 1.5;

        /// <summary>
        /// Strop na nesouhlas sirky proti mape (nebo odhadu filtru) [m]. Vetsi rozdil znamena, ze
        /// se prolozila jina dvojice hranic, ne ta cesta.
        /// </summary>
        public double MaxWidthDisagreementM = 1.5;

        /// <summary>Nad timto odstupem pozy od hrany se hrana nebere za „tu, po ktere jedeme" [m].</summary>
        public double MaxEdgeDistanceM = 8.0;

        /// <summary>
        /// O kolik smi robot byt vic od osy koridoru, nez je jeho polosirka [m] — tedy jak daleko
        /// <b>mimo cestu</b> jeste merenie plati.
        ///
        /// <para><b>Nacpak to je</b> (nalezeno merením 22. 8. 2026): bez teto kontroly hlasil
        /// stupen platna merenia i ve chvili, kdy pricna poloha byla <b>2,1 m od osy koridoru
        /// sirokeho 2 m</b> — robot tedy metr mimo cestu. Geometricky to nejde dohromady
        /// s tvrzenim „jsem na teto ceste": bud se prolozila jina dvojice hranic, nebo robot
        /// z cesty sjel a merenie uz nema co opravovat.</para>
        /// </summary>
        public double MaxOutsideCorridorM = 0.5;

        /// <summary>Vaha noveho merenia ve filtru sirky (viz <see cref="RoadWidthFilter"/>).</summary>
        public double WidthFilterAlpha = 0.05;

        /// <summary>
        /// Aktualizovat sirku jen kdyz je nesouhlas pricne polohy pod timto prahem [m] — jinak by
        /// se do sirky zapisovala chyba pozy a ta by se sama utvrzovala.
        /// </summary>
        public double WidthUpdateMaxDisagreementM = 0.3;

        /// <summary>Jmeno zdroje merenii ve fuzi a v diagnostice.</summary>
        public string MeasurementSource = "Corridor";

        /// <summary>Posilat i korekci kurzu?</summary>
        public bool SendHeading = true;

        /// <summary>
        /// Rezim gatingu merenii z koridoru. <b>Vychozi <c>Soft</c>, a to je podstatne.</b>
        ///
        /// <para>Naměřeno 22. 8. 2026: s <c>Reject</c> zahodil gating <b>77 %</b> korekci
        /// (215 z 280, NIS p50 10, max 196) a hlaseny nesouhlas s mapou proto v prubehu behu
        /// neklesal vubec. Neni to vada gatingu: merenie tvrdi „jsem si jisty na 3 cm" a pritom
        /// nesouhlasi o 55 cm, coz JE z pohledu filtru odlehla hodnota. Jenze prave tenhle
        /// nesouhlas je to, co ma merenie opravit — a s <c>Reject</c> se korekce nikdy neuplatni.</para>
        ///
        /// <para><c>Soft</c> misto zahozeni nafoukne <c>R' = R · NIS/prah</c>, takze velky nesouhlas
        /// se zvazi mirneji, ale <b>uplatni se</b> a poza se k mape dojede postupne. Presne tohle
        /// predepisuje rozhodnuti z 20. 8. 2026 (viz decisions.md: „nesouhlas je prechodny, staci
        /// projit tim prechodem, na coz je GateMode.Soft").</para>
        /// </summary>
        public Fusion.GateMode GateMode = Fusion.GateMode.Soft;
    }
}
