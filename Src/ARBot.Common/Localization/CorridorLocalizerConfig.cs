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

        // ⚠️ MaxLateralDisagreementM (strop na pricny nesouhlas s mapou, 1,5 m) tu byl do
        // 18. 9. 2026. Zrusen bez nahrady - duvod je u mista, kde brana stala
        // (CorridorLocalizer.Update). Strucne: testoval tutez velicinu jako EdgeAssociator, jen
        // pevnym pravitkem misto chi-kvadratu proti kovarianci pozy, a stal az za nim.

        /// <summary>
        /// Strop na nesouhlas sirky proti <b>odhadu</b> [m]. Vetsi rozdil znamena, ze se prolozila
        /// jina dvojice hranic, ne ta cesta.
        ///
        /// <para>⚠️ <b>Plati az od chvile, kdy ma odhad KVALITU</b> (<see cref="RoadWidthEstimator"/>).
        /// Do 15. 9. 2026 se brana ptala na <b>mapovou</b> sirku uz v prvnim cyklu, tedy drív, nez
        /// se filtr mel z ceho naucit - a protoze mapova sirka je u cest bez tagu <c>width</c>
        /// jen default <c>roadwidth=</c> (3 m), na ceste sirsi nez 4,5 m se prvni merenie neprijalo
        /// NIKDY, filtr se nezalozil a hrana zustala <b>nema navzdy</b>. Mapova sirka uz proto
        /// referenci brany neni.</para>
        /// </summary>
        public double MaxWidthDisagreementM = 1.5;

        /// <summary>Nastaveni odhadu sirky cest (okno, minimum vzorku, strop rozptylu).</summary>
        public RoadWidthEstimatorConfig WidthEstimator = new RoadWidthEstimatorConfig();

        /// <summary>
        /// <b>Nejistota MAPOVE sirky</b> (1 sigma) [m] pro pricnou polohu z JEDNE hrany
        /// (<see cref="CorridorConfig.SingleEdge"/>), kdyz sirka te cesty jeste neni naucena
        /// z oboustrannych merení (<see cref="RoadWidthEstimator.TryGetWidth"/>). Parametr
        /// <c>corridorsinglewidthstd=</c>.
        ///
        /// <para>Do pricne polohy jde <b>polovinou</b>: <c>sigma = sqrt(sigma_hrany² + (tohle/2)²)</c>,
        /// takze vychozi 1 m da sigmu aspon 0,5 m. Mapova sirka je u cest bez tagu <c>width</c>
        /// jen <c>roadwidth=</c> (3 m) - v Modranech 23. 9. 2026 pri skutecne ~5 m ceste. ⚠️ Je to
        /// ale <b>bias, ne sum</b>: sigma ho neodstrani, jen mu ubere autoritu; poloha se ustali
        /// posunuta o polovinu chyby sirky. Kurz z jedne hrany na sirce nezavisi.</para>
        ///
        /// <para>S <b>naucenou</b> sirkou se misto tehle hodnoty bere rozptyl odhadu (MAD) s podlahou
        /// <see cref="SingleEdgeLearnedWidthStdFloorM"/>.</para>
        /// </summary>
        public double SingleEdgeWidthStdM = 1.0;

        /// <summary>
        /// Podlaha nejistoty NAUCENE sirky [m] pro pricnou polohu z jedne hrany. Rozptyl okna
        /// (MAD) muze vyjit skoro nula, pritom sirka je median jen par desitek merení.
        /// </summary>
        public double SingleEdgeLearnedWidthStdFloorM = 0.1;

        /// <summary>Nad timto odstupem pozy od hrany se hrana nebere za „tu, po ktere jedeme" [m].</summary>
        public double MaxEdgeDistanceM = 8.0;

        /// <summary>
        /// Jak se vybira hrana, ke ktere se koridor vztahuje (<see cref="EdgeAssociator"/>).
        ///
        /// <para>⚠️ Do 16. 9. 2026 se brala prosta <b>nejblizsi</b> hrana a kurz do vyberu
        /// nevstupoval vubec — nad <c>20260916-164926.rec</c> se pak <b>polovina</b> cyklu
        /// parovala na PRICNOU ulici. <c>Association.Enabled = false</c> vraci puvodni chovani
        /// pro A/B.</para>
        /// </summary>
        public EdgeAssociationConfig Association = new EdgeAssociationConfig();

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

        /// <summary>
        /// Vaha noveho merenia ve filtru sirky (viz <see cref="RoadWidthFilter"/>).
        /// <para>⚠️ <b>Od 15. 9. 2026 se nepouziva</b> — sirku odhaduje <see cref="RoadWidthEstimator"/>
        /// (okno + median), ne exponencialni prumer. Pole i <c>RoadWidthFilter</c> zustavaji,
        /// dokud se nova cesta neproveri na datech ze zarizeni.</para>
        /// </summary>
        public double WidthFilterAlpha = 0.05;

        /// <summary>
        /// Aktualizovat sirku jen kdyz je nesouhlas pricne polohy pod timto prahem [m].
        ///
        /// <para>⚠️ <b>Od 15. 9. 2026 se nepouziva.</b> Puvodni zduvodneni („jinak by se do sirky
        /// zapisovala chyba pozy a ta by se sama utvrzovala") je <b>nepresne</b>: sirka je rozdil
        /// offsetu dvou primek v ramci robotu (<c>CorridorFinder</c>: <c>Width = cL − cR</c>),
        /// takze pozu nepouziva vubec a chyba pozy se do ni dostat nemuze. Co velky pricny
        /// nesouhlas signalizuje, je <b>spatne prolozeni</b> nebo spatne prirazeni k hrane — a na
        /// spatne prirazeni je <c>EdgeAssociator</c> (chi-kvadrat pres azimut a pricnou odchylku),
        /// na spatne prolozeni <see cref="MaxOutsideCorridorM"/> a sirkove brany. Podminovat
        /// uceni na 0,3 m by navic vyrobilo tyz zamek, ktery se odstranoval: pri chybe pozy 0,6 m
        /// by se odhad nezalozil nikdy. Drzi to <c>CorridorWidthTrustTests</c>.</para>
        ///
        /// <para>⚠️ Do 18. 9. 2026 tu stalo „a na to staci <c>MaxLateralDisagreementM</c>, pod
        /// kterym se odhad uci" — ta brana uz neexistuje, takze se odhad uci ze <b>vsech</b>
        /// cyklu, ktere prosly prirazenim.</para>
        /// </summary>
        public double WidthUpdateMaxDisagreementM = 0.3;

        /// <summary>Jmeno zdroje merenii ve fuzi a v diagnostice.</summary>
        public string MeasurementSource = "Corridor";

        // --- Odtlumeni: nafouknuti sigmy a skrceni kadence -----------------------------------
        //
        // Tataz lecba a tyz duvod jako gpsposstd u GPS a imuheadingstd + imuheadinghz u kompasu:
        // filtr bere merenia za NEZAVISLA, jenze koridor meri snimek co snimek TYZ fyzicky okraj
        // cesty (tyz stin, tyz obrubnik, tataz trava), takze jeho chyba je casove korelovana
        // a sto odectu nese informaci mnohem mensiho poctu.
        //
        // ⚠️ Dekorelacni cas koridoru ZMERENY NENI. U plosne korelace vysel ~3 s (odtud
        // MapCorrelatorConfig.MinPeriod) a u kompasu τ ≳ 600 s (odtud imuheadinghz). Tyhle tri
        // hodnoty existuji proto, aby to slo z dat NASTAVIT, az bude zaznam - ne aby se hadalo.
        //
        // Vychozi je vsude 0 = dnesni chovani. U imuheadingstd je default 5°, protoze ten bias byl
        // zmereny; tady zmereneho neni nic, takze nenulovy default by byl prave to, co si projekt
        // jinde vycita. Viz doc/map-correlation-localization.md.

        /// <summary>
        /// Prirazek k sigme <b>pricne polohy</b> [m]; sklada se <b>kvadraticky</b> s tou
        /// z prolozeni. 0 = zadne nafouknuti (stare chovani pro A/B).
        ///
        /// <para><b>Kvadraticky, ne maximem:</b> kdyz vyskoci sigma z reziduí, ma vysledna sigma
        /// rust dal - stejne jako u <c>CompassHeadingStdFloor</c>.</para>
        /// </summary>
        public double SigmaLateralExtraM = 0;

        /// <summary>
        /// Prirazek k sigme <b>kurzu</b> [rad]; sklada se kvadraticky s tou z prolozeni.
        /// 0 = zadne nafouknuti.
        /// </summary>
        public double SigmaHeadingExtraRad = 0;

        /// <summary>
        /// Nejmensi odstup mezi <b>odeslanimi</b> do fuze [s]; 0 = neomezeno (kazdy cyklus).
        ///
        /// <para>⚠️ <b>Skrti se jen POSILANI, ne vypocet</b> — na rozdil od
        /// <c>MapCorrelatorConfig.MinPeriod</c>, ktery skrti cely cyklus, protoze stoji cele jadro.
        /// Koridor stoji zlomek milisekundy a <c>RoadCorridorMsg</c> je to cenne: chodi dal v plne
        /// kadenci, takze <c>ARBot.Analyze corridor</c> ani A/B pres <c>corridorsend=</c> nic
        /// neztrati - a hlavne jde odhad kvality i prahy skrceni proladit offline ze zaznamu.</para>
        ///
        /// <para>⚠️ <b>Kvotu spotrebuje jen USPESNE odeslani.</b> Kdyby ji sebral i cyklus shozeny
        /// na jine brane, koridor by mlcel tim vic, cim hur mu to jde.</para>
        /// </summary>
        public double MinSendPeriodSec = 0;

        /// <summary>Posilat i korekci kurzu?</summary>
        public bool SendHeading = true;

        // --- Limit kroku korekce (21. 9. 2026) ---------------------------------------------------
        //
        // Na Robotouru 19. 9. 2026 prisel kazdy skok pozy (0,6-4 m) do 0,1 s po prijatem mereni
        // koridoru: filtr nahromadeny drift stahl v JEDNOM kroku, robot se skokem ocitl v blokovane
        // casti gridu a presel do uniku. Sigma to neresi (velka sigma drift jen zakonzervuje,
        // zmereno 20. 9. 2026 protifaktickym replayem), zahozeni taky ne (tvrdy gate delal vysledek
        // horsi nez nekorigovat, 25. 8. 2026). Lecba je RYCHLOSTNI LIMIT: krok filtru na jedno
        // mereni smi byt nejvys slew × Δt, kde Δt je odstup od predchoziho odeslani; filtr toho
        // dosahne nafouknutim R (IMeasurement.MaxStep, Ekf.UpdateStep), takze zustane konzistentni
        // a zbytek inovace stahne dalsimi merenimi. Navrh PoseSlew na VYSTUPU fuze autor 20. 9.
        // zamitl (dve pozy v systemu); tohle je varianta uvnitr filtru.
        //
        // Vychozi 0 = dnesni chovani (A/B); hodnota se ma nastavit z dat
        // (ARBot.Analyze corridorstd --slew=). Viz doc/map-correlation-localization.md.

        /// <summary>
        /// Rychlostni limit <b>pricne</b> korekce z koridoru [m/s]; 0 = bez limitu. Na jedno
        /// mereni smi poza podel normaly cesty uhnout nejvys <c>SlewRateMps × Δt</c>
        /// (Δt = odstup od predchoziho odeslani, orezany na <see cref="SlewDtFloorSec"/> az
        /// <see cref="SlewDtCapSec"/>).
        /// </summary>
        public double SlewRateMps = 0;

        /// <summary>
        /// Rychlostni limit korekce <b>kurzu</b> z koridoru [rad/s]; 0 = bez limitu. Grid je
        /// kotveny ve svete, takze otoceni pozy o dθ posune jeho obsah o R·dθ - proto ma kurz
        /// vlastni limit.
        /// </summary>
        public double SlewRateHeadingRadPerSec = 0;

        /// <summary>
        /// Strop na Δt pro limit kroku [s]. Bez nej by po dlouhe mezere koridoru (stani, vypadek
        /// kamery) prvni mereni smelo skocit libovolne - a to je presne skok, ktery se tu krotí.
        /// Prvni odeslani a skok casu vzad (seek) berou strop.
        /// </summary>
        public double SlewDtCapSec = 1.0;

        /// <summary>
        /// Podlaha na Δt pro limit kroku [s]. Dve mereni v temz okamziku (obe kamery) by jinak dala
        /// limit 0, a nulovy limit filtr bere jako VYPNUTY - podlaha z neho udela maly, ne zadny.
        /// </summary>
        public double SlewDtFloorSec = 0.02;

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
