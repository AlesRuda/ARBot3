using System;

namespace ARBot.Common.Localization
{
    /// <summary>Jedna uroven hrube-jemneho skenovani (viz doc/map-correlation-localization.md).</summary>
    public sealed class ScanLevel
    {
        /// <summary>Krok posunu [m].</summary>
        public double StepM;

        /// <summary>Krok kurzu [rad].</summary>
        public double StepHeadingRad;

        /// <summary>Polovina okna posunu [m] (okolo stredu z predchozi urovne).</summary>
        public double HalfRangeM;

        /// <summary>Polovina okna kurzu [rad].</summary>
        public double HalfRangeHeadingRad;

        /// <summary>Podvzorkovani dukazu: bere se kazdy N-ty. 1 = vsechny.</summary>
        public int Stride = 1;
    }

    /// <summary>
    /// Konfigurace korelace occupancy gridu s mapou. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Vychozi hodnoty jsou odhad k naladeni nad zaznamy (faze 4), ne merena pravda.</b></para>
    /// </summary>
    public sealed class MapCorrelatorConfig
    {
        /// <summary>
        /// Posilat merenia do fuze? <c>false</c> = korelator jen pocita a hlasi zpravou.
        ///
        /// <para><b>Drive se to jmenovalo <c>Enabled</c> a bylo to past</b> (prejmenovano 20. 8. 2026):
        /// cetlo se to jako "korelator je zapnuty", ale test je az ZA celym vypoctem - sken, rastr,
        /// dukazni seznam i kovariance se spocitaji vzdycky, takze <c>false</c> neusporilo nic.
        /// Na "nepocitat to vubec" je parametr prikazove radky <c>mapcorr=false</c>, ktery ten
        /// stupen v <c>ARBotRuntime.WireRun</c> vubec nezalozi. Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public bool SendCorrections = true;

        /// <summary>Absolutni hodnota LRoad, od ktere bunka vstupuje do korelace [log-odds].</summary>
        public float EvidenceThreshold = 0.4f;

        /// <summary>Pod timto skore korelator mlci (robot nejspis neni na mapovane ceste).</summary>
        public double MinScore = 0.25;

        /// <summary>O kolik musi byt maximum lepsi nez konkurent, aby shoda platila za jednoznacnou.</summary>
        public double AmbiguityMargin = 0.10;

        /// <summary>
        /// Od jake vzdalenosti od maxima se zacina hledat konkurencni maximum [m] - tedy zacatek
        /// sweepu podel osy (viz <see cref="CorrelationScorer.BestRivalAlongAxis"/>). Blizko maxima
        /// je skore skoro stejne u kazdeho kandidata, takze konkurent musi byt VZDALENY - jinak by
        /// nejednoznacnost hlasil kazdy cyklus. Musi byt <= <see cref="SearchRangeM"/>.
        /// </summary>
        public double AmbiguitySeparationM = 1.0;

        /// <summary>Min. pocet dukaznich bunek; pod tim se nekoreluje.</summary>
        public int MinEvidenceCells = 400;

        /// <summary>
        /// Skala kovariance ze zakriveni skore (C = -Alpha * H^-1). Skore neni log-verohodnost,
        /// takze zakriveni ma spravny TVAR, ne absolutni skalu. Startovni bod pro faze 4.
        /// </summary>
        public double Alpha = 0.05;

        /// <summary>
        /// Referencni mnozstvi <b>informativniho</b> dukazu [<b>m² · log-odds</b>], pri kterem plati
        /// <see cref="Alpha"/> beze zmeny. Nula = skalovani vypnuto (puvodni chovani).
        ///
        /// <para><b>Nacpak to je — otevreny ukol c. 1 „honestni sigma".</b> Skore je normovany
        /// PODIL souhlasicich bunek, takze o velikosti vzorku za sebou nevi nic. Sigma odvozena
        /// z jeho zakriveni (× konstantni <see cref="Alpha"/>) to nevi taky. Je to jako s anketou:
        /// tri dotazani se stoprocentni shodou vypadaji lip nez tri tisice s 94 %, ale verit se da
        /// druhemu cislu.</para>
        ///
        /// <para><b>Dusledek byl obraceny, nez by clovek cekal:</b> maly oblak hlasil MENSI sigma
        /// nez velky (2 214 bunek → 0,1412 m, 18 465 bunek → 0,2737 m), protoze nema nudne bunky
        /// daleko od okraje, ktere by procento redily. Vetsi jistota tam, kde je podkladu nejmin.</para>
        ///
        /// <para><b>Oprava:</b> <c>alphaEff = Alpha · (ReferenceInformativeEvidence / E_inf)</c>, kde
        /// <c>E_inf</c> je dukaz bunek, ktere skutecne rozlisuji mezi kandidaty (viz
        /// <see cref="CorrelationScorer.InformativeEvidence"/>). Sigma pak roste jako
        /// <c>1/sqrt(E_inf)</c> — tedy presne tak, jak se chova smerodatna odchylka podilu.
        /// Pri <c>E_inf = ReferenceInformativeEvidence</c> vyjde tataz sigma jako driv, takze se
        /// nemeni absolutni skala, jen jeji zavislost na mnozstvi dukazu.</para>
        ///
        /// <para><b>JEDNOTKY: m² · log-odds</b>, ne pocet bunek (prevedeno 25. 8. 2026 vecer).
        /// Puvodne se skalovalo surovym poctem informativnich bunek, a ten roste jako
        /// <c>1/plocha bunky</c> — reference namerena pri 5 cm by pri 10 cm znamenala ctyrikrat
        /// jine mnozstvi informace. Prave tohle branilo tomu, aby oprava mohla byt vychozim stavem.
        /// Ve fyzikalnich jednotkach je hodnota prenosna mezi rozlisenimi gridu i mezi kroky
        /// derivace (viz <see cref="CorrelationScorer.InformativeEvidence"/>); drzi to testy
        /// <c>HonestniSigma_ReferenceNezavisiNaRozliseniGridu</c> a <c>...NaKrokuDerivace</c>.
        /// <b>Prepocet stare hodnoty:</b> <c>surovy pocet × plocha bunky</c>, tedy namerenych
        /// 15 000 bunek pri 5 cm = <c>15000 × 0,0025</c> = <b>37,5</b>.</para>
        ///
        /// <para><b>VYCHOZI STAV od 25. 8. 2026 vecer: zapnuto</b> (37,5). Rozhodnuti autora, kdyz
        /// se zmerilo, ze reference je uz prenositelna mezi rozlisenimi gridu i kroky derivace —
        /// konstanta <c>Alpha · ReferenceInformativeEvidence</c> nastavuje jen absolutni skalu,
        /// presne jako predtim <c>Alpha</c> sama, takze zapnutim nevznika zadna nova vazba na scenu.
        /// <b>Nula = puvodni chovani</b> s konstantni <c>Alpha</c>, k A/B srovnani
        /// (<c>mapcorrref=0</c>). Cena zapnuti: strop sigma zahodi patologicky male oblaky
        /// (namereno 31 prijatych cyklu z 36).</para>
        /// </summary>
        public double ReferenceInformativeEvidence = 37.5;

        /// <summary>
        /// Nejvyssi hodnota <see cref="ReferenceInformativeEvidence"/>, ktera se jeste bere jako
        /// mysleny fyzikalni udaj [m² · log-odds].
        ///
        /// <para><b>Nacpak past:</b> do 25. 8. 2026 byla reference v POCTECH BUNEK a v dokumentaci
        /// i v prikazovych radkach se nosila hodnota <c>15000</c>. Ta same v novych jednotkach by
        /// znamenala 400x vic dukazu, tedy dvacetkrat vetsi sigmu — a vsechna merenia by tise
        /// spadla pod strop sigma. Radeji hlasitá chyba nez tichy nesmysl.</para>
        /// </summary>
        public const double MaxReferenceInformativeEvidence = 1000.0;

        /// <summary>
        /// Krok numericke druhe derivace skore pro posun [m]. Musi byt VYRAZNE VETSI nez rozliseni
        /// rastru: skore je kvuli rastru schodovite, takze na 5 cm by druha derivace merila
        /// kvantizacni sum, ne zakriveni maxima.
        /// </summary>
        public double HessianStepM = 0.20;

        /// <summary>Krok numericke druhe derivace skore pro kurz [rad]. Tentyz duvod jako u posunu.</summary>
        public double HessianStepHeadingRad = 2.0 * Math.PI / 180.0;

        /// <summary>Dolni hranice sigma posunu [m] - rozliseni gridu.</summary>
        public double SigmaFloorM = 0.05;

        /// <summary>Dolni hranice sigma kurzu [rad].</summary>
        public double SigmaFloorHeadingRad = 0.5 * Math.PI / 180.0;

        /// <summary>Nad touto sigma se osa posunu neposila [m].</summary>
        public double SigmaCeilingM = 5.0;

        /// <summary>Nad touto sigma se kurz neposila [rad].</summary>
        public double SigmaCeilingHeadingRad = 5.0 * Math.PI / 180.0;

        /// <summary>Nad timto posunem se nekoriguje vubec a hlasi se ztrata lokalizace [m].</summary>
        public double MaxOffsetM = 2.0;

        /// <summary>
        /// Rozsireni rastru mapy za hranu gridu [m]; musi byt >= <see cref="SearchRangeM"/> a v
        /// provozu se jeste zvedne na <see cref="RequiredRasterMarginM"/> podle skutecne geometrie
        /// gridu. Vychozi 4,0 m staci pro produkcni grid (256 bunek po 5 cm), takze se bezne
        /// nerozsiruje.
        /// </summary>
        public double MapRasterMarginM = 4.0;

        /// <summary>
        /// Min. odstup dvou korelaci = <b>DEKORELACNI CAS</b>, ne jen ochrana proti hustsim
        /// snapshotum.
        ///
        /// <para><b>Proc 3 s</b> (zmereno 25. 8. 2026, drive 400 ms). Grid drzi jen ~2,5 s historie,
        /// takze dva cykly blizsi nez to koreluji z VELKE CASTI TEHOZ nahromadeneho oblaku — jejich
        /// chyby nejsou nezavisle, ale fuze je jako nezavisle bere a kovarianci zuzuje rychleji, nez
        /// informace opravnuje. Autokorelace chyby to potvrdila: <c>rho(1) = 0,44..0,66</c>,
        /// <c>rho(2)</c> uz kolem nuly a dal zaporna. Cinitel nadsazeni informace
        /// <c>1 + 2·Σρ</c> vysel 1,88–2,44 na trech bezech.</para>
        ///
        /// <para><b>Ze je to fyzikalni konstanta, a ne artefakt vzorkovani</b>, ukazaly tytez tri
        /// behy: mely RUZNOU periodu cyklu (1,17 / 1,56 / 1,66 s — korelator je vypoctove vazany,
        /// takze perioda zavisi na rychlosti stroje), a presto vysel TYZ dekorelacni cas
        /// 2,85 / 2,93 / 3,31 s. Perioda 3 s je tedy zaokrouhlena namerena hodnota.</para>
        ///
        /// <para><b>Druhy, nezavisly duvod:</b> jeden cyklus stoji <b>1,31 s</b> (median, oblak
        /// 45 000 bunek) — tedy cele jadro, ne „ctvrt jadra", jak se dosud verilo. Pri odstupu 3 s
        /// klesne zatez na ~45 %. A hlavne: bez tohoto odstupu je FREKVENCE MERENII dana rychlostí
        /// CPU, takze na rychlejsim stroji by fuze byla VIC presvedcena o tomtez. To je vlastnost,
        /// kterou nikdo nechce mit nahodou.</para>
        ///
        /// <para>Viz doc/map-correlation-localization.md, „Casova korelace mezi cykly".</para>
        /// </summary>
        public TimeSpan MinPeriod = TimeSpan.FromSeconds(3.0);

        /// <summary>Zdroj merenia pro fuzi a telemetrii.</summary>
        public string MeasurementSource = "MapCorr";

        /// <summary>
        /// Rezim gatingu korekci z korelace. <b>Vychozi <see cref="Fusion.GateMode.Soft"/></b>
        /// (od 25. 8. 2026); <c>mapcorrgate=reject</c> vrati puvodni tvrdy gate pro A/B.
        ///
        /// <para><b>Tvrdy gate byl VADA — namereno.</b> Nad behem se skutecnym driftem
        /// (<c>wheelslip=1.03,0.97 imubias=3,0.2</c>, mapa videni = mapa jizdy) hlasi korelator chybu
        /// pozy SPRAVNE (vlastni chyba 0,02-0,06 m, sd(z) 0,74), ale <b>42-46 % korekci zahodil
        /// tvrdy gate</b> (NIS p50 3,6, p90 az 124) — a vysledna poloha byla HORSI, nez kdyz se
        /// nekorigovalo vubec. Pricna chyba pozy p50, dva behy na variantu:</para>
        ///
        /// <list type="table">
        ///   <item><term>korekce vypnute</term><description>0,674 / 0,675 m</description></item>
        ///   <item><term>tvrdy gate (Reject)</term><description>0,847 / 0,816 m — HORSI nez nic</description></item>
        ///   <item><term>soft gate</term><description>0,589 / 0,636 m — lepsi nez nic</description></item>
        /// </list>
        ///
        /// <para><b>Proc tvrdy gate skodi:</b> zahazuje prave ty VELKE korekce, ktere jsou potreba,
        /// a co projde, je vybrane podle toho, ze uz souhlasi. Vysledkem je zaujaty podvzorek, tedy
        /// horsi nez nekorigovat. <see cref="Fusion.GateMode.Soft"/> (<c>R' = R × NIS/prah</c>)
        /// odlehle merenie jen malo zvazi, nikdy nevypne — presne to, co
        /// doc/map-correlation-localization.md navrhuje uz od rozvahy o prime korekci: nesouhlas je
        /// PRECHODNY, takze staci jim projit.</para>
        ///
        /// <para>⚠️ <b>Cena:</b> se Soft se nezahodi NIC (0 % zamitnutych), takze gate uz nechrani
        /// proti korelaci, ktera se skutecne myli (spatna mapa, spatna kalibrace kamer). Tim roste
        /// vaha <b>podminky 3</b> (strop na nesouhlas s GPS) — a ta je porad otevrena. GPS to
        /// nezastoupi: ma sigma 1,5 m proti 0,088 m korelace, takze submetrovy odtah v jejim NIS
        /// vubec nevidi (zmereno).</para>
        /// </summary>
        public Fusion.GateMode GateMode = Fusion.GateMode.Soft;

        /// <summary>Urovne skenovani od nejhrubsi k nejjemnejsi.</summary>
        public ScanLevel[] Levels =
        {
            new ScanLevel { StepM = 0.40, StepHeadingRad = 4.0 * Math.PI / 180.0,
                            HalfRangeM = 2.5, HalfRangeHeadingRad = 8.0 * Math.PI / 180.0, Stride = 4 },
            new ScanLevel { StepM = 0.10, StepHeadingRad = 1.0 * Math.PI / 180.0,
                            HalfRangeM = 0.4, HalfRangeHeadingRad = 2.0 * Math.PI / 180.0, Stride = 1 },
            new ScanLevel { StepM = 0.05, StepHeadingRad = 0.5 * Math.PI / 180.0,
                            HalfRangeM = 0.1, HalfRangeHeadingRad = 0.5 * Math.PI / 180.0, Stride = 1 },
        };

        /// <summary>Nejvetsi posun, ktery muze kandidat mit = polovina okna nejhrubsi urovne [m].</summary>
        public double SearchRangeM => Levels.Length > 0 ? Levels[0].HalfRangeM : 0.0;

        /// <summary>
        /// Marze rastru, kterou si vyzada SKUTECNA geometrie gridu [m]:
        /// <c>SearchRangeM + HessianStepM + polovina diagonaly * sin(HalfRangeHeadingRad)</c>.
        ///
        /// <para><b>Proc je tam clen s rotaci:</b> kandidat oblak nejen POSUNE, ale i OTOCI kolem
        /// robotu - a rohova bunka gridu je od robotu daleko, takze ji uz maly uhel odnese hodne.
        /// Pri produkcnich hodnotach (256 bunek po 5 cm, tedy polovina diagonaly 9,05 m) posune 8°
        /// rotace rohovou bunku o 1,26 m; s posunem az 2,4 m a krokem Hessianu 0,2 m je to 3,66 m
        /// proti holym 3,0 m predchozi marze - a dukazy tedy skutecne padaly mimo rastr.</para>
        ///
        /// <para><b>Proc to vadi:</b> dukaz mimo rastr se preskoci z citatele I jmenovatele skore
        /// (mimo rastr znamena "nevim", ne "neni cesta"). U extremnich kandidatu jsou preskocene
        /// bunky prevazne ty NESOUHLASNE, takze vynechanim skore takoveho kandidata ROSTE - tedy
        /// zaujatost presne obracenym smerem, nez by clovek chtel.</para>
        ///
        /// <para>Krok Hessianu je v souctu proto, ze kovariance ohledava jeste
        /// <see cref="HessianStepM"/> za nalezenym maximem.</para>
        /// </summary>
        /// <param name="gridSize">Pocet bunek gridu na stranu.</param>
        /// <param name="resolution">Velikost bunky gridu [m].</param>
        public double RequiredRasterMarginM(int gridSize, double resolution)
        {
            double halfDiagonal = gridSize * resolution * Math.Sqrt(2.0) / 2.0;
            double heading = Levels != null && Levels.Length > 0 ? Levels[0].HalfRangeHeadingRad : 0.0;
            return SearchRangeM + HessianStepM + halfDiagonal * Math.Sin(heading);
        }

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (Levels == null || Levels.Length == 0)
                throw new ArgumentException("MapCorrelatorConfig.Levels musi mit aspon jednu uroven.");

            for (int i = 0; i < Levels.Length; i++)
            {
                var l = Levels[i];
                if (l.StepM <= 0 || l.StepHeadingRad <= 0)
                    throw new ArgumentException($"MapCorrelatorConfig.Levels[{i}]: kroky musi byt > 0.");
                if (l.HalfRangeM < 0 || l.HalfRangeHeadingRad < 0)
                    throw new ArgumentException($"MapCorrelatorConfig.Levels[{i}]: okna nesmi byt zaporna.");
                if (l.Stride < 1)
                    throw new ArgumentException($"MapCorrelatorConfig.Levels[{i}]: Stride musi byt >= 1.");
            }

            // Podlaha, ne cela pravda: skutecnou potrebu zna az volajici, ktery vidi velikost a
            // rozliseni gridu (viz RequiredRasterMarginM) - a ten si marzi za behu zvedne.
            if (MapRasterMarginM < SearchRangeM)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.MapRasterMarginM ({MapRasterMarginM}) musi byt >= "
                    + $"SearchRangeM ({SearchRangeM}), jinak kandidat saha mimo rastr.");
            if (Alpha <= 0)
                throw new ArgumentException($"MapCorrelatorConfig.Alpha musi byt > 0, je {Alpha}.");
            if (ReferenceInformativeEvidence < 0)
                throw new ArgumentException(
                    "MapCorrelatorConfig.ReferenceInformativeEvidence nesmi byt zaporna "
                    + $"(je {ReferenceInformativeEvidence}); nula znamena vypnute skalovani.");
            if (ReferenceInformativeEvidence > MaxReferenceInformativeEvidence)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.ReferenceInformativeEvidence ({ReferenceInformativeEvidence}) "
                    + $"je nad hranici {MaxReferenceInformativeEvidence} m²·log-odds. Nejde omylem "
                    + "o starou hodnotu v POCTECH BUNEK? Od 25. 8. 2026 je reference fyzikalni "
                    + "velicina - prepocet je 'pocet × plocha bunky', tedy 15000 pri 5 cm = 37,5.");
            if (HessianStepM <= 0 || HessianStepHeadingRad <= 0)
                throw new ArgumentException("MapCorrelatorConfig: kroky Hessianu musi byt > 0.");
            if (EvidenceThreshold <= 0)
                throw new ArgumentException("MapCorrelatorConfig.EvidenceThreshold musi byt > 0.");
            if (MinEvidenceCells < 1)
                throw new ArgumentException("MapCorrelatorConfig.MinEvidenceCells musi byt >= 1.");
            if (SigmaFloorM <= 0 || SigmaFloorHeadingRad <= 0)
                throw new ArgumentException("MapCorrelatorConfig: dolni hranice sigma musi byt > 0.");
            if (SigmaFloorM > SigmaCeilingM)
                throw new ArgumentException(
                    $"MapCorrelatorConfig: SigmaFloorM ({SigmaFloorM}) > SigmaCeilingM ({SigmaCeilingM}).");
            if (SigmaFloorHeadingRad > SigmaCeilingHeadingRad)
                throw new ArgumentException(
                    "MapCorrelatorConfig: SigmaFloorHeadingRad > SigmaCeilingHeadingRad.");
            if (MaxOffsetM <= 0)
                throw new ArgumentException("MapCorrelatorConfig.MaxOffsetM musi byt > 0.");
            // Skore je z rozsahu -1..1, takze mimo nej je prah bud vzdy, nebo nikdy splneny.
            if (MinScore < -1.0 || MinScore > 1.0)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.MinScore ({MinScore}) musi byt v rozsahu -1..1 - skore "
                    + "je normovane, takze mimo nej by prah platil vzdy, nebo nikdy.");
            // Zaporna marze by test nejednoznacnosti OBRATILA: prah by lezel NAD maximem, takze by
            // se jako nejednoznacny hlasil i cyklus s vyrazne horsim konkurentem.
            if (AmbiguityMargin < 0)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.AmbiguityMargin ({AmbiguityMargin}) nesmi byt zaporna - "
                    + "prah by se dostal nad maximum a test nejednoznacnosti by se obratil.");
            // Zaporna perioda znamena tise vypnute omezovani (odstup se pak porovnava se zapornym
            // cislem, tedy nikdy neplati).
            if (MinPeriod < TimeSpan.Zero)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.MinPeriod ({MinPeriod}) nesmi byt zaporna - omezovani "
                    + "frekvence by bylo tise vypnute.");
            if (AmbiguitySeparationM <= 0)
                throw new ArgumentException("MapCorrelatorConfig.AmbiguitySeparationM musi byt > 0.");
            if (AmbiguitySeparationM > SearchRangeM)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.AmbiguitySeparationM ({AmbiguitySeparationM}) musi byt <= "
                    + $"SearchRangeM ({SearchRangeM}), jinak se konkurent nikdy nevzorkuje a test "
                    + "nejednoznacnosti je tise vypnuty.");
        }
    }
}
