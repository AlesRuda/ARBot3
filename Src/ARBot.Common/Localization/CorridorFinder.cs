using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Algorithms.ML;
using ARBot.Common.Common;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Najde <see cref="RoadCorridor"/> z hranicnich bodu cesty: RANSAC prolozi levou a pravou
    /// hranici primkou, v miste robotu se spocita <b>kolmice na cestu</b> a z pruseciku vyjde
    /// sirka cesty, pricna poloha robotu a odchylka osy.
    ///
    /// <para><b>Proc prave takhle.</b> Je to varianta, ktera se na predchozim robotu osvedcila
    /// (podnet autora) — na rozdil od <c>PathMapCorelator</c>, ktery se odladit nepodarilo, a na
    /// rozdil od plosne korelace, ktera plati za informaci, kterou vnitrek cesty nenese. Merene
    /// nad zaznamem 21. 8. 2026: pricna poloha sd 3 cm, smer sd 0,77°, sirka 2,01 m proti 2,00 m
    /// v mape, cena ~0,1 ms na snimek. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para><b>Bez stavu.</b> Trida nic nepamatuje: dostane dve sady bodu (typicky levou hranici
    /// z jedne kamery a pravou z druhe, parovane casem) a vrati jeden koridor. Parovani kamer
    /// i navazani na mapu patri do volajiciho stupne — diky tomu jde tohle testovat bez HW.</para>
    ///
    /// <para><b>Vstup je v ramci robotu</b> (X vpred, Y vlevo) — hranicni body uz metricke nese
    /// <c>PathEdge.LeftPoint</c>/<c>RightPoint</c>, dopoctene na vlakne kamery.</para>
    /// </summary>
    public sealed class CorridorFinder
    {
        private readonly CorridorConfig cfg;

        public CorridorFinder(CorridorConfig config = null)
        {
            cfg = config ?? new CorridorConfig();
        }

        /// <summary>Nastaveni, se kterym se hleda.</summary>
        public CorridorConfig Config => cfg;

        /// <summary>
        /// Koridor z hranicnich bodu v ramci robotu. Vzdy vraci vysledek - kdyz koridor nevznikl,
        /// je duvod v <see cref="RoadCorridor.Reason"/>.
        /// </summary>
        public RoadCorridor Find(IReadOnlyList<Point2D> leftPoints, IReadOnlyList<Point2D> rightPoints)
        {
            var r = new RoadCorridor
            {
                PointsLeft = leftPoints?.Count ?? 0,
                PointsRight = rightPoints?.Count ?? 0,
            };

            if (r.PointsLeft < cfg.MinPoints && r.PointsRight < cfg.MinPoints)
            {
                r.Reason = CorridorReason.TooFewPoints;
                return r;
            }
            if (r.PointsLeft < cfg.MinPoints || r.PointsRight < cfg.MinPoints)
            {
                r.Reason = CorridorReason.OneSideOnly;
                return r;
            }

            var left = Fit(leftPoints);
            var right = Fit(rightPoints);
            r.InliersLeft = left.inliers; r.InliersRight = right.inliers;
            r.ResidualLeft = left.rms; r.ResidualRight = right.rms;

            if (left.line == null || right.line == null)
            {
                r.Reason = CorridorReason.TooFewPoints;
                return r;
            }
            if (left.inliers < cfg.MinInliers || right.inliers < cfg.MinInliers)
            {
                r.Reason = CorridorReason.TooFewInliers;
                return r;
            }

            // Smer koridoru: prumer smeru obou hranic (primka nema orientaci, proto se normalizuje
            // na +-90°). Odchylka obou smeru je zaroven kontrola, ze jde o koridor.
            double aL = Normalize(left.line.Angle), aR = Normalize(right.line.Angle);
            double parallelError = Normalize(aL - aR);
            r.ParallelErrorRad = Math.Abs(parallelError);
            // Obe strany zvlast - i kdyz se zamitne. Bez toho nejde poznat, ktera hranice je vedle
            // (prumer se pri zamitnuti nepocita). Viz doc/map-correlation-localization.md.
            r.DirectionLeftRad = aL;
            r.DirectionRightRad = aR;
            if (r.ParallelErrorRad > cfg.MaxParallelErrorRad)
            {
                r.Reason = CorridorReason.NotParallel;
                return r;
            }
            double dir = Normalize(aL - parallelError / 2);
            r.DirectionRad = dir;

            // Kolmice na cestu v miste robotu: leva normala smeru. Offset primky podel te normaly
            // je znamenkova vzdalenost hranice od robotu (robot je v pocatku).
            double nx = -Math.Sin(dir), ny = Math.Cos(dir);
            double cL = Offset(left.line, nx, ny);
            double cR = Offset(right.line, nx, ny);
            if (cL < cR) { var t = cL; cL = cR; cR = t; }     // vyssi offset je leva hranice

            r.Width = cL - cR;
            r.Lateral = -(cL + cR) / 2;                        // + = robot vlevo od osy
            if (r.Width < cfg.MinWidthM || r.Width > cfg.MaxWidthM)
            {
                r.Reason = CorridorReason.WidthOutOfRange;
                return r;
            }

            // Honestni sigma z rozptylu reziduí.
            //
            // POZOR, zamerne se NEDELI sqrt(n): sousedni hranicni body pochazi ze sousednich radku
            // tehoz obrazu, takze si chybu detekce SDILEJI (stin, rozmazana hranice, tráva
            // prerustajici asfalt posunou celou hranici, ne jeden bod). Delenim sqrt(n) by vysla
            // milimetrova jistota z dat, ktera ji nemaji - presne ta vada, kterou ma tenhle
            // estimator na plosne korelaci nahradit. Podlaha sigma odpovida NAMERENE
            // opakovatelnosti (3 cm nad zaznamem 21. 8. 2026), tedy systematice, kterou rezidua
            // nevidi vubec.
            r.SigmaLateral = Math.Max(cfg.SigmaFloorM, (left.rms + right.rms) / 2);

            // Sigma smeru: rezidua na delce useku, ktery hranice pokryva (delsi usek = presnejsi smer).
            double spanL = Span(leftPoints), spanR = Span(rightPoints);
            double span = Math.Max(0.5, (spanL + spanR) / 2);
            r.SigmaDirectionRad = Math.Max(cfg.SigmaFloorRad,
                                           Math.Atan2((left.rms + right.rms) / 2, span));

            r.Reason = CorridorReason.Ok;
            return r;
        }

        private sealed class Holder { public Point2D P; public bool Inlier; }

        /// <summary>RANSAC prolozeni primkou + rezidua a pocet inlieru.</summary>
        private (Line2D line, int inliers, double rms) Fit(IReadOnlyList<Point2D> pts)
        {
            if (pts == null || pts.Count < cfg.MinPoints) return (null, 0, 0);

            var holders = new List<Holder>(pts.Count);
            for (int i = 0; i < pts.Count; i++) holders.Add(new Holder { P = pts[i] });

            var line = RANSAC.LinearRegresion(holders, 3, cfg.InlierThresholdM, 0.99,
                                              h => h.P, h => h.Inlier = true);
            if (line == null) return (null, 0, 0);

            // RANSAC hleda KONSENZUS - vrati model z minimalniho vzorku, ktery ma nejvic inlieru.
            // To je jeho uloha, ne vada; prolozeni pres nalezenou konsenzualni sadu je prace
            // volajiciho. Bez nej nese primka sum tech tri bodu (nad syntetickym sumem 5 cm to
            // delalo 5 cm chybu pricne polohy), a rezidua by se meřila proti spatne primce.
            var inliers = holders.Where(h => h.Inlier).Select(h => h.P).ToList();
            if (inliers.Count >= 2)
            {
                var refined = Line2D.LinearRegesion(inliers);
                if (refined != null) line = refined;
            }

            double sum = 0;
            int n = 0;
            foreach (var p in inliers)
            {
                double d = line.Distance(p);
                sum += d * d;
                n++;
            }
            return (line, n, n == 0 ? 0 : Math.Sqrt(sum / n));
        }

        /// <summary>Offset primky podel normaly (n · p pro libovolny bod p na primce).</summary>
        private static double Offset(Line2D line, double nx, double ny)
        {
            var p = line.ProjectOntoLine(new Point2D(0, 0));
            return nx * p.X + ny * p.Y;
        }

        /// <summary>Delka useku, ktery body pokryvaji (nejvzdalenejsi dvojice v ose X i Y).</summary>
        private static double Span(IReadOnlyList<Point2D> pts)
        {
            if (pts == null || pts.Count < 2) return 0;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            double dx = maxX - minX, dy = maxY - minY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Normalizace uhlu primky na (−90°, 90°] — primka nema orientaci.</summary>
        private static double Normalize(double a)
        {
            while (a > Math.PI / 2) a -= Math.PI;
            while (a <= -Math.PI / 2) a += Math.PI;
            return a;
        }
    }
}
