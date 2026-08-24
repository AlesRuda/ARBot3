using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;

namespace ARBot.Analyze
{
    /// <summary>
    /// Rozbor lokalni mapy (occupancy grid) ze zaznamu: <b>cim je ktera bunka blokovana</b> —
    /// geometrii (hloubka), semantikou (barva), nebo obojim.
    ///
    /// <para><b>K cemu to je.</b> Hranice v lokalni mape se ma porovnavat s hranicemi cesty
    /// z kamer, ale ty jdou jinou geometrickou cestou (zpetna projekce pres MERENOU hloubku)
    /// nez semanticky kanal gridu (dopredna projekce na ROVINU zeme). Nez se cokoli prekonfiguruje,
    /// je potreba vedet, ktery kanal tu hranici dnes vubec kresli.</para>
    ///
    /// <para><b>Simulace „ideálni rovina".</b> <see cref="OccupancyGridMsg"/> nese oba kanaly
    /// zvlast, takze jde dopredu spocitat, co by udelalo, kdyby hloubka hlasila hladkou sjizdnou
    /// rovinu: geometricky kanal se nahradi hodnotou „volno" a stav se prepocita. Odpoved tedy
    /// nepotrebuje novy beh. Viz doc/occupancy-and-local-planning.md.</para>
    /// </summary>
    public static class OccupancyReport
    {
        public static void Run(RecordFile rec)
        {
            var grids = new List<OccupancyGridMsg>();
            foreach (var e in rec.Index)
                if (e.MsgName == "OccupancyGridMsg" && rec.Read(e) is OccupancyGridMsg g) grids.Add(g);

            Console.WriteLine($"OccupancyGridMsg: {grids.Count} zprav");
            if (grids.Count == 0) return;

            var m = grids[grids.Count - 1];
            Console.WriteLine($"posledni grid:   {m.Size}x{m.Size}  res={m.Resolution:F3} m  "
                              + $"origin=({m.OriginX},{m.OriginY})  t={m.TimeStamp:HH:mm:ss.fff}");
            Console.WriteLine($"prahy:           free={m.FreeThreshold:F2}  blocked={m.BlockedThreshold:F2}  "
                              + $"scale={m.Scale:F4}");
            Console.WriteLine($"semanticky kanal v zazname: {(m.Road != null ? "ano" : "NE (verze 0?)")}");
            Console.WriteLine();

            Dump("SOUCASNY STAV", m, forceGeometryFree: false);
            Dump("SIMULACE: hloubka hlasi hladkou sjizdnou rovinu (occ = volno vsude)",
                 m, forceGeometryFree: true);

            Boundary(m);
        }

        /// <summary>
        /// Rozpad bunek podle stavu a podle toho, ktery kanal je blokuje. Pri
        /// <paramref name="forceGeometryFree"/> se geometricky kanal nahradi hodnotou „volno" —
        /// tim se simuluje ideálni rovina, aniz by se cokoli spoustelo znovu.
        /// </summary>
        private static void Dump(string title, OccupancyGridMsg m, bool forceGeometryFree)
        {
            int free = 0, blocked = 0, unknown = 0;
            int byGeom = 0, bySem = 0, byBoth = 0;

            for (int j = 0; j < m.Size; j++)
                for (int i = 0; i < m.Size; i++)
                {
                    int idx = i + j * m.Size;
                    float o = forceGeometryFree ? m.FreeThreshold : m.Occ[idx] * m.Scale;
                    float r = m.Road != null ? m.Road[idx] * m.Scale : 0f;

                    bool bo = o >= m.BlockedThreshold, br = r >= m.BlockedThreshold;
                    if (bo || br)
                    {
                        blocked++;
                        if (bo && br) byBoth++;
                        else if (bo) byGeom++;
                        else bySem++;
                    }
                    else if (o <= m.FreeThreshold && r <= m.FreeThreshold) free++;
                    else unknown++;
                }

            int total = m.Size * m.Size;
            Console.WriteLine($"--- {title} ---");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  Free {0,7} ({1,5:F1} %)   Blocked {2,7} ({3,5:F1} %)   Unknown {4,7} ({5,5:F1} %)",
                free, 100.0 * free / total, blocked, 100.0 * blocked / total,
                unknown, 100.0 * unknown / total));
            Console.WriteLine($"  z blokovanych: jen geometrie {byGeom}, jen semantika {bySem}, oboji {byBoth}");
            Console.WriteLine();
        }

        /// <summary>
        /// Kde lezi hranice mezi sjizdnym a nesjizdnym — a jestli ji kresli tentyz kanal, se kterym
        /// se maji srovnavat hranice cesty z kamer. Pocita se pocet <b>prechodu</b> mezi sousedy
        /// v radku a to, cim je blokovana ta nesjizdna strana.
        /// </summary>
        private static void Boundary(OccupancyGridMsg m)
        {
            int transitions = 0, geomSide = 0, semSide = 0, bothSide = 0;
            for (int j = 0; j < m.Size; j++)
                for (int i = 1; i < m.Size; i++)
                {
                    var a = m.State(i - 1, j);
                    var b = m.State(i, j);
                    if (a == b) continue;
                    if (!((a == CellState.Free && b == CellState.Blocked)
                       || (a == CellState.Blocked && b == CellState.Free))) continue;

                    transitions++;
                    int idx = (b == CellState.Blocked ? i : i - 1) + j * m.Size;
                    bool bo = m.Occ[idx] * m.Scale >= m.BlockedThreshold;
                    bool br = m.Road != null && m.Road[idx] * m.Scale >= m.BlockedThreshold;
                    if (bo && br) bothSide++;
                    else if (bo) geomSide++;
                    else semSide++;
                }

            Console.WriteLine("Prechody Free<->Blocked v radcich (to, co je v mape videt jako hranice):");
            Console.WriteLine($"  celkem {transitions}; blokovana strana: jen geometrie {geomSide}, "
                              + $"jen semantika {semSide}, oboji {bothSide}");
            Console.WriteLine();
        }
    }
}
