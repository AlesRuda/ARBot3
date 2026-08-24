using System;
using System.Collections.Generic;

namespace ARBot.Common.Common
{
    /// <summary>Jak se prokladaji body primkou.</summary>
    public enum LineFitMode
    {
        /// <summary>
        /// Puvodni <see cref="Line2D.LinearRegesion(IEnumerable{Point2D})"/> - obycejna metoda
        /// nejmensich kvadratu podel te osy, ktera ma vetsi rozptyl.
        /// </summary>
        LeastSquares = 0,

        /// <summary>Ortogonalni regrese (TLS) - minimalizuje KOLME vzdalenosti.</summary>
        Orthogonal = 1,

        /// <summary>
        /// Ortogonalni regrese s Huberovou vahou na <b>reziduu</b> (IRLS) - odlehle body ztraci
        /// vliv, vzdalene si ho drzi.
        /// </summary>
        OrthogonalHuber = 2,

        /// <summary>
        /// Ortogonalni regrese minimalizujici <b>soucet absolutnich</b> odchylek (L1 / LAD, pres
        /// IRLS s vahou <c>1/|r|</c>) - tedy prolozeni, ktere cili <b>MEDIAN</b>, ne prumer.
        ///
        /// <para>Presne to, co u zesikmeneho rozdeleni odchylek chceme: skutecny okraj vozovky sedi
        /// na medianu odchylek, ne na jejich prumeru. Cena je mensi statisticka efektivita nez
        /// u nejmensich kvadratu, kdyby byl sum symetricky a gaussovsky (~64 %) - u naseho sumu se
        /// to vyplati, ale je to vymena vychyleni za rozptyl, takze se meri obojí.</para>
        /// </summary>
        OrthogonalL1 = 3,

        /// <summary>
        /// Ortogonalni regrese s <b>Tukeyho biweight</b> vahou (IRLS) - na rozdil od Hubera
        /// odlehle body <b>uplne zahodi</b> (vaha jde na nulu), ne jen potlaci.
        ///
        /// <para>Nacpak, kdyz uz je tu Huber: Huberova vaha je v chvostu <b>omezena, ale nenulova</b>
        /// (<c>k*s/|r|</c>), takze jednostranny chvost porad tahne konstantni silou. Redescendujici
        /// vaha ho utne uplne. Cena je, ze se muze "zacyklit" na spatne podmnozine, kdyz je start
        /// spatny - proto startuje z L1, ne z nevazeneho prolozeni.</para>
        /// </summary>
        OrthogonalTukey = 4,
    }

    /// <summary>
    /// Prolozeni bodu primkou. Doplnuje <see cref="Line2D.LinearRegesion(IEnumerable{Point2D})"/>
    /// dvema veci, ktere hranova lokalizace potrebuje.
    ///
    /// <para><b>1. Ortogonalni regrese misto osove.</b> Puvodni regrese minimalizuje rezidua
    /// <b>podel jedne osy</b> (podle <c>|dx| &gt; |dy|</c> vybere x nebo y), zatimco hradlovani
    /// inlieru i vysledna sigma se meri <b>kolmou</b> vzdalenosti (<see cref="Line2D.Distance"/>).
    /// Estimator tedy neminimalizoval to, co se pak vyhodnocuje. Navic ta vetev je <b>nespojita</b>:
    /// u primky blizko +-45 stupnu estimator skace mezi dvema ruznymi prolozenimi, takze odhad
    /// uhlu cuka. TLS obojí resi - minimalizuje kolme vzdalenosti a zadnou vetev nema.</para>
    ///
    /// <para><b>2. Robustni vaha na reziduu, ne na vzdalenosti.</b> Vazeni 1/sigma^2 <b>podle
    /// vzdalenosti bodu od robotu</b> bylo zmereno 23. 8. 2026 a vysledek ZHORSILO: vzdalene body
    /// jsou sice nejistejsi, ale zaroven jsou to jedine, co urcuje SMER primky - jejich
    /// potlacenim se zkrati efektivni zakladna a smer zasumi vic, nez kolik se ziska. Huberova
    /// vaha je jina vec: potlaci bod, ktery <b>nesouhlasi</b>, bez ohledu na to, jak je daleko.
    /// Vzdaleny bod, ktery na primce sedi, si plnou vahu podrzi - zakladna se nezkrati.</para>
    ///
    /// <para><b>Rezidua se meri v jednotkach vlastni tolerance bodu.</b> Kdyz je zadana
    /// <c>tolerance</c> (typicky tyz rostouci prah, jakym RANSAC hradluje inliery - viz
    /// <c>CorridorConfig.InlierThresholdPerMeter</c>), pocita se rezidualem podelenym toleranci
    /// daneho bodu. Bez toho by Huber potlacoval prave vzdalene body, tedy presne tu chybu, kterou
    /// merenie vyvratilo: vzdaleny bod ma vetsi rezidua uz z definice, ne proto, ze by byl spatny.</para>
    /// </summary>
    public static class LineFit
    {
        /// <summary>
        /// Prolozi body primkou zvolenym zpusobem. Vraci <c>null</c>, kdyz to nejde (pod dva body,
        /// nebo vsechny body v jednom miste).
        /// </summary>
        /// <param name="points">Prokladane body.</param>
        /// <param name="mode">Ktery estimator.</param>
        /// <param name="tolerance">Tolerance bodu pro normalizaci rezidua u Hubera; null = vsechny
        /// body tymz metrem (pak se meritko vezme z MAD rezidui).</param>
        /// <param name="huberK">Kde zacina potlaceni [nasobek meritka rezidui]. 1,5 = bod
        /// s reziduem 1,5x tolerance ma jeste plnou vahu, dvojnasobne uz jen 0,75.</param>
        /// <param name="iterations">Kolik iteraci IRLS.</param>
        public static Line2D Fit(IReadOnlyList<Point2D> points, LineFitMode mode,
                                 Func<Point2D, double> tolerance = null,
                                 double huberK = 1.5, int iterations = 4)
        {
            if (points == null || points.Count < 2) return null;

            switch (mode)
            {
                case LineFitMode.Orthogonal:
                    return Orthogonal(points, null);

                case LineFitMode.OrthogonalHuber:
                    return Irls(points, tolerance, huberK, iterations, Weight.Huber);

                case LineFitMode.OrthogonalL1:
                    // L1 nema meritko (vaha 1/|r| je na nem nezavisla) a konverguje pomaleji,
                    // proto vic iteraci. Tolerance se ignoruje zamerne - v jejich jednotkach by to
                    // byla tataz vaha, jen prenasobena konstantou.
                    return Irls(points, null, 0, Math.Max(iterations, 8), Weight.L1);

                case LineFitMode.OrthogonalTukey:
                    // Startuje z L1, ne z nevazeneho: redescendujici vaha si pri spatnem startu
                    // dokaze "vybrat" spatnou podmnozinu a uz z ni nevyleze.
                    return Irls(points, null, huberK, Math.Max(iterations, 6), Weight.Tukey,
                                start: Irls(points, null, 0, 8, Weight.L1));

                default:
                    return Line2D.LinearRegesion(points);
            }
        }

        /// <summary>Ktera vahova funkce se v IRLS pouzije.</summary>
        private enum Weight { Huber, L1, Tukey }

        /// <summary>
        /// Ortogonalni regrese (TLS): smer primky je <b>hlavni osa</b> rozptylu bodu, tedy vlastni
        /// vektor rozptylove matice s vetsim vlastnim cislem. Primka prochazi teznistem.
        /// </summary>
        /// <param name="w">Vahy bodu; null = vsechny stejne.</param>
        public static Line2D Orthogonal(IReadOnlyList<Point2D> points, double[] w)
        {
            if (points == null || points.Count < 2) return null;

            double sw = 0, sx = 0, sy = 0;
            for (int i = 0; i < points.Count; i++)
            {
                double wi = w == null ? 1.0 : w[i];
                sw += wi; sx += wi * points[i].X; sy += wi * points[i].Y;
            }
            if (sw <= 0) return null;

            double cx = sx / sw, cy = sy / sw;

            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < points.Count; i++)
            {
                double wi = w == null ? 1.0 : w[i];
                double dx = points[i].X - cx, dy = points[i].Y - cy;
                sxx += wi * dx * dx; syy += wi * dy * dy; sxy += wi * dx * dy;
            }

            // Izotropni oblak (sxx == syy a sxy == 0) nema hlavni osu - atan2(0,0) by dalo 0, tedy
            // tichou lez o vodorovne primce. Radsi null; volajici uz vi, co s nim.
            if (Math.Abs(sxy) < 1e-18 && Math.Abs(sxx - syy) < 1e-18) return null;

            double theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            double ux = Math.Cos(theta), uy = Math.Sin(theta);

            // Primka smeru (ux, uy) teznistem; konstruktor bere normalu, tedy levou normalu smeru.
            return new Line2D(new Vector2D(-uy, ux), new Point2D(cx, cy));
        }

        /// <summary>
        /// IRLS (iterativne prevazovane nejmensi kvadraty) nad ortogonalni regresi: opakovane
        /// spocita kolma rezidua, z nich vahy a znovu prolozi.
        /// </summary>
        /// <param name="start">Ze ktere primky startovat; null = z nevazeneho prolozeni.</param>
        private static Line2D Irls(IReadOnlyList<Point2D> points, Func<Point2D, double> tolerance,
                                   double k, int iterations, Weight kind, Line2D start = null)
        {
            var line = start ?? Orthogonal(points, null);
            if (line == null || points.Count < 3) return line;

            int n = points.Count;
            var w = new double[n];
            var r = new double[n];

            for (int it = 0; it < Math.Max(1, iterations); it++)
            {
                // Rezidua v jednotkach vlastni tolerance bodu (bez tolerance zustanou v metrech).
                for (int i = 0; i < n; i++)
                {
                    double d = line.Distance(points[i]);
                    if (tolerance != null)
                    {
                        double tol = tolerance(points[i]);
                        if (tol > 1e-9) d /= tol;
                    }
                    r[i] = d;
                }

                // Meritko: bez tolerance se vezme z dat (MAD), s toleranci je meritko prave 1 -
                // rezidua uz jsou v jejich jednotkach.
                double scale = tolerance != null ? 1.0 : Mad(r);
                if (scale <= 1e-9) return line;      // vsechna rezidua nulova, neni co zlepsovat

                switch (kind)
                {
                    case Weight.Huber:
                        double c = k * scale;
                        for (int i = 0; i < n; i++) w[i] = r[i] <= c ? 1.0 : c / r[i];
                        break;

                    case Weight.L1:
                        // Vaha 1/|r| dela z minimalizace soucet ABSOLUTNICH odchylek. Podlaha
                        // brani deleni nulou u bodu, ktery na primce lezi presne.
                        double floor = 1e-3 * scale;
                        for (int i = 0; i < n; i++) w[i] = 1.0 / Math.Max(r[i], floor);
                        break;

                    case Weight.Tukey:
                        // Redescendujici: za c*scale je vaha presne nula (bod se zahodi).
                        // 4,685 je bezna volba (95% efektivita pri gaussovskem sumu).
                        double cc = (k > 0 ? k : 1.0) * 4.685 * scale;
                        int alive = 0;
                        for (int i = 0; i < n; i++)
                        {
                            double u = r[i] / cc;
                            if (u >= 1) { w[i] = 0; continue; }
                            double t = 1 - u * u;
                            w[i] = t * t;
                            alive++;
                        }
                        // Kdyby redescendujici vaha utla skoro vsechno, prolozeni by stalo na
                        // hrstce bodu - to je horsi nez nechat chvost tahnout. Radsi konec.
                        if (alive < 3) return line;
                        break;
                }

                var next = Orthogonal(points, w);
                if (next == null) return line;
                line = next;
            }
            return line;
        }

        /// <summary>Median absolutnich hodnot prenasobeny 1,4826 (konzistentni odhad sigma).</summary>
        private static double Mad(double[] r)
        {
            var copy = (double[])r.Clone();
            Array.Sort(copy);
            double med = copy.Length % 2 == 1
                ? copy[copy.Length / 2]
                : 0.5 * (copy[copy.Length / 2 - 1] + copy[copy.Length / 2]);
            return 1.4826 * med;
        }
    }
}
