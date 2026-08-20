using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Skore shody dukazniho oblaku s mapou pro kandidatni chybu pozy.
    /// Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Model kandidata:</b> cely oblak se otoci o <c>phi</c> KOLEM ROBOTU a posune o
    /// <c>(dx, dy)</c>. Kdyz je maximum v <c>(dx*, dy*, phi*)</c>, znamena to "skutecna poloha je
    /// odhad + (dx*, dy*), skutecny kurz je odhad + phi*".</para>
    /// </summary>
    public sealed class CorrelationScorer
    {
        private readonly EvidenceCloud cloud;
        private readonly RoadRaster raster;
        private readonly double robotX;
        private readonly double robotY;

        /// <param name="cloud">Dukazni bunky z kanalu LRoad.</param>
        /// <param name="raster">Vozovka podle mapy zarovnana s gridem.</param>
        /// <param name="robotX">Odhadovana poloha robotu - stred rotace kandidata [m].</param>
        /// <param name="robotY">Odhadovana poloha robotu - stred rotace kandidata [m].</param>
        public CorrelationScorer(EvidenceCloud cloud, RoadRaster raster, double robotX, double robotY)
        {
            this.cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            this.raster = raster ?? throw new ArgumentNullException(nameof(raster));
            this.robotX = robotX;
            this.robotY = robotY;
        }

        /// <summary>
        /// Normovane skore shody v rozsahu -1..1 (1 = dokonala shoda, 0 = zadna informace,
        /// zaporne = shoda naopak). Normovani delenim souctem vah dela skore POROVNATELNE mezi
        /// cykly, takze slouzi zaroven jako metrika kvality.
        /// </summary>
        /// <param name="dx">Kandidatni posun na vychod [m].</param>
        /// <param name="dy">Kandidatni posun na sever [m].</param>
        /// <param name="phi">Kandidatni chyba kurzu [rad].</param>
        /// <param name="stride">Bere se kazdy N-ty dukaz (hrube urovne skenovani). 1 = vsechny.</param>
        public double Score(double dx, double dy, double phi, int stride)
        {
            if (stride < 1) stride = 1;

            double c = Math.Cos(phi), s = Math.Sin(phi);
            double baseX = robotX + dx, baseY = robotY + dy;

            double num = 0.0, den = 0.0;
            for (int i = 0; i < cloud.Count; i += stride)
            {
                double rx = cloud.X[i] - robotX;
                double ry = cloud.Y[i] - robotY;
                double qx = baseX + (c * rx - s * ry);
                double qy = baseY + (s * rx + c * ry);

                // Mimo rastr = "nevim", ne "neni cesta" - takovy dukaz se PRESKOCI vcetne jmenovatele,
                // jinak by okraj rastru tlacil odhad dovnitr.
                if (!raster.TryIsRoad(qx, qy, out bool isRoad)) continue;

                double w = cloud.W[i];
                num += w * (isRoad ? -1.0 : 1.0);
                den += Math.Abs(w);
            }

            return den > 0.0 ? num / den : 0.0;
        }

        /// <summary>Tolerance rovnosti skore pri hledani maxima - viz remizove pravidlo v
        /// <see cref="Scan"/>.</summary>
        private const double TieEps = 1e-9;

        /// <summary>
        /// Hrube-jemne prohledani okna <c>(dx, dy, phi)</c>. Kazda uroven hleda v okne kolem maxima
        /// z predchozi, takze cena roste s poctem urovni, ne s velikosti okna.
        /// </summary>
        public ScanResult Scan(MapCorrelatorConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (cfg.Levels == null || cfg.Levels.Length == 0)
                throw new ArgumentException("MapCorrelatorConfig.Levels je prazdne.", nameof(cfg));

            double centerX = 0.0, centerY = 0.0, centerPhi = 0.0;
            double bestDx = 0.0, bestDy = 0.0, bestPhi = 0.0, best = double.NegativeInfinity;
            int bestDist = int.MaxValue;
            double coarsePeak = 0.0;
            int candidates = 0;

            for (int li = 0; li < cfg.Levels.Length; li++)
            {
                var lvl = cfg.Levels[li];
                bool isCoarse = li == 0;

                best = double.NegativeInfinity;
                bestDist = int.MaxValue;
                int nT = (int)Math.Round(lvl.HalfRangeM / lvl.StepM);
                int nH = (int)Math.Round(lvl.HalfRangeHeadingRad / lvl.StepHeadingRad);

                for (int ix = -nT; ix <= nT; ix++)
                {
                    double dx = centerX + ix * lvl.StepM;
                    for (int iy = -nT; iy <= nT; iy++)
                    {
                        double dy = centerY + iy * lvl.StepM;
                        for (int ip = -nH; ip <= nH; ip++)
                        {
                            double phi = centerPhi + ip * lvl.StepHeadingRad;
                            double sc = Score(dx, dy, phi, lvl.Stride);
                            candidates++;

                            // Vzdalenost kandidata od STREDU okna, merena v KROCICH (bez jednotek,
                            // takze se posun a kurz daji porovnat).
                            int dist = ix * ix + iy * iy + ip * ip;

                            if (sc > best + TieEps)
                            {
                                best = sc;
                                bestDx = dx; bestDy = dy; bestPhi = phi; bestDist = dist;
                            }
                            else if (sc > best - TieEps && dist < bestDist)
                            {
                                // REMIZA. Na plose je skore casto PRESNE stejne - posun PODEL prime
                                // cesty nemeni nic, co robot vidi. Pak se bere kandidat NEJBLIZ
                                // STREDU okna, tedy nejmensi korekce: kdyz data nedavaji zadny duvod
                                // jednu z remizovych moznosti preferovat, spravna odpoved je
                                // "neopravuj". Naivni "prvni vyhrava" vracelo OKRAJ okna a korelator
                                // pak hlasil nekolikametrovou korekci, kterou sam zamitl jako ztratu
                                // lokalizace. Zjisteno integracnim testem 2026-08-19.
                                if (sc > best) best = sc;
                                bestDx = dx; bestDy = dy; bestPhi = phi; bestDist = dist;
                            }
                        }
                    }
                }

                if (isCoarse) coarsePeak = best;
                centerX = bestDx; centerY = bestDy; centerPhi = bestPhi;
            }

            // Referencni skore pro test nejednoznacnosti: NALEZENE maximum vyhodnocene se stride
            // nejhrubsi urovne. Konkurent se meri kolem tehoz bodu a s tymz stride, takze jsou to
            // soumeritelna cisla. (Brat sem CoarsePeakScore by znamenalo srovnavat dva RUZNE body -
            // viz ScanResult.CoarseStrideScoreAtPeak.) Do Candidates se to nepocita: neni to
            // kandidat skenu, ale jedno vyhodnoceni navic.
            double coarseStrideAtPeak = Score(bestDx, bestDy, bestPhi, cfg.Levels[0].Stride);

            return new ScanResult
            {
                Dx = bestDx,
                Dy = bestDy,
                Phi = bestPhi,
                Score = best,
                CoarsePeakScore = coarsePeak,
                CoarseStrideScoreAtPeak = coarseStrideAtPeak,
                Candidates = candidates,
            };
        }

        /// <summary>
        /// Nejlepsi skore konkurencniho maxima posunuteho PODEL zadane osy. Slouzi k rozpoznani
        /// nejednoznacnosti (soubezna cesta).
        ///
        /// <para><b>Proc podel osy a ne v 2D:</b> na PRIME ceste je kandidat posunuty PODEL cesty
        /// presne stejne dobry jako maximum - posun podel prime cesty nemeni nic, co robot vidi.
        /// To ale NENI nejednoznacnost: je to tataz odpoved posunuta ve smeru, ktery uz odhad
        /// prohlasil za neznamy (nekonecna sigma volne osy), a ta osa se do fuze beztak neposila.
        /// Merit konkurenta ve 2D proto vyrabelo falesnou nejednoznacnost na kazde prime ceste
        /// a potlacovalo i dobre urcenou PRICNOU korekci - tedy hlavni vystup cele funkce.
        /// Konkurent posunuty podel URCENE osy je naopak nejednoznacnost skutecna (soubezna cesta).
        /// Zjisteno integracnim testem 2026-08-19; viz doc/map-correlation-localization.md.</para>
        /// </summary>
        /// <param name="peak">Nalezene maximum.</param>
        /// <param name="axisAngle">Smer LEPE urcene osy [rad] (z <see cref="CorrelationCovariance"/>).</param>
        /// <param name="cfg">Bere se z ni odstup konkurenta, rozsah hledani a stride nejhrubsi urovne.</param>
        public double BestRivalAlongAxis(ScanResult peak, double axisAngle, MapCorrelatorConfig cfg)
        {
            if (peak == null) throw new ArgumentNullException(nameof(peak));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double ux = Math.Cos(axisAngle), uy = Math.Sin(axisAngle);

            // Krok z NEJJEMNEJSI urovne, ne z nejhrubsi. Konkurent je uzky vrchol (siroky asi jako
            // cesta), takze hrubym krokem se DA MINOUT: pri kroku 0,4 m se vzorkuje 1,0 / 1,4 / 1,8
            // a rival na presne 2,0 m (rozestup soubeznych cest) se nikdy netrefi - merenim overeno,
            // ze tam melo byt skore 0,958 misto nalezenych 0,625. Zjisteno integracnim testem
            // 2026-08-19.
            double step = cfg.Levels[cfg.Levels.Length - 1].StepM;

            // Stride ale ZUSTAVA z nejhrubsi urovne - jinak by se skore konkurenta porovnavalo
            // s ScanResult.CoarseStrideScoreAtPeak z jinak podvzorkovaneho oblaku, tedy
            // nesoumeritelna cisla.
            int stride = cfg.Levels[0].Stride;

            double best = double.NegativeInfinity;
            for (double t = cfg.AmbiguitySeparationM; t <= cfg.SearchRangeM + 1e-9; t += step)
            {
                double a = Score(peak.Dx + t * ux, peak.Dy + t * uy, peak.Phi, stride);
                double b = Score(peak.Dx - t * ux, peak.Dy - t * uy, peak.Phi, stride);
                if (a > best) best = a;
                if (b > best) best = b;
            }
            return best;
        }
    }
}
