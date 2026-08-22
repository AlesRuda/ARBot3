using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Models;
using ARBot.Common.Occupancy;
using ARBot.Common.Telemetry;

namespace ARBot.Telemetry
{
    /// <summary>
    /// Registr sloupcu telemetrickeho pohledu - <b>pridat udaj = jeden zaznam v <see cref="All"/></b>.
    ///
    /// <para>Zamerne v UI vrstve: jednotky, format a "co ma smysl kreslit" jsou prezentacni vec,
    /// takze se kvuli nim nesaha do <c>ARBot.Common</c> a domena nedostane starosti UI.
    /// Viz doc/telemetry-view.md.</para>
    ///
    /// <para>Registr zaroven urcuje, ktere zpravy sken ze zaznamu vubec cte - typ, ktery tu nema
    /// sloupec, se preskoci bez cteni.</para>
    /// </summary>
    public static class TelemetryColumns
    {
        /// <summary>Vsechny sloupce v poradi, v jakem se maji zobrazit.</summary>
        public static IReadOnlyList<ColumnSpec> All { get; } = Build();

        private static List<ColumnSpec> Build()
        {
            var list = new List<ColumnSpec>
        {
            // --- fuzovana poza ---
            Num<RobotStateMsg>("X [m]", m => m.X,
                "Poloha robotu v ose X (world ENU, východ) z fúze EKF. Počátek = místo startu."),
            Num<RobotStateMsg>("Y [m]", m => m.Y,
                "Poloha robotu v ose Y (world ENU, sever) z fúze EKF. Počátek = místo startu."),
            Num<RobotStateMsg>("theta [°]", m => Conversions.Rad2Deg(m.Theta),
                "Kurz robotu z fúze: matematická orientace ve world ENU (0° = východ, kladně proti "
                + "směru hodinových ručiček). Tlačítkem Azimut v liště se přepne na světovou konvenci "
                + "(0° = sever, po směru hodinových ručiček).", "F1"),
            Num<RobotStateMsg>("v [m/s]", m => m.V,
                "Skutečná dopředná rychlost robotu z fúze (odometrie + IMU + GPS), ne požadovaná."),
            Num<RobotStateMsg>("omega [°/s]", m => Conversions.Rad2Deg(m.Omega),
                "Skutečná úhlová rychlost otáčení z fúze; kladně = doleva (proti hodinovým "
                + "ručičkám); s přepínačem Azimut je kladně doprava.", "F1"),

            // --- skutecna poza (ground truth, jen virtualni HW) ---
            // Emituje se na temze tiku a se stejnym casem jako RobotStateMsg, takze rozdil proti
            // sloupcum X/Y/theta vyse je primo chyba lokalizace. Viz doc/virtual-hw.md.
            Num<GroundTruthMsg>("truth X [m]", m => m.X,
                "SKUTEČNÁ poloha simulovaného robota v ose X (world ENU, východ). Jen při "
                + "virtuálním HW. Rozdíl proti „X“ je chyba lokalizace."),
            Num<GroundTruthMsg>("truth Y [m]", m => m.Y,
                "SKUTEČNÁ poloha simulovaného robota v ose Y (world ENU, sever). Jen při "
                + "virtuálním HW. Rozdíl proti „Y“ je chyba lokalizace."),
            Num<GroundTruthMsg>("truth theta [°]", m => Conversions.Rad2Deg(m.Theta),
                "SKUTEČNÝ kurz simulovaného robota (matematická orientace, 0° = východ). Rozdíl "
                + "proti „theta“ je chyba kurzu.", "F1"),
            Num<GroundTruthMsg>("truth v [m/s]", m => m.V,
                "SKUTEČNÁ dopředná rychlost simulovaného robota (po prokluzu kol). Rozdíl proti "
                + "rychlosti z odometrie ukazuje, kolik kola proklouzla."),
            Num<GroundTruthMsg>("prokluz L [-]", m => m.LeftWheelSlip,
                "Nastavený prokluz levého kola v tomto běhu (1 = ideál). V záznamu kvůli tomu, "
                + "aby šlo dohledat, s jakou vnucenou chybou experiment běžel.", "F4"),
            Num<GroundTruthMsg>("prokluz P [-]", m => m.RightWheelSlip,
                "Nastavený prokluz pravého kola v tomto běhu (1 = ideál).", "F4"),

            // --- ridici zasah ---
            Num<DriveCommandMsg>("cmd v [m/s]", m => m.Speed,
                "Požadovaná dopředná rychlost poslaná do motorů. Rozdíl proti „v“ ukazuje, jak "
                + "robot na příkaz reaguje."),
            Num<DriveCommandMsg>("cmd omega [°/s]", m => Conversions.Rad2Deg(m.RotationSpeed),
                "Požadovaná úhlová rychlost poslaná do motorů; kladně = doleva (s přepínačem Azimut doprava).", "F1"),
            Num<DriveCommandMsg>("cmd dif [m/s]", m => m.Dif,
                "Rozdíl rychlostí levého a pravého pásu odpovídající požadovanému zatáčení "
                + "(diferenciální řízení)."),
            Flag<DriveCommandMsg>("STOP", m => m.EmergencyStop,
                "Nouzové zastavení v tomto příkazu: „STOP“ = motory zastaveny bezpečnostní "
                + "podmínkou, „-“ = jede se normálně."),

            // --- lokalni plan ---
            Enum<LocalPlanMsg, LocalPlanStatus>("plan stav", m => m.Status,
                "Výsledek lokálního plánovače nad occupancy gridem (nalezena cesta / cíl "
                + "nedosažitelný / bez cíle…). Viz doc/occupancy-and-local-planning.md."),
            Num<LocalPlanMsg>("plan delka [m]", m => m.LengthM,
                "Délka naplánované lokální cesty od robotu k lokálnímu cíli („mrkvi“)."),
            Num<LocalPlanMsg>("plan odstup [m]", m => m.MinClearanceM,
                "Nejmenší odstup naplánované cesty od překážky. Malá hodnota = plán vede těsně "
                + "kolem překážky, plánovač tam sráží rychlost."),
            Num<LocalPlanMsg>("plan bodu", m => m.WayPoints?.Length ?? 0,
                "Počet waypointů, které plánovač předal regulátoru.", "F0"),
            Num<LocalPlanMsg>("plan vypocet [ms]", m => m.ComputeMs,
                "Doba výpočtu jednoho lokálního plánu (A*). Diagnostika zátěže řídicí smyčky.", "F1"),

            // --- globalni navigace ---
            Enum<GlobalNavMsg, GlobalNavStatus>("nav stav", m => m.Status,
                "Stav globální navigace nad OSM sítí (bez cíle / jede k cíli / cíl dosažen / "
                + "bloudění / zásek…). Viz doc/global-navigation-runtime.md."),
            Num<GlobalNavMsg>("do cile [m]", m => m.RouteLengthM,
                "Zbývající délka trasy do cíle měřená po síti cest (ne vzdušnou čarou).", "F0"),
            Num<GlobalNavMsg>("hran trasy", m => m.RouteEdgeCount,
                "Počet hran grafu, které do cíle ještě zbývají projet.", "F0"),
            Num<GlobalNavMsg>("od site [m]", m => m.OffRouteDist,
                "Kolmá vzdálenost robotu od hrany, po které má jet. Rostoucí hodnota = robot "
                + "sjíždí z trasy."),
            // Tri desetinna mista: phi se mezi takty meni o zlomky sekundy a na F1 vypadalo
            // zamrzle (dva sousedni radky mely tutez hodnotu, i kdyz robot popojel).
            Num<GlobalNavMsg>("fi [s]", m => m.Phi,
                "Cost-to-goal aktuální pozice v poli globální navigace (odhad zbývajícího času "
                + "jízdy do cíle podle dopravního profilu).", "F3"),
            Num<GlobalNavMsg>("uzavreno hran", m => m.ClosureCount,
                "Kolik hran navigace zatím uzavřela jako neprůjezdné (přehrazená cesta). Skok "
                + "znamená, že se právě přeplánovávalo.", "F0"),

            // --- korelace s mapou (odhad polohy) ---
            Num<MapCorrelationMsg>("korel dx [m]", m => m.Dx,
                "Naměřený posun na východ: skutečná poloha = odhad + dx. Trvale nenulová hodnota "
                + "znamená systematickou chybu lokalizace. Viz doc/map-correlation-localization.md."),
            Num<MapCorrelationMsg>("korel dy [m]", m => m.Dy,
                "Naměřený posun na sever: skutečná poloha = odhad + dy."),
            Num<MapCorrelationMsg>("korel fi [°]", m => Deg(m.Phi),
                "Naměřená chyba kurzu: skutečný kurz = odhad + fi."),
            Num<MapCorrelationMsg>("korel skore", m => m.Score,
                "Shoda semantiky gridu s vozovkou podle mapy (-1 až 1). Zároveň metrika kvality: "
                + "pod prahem korelátor mlčí, protože robot nejspíš není na mapované cestě.", "F3"),
            Num<MapCorrelationMsg>("korel konkurent", m => m.SecondBestScore,
                "Skóre nejlepšího vzdáleného konkurenta. Když se přiblíží skóre maxima, je shoda "
                + "nejednoznačná (souběžná cesta) a nekoriguje se.", "F3"),
            Num<MapCorrelationMsg>("korel konk+", m => m.SecondBestScoreLoose,
                "Skóre nejlepšího konkurenta podél HŮŘE určené osy (podél cesty). Když je vypnutý "
                + "sloupec „korel os+“ a přitom je tohle číslo blízko skóre maxima, osu vynechal "
                + "právě tenhle konkurent, ne strop sigma — a to je příznak falešné podélné "
                + "jistoty. Prázdné (−∞) znamená, že se konkurent vůbec neměřil.", "F3"),
            Num<MapCorrelationMsg>("korel osa [°]", m => Deg(m.TightAxisAngle),
                "Směr LÉPE určené osy (matematicky, 0° = východ). Přímá kontrola, že určená osa "
                + "míří skutečně napříč cestou — jinak korekce tlačí robot jinam, než se čeká.", "F1"),
            Num<MapCorrelationMsg>("korel sig- [m]", m => m.SigmaTight,
                "Sigma LÉPE určené osy posunu — na cestě typicky napříč. Malá hodnota = příčné "
                + "poloze se dá věřit."),
            Num<MapCorrelationMsg>("korel sig+ [m]", m => m.SigmaLoose,
                "Sigma HŮŘE určené osy posunu — na přímé cestě podél. Velká hodnota je správná "
                + "odpověď, ne chyba: podélná poloha bez odbočky není určená."),
            Num<MapCorrelationMsg>("korel sig fi [°]", m => Deg(m.SigmaPhi),
                "Sigma naměřené chyby kurzu."),
            Num<MapCorrelationMsg>("korel bunek", m => m.EvidenceCells,
                "Kolik buněk gridu vstoupilo do korelace. Malé číslo = semantika ještě nemá dost "
                + "dat (souvisí s okluzním pravidlem InShadow).", "F0"),
            Flag<MapCorrelationMsg>("korel", m => m.Emitted,
                "Poslala se do fúze aspoň jedna korekce? Když ne, důvod je ve sloupci „korel duvod“."),
            Flag<MapCorrelationMsg>("korel os-", m => m.EmitTightAxis,
                "Poslala se korekce podél LÉPE určené osy (na cestě typicky napříč)? Na přímé cestě "
                + "je to běžný stav."),
            Flag<MapCorrelationMsg>("korel os+", m => m.EmitLooseAxis,
                "Poslala se korekce podél HŮŘE určené osy (podél cesty)? Na přímé cestě má být "
                + "vypnutá — podélná sigma přeroste strop. Když svítí trvale, něco předstírá "
                + "podélnou jistotu."),
            Flag<MapCorrelationMsg>("korel kurz", m => m.EmitHeading,
                "Poslala se korekce kurzu?"),
            Enum<MapCorrelationMsg, MapCorrelationReason>("korel duvod", m => m.Reason,
                "Proč se (ne)korigovalo: Ok / málo důkazů / nízké skóre / nejednoznačné / "
                + "příliš velký posun / žádné maximum."),
            Num<MapCorrelationMsg>("korel vypocet [ms]", m => m.ProcessingMs,
                "Doba výpočtu jednoho cyklu korelace. Diagnostika zátěže (na ARM je to hlídané).", "F1"),
            Num<MapCorrelationMsg>("korel zahozeno fuzi", m => m.DroppedByFusion,
                "Kolik korekcí z korelace už fúze zahodila, protože přišly STARŠÍ než okno historie "
                + "(kumulativně za běh). Když tohle roste, „korel“ svítí a přitom se do fúze "
                + "nedostane nic — výpočet je pomalejší, než dovolí okno. Naměřeno 21. 8. 2026 "
                + "v Debug buildu: 12 korekcí z 5 cyklů.", "F0"),

            // --- hranova lokalizace: koridor z obrazu proti ose cesty z mapy (corridor=true) ---
            Num<RoadCorridorMsg>("kor sirka [m]", m => m.Width,
                "Šířka koridoru měřená z hranic cesty v obraze. Srovnává se se šířkou z mapy "
                + "(sloupec „kor sirka mapa“); trvalý rozdíl znamená, že OSM `width` nesedí.", "F2"),
            Num<RoadCorridorMsg>("kor pricne [m]", m => m.Lateral,
                "Příčná poloha robotu vůči ose koridoru z kamer; kladné = robot je vlevo od osy."),
            Num<RoadCorridorMsg>("kor smer [°]", m => Deg(m.DirectionRad),
                "Směr cesty v rámci robotu podle kamer; 0 = cesta vede přímo vpřed."),
            Num<RoadCorridorMsg>("kor sig pricne [m]", m => m.SigmaLateral,
                "σ příčné polohy z rozptylu reziduí proložení. Nedělí se √n (sousední hraniční body "
                + "si chybu detekce sdílejí), podlaha 3 cm odpovídá naměřené opakovatelnosti.", "F3"),
            Num<RoadCorridorMsg>("kor inlieru L", m => m.InliersLeft,
                "Kolik bodů RANSAC použil na levou hranici. Málo inlierů = přímka proložená šumem; "
                + "to je ten gate, který nad záznamem zahodil polovinu snímků.", "F0"),
            Num<RoadCorridorMsg>("kor inlieru P", m => m.InliersRight,
                "Totéž pro pravou hranici. Každá kamera vidí jen jednu stranu, takže koridor "
                + "vzniká z dvojice snímků.", "F0"),
            Num<RoadCorridorMsg>("kor nerovnobeznost [°]", m => Deg(m.ParallelErrorRad),
                "O kolik se liší směr levé a pravé hranice. Nad 10° se cyklus zahodí jako "
                + "„NotParallel“ — to je za jízdy nejčastější důvod, proč koridor nic nepošle "
                + "(u stojícího robota nenastane vůbec). Ve starších záznamech je 0.", "F1"),
            Num<RoadCorridorMsg>("kor hranice L [°]", m => Deg(m.DirectionLeftRad),
                "Směr LEVÉ hranice v rámci robotu (0 = cesta vede rovně vpřed). Spolu s pravou "
                + "řekne, která strana je vedle — průměr („kor smer“) se při zamítnutí nepočítá.", "F1"),
            Num<RoadCorridorMsg>("kor hranice P [°]", m => Deg(m.DirectionRightRad),
                "Totéž pro pravou hranici. Symetrické sbíhání obou (L kladně, P záporně) = hranice "
                + "se ohýbají dovnitř s rostoucí vzdáleností; symetrické rozbíhání = konec cesty.", "F1"),
            Num<RoadCorridorMsg>("kor pricne mapa [m]", m => m.MapLateral,
                "Odstup pózy od osy cesty podle mapy; kladné = vlevo. Protistrana k „kor pricne“."),
            Num<RoadCorridorMsg>("kor sirka mapa [m]", m => m.MapWidth,
                "Šířka, se kterou se srovnávalo — z filtru šířky, dokud nemá odhad, tak z mapy.", "F2"),
            Num<RoadCorridorMsg>("kor rozdil pricne [m]", m => m.LateralDisagreement,
                "KAMERA MINUS MAPA příčně — vlastní chyba lokalizace, kterou měření opravuje. "
                + "Nad záznamem se takto našel vnucený rozdíl dvou map: 0,51 m ± 0,03."),
            Num<RoadCorridorMsg>("kor rozdil smer [°]", m => Deg(m.HeadingDisagreementRad),
                "Kamera minus mapa ve sklonu cesty — chyba kurzu."),
            Num<RoadCorridorMsg>("kor rozdil sirka [m]", m => m.WidthDisagreement,
                "Kamera minus mapa v šířce. Velký rozdíl = proložila se jiná dvojice hranic než "
                + "ta cesta, a měření se nepustí.", "F2"),
            Flag<RoadCorridorMsg>("kor pricna", m => m.EmittedLateral,
                "Poslala se do fúze příčná korekce?"),
            Flag<RoadCorridorMsg>("kor kurz", m => m.EmittedHeading,
                "Poslala se korekce kurzu?"),
            Enum<RoadCorridorMsg, ARBot.Common.Localization.CorridorReason>("kor duvod", m => m.CorridorReason,
                "Proč koridor (ne)vznikl: Ok / málo bodů / jen jedna hranice / hranice nejsou "
                + "rovnoběžné / nesmyslná šířka / málo inlierů."),
            Num<RoadCorridorMsg>("kor zahozeno fuzi", m => m.DroppedByFusion,
                "Kolik měření z hranové lokalizace už fúze zahodila, protože přišla STARŠÍ než okno "
                + "historie (kumulativně). „kor pricna“ říká „poslali jsme“, ne „došlo to“ — když "
                + "tohle roste, do fúze se nedostane nic. Stejná past jako u plošné korelace.", "F0"),
            Enum<RoadCorridorMsg, ARBot.Common.Localization.CorridorFixReason>("kor fix", m => m.FixReason,
                "Proč se z koridoru (ne)stalo měření: Ok / bez koridoru / chybí druhá kamera / "
                + "fúze nezná pózu / mapa bez cesty / hrana daleko / nesouhlas příčně / nesouhlas šířky."),

            // --- verdikt fuze u jednotlivych merenii (jen s parametrem measdiag=) ---
            // Zdroj merenia nese INamedMessage.Name, takze radky rozlisi sloupec „Jmeno“; vlastni
            // textovy sloupec by tabulka (ciselna) neumela.
            Num<MeasurementDiagMsg>("mereni NIS", m => m.Nis,
                "Normalized innovation squared — jak daleko bylo měření od predikce, v jednotkách "
                + "vlastního rozptylu. Nad prahem gatingu se měření zahodí. NaN = měření přišlo "
                + "pozdě, na NIS vůbec nedošlo.", "F2"),
            Enum<MeasurementDiagMsg, ARBot.Common.Fusion.MeasurementVerdict>("mereni verdikt", m => m.Verdict,
                "Jak s měřením fúze naložila: Accepted (aplikovalo se) / GatedOut (zamítl gating "
                + "jako odlehlé) / TooOld (přišlo starší než okno historie, do filtru vůbec "
                + "nevstoupilo). Rozdíl mezi GatedOut a TooOld je rozdíl mezi „opravit sigma“ "
                + "a „zkrátit výpočet“."),

            // --- surove GPS (bez fuze) ---
            Num<GPSState>("GPS lat [°]", m => m.Latitude,
                "Zeměpisná šířka přímo z GPS přijímače (WGS84), tedy bez fúze.", "F6"),
            Num<GPSState>("GPS lon [°]", m => m.Longitude,
                "Zeměpisná délka přímo z GPS přijímače (WGS84), tedy bez fúze.", "F6"),
            Enum<GPSState, GPSState.FixQuality>("GPS fix", m => (int)m.Quality,
                "Kvalita fixu hlášená přijímačem (žádný fix / GPS / DGPS / RTK…). Určuje, jakou "
                + "váhu má GPS ve fúzi."),
            Num<GPSState>("GPS satelitu", m => m.NumberOfSatellites,
                "Počet družic použitých k výpočtu polohy. Propad bývá první příznak zákrytu.", "F0"),
            Num<GPSState>("GPS HDOP", m => m.Hdop,
                "Horizontální rozptyl přesnosti — čím menší, tím lepší geometrie družic "
                + "(pod 1 výborné, nad 5 nepoužitelné)."),

            // --- surove GPS: dopocitane udaje (kurz a rychlost z pohybu nebo ze dvou anten) ---
            Num<GPSState>("GPS alt [m]", m => m.Altitude,
                "Výška nad elipsoidem z GPS. Bývá výrazně horší než poloha, do fúze nevstupuje.", "F1"),
            Num<GPSState>("GPS v [m/s]", m => m.Speed ?? m.DynamicSpeed,
                "Rychlost z přijímače (dvouanténní, jinak dopočtená z pohybu). Nezávislá kontrola "
                + "rychlosti z fúze i z kol."),
            Num<GPSState>("GPS kurz [°]", m => Deg(m.Orientation ?? m.DynamicOrientation),
                "Kurz z GPS: přednost má dvouanténní Orientation, jinak se dopočítá z pohybu "
                + "(v klidu je nesmyslný). Zobrazuje se v konvenci zvolené v liště.", "F0"),

            // --- motory (MotorStateBase) - to, co robot skutecne udelal, proti tomu co dostal prikazem ---
            Num<MotorStateBase>("kolo L [m/s]", m => m.LeftWheelSpeed,
                "Skutečná rychlost levého kola. Driver ji měří ze svého vzorkovacího intervalu, "
                + "takže nezávisí na tom, kdo a kdy měření vyzvedl."),
            Num<MotorStateBase>("kolo R [m/s]", m => m.RightWheelSpeed,
                "Skutečná rychlost pravého kola. Rozdíl proti levému ukazuje reálné zatáčení."),
            // Odometrie prepoctena na pohyb robotu - tataz dvojice velicin, jakou zadava ridici
            // smycka (cmd v / cmd omega) a jakou vydava fuze (v / omega), takze jdou srovnat
            // vedle sebe: prikaz -> skutecnost -> co si o tom mysli fuze.
            Num<MotorStateBase>("odo v [m/s]", m => (m.LeftWheelSpeed + m.RightWheelSpeed) / 2,
                "Dopředná rychlost robotu spočtená z rychlostí kol ((vL + vR) / 2). Měřená "
                + "skutečnost, bez fúze a bez GPS."),
            Num<MotorStateBase>("odo omega [°/s]", m => Conversions.Rad2Deg(
                    (m.RightWheelSpeed - m.LeftWheelSpeed) / Profile.Rozchod),
                "Rychlost otáčení z odometrie ((vR − vL) / rozchod, rozchod " + Profile.Rozchod
                + " m) — tentýž vzorec, jakým ji počítá fúze. Rozdíl proti „cmd omega“ ukazuje, "
                + "jestli robot zatáčku vůbec provedl.", "F1"),
            Num<MotorStateBase>("enc L [m]", m => m.LeftEncoder,
                "Kumulativní ujetá dráha levého kola od startu (od verze 2 zprávy kumulativní, "
                + "ne přírůstek).", "F1"),
            Num<MotorStateBase>("enc R [m]", m => m.RightEncoder,
                "Kumulativní ujetá dráha pravého kola od startu.", "F1"),
            Num<MotorStateBase>("bat [V]", m => m.Voltage,
                "Napětí baterie. Propad při rozjezdu = zatížení, trvalý pokles = vybíjení."),
            Num<MotorStateBase>("I L [A]", m => m.LeftMotorCurrent,
                "Proud levého motoru. Vysoký proud při malé rychlosti = zablokované kolo nebo "
                + "těžký terén."),
            Num<MotorStateBase>("I R [A]", m => m.RightMotorCurrent,
                "Proud pravého motoru."),
            Flag<MotorStateBase>("HW STOP", m => m.IsEmergencyStop,
                "Nouzové zastavení hlášené HARDWAREM motorů. Proti sloupci „STOP“ (co si o stopu "
                + "myslela řídicí smyčka) ukáže, jestli se smyčka a hardware shodnou."),
            Num<MotorStateBase>("mot drop", m => m.DropedOutNum,
                "Kolik vzorků driver motorů zahodil před tímto (nestíhané čtení). Nenulové = "
                + "měření chybí a odometrie ve fúzi je řidší, než by měla být.", "F0"),

            // --- IMU (IMUState) - orientace a dynamika v telovem ramci FLU (X vpred, Y vlevo, Z nahoru) ---
            Num<IMUState>("IMU yaw [°]", m => Deg(m.YPR()?.Yaw),
                "Kurz z IMU dopočtený z kvaternionu, ve stejné konvenci jako „theta“ (řídí ji přepínač Azimut). Proti "
                + "„theta“ z fúze ukáže, jak moc fúze IMU koriguje.", "F1"),
            Num<IMUState>("IMU pitch [°]", m => Deg(m.YPR()?.Pitch),
                "Naklonění dopředu/dozadu. Ve svahu vysvětluje, proč robot zpomalil nebo prokluzuje.", "F1"),
            Num<IMUState>("IMU roll [°]", m => Deg(m.YPR()?.Roll),
                "Naklonění do stran. Velká hodnota = riziko převrácení.", "F1"),
            Num<IMUState>("gyro z [°/s]", m => Deg(m.AngularVelocity?.Z),
                "Rychlost otáčení kolem svislé osy těla (Z nahoru), tedy měřená rychlost zatáčení; znaménko řídí přepínač Azimut. "
                + "Srovnej s „omega“ z fúze a „cmd omega“ z příkazu.", "F1"),
            Num<IMUState>("acc x [m/s²]", m => m.Acceleration?.X,
                "Zrychlení v podélné ose těla (X vpřed). Skoky = rázy z terénu nebo tvrdé brzdění."),
            Num<IMUState>("acc z [m/s²]", m => m.Acceleration?.Z,
                "Zrychlení ve svislé ose těla (Z nahoru). Obsahuje i tíhové zrychlení, pokud ho "
                + "driver neodečítá — sleduj spíš rozkmit než absolutní hodnotu."),
            Num<IMUState>("IMU conf", m => m.Confidence,
                "Důvěra hlášená IMU k tomuto vzorku (0–1). Nízká = orientaci neber vážně."),
            Num<IMUState>("IMU drop", m => m.DropedOutNum,
                "Kolik vzorků IMU se zahodilo před tímto. Nenulové = fúze má ve vstupu díry.", "F0"),
            };

            // Uhlove udaje na JEDNOM miste - jinak se konvence rozejdou (drive mel GPS kurz
            // azimut, kdezto theta a IMU yaw matematickou orientaci). Ulozeno je vzdy matematicky
            // (kurz 0 = vychod a +CCW, kladna rychlost = doleva); prepnuti zobrazeni resi lista
            // pohledu pres TelemetryTable.AngleMode. Viz doc/telemetry-view.md.
            Mark(list, AngleKind.Heading, "theta [°]", "IMU yaw [°]", "GPS kurz [°]");
            Mark(list, AngleKind.Rate, "omega [°/s]", "cmd omega [°/s]", "gyro z [°/s]",
                 "odo omega [°/s]");
            // POZN.: IMU pitch/roll jsou naklony, ne kurzy - konvence se jich netyka.

            return list;
        }

        /// <summary>
        /// Oznaci sloupce podle zahlavi jako uhlove. <b>Neznamé zahlavi je chyba</b> - kdyby se
        /// sloupec prejmenoval, priznak by se jinak tise ztratil a tabulka by zase michala dve
        /// konvence.
        /// </summary>
        private static void Mark(List<ColumnSpec> columns, AngleKind kind, params string[] headers)
        {
            foreach (var header in headers)
            {
                var spec = columns.Find(c => c.Header == header);
                if (spec == null)
                    throw new InvalidOperationException(
                        $"TelemetryColumns: sloupec \"{header}\" neexistuje, nelze mu nastavit {kind}.");
                spec.Angle = kind;
            }
        }

        /// <summary>
        /// Ciselny sloupec z jedne zpravy. <c>MsgName</c> se bere z prototypu, aby se nazev typu
        /// nepsal jako retezec (a nerozesel se pri prejmenovani).
        /// </summary>
        /// <param name="description">Vysvetleni udaje do tooltipu (zahlavi je jen zkratka).</param>
        private static ColumnSpec Num<T>(string header, Func<T, double?> value, string description,
                                         string format = "F2")
            where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Description = description,
                Format = format,
                Value = m => m is T typed ? value(typed) : (double?)null,
            };

        /// <summary>
        /// Radiany na stupne pro NEPOVINNOU hodnotu - senzorova pole jsou nullable (IMU nemusi
        /// dodat orientaci ani gyro), a chybejici hodnota musi zustat chybejici, ne nula.
        /// </summary>
        private static double? Deg(double? rad)
            => rad.HasValue ? Conversions.Rad2Deg(rad.Value) : (double?)null;

        /// <summary>Logicky sloupec: zobrazi se jako zkratka (kdyz plati), jinak pomlcka.</summary>
        private static ColumnSpec Flag<T>(string header, Func<T, bool> value, string description)
            where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Description = description,
                Format = "F0",
                Value = m => m is T typed ? (value(typed) ? 1.0 : 0.0) : (double?)null,
                Text = v => v != 0 ? header : "-",
            };

        /// <summary>Vyctovy sloupec: v tabulce jmeno hodnoty, v grafu schod.</summary>
        private static ColumnSpec Enum<T, TEnum>(string header, Func<T, int> value, string description)
            where T : Message, new() where TEnum : struct, System.Enum
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Description = description,
                Format = "F0",
                Value = m => m is T typed ? value(typed) : (double?)null,
                // Prevod resi EnumPresentation v ARBot.Common - je tam kvuli testum a kvuli pasti
                // s podkladovym typem vyctu, kterou popisuje jeho dokumentace.
                Text = v => EnumPresentation.Text<TEnum>((int)v),
            };
    }
}
