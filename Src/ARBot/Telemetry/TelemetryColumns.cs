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
            Num<RobotStateMsg>("X [m]", m => m.X),
            Num<RobotStateMsg>("Y [m]", m => m.Y),
            Num<RobotStateMsg>("theta [°]", m => Conversions.Rad2Deg(m.Theta), "F1"),
            Num<RobotStateMsg>("v [m/s]", m => m.V),
            Num<RobotStateMsg>("omega [°/s]", m => Conversions.Rad2Deg(m.Omega), "F1"),

            // --- ridici zasah ---
            Num<DriveCommandMsg>("cmd v [m/s]", m => m.Speed),
            Num<DriveCommandMsg>("cmd omega [°/s]", m => Conversions.Rad2Deg(m.RotationSpeed), "F1"),
            Num<DriveCommandMsg>("cmd dif [m/s]", m => m.Dif),
            Flag<DriveCommandMsg>("STOP", m => m.EmergencyStop),

            // --- lokalni plan ---
            Enum<LocalPlanMsg, LocalPlanStatus>("plan stav", m => m.Status),
            Num<LocalPlanMsg>("plan delka [m]", m => m.LengthM),
            Num<LocalPlanMsg>("plan odstup [m]", m => m.MinClearanceM),
            Num<LocalPlanMsg>("plan bodu", m => m.WayPoints?.Length ?? 0, "F0"),
            Num<LocalPlanMsg>("plan vypocet [ms]", m => m.ComputeMs, "F1"),

            // --- globalni navigace ---
            Enum<GlobalNavMsg, GlobalNavStatus>("nav stav", m => m.Status),
            Num<GlobalNavMsg>("do cile [m]", m => m.RouteLengthM, "F0"),
            Num<GlobalNavMsg>("hran trasy", m => m.RouteEdgeCount, "F0"),
            Num<GlobalNavMsg>("od site [m]", m => m.OffRouteDist),
            Num<GlobalNavMsg>("fi [s]", m => m.Phi, "F1"),
            Num<GlobalNavMsg>("uzavreno hran", m => m.ClosureCount, "F0"),

            // --- surove GPS (bez fuze) ---
            Num<GPSState>("GPS lat [°]", m => m.Latitude, "F6"),
            Num<GPSState>("GPS lon [°]", m => m.Longitude, "F6"),
            Enum<GPSState, GPSState.FixQuality>("GPS fix", m => (int)m.Quality),
            Num<GPSState>("GPS satelitu", m => m.NumberOfSatellites, "F0"),
            Num<GPSState>("GPS HDOP", m => m.Hdop),
        };

        /// <summary>
        /// Ciselny sloupec z jedne zpravy. <c>MsgName</c> se bere z prototypu, aby se nazev typu
        /// nepsal jako retezec (a nerozesel se pri prejmenovani).
        /// </summary>
        private static ColumnSpec Num<T>(string header, Func<T, double> value, string format = "F2")
            where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Format = format,
                Value = m => m is T typed ? value(typed) : (double?)null,
            };

        /// <summary>Logicky sloupec: zobrazi se jako zkratka (kdyz plati), jinak pomlcka.</summary>
        private static ColumnSpec Flag<T>(string header, Func<T, bool> value) where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Format = "F0",
                Value = m => m is T typed ? (value(typed) ? 1.0 : 0.0) : (double?)null,
                Text = v => v != 0 ? header : "-",
            };

        /// <summary>Vyctovy sloupec: v tabulce jmeno hodnoty, v grafu schod.</summary>
        private static ColumnSpec Enum<T, TEnum>(string header, Func<T, int> value)
            where T : Message, new() where TEnum : struct, System.Enum
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Format = "F0",
                Value = m => m is T typed ? value(typed) : (double?)null,
                Text = v => System.Enum.IsDefined(typeof(TEnum), (int)v)
                            ? ((TEnum)(object)(int)v).ToString()
                            : ((int)v).ToString(),
            };
    }
}
