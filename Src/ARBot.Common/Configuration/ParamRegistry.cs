using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Missions;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Configuration
{
    /// <summary>
    /// Seznam VSECH konfiguracnich parametru aplikace - jedine misto, kde parametr vznika, a od
    /// 4. 9. 2026 i jedine misto, odkud se cte: kazdy parametr je verejne staticke pole typovaneho
    /// odkazu (<see cref="BoolParam"/>, <see cref="DoubleParam"/>, <see cref="StringParam"/>,
    /// <see cref="PathParam"/>), takze se cte <c>ParamRegistry.NoUart.Value</c>. Spatny klic se
    /// nepreloži, misto cteni najde <i>Find references</i>, a default je definovany presne jednou.
    ///
    /// <para><b>Jmeno pole = klic v PascalCase</b> (<c>no_uart</c> -&gt; <c>NoUart</c>,
    /// <c>st_images_active</c> -&gt; <c>StImagesActive</c>); shodu hlida
    /// <c>ParamRegistryGuardTests</c> reflexi. Popis, kategorie a validace jsou u deklarace, panel
    /// Konfigurace i zapis profilu jedou nad <see cref="All"/>.</para>
    ///
    /// <para><b>Defaulty z kodu se nepisou rucne:</b> co ma pravdu v <see cref="Profile"/>
    /// (<c>maxspeed</c>, <c>safedist</c>, porty UART) nebo v konfiguracni tride
    /// (<c>freerunlook</c> = <see cref="FreeRunConfig.LookaheadM"/>, <c>depotfix</c>,
    /// <c>depthnoise</c>/<c>grassrough</c>/<c>grassheight</c> = <see cref="SyntheticSceneOptions"/>),
    /// si registr pri startu PRECTE (<see cref="Fmt"/>). Driv tu byla textova kopie s varovanim
    /// "kdyz se zmeni tam, musi se zmenit i tady" - a presne to se 3. 9. 2026 delalo rucne. Porty
    /// UART byly "default z kodu podle detekce", ve skutecnosti jsou to konstanty podle platformy
    /// (<c>#if IsX64</c> / <c>IsARM64</c>) - ty ted bydli v <see cref="Profile"/>.</para>
    ///
    /// <para>Statická inicializace: pole se plni v poradi deklarace a kazde se pres <see cref="Add{T}"/>
    /// zapise do <see cref="All"/>, ktery je deklarovany jako prvni. <see cref="Profile"/> ani
    /// konfiguracni tridy na registr nesahaji, takze cyklus nevznika. Viz doc/configuration.md.</para>
    /// </summary>
    public static class ParamRegistry
    {
        private static readonly List<ParamDef> all = new List<ParamDef>();

        /// <summary>Vsechny parametry v poradi deklarace (kategorie po kategoriich).</summary>
        public static IReadOnlyList<ParamDef> All => all;

        private const string K_HW = "Hardware";
        private const string K_MAPA = "Mapy a svet";
        private const string K_FUZE = "Fuze a lokalizace";
        private const string K_MISE = "Mise";
        private const string K_SIM = "Virtualni HW a simulace";
        private const string K_DIAG = "Diagnostika";
        private const string K_TEST = "Self-test a snimky";

        // --- Hardware -----------------------------------------------------------------
        public static readonly BoolParam NoUart = Bool("no_uart", "false", K_HW,
              "Preskoci UART senzory (IMU/GPS/motor). Odpojene drivery hazi vyjimky v tesne "
              + "smycce, coz zkresluje mereni vykonu vizualni cesty.");
        public static readonly DoubleParam MaxSpeed = Num("maxspeed", Fmt(Profile.MaxAllowedSpeed), K_HW,
              "Strop rychlosti jizdy [m/s]. Prenese se do Profile.MaxAllowedSpeed pri startu, "
              + "takze plati pro CELE rizeni naraz: driver motoru, rychlostni profil i "
              + "rychlostni obalku lokalniho planovace. Musi byt > 0; hodnota nad technicky "
              + "dosazitelnou rychlost se orizne (s hlaskou). Default je hodnota z Profile pro "
              + "tuto platformu.", ParamParsers.Kladne);
        public static readonly StringParam Envelope = Vycet("envelope", "directional", new[] { "directional", "radial" }, K_HW,
              "Model rychlostniho stropu z odstupu od prekazek v lokalnim planovaci: 'directional' "
              + "(vychozi od 3. 9. 2026: podel prekazky uzka rampa, kolmo na ni brzdna draha) nebo "
              + "'radial' (puvodni jedina rampa SafeDist..PrefDist bez ohledu na smer - pro A/B).");
        public static readonly DoubleParam SafeDist = Num("safedist", Fmt(Profile.SafeDist), K_HW,
              "TVRDY minimalni odstup od prekazek [m] pro lokalni planovac: blize je neprujezdno. "
              + "Prenese se do Profile.SafeDist pri startu (stejne jako maxspeed). Musi byt > 0. "
              + "Kdyz je >= Profile.PrefDist, PrefDist se posune nad nej se zachovanym rozestupem "
              + "(s hlaskou), jinak by LocalPlannerConfig.Validate() shodil start. "
              + "Default je hodnota z Profile.", ParamParsers.Kladne);
        public static readonly StringParam UartAHRS = Text("UartAHRS", Profile.PortAHRS, K_HW,
              "Seriovy port IMU (VN100). Default podle platformy (Profile.PortAHRS: Windows COM5, "
              + "OrangePI /dev/serial/by-id/...). Prazdny = senzor se nezaklada.");
        public static readonly StringParam UartMotor = Text("UartMotor", Profile.PortMotor, K_HW,
              "Seriovy port ridici jednotky motoru (SDC2160). Default podle platformy (Profile.PortMotor).");
        public static readonly StringParam UartGPS = Text("UartGPS", Profile.PortGPS, K_HW,
              "Seriovy port GPS (uBlox). Default podle platformy (Profile.PortGPS).");

        // --- Mapy a svet --------------------------------------------------------------
        public static readonly PathParam Map = Cesta("map", null, K_MAPA,
              "OSM mapa, podle ktere robot jede (silnicni sit pro globalni navigaci).");
        public static readonly PathParam VisionMap = Cesta("visionmap", null, K_MAPA,
              "OSM mapa, ze ktere renderuji VIRTUALNI KAMERY - kdyz se lisi od map=, je "
              + "vnucena chyba v datech, ne v pozorovateli. Viz doc/virtual-hw.md.");
        public static readonly DoubleParam RoadWidth = Num("roadwidth", "3", K_MAPA,
              "Vychozi sirka cesty [m] pro useky, ktere ji v mape nemaji.");
        public static readonly StringParam Start = Slozeny("start", null, ParamParsers.LatLonOrGps, K_MAPA,
              "Vychozi poloha: 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps' (pocka na prvni "
              + "pouzitelny fix a vypne hadani polohy z mapy).");
        public static readonly StringParam Goal = Slozeny("goal", null, s => ParamParsers.LatLon(s), K_MAPA,
              "Cil jizdy 'lat,lon' ve stupnich - protejsek ke start=. Bez nej robot pri "
              + "bezobsluznem behu stoji (regulator je null, coz je bezpecny stav).");

        // --- Fuze a lokalizace --------------------------------------------------------
        public static readonly BoolParam MapCorr = Bool("mapcorr", "false", K_FUZE,
              "Zapina korelaci occupancy gridu s mapou (odhad chyby polohy a kurzu). Ve "
              + "vychozim stavu vypnuta - stoji cele jadro. "
              + "Viz doc/map-correlation-localization.md.");
        public static readonly BoolParam MapCorrSend = Bool("mapcorrsend", "true", K_FUZE,
              "Posilat korekce z korelace do fuze, nebo je jen merit.");
        public static readonly StringParam MapCorrGate = Vycet("mapcorrgate", "soft", new[] { "soft", "reject" }, K_FUZE,
              "Hradlovani korekci: 'soft' (vychozi) nebo 'reject'. Tvrde hradlo zahazuje "
              + "prave ty velke korekce, ktere jsou potreba - zmereno, ze delalo vysledek horsi.");
        public static readonly DoubleParam MapCorrRef = Num("mapcorrref",
              Fmt(new Localization.MapCorrelatorConfig().ReferenceInformativeEvidence), K_FUZE,
              "Referencni informativni dukaz [m^2 * log-odds] pro skalovani sigma korelace. "
              + "0 vrati konstantni alfa pro A/B srovnani. Default = MapCorrelatorConfig.");
        public static readonly BoolParam Corridor = Bool("corridor", "false", K_FUZE,
              "Zapina hranovou lokalizaci (poloha a kurz z okraju koridoru proti mape).");
        public static readonly BoolParam CorridorSend = Bool("corridorsend", "true", K_FUZE,
              "Posilat mereni z hranove lokalizace do fuze, nebo je jen merit.");
        public static readonly StringParam CorridorTol = Slozeny("corridortol", null,
              ParamParsers.Pair("konstanta,prirustekNaMetr", minA: 0, minB: 0, aStrict: true), K_FUZE,
              "Prah inlieru RANSACu ve tvaru 'konstanta,prirustekNaMetr' [m]. Vzdalena hranice "
              + "je radove nejistejsi nez blizka, takze jeden prah pro vsechny body je spatne.");
        public static readonly StringParam MeasDiag = Text("measdiag", null, K_FUZE,
              "Diagnostika mereni ve fuzi: 'true' nebo '*' pro vsechna mereni (stovky za "
              + "sekundu), jinak filtr na zdroj mereni.");

        // --- Mise ----------------------------------------------------------------------
        // Vycet MUSI odpovidat switchi v ARBotRuntime - kdyz pribude mise, patri i sem.
        public static readonly StringParam Mission = Vycet("mission", "none", new[] { "none", "freerun", "robotour" }, K_MISE,
              "Vyber mise: none | freerun | robotour. Mise se vylucuji, proto selektor a ne "
              + "booleovske prepinace - dve zaroven by si prepisovaly mrkev.");
        public static readonly DoubleParam FreeRunLook = Num("freerunlook", Fmt(new FreeRunConfig().LookaheadM), K_MISE,
              "Lookahead mrkve mise FreeRun [m] - jedina skutecna ladici konstanta te mise. "
              + "Default = FreeRunConfig.LookaheadM.");
        public static readonly DoubleParam DepotFix = Num("depotfix", Fmt(new RobotourConfig().DepotFixSec), K_MISE,
              "Jak dlouho [s] musi fix v depu neprerusene vyhovovat, nez se mise Robotour "
              + "zarmuje. Default = RobotourConfig.DepotFixSec.");
        public static readonly BoolParam AutoRun = Bool("autorun", "false", K_MISE,
              "Spustit rezim Run sam po startu aplikace, bez klikani v UI. Na zarizeni se "
              + "aplikace pousti pres SSH profilem, kde neni co klikat. POZOR: je-li zapnuta "
              + "mise, ROBOT SE ROZJEDE bez dalsiho pokynu - zastavi ho jen nouzove zastaveni "
              + "nebo Stop v UI. Ignoruje se pri selftest=true (ten si Run spousti sam).");
        public static readonly StringParam QrCamera = Text("qrcamera", null, K_MISE,
              "Kamera, ze ktere se cte QR kod. Prazdna hodnota znamena VSECHNY kamery.");

        // --- Virtualni HW a simulace ---------------------------------------------------
        public static readonly BoolParam VirtualHw = Bool("virtualhw", "false", K_SIM,
              "Misto skutecneho HW zalozi simulovane senzory (kamery renderovane z mapy).");
        public static readonly StringParam CameraPose = Vycet("camerapose", "truth", new[] { "truth", "fusion" }, K_SIM,
              "Z ceho renderuji virtualni kamery: 'truth' (ground truth - chyba odhadu je pak "
              + "meritelna) nebo 'fusion' (kamera prisroubovana k odhadu chybu strukturalne "
              + "skryva).");
        public static readonly StringParam PoseError = Slozeny("poseerror", null, ParamParsers.PoseError, K_SIM,
              "Umela chyba pozy 'vpred,vlevo[,stupne]' - vnuti do renderu znamy posun, takze "
              + "korelace s mapou ma proti cemu merit.");
        public static readonly StringParam WheelSlip = Slozeny("wheelslip", null,
              ParamParsers.Pair("vlevo,vpravo", minA: 0, minB: 0, aStrict: true, bStrict: true), K_SIM,
              "Systematicky prokluz kol 'vlevo,vpravo' (1 = ideal; neprumeruje se pryc, "
              + "na rozdil od bileho sumu).");
        public static readonly StringParam ImuBias = Slozeny("imubias", null, ParamParsers.Pair("kurzDeg,gyroDegZaS"), K_SIM,
              "Systematicky bias IMU 'kurzDeg,gyroDegZaS' - pomalu rostouci chyba kurzu.");
        public static readonly StringParam ImuNoise = Slozeny("imunoise", null,
              ParamParsers.Pair("kurzDeg,gyroDegZaS", minA: 0, minB: 0), K_SIM,
              "Sum simulovaneho IMU 'kurzDeg,gyroDegZaS' (sigma).");
        public static readonly StringParam GpsNoise = Slozeny("gpsnoise", null,
              ParamParsers.Pair("polohaM,rychlostMps", minA: 0, minB: 0), K_SIM,
              "Sum simulovane GPS 'polohaM,rychlostMps' (sigma).");
        public static readonly DoubleParam DepthNoise = Num("depthnoise", Fmt(new SyntheticSceneOptions().DepthNoiseM), K_SIM,
              "Sum hloubky syntetickeho obrazu [m]. 0 = exaktni zpetna projekce hranic. "
              + "Default = SyntheticSceneOptions.", ParamParsers.Nezaporne);
        public static readonly DoubleParam GrassRough = Num("grassrough", Fmt(new SyntheticSceneOptions().GrassRoughnessM), K_SIM,
              "Drsnost travy [m]. Ridi rezidua prolozeni koridoru - je to podlaha presnosti "
              + "dana tvarem okraje travy, ne hloubkovym senzorem. Default = SyntheticSceneOptions.",
              ParamParsers.Nezaporne);
        public static readonly DoubleParam GrassHeight = Num("grassheight", Fmt(new SyntheticSceneOptions().GrassHeightM), K_SIM,
              "Vyska travy nad vozovkou [m]. Nenulova rusi exaktnost zpetne projekce hranic. "
              + "Default = SyntheticSceneOptions.", ParamParsers.Nezaporne);

        // --- Diagnostika ---------------------------------------------------------------
        public static readonly StringParam Open = Slozeny("open", null, ParamParsers.Views, K_DIAG,
              "Pohledy, ktere se otevrou hned po startu, oddelene carkou (napr. 'world,telemetry'); "
              + "posledni je aktivni zalozka. Znama jmena: " + string.Join(", ", ParamParsers.ViewNames)
              + ". Nezavisle na selftest= (st_world/st_robot/st_images jsou jen pro mereni) i autorun=. "
              + "Na zarizeni ovladanem pres vzdalenou plochu z mobilu je menu prakticky neovladatelne - "
              + "profil tak otevre, co ma obsluha videt.");
        public static readonly BoolParam Diag = Bool("diag", "true", K_DIAG,
              "Diagnosticke stupne v pipeline (vetsi objem zprav ve streamu i v zaznamu).");
        public static readonly BoolParam Perf = Bool("perf", "true", K_DIAG,
              "Meri, jestli ridici smycka stiha svou periodu (zprava PerfMsg 1x za sekundu). "
              + "Viz doc/perf-monitoring.md.");
        public static readonly StringParam DataRoot = Text("dataroot", null, K_DIAG,
              "Datovy adresar: proti nemu se resi VSECHNY relativni cesty (zaznamy, logy, profily, "
              + "mapy) misto korene repa / adresare aplikace. Pro nasazeni stinovou kopii - binarky "
              + "bezi z kopie bokem, data zustavaji v puvodnim adresari. Jen z PRIKAZOVE RADKY: "
              + "profil se hleda az podle nej. Prazdne = dosavadni chovani.");
        public static readonly StringParam Record = Text("record", null, K_DIAG,
              "Zaznam behu pri startu rezimu Run: 'true' zalozi records/yyyyMMdd-HHmmss.rec "
              + "v korenu repa, jinak se hodnota bere jako CESTA k .rec souboru (relativni "
              + "se resi proti korenu). Prazdne nebo 'false' = bez zaznamu. Tlacitko "
              + "'Run + zaznam' v UI ma prednost - vyslovna volba cloveka prebiji profil.");
        public static readonly DoubleParam PerfWarn = Num("perfwarn", "70", K_DIAG,
              "Obsazenost periody [%], od ktere se hlasi varovani. Hodnota je zatim odhad - "
              + "naostro se nastavi az podle prvniho mereni na zarizeni.");
        public static readonly BoolParam WebOpen = Bool("webopen", "false", K_DIAG,
              "Po nastartovani nahledu otevrit stranku ve vychozim prohlizeci. Pro vyvoj na Windows "
              + "(launch profil); na zarizeni bez displeje nechat vypnute - prohlizec tam nema kde "
              + "vyskocit. Bez web= se ignoruje. Viz doc/headless.md.");
        public static readonly DoubleParam Web = Num("web", "0", K_DIAG,
              "Port weboveho nahledu v ARBot.Headless (0 = vypnuto). Stranka ukaze snimek kamery, "
              + "pravdepodobnost cesty z RGB, pudorys s lokalni mapou a stav mise a nabidne zastaveni "
              + "robota. Posloucha na VSECH rozhranich BEZ HESLA - kdokoli v siti muze robota zastavit "
              + "(rozjet ne). V UI aplikaci se ignoruje. Viz doc/headless.md.", ParamParsers.WebPort);

        // --- Self-test a snimky --------------------------------------------------------
        public static readonly BoolParam SelfTest = Bool("selftest", "false", K_TEST,
              "Bezobsluzny self-test: otevre okna, spusti Run, pocka, ulozi souhrn a skonci. "
              + "Viz doc/selftest.md.");
        public static readonly StringParam StName = Text("st_name", "baseline", K_TEST,
              "Jmeno mereni v souhrnnem CSV - odlisuje vetve A/B.");
        public static readonly DoubleParam StSeconds = Num("st_seconds", "30", K_TEST, "Delka mereni [s].");
        public static readonly BoolParam StRecord = Bool("st_record", "false", K_TEST, "Zaznamenavat beh do .rec souboru.");
        public static readonly BoolParam StImages = Bool("st_images", "false", K_TEST, "Otevrit okno Images.");
        public static readonly BoolParam StImagesActive = Bool("st_images_active", "false", K_TEST,
              "Nechat okno Images aktivni (vykresluje se, tedy zatezuje).");
        public static readonly BoolParam StRobot = Bool("st_robot", "true", K_TEST, "Otevrit robot-centricky pohled.");
        public static readonly BoolParam StWorld = Bool("st_world", "false", K_TEST, "Otevrit World pohled.");
        public static readonly PathParam StOut = Cesta("st_out", null, K_TEST, "Soubor se souhrnem mereni (CSV).");
        public static readonly BoolParam StShot = Bool("st_shot", "false", K_TEST, "Ulozit snimek okna na konci mereni.");
        public static readonly BoolParam StVideo = Bool("st_video", "false", K_TEST, "Poridit videozaznam okna.");
        public static readonly DoubleParam StVideoSeconds = Num("st_video_seconds", "5", K_TEST, "Delka videozaznamu [s].");
        public static readonly DoubleParam StVideoFps = Num("st_video_fps", "8", K_TEST, "Snimkova frekvence videozaznamu.");
        public static readonly DoubleParam StVideoScale = Num("st_video_scale", "3", K_TEST,
              "Delitel rozliseni videozaznamu (3 = tretinova sirka i vyska).");
        public static readonly StringParam StVideoFormat = Text("st_video_format", null, K_TEST, "Format videa: mp4 nebo gif.");
        public static readonly PathParam Ffmpeg = Cesta("ffmpeg", null, K_TEST,
              "Cesta k ffmpeg. Bez nej se pouzije nahradni cesta bez roury.");
        public static readonly BoolParam TelemetryShot = Bool("telemetryshot", "false", K_TEST,
              "Bezobsluzny snimek telemetrickeho pohledu nad zaznamem.");
        public static readonly PathParam TsRec = Cesta("ts_rec", null, K_TEST,
              "Zaznam pro telemetryshot. Bez nej se vezme nejnovejsi indexovany zaznam.");
        public static readonly BoolParam WorldShot = Bool("worldshot", "false", K_TEST,
              "Bezobsluzny snimek World pohledu.");

        // config= sam do registru NEPATRI - neni to nastaveni aplikace, ale volba, ODKUD se
        // nastaveni bere. Kdyby v registru byl, sel by zapsat do profilu a profil by mohl
        // ukazat na jiny profil.

        // ============================== dotazy ==============================

        /// <summary>Najde parametr podle jmena, case-insensitive.</summary>
        public static bool TryGet(string name, out ParamDef def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var d in all)
            {
                if (string.Equals(d.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    def = d;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Overi dvojice z profilu proti registru a vrati VSECHNY nalezene vady (prazdny seznam =
        /// v poradku). Vraci vycet, ne prvni chybu, aby sly opravit najednou.
        ///
        /// <para>Je to <b>jedine misto, kde jsou pravidla platnosti profilu</b> - pouziva ho
        /// <see cref="ParamStore.Build"/> pri startu i panel Konfigurace pri nacteni. Kdyby to
        /// byla dve mista, mohl by panel nacist profil, ktery aplikace pri startu odmitne.</para>
        /// </summary>
        public static List<string> Validate(IEnumerable<KeyValuePair<string, string>> pairs)
        {
            var vady = new List<string>();
            if (pairs == null) return vady;

            foreach (var pair in pairs)
            {
                if (!TryGet(pair.Key, out var def))
                {
                    vady.Add($"neznamy parametr '{pair.Key}'");
                    continue;
                }
                var vysledek = def.Validate(pair.Value);
                if (!vysledek.Ok)
                    vady.Add($"'{pair.Key}={pair.Value}': {vysledek.Error}");
            }
            return vady;
        }

        // ============================== deklarace ==============================

        /// <summary>Zapise popis do <see cref="All"/> a vrati odkaz - deklarace je jeden vyraz.</summary>
        private static T Add<T>(T p) where T : Param
        {
            all.Add(p.Def);
            return p;
        }

        /// <summary>Default z kodu v textove podobe (InvariantCulture, "R" - bez ztraty presnosti).</summary>
        private static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static BoolParam Bool(string name, string def, string category, string description)
            => Add(new BoolParam(new ParamDef { Name = name, Type = ParamType.Bool, Default = def,
                                                Category = category, Description = description }));

        /// <summary>Cislo; <paramref name="parse"/> pridava omezeni rozsahu (napr. <see cref="ParamParsers.Kladne"/>).</summary>
        private static DoubleParam Num(string name, string def, string category, string description,
                                       Func<string, ParamParseResult> parse = null)
            => Add(new DoubleParam(new ParamDef { Name = name, Type = ParamType.Double, Default = def,
                                                  Parse = parse, Category = category, Description = description }));

        private static StringParam Text(string name, string def, string category, string description)
            => Add(new StringParam(new ParamDef { Name = name, Type = ParamType.String, Default = def,
                                                  Category = category, Description = description }));

        private static PathParam Cesta(string name, string def, string category, string description)
            => Add(new PathParam(new ParamDef { Name = name, Type = ParamType.Path, Default = def,
                                                Category = category, Description = description }));

        /// <summary>Parametr s uplnym vyctem povolenych hodnot (panel z nej muze udelat seznam).</summary>
        private static StringParam Vycet(string name, string def, string[] hodnoty,
                                         string category, string description)
            => Add(new StringParam(new ParamDef { Name = name, Type = ParamType.String, Default = def,
                                                  AllowedValues = hodnoty,
                                                  Category = category, Description = description }));

        /// <summary>Parametr se slozenou hodnotou; <paramref name="parse"/> je TYZ kod, jaky
        /// pouzije runtime pri skutecnem cteni (viz <see cref="ParamParsers"/>).</summary>
        private static StringParam Slozeny(string name, string def,
                                           Func<string, ParamParseResult> parse,
                                           string category, string description)
            => Add(new StringParam(new ParamDef { Name = name, Type = ParamType.String, Default = def,
                                                  Parse = parse,
                                                  Category = category, Description = description }));
    }
}
