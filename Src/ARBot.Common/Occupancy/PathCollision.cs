using System;
using ARBot.Common.Configuration;
using ARBot.Common.Regulators;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Koliduje draha, po ktere robot prave jede, s AKTUALNI mapou? Volá ji
    /// <see cref="LocalNavigator"/> kazdy cyklus, kdyz novy plan nevznikl (nouzove zastaveni).
    /// Vytazeno z navigatoru 9. 10. 2026, aby slo testovat primo (lp-unik-kontrola-kolize-startu).
    /// </summary>
    public static class PathCollision
    {
        /// <summary>
        /// Kontroluje se jen usek, na ktery uz je robot fakticky zavazany - od jeho aktualni polohy
        /// dopredu o <b>brzdnou drahu + jeden takt reakce + rezerva</b>. Dal do budoucna to nema smysl:
        /// tam prekazku vyresi priste uspesne preplanovani objezdem.
        ///
        /// <para>Kolize = odstup pod <see cref="LocalPlannerConfig.SafeDist"/> nebo bunka
        /// <see cref="CellState.Blocked"/>. <see cref="CellState.Unknown"/> kolize NENI - to resi
        /// rychlostni obalka (nejed rychleji, nez z ceho zastavis na hranici potvrzeneho).</para>
        /// </summary>
        /// <param name="robotV">Rychlost robotu [m/s] (znamenko nehraje roli).</param>
        /// <param name="escapePath">Je draha UNIKEM z blokovane bunky? Pak se kolize posuzuje jen podle
        /// geometrie - viz doc/occupancy-and-local-planning.md.</param>
        /// <param name="hitDistance">Vzdalenost k nalezene kolizi podel drahy [m]; jinak NaN.</param>
        public static bool Collides(OccupancyGrid grid, ClearanceField field, LocalPlannerConfig cfg,
                                    RegulatorWayPoint[] path, double robotX, double robotY, double robotV,
                                    bool escapePath, out double hitDistance)
        {
            hitDistance = double.NaN;
            if (path == null || path.Length < 2) return false;

            double v = Math.Abs(robotV);
            double check = v * v / (2.0 * cfg.MaxAcceleration)      // brzdna draha z aktualni rychlosti
                           + v * (Profile.Ts / 1000.0)              // jeden takt nez zasah dojede
                           + grid.Resolution;                       // rezerva na diskretizaci
            // POZOR: i u stojiciho robotu je check = Resolution (rezerva), takze se kontroluje prvnich
            // 5 cm drahy vcetne bunky pod robotem. Tahle podminka plati jen pri nulovem rozliseni.
            if (check <= 0) return false;

            // Zacatek kontroly = prumet robotu na drahu (nejblizsi bod), aby se uz projeta cast
            // drahy nekontrolovala.
            FindClosest(path, robotX, robotY, out int seg, out double t);

            // Bunka, na ktere robot PRAVE stoji. Unik z ni smi odjet, i kdyz ji blokuje geometrie -
            // stejne pravidlo jako LocalPathPlanner.PlanEscape (start smi byt geometricky blokovany).
            // Bez vyjimky unikova draha "kolidovala v 0,00 m" s bunkou pod robotem, regulator se
            // zahodil a log hlasil falesne NOUZOVE ZASTAVENI (lp-unik-kontrola-kolize-startu,
            // 1. 10. 2026 3x, 18. 9. 4x).
            int robotCx = grid.CellX(robotX), robotCy = grid.CellY(robotY);

            double step = grid.Resolution * 0.5;
            double traveled = 0;
            for (int i = seg; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y;
                double dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;

                double from = (i == seg) ? t * len : 0;
                for (double s = from; s <= len; s += step)
                {
                    double d = traveled + (s - from);
                    if (d > check) return false;                    // za dosahem zavazku - konec

                    double x = x0 + dx * (s / len), y = y0 + dy * (s / len);
                    int cx = grid.CellX(x), cy = grid.CellY(y);
                    if (!grid.Contains(cx, cy)) continue;           // mimo mapu = nevim, ne kolize

                    // Unikova draha vede ZAMERNE pres blokovane bunky (semanticky) a s malym
                    // odstupem - jinak by robot z blokovane bunky nikdy neodjel. Kolizi tam tedy
                    // dela jen GEOMETRIE, tedy totez pravidlo, jakym unik planoval.
                    bool hit = escapePath
                        ? !(cx == robotCx && cy == robotCy)
                          && grid.BlockReason(cx, cy).HasFlag(CellBlockReason.Geometry)
                        : grid.State(cx, cy) == CellState.Blocked || field.Distance(cx, cy) < cfg.SafeDist;
                    if (hit)
                    {
                        hitDistance = d;
                        return true;
                    }
                }
                traveled += len - from;
            }
            return false;
        }

        /// <summary>Najde nejblizsi bod na lomene care k <c>(x,y)</c>: index useku + parametr t v nem.</summary>
        private static void FindClosest(RegulatorWayPoint[] path, double x, double y,
                                        out int segment, out double t)
        {
            segment = 0;
            t = 0;
            double best = double.MaxValue;

            for (int i = 0; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y;
                double dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double len2 = dx * dx + dy * dy;
                if (len2 < 1e-18) continue;

                double u = ((x - x0) * dx + (y - y0) * dy) / len2;
                if (u < 0) u = 0; else if (u > 1) u = 1;

                double px = x0 + dx * u - x, py = y0 + dy * u - y;
                double d2 = px * px + py * py;
                if (d2 < best) { best = d2; segment = i; t = u; }
            }
        }
    }
}
