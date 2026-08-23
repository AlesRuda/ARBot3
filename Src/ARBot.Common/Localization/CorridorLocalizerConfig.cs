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
        ///
        /// <para><b>Vychozich 400 ms je namerenych</b> (23. 8. 2026). Kamery jedou jen ~6,8 Hz
        /// (rozestup vlastnich snimku p50 147 ms, p90 292 ms) a nejsou fazove svazane. Vysledek
        /// na teze 40s trase:</para>
        ///
        /// <code>
        ///   okno    kompenzace   NoPair   Ok   NotParallel   prijato z cyklu
        ///    60 ms      ne         260     76      110            16 %
        ///   200 ms      ano         75    106      116            34 %
        ///   400 ms      ano         20    159       81            55 %
        /// </code>
        ///
        /// <para>Sirsi okno s kompenzaci tedy nejen ztrojnasobilo pocet merenii, ale
        /// <b>NotParallel i kleslo</b> (110 → 81) - dvojice jsou po prepoctu konzistentnejsi.
        /// Kvalita prijatych je pritom lepsi: sirka 1,98 m proti mapovym 1,98 a pricny nesouhlas
        /// 0,010 m.</para>
        ///
        /// <para><b>Puvodnich 60 ms bylo malo</b> - ale ne z duvodu, ktery se nabizel. Parovani se
        /// diva jen DOZADU (<c>lastByCamera</c> drzi posledni uz prijaty snimek), takze rozhoduje
        /// rozestup k PREDCHOZIMU snimku druhe kamery, ne k nejblizsimu. Ten je pri periode 147 ms
        /// a nahodne fazi rovnomerne 0-147 ms, takze do 60 ms padne jen ~40 % - odtud namerenych
        /// ~60 % <c>NoPair</c>.</para>
        ///
        /// <para><b>Sirsi okno ma smysl jen s kompenzaci pohybu</b>
        /// (<see cref="CompensateCameraSkew"/>). Bez ni se skladaji hranice videne z ruznych poz
        /// a nerovnobeznost si tim clovek vyrobi sam: pri 1,2 m/s a 200 ms je to 0,24 m posunu.</para>
        /// </summary>
        public double MaxCameraSkewMs = 400;

        /// <summary>
        /// Prepocitat body druhe kamery z jejiho casu do casu aktualniho snimku podle ROZDILU poz?
        /// Vychozi zapnuto; vypina se jen pro A/B.
        ///
        /// <para>Pouziva se pouze <b>relativni</b> pohyb mezi dvema blizkymi casy (desetiny
        /// sekundy, tedy prakticky odometrie), ne absolutni poloha - merenie proto zustava
        /// nezavisle na chybe lokalizace.</para>
        /// </summary>
        public bool CompensateCameraSkew = true;

        /// <summary>
        /// Pod timto rozestupem se kompenzace neresi [ms] - posun je pod rozlisenim merenia
        /// (pri 1,2 m/s je 20 ms 2,4 cm) a usetri se dva dotazy do fuze na kazdy snimek.
        /// </summary>
        public double NoCompensationSkewMs = 20;

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
