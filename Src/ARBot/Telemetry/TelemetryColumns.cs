using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
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

        private static List<ColumnSpec> Build() => new List<ColumnSpec>
        {
            // --- fuzovana poza ---
            Num<RobotStateMsg>("X [m]", m => m.X,
                "Poloha robotu v ose X (world ENU, východ) z fúze EKF. Počátek = místo startu."),
            Num<RobotStateMsg>("Y [m]", m => m.Y,
                "Poloha robotu v ose Y (world ENU, sever) z fúze EKF. Počátek = místo startu."),
            Num<RobotStateMsg>("theta [°]", m => Conversions.Rad2Deg(m.Theta),
                "Kurz robotu z fúze: matematická orientace ve world ENU (0° = východ, kladně proti "
                + "směru hodinových ručiček). Není to azimut!", "F1"),
            Num<RobotStateMsg>("v [m/s]", m => m.V,
                "Skutečná dopředná rychlost robotu z fúze (odometrie + IMU + GPS), ne požadovaná."),
            Num<RobotStateMsg>("omega [°/s]", m => Conversions.Rad2Deg(m.Omega),
                "Skutečná úhlová rychlost otáčení z fúze; kladně = doleva (proti hodinovým "
                + "ručičkám).", "F1"),

            // --- ridici zasah ---
            Num<DriveCommandMsg>("cmd v [m/s]", m => m.Speed,
                "Požadovaná dopředná rychlost poslaná do motorů. Rozdíl proti „v“ ukazuje, jak "
                + "robot na příkaz reaguje."),
            Num<DriveCommandMsg>("cmd omega [°/s]", m => Conversions.Rad2Deg(m.RotationSpeed),
                "Požadovaná úhlová rychlost poslaná do motorů; kladně = doleva.", "F1"),
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
        };

        /// <summary>
        /// Ciselny sloupec z jedne zpravy. <c>MsgName</c> se bere z prototypu, aby se nazev typu
        /// nepsal jako retezec (a nerozesel se pri prejmenovani).
        /// </summary>
        /// <param name="description">Vysvetleni udaje do tooltipu (zahlavi je jen zkratka).</param>
        private static ColumnSpec Num<T>(string header, Func<T, double> value, string description,
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
                Text = v => System.Enum.IsDefined(typeof(TEnum), (int)v)
                            ? ((TEnum)(object)(int)v).ToString()
                            : ((int)v).ToString(),
            };
    }
}
