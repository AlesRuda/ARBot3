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
                if (!def.IsValidValue(pair.Value))
                    vady.Add($"'{pair.Key}={pair.Value}' neni platna hodnota typu {def.Type}");
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
            Konst("start", ParamType.String, null, K_MAPA,
                  "Vychozi poloha: 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps' (pocka na prvni "
                  + "pouzitelny fix a vypne hadani polohy z mapy).");
            Konst("goal", ParamType.String, null, K_MAPA,
                  "Cil jizdy 'lat,lon' ve stupnich - protejsek ke start=. Bez nej robot pri "
                  + "bezobsluznem behu stoji (regulator je null, coz je bezpecny stav).");

            // --- Fuze a lokalizace --------------------------------------------------------
            Konst("mapcorr", ParamType.Bool, "false", K_FUZE,
                  "Zapina korelaci occupancy gridu s mapou (odhad chyby polohy a kurzu). Ve "
                  + "vychozim stavu vypnuta - stoji cele jadro. "
                  + "Viz doc/map-correlation-localization.md.");
            Konst("mapcorrsend", ParamType.Bool, "true", K_FUZE,
                  "Posilat korekce z korelace do fuze, nebo je jen merit.");
            Konst("mapcorrgate", ParamType.String, "soft", K_FUZE,
                  "Hradlovani korekci: 'soft' (vychozi) nebo 'reject'. Tvrde hradlo zahazuje "
                  + "prave ty velke korekce, ktere jsou potreba - zmereno, ze delalo vysledek horsi.");
            Konst("mapcorrref", ParamType.Double, "37.5", K_FUZE,
                  "Referencni informativni dukaz [m^2 * log-odds] pro skalovani sigma korelace. "
                  + "0 vrati konstantni alfa pro A/B srovnani.");
            Konst("corridor", ParamType.Bool, "false", K_FUZE,
                  "Zapina hranovou lokalizaci (poloha a kurz z okraju koridoru proti mape).");
            Konst("corridorsend", ParamType.Bool, "true", K_FUZE,
                  "Posilat mereni z hranove lokalizace do fuze, nebo je jen merit.");
            Konst("corridortol", ParamType.String, null, K_FUZE,
                  "Prah inlieru RANSACu ve tvaru 'konstanta,prirustekNaMetr' [m]. Vzdalena hranice "
                  + "je radove nejistejsi nez blizka, takze jeden prah pro vsechny body je spatne.");
            Konst("measdiag", ParamType.String, null, K_FUZE,
                  "Diagnostika mereni ve fuzi: 'true' nebo '*' pro vsechna mereni (stovky za "
                  + "sekundu), jinak filtr na zdroj mereni.");

            // --- Mise ----------------------------------------------------------------------
            Konst("mission", ParamType.String, "none", K_MISE,
                  "Vyber mise: none | freerun | robotour. Mise se vylucuji, proto selektor a ne "
                  + "booleovske prepinace - dve zaroven by si prepisovaly mrkev.");
            Konst("freerunlook", ParamType.Double, "3", K_MISE,
                  "Lookahead mrkve mise FreeRun [m] - jedina skutecna ladici konstanta te mise.");
            Konst("depotfix", ParamType.Double, "5", K_MISE,
                  "Jak dlouho [s] musi fix v depu neprerusene vyhovovat, nez se mise Robotour "
                  + "zarmuje.");
            Konst("qrcamera", ParamType.String, null, K_MISE,
                  "Kamera, ze ktere se cte QR kod. Prazdna hodnota znamena VSECHNY kamery.");

            // --- Virtualni HW a simulace ---------------------------------------------------
            Konst("virtualhw", ParamType.Bool, "false", K_SIM,
                  "Misto skutecneho HW zalozi simulovane senzory (kamery renderovane z mapy).");
            Konst("camerapose", ParamType.String, "truth", K_SIM,
                  "Z ceho renderuji virtualni kamery: 'truth' (ground truth - chyba odhadu je pak "
                  + "meritelna) nebo 'fusion' (kamera prisroubovana k odhadu chybu strukturalne "
                  + "skryva).");
            Konst("poseerror", ParamType.String, null, K_SIM,
                  "Umela chyba pozy 'vpred,vlevo[,stupne]' - vnuti do renderu znamy posun, takze "
                  + "korelace s mapou ma proti cemu merit.");
            Konst("wheelslip", ParamType.String, null, K_SIM,
                  "Systematicky prokluz kol (neprumeruje se pryc, na rozdil od bileho sumu).");
            Konst("imubias", ParamType.String, null, K_SIM,
                  "Systematicky bias IMU - pomalu rostouci chyba kurzu.");
            Konst("imunoise", ParamType.String, null, K_SIM,
                  "Sum simulovaneho IMU.");
            Konst("gpsnoise", ParamType.String, null, K_SIM,
                  "Sum simulovane GPS.");
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
