using System;
using System.Collections.Generic;
using ARBot.Common.Common;

namespace ARBot.Common.Maps.OsmNav.Navigation
{
    /// <summary>
    /// Vyber "mrkve" - bodu na trase, ktery globalni vrstva predava lokalnimu planovaci.
    /// <para>
    /// Pravidlo: <b>posledni bod trasy, ktery je jeste uvnitr lokalni mapy</b>, pocitano postupem
    /// po trase od prumetu robota k <b>prvnimu</b> vystupu z mapy.
    /// </para>
    /// <para>
    /// Proc az na okraj a ne "par metru dopredu": blizka mrkev dela z lokalniho planovace
    /// kratkozrake zvire - v bludisti by zajel do chodby, ktera o kus dal konci, ackoli to
    /// occupancy grid uz vedel. Mrkev na okraji nuti A* prohledat celou znamou mapu.
    /// </para>
    /// <para>
    /// Proc PRVNI vystup a ne posledni: kdyby se trasa z mapy vynorila a zase vratila, byl by
    /// pozdejsi kus uvnitr mapy <b>nespojeny</b> s robotem a cil na nem by lokalni planovac
    /// nedokazal poctive obslouzit.
    /// </para>
    /// <para>
    /// <b>Ctverec je vystredeny na KOLMEM PRUMETU robota na trasu, ne na robotu</b> (od 29. 9. 2026).
    /// Na trase je to totez. Mimo ni mrkev lezi porad kus PRED prumetem, takze robot najizdi k trase
    /// sikmo a pritom po ni postupuje; mrkev mimo lokalni mapu orizne lokalni planovac
    /// (<c>LocalPathPlanner.ClipToGrid</c>, stav <c>Partial</c>). Do te doby byl ctverec kolem robota
    /// a pri odstupu nad polovinu mapy (5,9 m) se vracel <b>prumet samotny</b> — robot mel jet kolmo
    /// na trasu, nepostupoval a detektor bez postupu zavrel hranu (Track 29. 9. 2026 v Modranech,
    /// ~100 s pri odstupu 6,5–9 m). Plati i nad <c>OffRouteMaxM</c> (rozhodnuti autora: i tam ma
    /// mrkev tahnout po trase smerem k cili, ne kolmo).
    /// </para>
    /// Viz doc/global-navigation-runtime.md.
    /// </summary>
    public static class RouteCarrot
    {
        /// <summary>
        /// Vrati posledni bod trasy, ktery je jeste uvnitr lokalni mapy.
        /// </summary>
        /// <param name="route">Trasa jako lomena cara v lokalni ENU rovine [m].</param>
        /// <param name="robot">Poloha robota [m]; ctverec se vystredi na jeho kolmem prumetu na trasu.</param>
        /// <param name="halfExtentM">Polovina hrany lokalni mapy [m], uz zmensena o okraj.</param>
        /// <returns>Mrkev, nebo null kdyz trasa neexistuje.</returns>
        public static Point2D? Find(IReadOnlyList<Point2D> route, Point2D robot, double halfExtentM)
        {
            if (route == null || route.Count == 0 || halfExtentM <= 0) return null;
            if (route.Count == 1) return route[0];

            ProjectOntoRoute(route, robot, out int segment, out double t);

            double ax = route[segment].X + t * (route[segment + 1].X - route[segment].X);
            double ay = route[segment].Y + t * (route[segment + 1].Y - route[segment].Y);

            // Stred ctverce = kolmy prumet robota na trasu (viz hlavicka tridy). Robot na trase
            // s prumetem splyva; mimo ni se tak mrkev meri od prumetu, ne od robota.
            var center = new Point2D(ax, ay);

            // Postup po trase od prumetu k prvnimu vystupu ze ctverce.
            for (int i = segment; i + 1 < route.Count; i++)
            {
                double bx = route[i + 1].X, by = route[i + 1].Y;

                if (TryExit(ax, ay, bx, by, center, halfExtentM, out double ex, out double ey))
                    return new Point2D(ex, ey);

                ax = bx; ay = by;   // cely usek je uvnitr - pokracuj dalsim
            }

            // Cela zbyla trasa je uvnitr mapy => mrkev je primo cil.
            return new Point2D(ax, ay);
        }

        /// <summary>
        /// Najde bod, kde usek A→B opousti ctverec kolem <paramref name="robot"/>. Predpoklada, ze A je uvnitr;
        /// pak staci nejmensi kladny parametr pruniku s ctyrmi hranicnimi primkami (slab metoda).
        /// </summary>
        private static bool TryExit(double ax, double ay, double bx, double by,
                                    Point2D robot, double half, out double x, out double y)
        {
            x = 0; y = 0;

            double dx = bx - ax, dy = by - ay;
            double s = double.PositiveInfinity;

            if (Math.Abs(dx) > 1e-12)
            {
                double bound = dx > 0 ? robot.X + half : robot.X - half;
                s = Math.Min(s, (bound - ax) / dx);
            }
            if (Math.Abs(dy) > 1e-12)
            {
                double bound = dy > 0 ? robot.Y + half : robot.Y - half;
                s = Math.Min(s, (bound - ay) / dy);
            }

            if (s < 0 || s > 1 || double.IsInfinity(s)) return false;

            x = ax + s * dx;
            y = ay + s * dy;
            return true;
        }

        /// <summary>
        /// Promitne robota na lomenou caru trasy - vrati index useku a parametr na nem.
        /// </summary>
        private static void ProjectOntoRoute(IReadOnlyList<Point2D> route, Point2D robot,
                                             out int segment, out double t)
        {
            segment = 0;
            t = 0;
            double best = double.PositiveInfinity;

            for (int i = 0; i + 1 < route.Count; i++)
            {
                double ax = route[i].X, ay = route[i].Y;
                double dx = route[i + 1].X - ax, dy = route[i + 1].Y - ay;

                double len2 = dx * dx + dy * dy;
                double ti = len2 > 0 ? ((robot.X - ax) * dx + (robot.Y - ay) * dy) / len2 : 0;
                if (ti < 0) ti = 0;
                else if (ti > 1) ti = 1;

                double px = ax + ti * dx - robot.X;
                double py = ay + ti * dy - robot.Y;
                double d2 = px * px + py * py;

                if (d2 < best)
                {
                    best = d2;
                    segment = i;
                    t = ti;
                }
            }
        }
    }
}
