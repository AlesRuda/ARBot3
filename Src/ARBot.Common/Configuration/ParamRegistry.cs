using System;
using System.Collections.Generic;

namespace ARBot.Common.Configuration
{
    /// <summary>
    /// Seznam VSECH konfiguracnich parametru aplikace - jedine misto, kde parametr vznika.
    ///
    /// <para>Je to centralni deklarace, ne samoregistrace pri cteni: panel musi umet vypsat
    /// i parametry vetvi, ktere v tomhle behu nebezi (pri <c>mission=robotour</c> i klice
    /// FreeRunu, protoze prave je clovek hleda, kdyz chce misi prepnout). Jedinou vadu centralni
    /// deklarace - da se na ni zapomenout - hlida <c>ParamRegistryGuardTests</c>, ktery skenuje
    /// zdrojaky a porovnava je s timhle seznamem. Viz doc/configuration.md.</para>
    ///
    /// <para><b>Vychozi hodnoty se opisuji z mist cteni</b> - zmena hodnoty tady je zmena chovani
    /// aplikace, ne uklid. Ctyri z nich ale nejsou u volani, nybrz v konfiguracnich tridach:
    /// <c>freerunlook</c> = <c>FreeRunConfig.LookaheadM</c>, <c>depotfix</c> =
    /// <c>RobotourConfig.DepotFixSec</c>, <c>grassrough</c> a <c>depthnoise</c> =
    /// <c>SyntheticSceneOptions</c>. Kdyz se zmeni tam, musi se zmenit i tady - jinak bude panel
    /// ukazovat lez.</para>
    /// </summary>
    public static class ParamRegistry
    {
        private static readonly List<ParamDef> all = new List<ParamDef>();

        /// <summary>Vsechny parametry v poradi deklarace (kategorie po kategoriich).</summary>
        public static IReadOnlyList<ParamDef> All => all;

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

        /// <summary>Prida parametr do seznamu; vraci ho, aby sel deklarovat v jednom vyrazu.</summary>
        private static ParamDef Add(ParamDef d)
        {
            all.Add(d);
            return d;
        }

        /// <summary>Parametr s konstantni vychozi hodnotou.</summary>
        private static ParamDef Konst(string name, ParamType type, string def,
                                      string category, string description)
            => Add(new ParamDef { Name = name, Type = type, Default = def,
                                  Category = category, Description = description });

        /// <summary>Parametr, jehoz vychozi hodnotu urcuje az kod za behu.</summary>
        private static ParamDef ZKodu(string name, ParamType type, string defDescription,
                                      string category, string description)
            => Add(new ParamDef { Name = name, Type = type, DefaultFromCode = true,
                                  DefaultDescription = defDescription,
                                  Category = category, Description = description });

        /// <summary>
        /// Parametr, jehoz vychozi hodnotu urcuje kod, a ktery navic omezuje ROZSAH.
        /// Typ zustava cislo (panel s nim zachazi jako s cislem), <c>Parse</c> se pouzije az
        /// za kontrolou parsovatelnosti - viz <see cref="ParamDef.Validate"/>.
        /// </summary>
        private static ParamDef ZKoduKladne(string name, string defDescription,
                                            string category, string description)
            => Add(new ParamDef { Name = name, Type = ParamType.Double, DefaultFromCode = true,
                                  DefaultDescription = defDescription,
                                  Parse = ParamParsers.Kladne,
                                  Category = category, Description = description });

        /// <summary>Parametr s uplnym vyctem povolenych hodnot (panel z nej muze udelat seznam).</summary>
        private static ParamDef Vycet(string name, string def, string[] hodnoty,
                                      string category, string description)
            => Add(new ParamDef { Name = name, Type = ParamType.String, Default = def,
                                  AllowedValues = hodnoty,
                                  Category = category, Description = description });

        /// <summary>Parametr se slozenou hodnotou; <paramref name="parse"/> je TYZ kod, jaky
        /// pouzije runtime pri skutecnem cteni (viz <see cref="ParamParsers"/>).</summary>
        private static ParamDef Slozeny(string name, string def,
                                        Func<string, ParamParseResult> parse,
                                        string category, string description)
            => Add(new ParamDef { Name = name, Type = ParamType.String, Default = def,
                                  Parse = parse,
                                  Category = category, Description = description });

        static ParamRegistry()
        {
            const string K_HW = "Hardware";
            const string K_MAPA = "Mapy a svet";
            const string K_FUZE = "Fuze a lokalizace";
            const string K_MISE = "Mise";
            const string K_SIM = "Virtualni HW a simulace";
            const string K_DIAG = "Diagnostika";
            const string K_TEST = "Self-test a snimky";

            // --- Hardware -----------------------------------------------------------------
            Konst("no_uart", ParamType.Bool, "false", K_HW,
                  "Preskoci UART senzory (IMU/GPS/motor). Odpojene drivery hazi vyjimky v tesne "
                  + "smycce, coz zkresluje mereni vykonu vizualni cesty.");
            ZKoduKladne("maxspeed", "Profile.MaxAllowedSpeed (1,2 m/s)", K_HW,
                  "Strop rychlosti jizdy [m/s]. Prenese se do Profile.MaxAllowedSpeed pri startu, "
                  + "takze plati pro CELE rizeni naraz: driver motoru, rychlostni profil i "
                  + "rychlostni obalku lokalniho planovace. Musi byt > 0; hodnota nad technicky "
                  + "dosazitelnou rychlost se orizne (s hlaskou). Bez zadani plati hodnota z kodu.");
            ZKoduKladne("safedist", "Profile.SafeDist (0,7 m)", K_HW,
                  "TVRDY minimalni odstup od prekazek [m] pro lokalni planovac: blize je neprujezdno. "
                  + "Prenese se do Profile.SafeDist pri startu (stejne jako maxspeed). Musi byt > 0. "
                  + "Kdyz je >= Profile.PrefDist, PrefDist se posune nad nej se zachovanym rozestupem "
                  + "(s hlaskou), jinak by LocalPlannerConfig.Validate() shodil start. "
                  + "Bez zadani plati hodnota z kodu.");
            ZKodu("UartAHRS", ParamType.String, "podle detekce portu", K_HW,
                  "Seriovy port IMU (VN100). Bez zadani se pouzije port zjisteny pri startu.");
            ZKodu("UartMotor", ParamType.String, "podle detekce portu", K_HW,
                  "Seriovy port ridici jednotky motoru (SDC2160).");
            ZKodu("UartGPS", ParamType.String, "podle detekce portu", K_HW,
                  "Seriovy port GPS (uBlox).");

            // --- Mapy a svet --------------------------------------------------------------
            Konst("map", ParamType.Path, null, K_MAPA,
                  "OSM mapa, podle ktere robot jede (silnicni sit pro globalni navigaci).");
            Konst("visionmap", ParamType.Path, null, K_MAPA,
                  "OSM mapa, ze ktere renderuji VIRTUALNI KAMERY - kdyz se lisi od map=, je "
                  + "vnucena chyba v datech, ne v pozorovateli. Viz doc/virtual-hw.md.");
            Konst("roadwidth", ParamType.Double, "3", K_MAPA,
                  "Vychozi sirka cesty [m] pro useky, ktere ji v mape nemaji.");
            Slozeny("start", null, ParamParsers.LatLonOrGps, K_MAPA,
                  "Vychozi poloha: 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps' (pocka na prvni "
                  + "pouzitelny fix a vypne hadani polohy z mapy).");
            Slozeny("goal", null, s => ParamParsers.LatLon(s), K_MAPA,
                  "Cil jizdy 'lat,lon' ve stupnich - protejsek ke start=. Bez nej robot pri "
                  + "bezobsluznem behu stoji (regulator je null, coz je bezpecny stav).");

            // --- Fuze a lokalizace --------------------------------------------------------
            Konst("mapcorr", ParamType.Bool, "false", K_FUZE,
                  "Zapina korelaci occupancy gridu s mapou (odhad chyby polohy a kurzu). Ve "
                  + "vychozim stavu vypnuta - stoji cele jadro. "
                  + "Viz doc/map-correlation-localization.md.");
            Konst("mapcorrsend", ParamType.Bool, "true", K_FUZE,
                  "Posilat korekce z korelace do fuze, nebo je jen merit.");
            Vycet("mapcorrgate", "soft", new[] { "soft", "reject" }, K_FUZE,
                  "Hradlovani korekci: 'soft' (vychozi) nebo 'reject'. Tvrde hradlo zahazuje "
                  + "prave ty velke korekce, ktere jsou potreba - zmereno, ze delalo vysledek horsi.");
            Konst("mapcorrref", ParamType.Double, "37.5", K_FUZE,
                  "Referencni informativni dukaz [m^2 * log-odds] pro skalovani sigma korelace. "
                  + "0 vrati konstantni alfa pro A/B srovnani.");
            Konst("corridor", ParamType.Bool, "false", K_FUZE,
                  "Zapina hranovou lokalizaci (poloha a kurz z okraju koridoru proti mape).");
            Konst("corridorsend", ParamType.Bool, "true", K_FUZE,
                  "Posilat mereni z hranove lokalizace do fuze, nebo je jen merit.");
            Slozeny("corridortol", null,
                  ParamParsers.Pair("konstanta,prirustekNaMetr", minA: 0, minB: 0, aStrict: true),
                  K_FUZE,
                  "Prah inlieru RANSACu ve tvaru 'konstanta,prirustekNaMetr' [m]. Vzdalena hranice "
                  + "je radove nejistejsi nez blizka, takze jeden prah pro vsechny body je spatne.");
            Konst("measdiag", ParamType.String, null, K_FUZE,
                  "Diagnostika mereni ve fuzi: 'true' nebo '*' pro vsechna mereni (stovky za "
                  + "sekundu), jinak filtr na zdroj mereni.");

            // --- Mise ----------------------------------------------------------------------
            // Vycet MUSI odpovidat switchi v ARBotRuntime - kdyz pribude mise, patri i sem.
            Vycet("mission", "none", new[] { "none", "freerun", "robotour" }, K_MISE,
                  "Vyber mise: none | freerun | robotour. Mise se vylucuji, proto selektor a ne "
                  + "booleovske prepinace - dve zaroven by si prepisovaly mrkev.");
            Konst("freerunlook", ParamType.Double, "1.5", K_MISE,
                  "Lookahead mrkve mise FreeRun [m] - jedina skutecna ladici konstanta te mise.");
            Konst("depotfix", ParamType.Double, "5", K_MISE,
                  "Jak dlouho [s] musi fix v depu neprerusene vyhovovat, nez se mise Robotour "
                  + "zarmuje.");
            Konst("autorun", ParamType.Bool, "false", K_MISE,
                  "Spustit rezim Run sam po startu aplikace, bez klikani v UI. Na zarizeni se "
                  + "aplikace pousti pres SSH profilem, kde neni co klikat. POZOR: je-li zapnuta "
                  + "mise, ROBOT SE ROZJEDE bez dalsiho pokynu - zastavi ho jen nouzove zastaveni "
                  + "nebo Stop v UI. Ignoruje se pri selftest=true (ten si Run spousti sam).");
            Konst("qrcamera", ParamType.String, null, K_MISE,
                  "Kamera, ze ktere se cte QR kod. Prazdna hodnota znamena VSECHNY kamery.");

            // --- Virtualni HW a simulace ---------------------------------------------------
            Konst("virtualhw", ParamType.Bool, "false", K_SIM,
                  "Misto skutecneho HW zalozi simulovane senzory (kamery renderovane z mapy).");
            Vycet("camerapose", "truth", new[] { "truth", "fusion" }, K_SIM,
                  "Z ceho renderuji virtualni kamery: 'truth' (ground truth - chyba odhadu je pak "
                  + "meritelna) nebo 'fusion' (kamera prisroubovana k odhadu chybu strukturalne "
                  + "skryva).");
            Slozeny("poseerror", null, ParamParsers.PoseError, K_SIM,
                  "Umela chyba pozy 'vpred,vlevo[,stupne]' - vnuti do renderu znamy posun, takze "
                  + "korelace s mapou ma proti cemu merit.");
            Slozeny("wheelslip", null,
                  ParamParsers.Pair("vlevo,vpravo", minA: 0, minB: 0, aStrict: true, bStrict: true),
                  K_SIM,
                  "Systematicky prokluz kol 'vlevo,vpravo' (1 = ideal; neprumeruje se pryc, "
                  + "na rozdil od bileho sumu).");
            Slozeny("imubias", null, ParamParsers.Pair("kurzDeg,gyroDegZaS"), K_SIM,
                  "Systematicky bias IMU 'kurzDeg,gyroDegZaS' - pomalu rostouci chyba kurzu.");
            Slozeny("imunoise", null, ParamParsers.Pair("kurzDeg,gyroDegZaS", minA: 0, minB: 0),
                  K_SIM,
                  "Sum simulovaneho IMU 'kurzDeg,gyroDegZaS' (sigma).");
            Slozeny("gpsnoise", null, ParamParsers.Pair("polohaM,rychlostMps", minA: 0, minB: 0),
                  K_SIM,
                  "Sum simulovane GPS 'polohaM,rychlostMps' (sigma).");
            Konst("depthnoise", ParamType.Double, "0.003", K_SIM,
                  "Sum hloubky syntetickeho obrazu [m]. 0 = exaktni zpetna projekce hranic.");
            Konst("grassrough", ParamType.Double, "0.03", K_SIM,
                  "Drsnost travy [m]. Ridi rezidua prolozeni koridoru - je to podlaha presnosti "
                  + "dana tvarem okraje travy, ne hloubkovym senzorem.");
            Konst("grassheight", ParamType.Double, "0", K_SIM,
                  "Vyska travy nad vozovkou [m]. Nenulova rusi exaktnost zpetne projekce hranic.");

            // --- Diagnostika ---------------------------------------------------------------
            Konst("diag", ParamType.Bool, "true", K_DIAG,
                  "Diagnosticke stupne v pipeline (vetsi objem zprav ve streamu i v zaznamu).");
            Konst("perf", ParamType.Bool, "true", K_DIAG,
                  "Meri, jestli ridici smycka stiha svou periodu (zprava PerfMsg 1x za sekundu). "
                  + "Viz doc/perf-monitoring.md.");
            Konst("record", ParamType.String, null, K_DIAG,
                  "Zaznam behu pri startu rezimu Run: 'true' zalozi records/yyyyMMdd-HHmmss.rec "
                  + "v korenu repa, jinak se hodnota bere jako CESTA k .rec souboru (relativni "
                  + "se resi proti korenu). Prazdne nebo 'false' = bez zaznamu. Tlacitko "
                  + "'Run + zaznam' v UI ma prednost - vyslovna volba cloveka prebiji profil.");
            Konst("perfwarn", ParamType.Double, "70", K_DIAG,
                  "Obsazenost periody [%], od ktere se hlasi varovani. Hodnota je zatim odhad - "
                  + "naostro se nastavi az podle prvniho mereni na zarizeni.");

            // --- Self-test a snimky --------------------------------------------------------
            Konst("selftest", ParamType.Bool, "false", K_TEST,
                  "Bezobsluzny self-test: otevre okna, spusti Run, pocka, ulozi souhrn a skonci. "
                  + "Viz doc/selftest.md.");
            Konst("st_name", ParamType.String, "baseline", K_TEST,
                  "Jmeno mereni v souhrnnem CSV - odlisuje vetve A/B.");
            Konst("st_seconds", ParamType.Double, "30", K_TEST, "Delka mereni [s].");
            Konst("st_record", ParamType.Bool, "false", K_TEST, "Zaznamenavat beh do .rec souboru.");
            Konst("st_images", ParamType.Bool, "false", K_TEST, "Otevrit okno Images.");
            Konst("st_images_active", ParamType.Bool, "false", K_TEST,
                  "Nechat okno Images aktivni (vykresluje se, tedy zatezuje).");
            Konst("st_robot", ParamType.Bool, "true", K_TEST, "Otevrit robot-centricky pohled.");
            Konst("st_world", ParamType.Bool, "false", K_TEST, "Otevrit World pohled.");
            Konst("st_out", ParamType.Path, null, K_TEST, "Soubor se souhrnem mereni (CSV).");
            Konst("st_shot", ParamType.Bool, "false", K_TEST, "Ulozit snimek okna na konci mereni.");
            Konst("st_video", ParamType.Bool, "false", K_TEST, "Poridit videozaznam okna.");
            Konst("st_video_seconds", ParamType.Double, "5", K_TEST, "Delka videozaznamu [s].");
            Konst("st_video_fps", ParamType.Double, "8", K_TEST, "Snimkova frekvence videozaznamu.");
            Konst("st_video_scale", ParamType.Double, "3", K_TEST,
                  "Delitel rozliseni videozaznamu (3 = tretinova sirka i vyska).");
            Konst("st_video_format", ParamType.String, null, K_TEST, "Format videa: mp4 nebo gif.");
            Konst("ffmpeg", ParamType.Path, null, K_TEST,
                  "Cesta k ffmpeg. Bez nej se pouzije nahradni cesta bez roury.");
            Konst("telemetryshot", ParamType.Bool, "false", K_TEST,
                  "Bezobsluzny snimek telemetrickeho pohledu nad zaznamem.");
            Konst("ts_rec", ParamType.Path, null, K_TEST,
                  "Zaznam pro telemetryshot. Bez nej se vezme nejnovejsi indexovany zaznam.");
            Konst("worldshot", ParamType.Bool, "false", K_TEST,
                  "Bezobsluzny snimek World pohledu.");

            // config= sam do registru NEPATRI - neni to nastaveni aplikace, ale volba, ODKUD se
            // nastaveni bere. Kdyby v registru byl, sel by zapsat do profilu a profil by mohl
            // ukazat na jiny profil.
        }
    }
}
