using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Je sigma korelace s mapou poctiva?</b> Porovna <b>hlasenou</b> nejistotu
    /// (<see cref="MapCorrelationMsg.SigmaTight"/>) se <b>skutecnym rozptylem</b> chyby proti znamé
    /// odpovedi. To je test c. 1 z faze 4 (viz doc/map-correlation-localization.md) a zaroven jadro
    /// otevreneho ukolu „honestni sigma".
    ///
    /// <para><b>Znama odpoved.</b> Kdyz kamery renderuji z TUHO POSUNUTE mapy
    /// (<c>visionmap=OSM/SyntetickyRovnyPosunuty.osm</c> proti <c>map=OSM/SyntetickyRovny.osm</c>),
    /// je occupancy grid proti mape posunuty o znamy vektor a korelator MUSI ohlasit prave jeho
    /// opak. Zadava se prepinaci <c>--truedx</c> / <c>--truedy</c> (posun, ktery ma korelator najit).</para>
    ///
    /// <para><b>Meri se podel URCENE osy, ne v osach mapy.</b> Na rovne ceste je podelny posun
    /// nepozorovatelny, takze porovnavat cely vektor nema smysl — hlasena i skutecna hodnota se
    /// promitne na <see cref="MapCorrelationMsg.TightAxisAngle"/> a srovnava se az ta.</para>
    ///
    /// <para><b>Co znamena vysledek.</b> Kdyz je skutecny rozptyl chyby VETSI nez hlasena sigma,
    /// korelator si vymysli jistotu, kterou nema — a fuze mu podle ni da vahu. Pomer obojiho je
    /// primo cinitel, o ktery je autorita korelace proti GPS nadsazena.</para>
    /// </summary>
    public static class SigmaReport
    {
        /// <param name="skipSeconds">Zahodit prvnich N sekund korelaci. <b>Rozjezd je transient</b>
        /// (namereno 25. 8. 2026: chyba odeznivala z 0,50 na 0,05 m po ~25 s), takze bez odriznuti
        /// se "rozptyl" meri na prechodovem jevu, ne na sumu.</param>
        public static void Run(RecordFile rec, double trueDx, double trueDy, double skipSeconds = 0)
        {
            var msgs = new List<MapCorrelationMsg>();
            foreach (var e in rec.Index)
                if (e.MsgName == "MapCorrelationMsg" && rec.Read(e) is MapCorrelationMsg m) msgs.Add(m);

            Console.WriteLine($"MapCorrelationMsg: {msgs.Count} zprav");
            if (msgs.Count == 0)
            {
                Console.WriteLine("Zaznam korelaci nenese — pusti se parametrem mapcorr=true.");
                return;
            }

            if (skipSeconds > 0)
            {
                DateTime from = msgs[0].TimeStamp.AddSeconds(skipSeconds);
                int before = msgs.Count;
                msgs = msgs.Where(m => m.TimeStamp >= from).ToList();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "--skip={0:F1} s: zahozeno {1} zprav rozjezdu, zustava {2}",
                    skipSeconds, before - msgs.Count, msgs.Count));
                if (msgs.Count == 0)
                {
                    Console.WriteLine("Po odriznuti nezustalo nic.");
                    return;
                }
            }
            Console.WriteLine($"delka useku: {(msgs[msgs.Count - 1].TimeStamp - msgs[0].TimeStamp).TotalSeconds:F1} s"
                              + $"  ({msgs.Count / Math.Max(0.001, (msgs[msgs.Count - 1].TimeStamp - msgs[0].TimeStamp).TotalSeconds):F2} Hz)");
            Console.WriteLine();

            Console.WriteLine("Duvod (Reason):");
            foreach (var g in msgs.GroupBy(m => m.Reason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-6} {g.Count(),5}");
            Console.WriteLine();

            bool haveTruth = !double.IsNaN(trueDx) && !double.IsNaN(trueDy);
            Console.WriteLine(haveTruth
                ? $"Znamy posun k nalezeni: dx={trueDx:F3} m, dy={trueDy:F3} m (--truedx/--truedy)"
                : "Znamy posun nezadan (--truedx/--truedy) — poctivost sigmy nelze overit, "
                  + "tiskne se jen rozpad podle mnozstvi dukazu.");
            Console.WriteLine();

            // Bере se jen to, co korelator povazoval za pouzitelne (Reason = Ok = 0).
            var ok = msgs.Where(m => m.Reason == 0).ToList();
            Console.WriteLine($"Pouzitelnych cyklu (Reason=Ok): {ok.Count}");
            if (ok.Count == 0) return;
            Console.WriteLine();

            var sTight = new Stats("hlasena sigma tesne osy [m]");
            var sLoose = new Stats("hlasena sigma volne osy [m]");
            var cells = new Stats("bunek dukazu");
            var eInf = new Stats("INFORMATIVNI dukaz [m2*log-odds]");
            var errTight = new Stats("chyba podel tesne osy [m]");
            foreach (var m in ok)
            {
                sTight.Add(m.SigmaTight);
                if (!double.IsInfinity(m.SigmaLoose)) sLoose.Add(m.SigmaLoose);
                cells.Add(m.EvidenceCells);
                eInf.Add(m.InformativeEvidence);
                if (haveTruth) errTight.Add(AlongTight(m, trueDx, trueDy));
            }

            Console.WriteLine("  " + sTight.Line("m"));
            Console.WriteLine("  " + sLoose.Line("m"));
            Console.WriteLine("  " + cells.Line());
            Console.WriteLine("  " + eInf.Line());
            // Doba vypoctu: bez ni nejde poznat, jestli je perioda cyklu dana VYPOCTEM (a tedy
            // rychlosti stroje), nebo cekanim na snapshot. To rozhoduje o tom, jestli se pocet
            // merenii za sekundu da vubec povazovat za navrhovou vlastnost.
            var procMs = new Stats("doba vypoctu cyklu [ms]");
            foreach (var m in msgs) procMs.Add(m.ProcessingMs);
            Console.WriteLine("  " + procMs.Line("ms"));
            // Podil informativnich bunek se da spocitat jen po prevodu dukazu zpatky na bunky, a to
            // vyzaduje ROZLISENI GRIDU - to ve zprave o korelaci neni, tak se vytahne ze snapshotu
            // v temze zaznamu. Kdyz tam zadny neni (zaznam bez gridu), radek se vynecha; hadat
            // 5 cm by bylo tise nespravne pri jinem nastaveni.
            double cellArea = CellArea(rec);
            if (cellArea > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  podil informativniho dukazu: {0:F1} % z bunek (pri bunce {1:F0} cm)",
                    100.0 * (eInf.Median / cellArea) / Math.Max(1, cells.Median),
                    100.0 * Math.Sqrt(cellArea)));
            Console.WriteLine();

            // CO korelator vlastne hlasi - bez toho nejde overit ani konvence znamenka, ani jestli
            // je tesna osa tam, kde ma byt. Na ceste vedouci na vychod ma byt tesna osa SEVER,
            // tedy uhel ~90 (nebo -90) stupnu.
            var dx = new Stats("hlaseny Dx (na vychod) [m]");
            var dy = new Stats("hlaseny Dy (na sever) [m]");
            var ax = new Stats("uhel tesne osy [deg]");
            foreach (var m in ok)
            {
                dx.Add(m.Dx); dy.Add(m.Dy);
                ax.Add(m.TightAxisAngle * 180 / Math.PI);
            }
            // ⚠️ ZASADNI KONTROLA: vnuceny posun plati jen dokud robot STOJI TAM, kde ma. Robot
            // ale jede podle toho, co VIDI (occupancy grid z posunute mapy), takze se muze
            // vycentrovat na vizualne vnimanou cestu a fyzicky ujet - a pak posun mezi gridem
            // a mapou ROSTE. Bez tohoto rozpadu by se rust posunu spletl s chybou korelatoru.
            var truth = new List<(DateTime T, double X, double Y)>();
            // Casove rady pro odecteni VLASTNI chyby fuze (viz nize) - stavi se pri temze pruchodu
            // indexem, aby se velky zaznam necetl dvakrat.
            DateTime tBase0 = ok[0].TimeStamp;
            var truthTrack = new Track();
            var estTrack = new Track();
            foreach (var e in rec.Index)
            {
                if (e.MsgName == "GroundTruthMsg" && rec.Read(e) is GroundTruthMsg g)
                {
                    truth.Add((g.TimeStamp, g.X, g.Y));
                    truthTrack.Add(g.TimeStamp, tBase0, g.X, g.Y);
                }
                else if (e.MsgName == "RobotStateMsg" && rec.Read(e) is RobotStateMsg s)
                {
                    estTrack.Add(s.TimeStamp, tBase0, s.X, s.Y);
                }
            }
            truth.Sort((a, b) => a.T.CompareTo(b.T));
            truthTrack.Seal();
            estTrack.Seal();

            if (truth.Count > 0)
            {
                var ty = new Stats("skutecne Y robotu [m]");
                foreach (var t in truth) ty.Add(t.Y);
                Console.WriteLine("Kde robot SKUTECNE jel (ground truth) — kontrola, ze vnuceny posun je stály:");
                Console.WriteLine("  " + ty.Line("m"));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  na zacatku {0:F3} m, na konci {1:F3} m -> ujel pricne {2:F3} m",
                    truth[0].Y, truth[truth.Count - 1].Y, truth[truth.Count - 1].Y - truth[0].Y));
                // PODELNA drazka: kolik mapy se spotrebovalo. Bez toho nejde naplanovat DELSI beh —
                // robot startuje ve stredu obalky uzlu, takze ve smeru jizdy ma jen polovinu mapy.
                double alongM = truth[truth.Count - 1].X - truth[0].X;
                double durS = (truth[truth.Count - 1].T - truth[0].T).TotalSeconds;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  podelne ujel {0:F1} m za {1:F1} s  ({2:F2} m/s) -> na N s je potreba mapa 2*(N*v+10)",
                    alongM, durS, durS > 0 ? alongM / durS : 0));
                Console.WriteLine("  Kdyz se to hybe, NENI to chyba korelatoru - posun mezi gridem a mapou");
                Console.WriteLine("  se o tuhle hodnotu skutecne meni a korelator ho hlasi spravne.");
                Console.WriteLine();
            }

            Console.WriteLine("Co korelator hlasi (kontrola konvence i osy):");
            Console.WriteLine("  " + dx.Line("m"));
            Console.WriteLine("  " + dy.Line("m"));
            Console.WriteLine("  " + ax.Line("deg"));
            Console.WriteLine();

            if (haveTruth)
            {
                // TOHLE je ten test: rozptyl skutecne chyby proti hlasene sigme. Bere se smerodatna
                // odchylka kolem VLASTNIHO prumeru, ne kolem nuly - systematicky posun (ze zpozdeni
                // smycky nebo z vychylene osy) je jina vada nez podcenena nejistota.
                double mean = errTight.Mean;
                double sd = Sd(ok.Select(m => AlongTight(m, trueDx, trueDy)));
                double reported = sTight.Median;

                Console.WriteLine("POCTIVOST SIGMY — hlasena nejistota proti skutecnemu rozptylu:");
                Console.WriteLine("  " + errTight.Line("m"));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  systematicky posun (prumer chyby): {0,8:F4} m", mean));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  SKUTECNY rozptyl (sd kolem prumeru): {0,7:F4} m", sd));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  HLASENA sigma (median):              {0,7:F4} m", reported));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  pomer skutecny/hlaseny:              {0,7:F2}x", sd / Math.Max(1e-9, reported)));
                Console.WriteLine();
                Console.WriteLine("  Pomer > 1 znamena, ze korelator hlasi VETSI jistotu, nez ma - a fuze mu");
                Console.WriteLine("  podle ni da vahu. Je to primo cinitel, o ktery je jeho autorita nadsazena.");
                Console.WriteLine();

                var tSec = new List<double>(ok.Count);
                var errs = new List<double>(ok.Count);
                DateTime tBase = ok[0].TimeStamp;
                foreach (var m in ok)
                {
                    tSec.Add((m.TimeStamp - tBase).TotalSeconds);
                    errs.Add(AlongTight(m, trueDx, trueDy));
                }

                // ⚠️ KLICOVA OPRAVA MERIDLA (25. 8. 2026). Korelator hlasi posun proti ODHADU pozy:
                // "skutecna poloha = odhad + d". Spravna odpoved proto NENI konstantni posun mapy,
                // ale posun mapy PLUS vlastni chyba fuze:
                //
                //     d_ocekavane = (-shift) + (pravda - odhad)
                //
                // Bez druheho clenu se chyba FUZE pocita jako chyba KORELATORU. A prave to dela
                // "transient" na zacatku behu (EKF se rozjezdem usazuje) i cast "systematickeho
                // vychyleni". Odhad se bere z RobotStateMsg interpolovaneho v case cyklu; neni to
                // presne to, co videl `GetStateAt` (fixed-lag smoother pozdeji uzly precisti), ale
                // je to o rad blizsi pravde nez nula.
                // Odhad pozy se bere PRIMO ZE ZPRAVY (verze 5 a vys) - to je presne ta poza, proti
                // ktere korelator koreloval. U starsich zaznamu se dohleda interpolaci
                // RobotStateMsg, coz je APROXIMACE: GetStateAt vraci pozu z fixed-lag smootheru,
                // ktera se od publikovaneho stavu lisi. Rozdil obou cest report vypise, aby se
                // vedelo, o kolik ta aproximace lhala.
                int fromMsg = 0, fromTrack = 0;
                var poseGap = new Stats("rozdil poza-ve-zprave vs. interpolovana [m]");
                var tSecFix = new List<double>(ok.Count);
                var errsFix = new List<double>(ok.Count);
                var zs = new List<double>(ok.Count);
                var poseErr = new Stats("vlastni chyba fuze podel tesne osy [m]");
                var sigFix = new Stats("hlasena sigma u tychz cyklu [m]");
                if (truthTrack.Count > 0)
                {
                    for (int i = 0; i < ok.Count; i++)
                    {
                        var m = ok[i];
                        double t = tSec[i];
                        if (!truthTrack.TryAt(t, out double tx, out double ty)) continue;

                        double ex, ey;
                        bool interpolated = estTrack.TryAt(t, out double ix, out double iy);
                        if (m.HasPose)
                        {
                            ex = m.PoseX; ey = m.PoseY;
                            fromMsg++;
                            if (interpolated) poseGap.Add(Math.Sqrt((ix - ex) * (ix - ex) + (iy - ey) * (iy - ey)));
                        }
                        else if (interpolated) { ex = ix; ey = iy; fromTrack++; }
                        else continue;

                        double ux = Math.Cos(m.TightAxisAngle), uy = Math.Sin(m.TightAxisAngle);
                        double pe = (tx - ex) * ux + (ty - ey) * uy;
                        poseErr.Add(pe);
                        sigFix.Add(m.SigmaTight);
                        tSecFix.Add(t);
                        // Chyba korelatoru = hlaseny posun - (posun mapy + chyba fuze), vse podel osy.
                        double ef = AlongTight(m, trueDx, trueDy) - pe;
                        errsFix.Add(ef);
                        // NORMOVANA chyba: kazdy cyklus ma VLASTNI sigmu, takze porovnavat souhrnny
                        // rozptyl s medianem sigma michani hrusky s jabky. z = chyba / sigma toho
                        // CYKLU; u poctive sigmy ma z rozptyl 1 a |z| > 2 asi u 5 % cyklu.
                        if (m.SigmaTight > 0 && double.IsFinite(m.SigmaTight)) zs.Add(ef / m.SigmaTight);
                    }
                }

                if (errsFix.Count >= 4)
                {
                    double meanFix = errsFix.Average();
                    double sdFix = Sd(errsFix);
                    Console.WriteLine("POCTIVOST SIGMY PO ODECTENI VLASTNI CHYBY FUZE:");
                    Console.WriteLine(fromTrack == 0
                        ? $"  odhad pozy: PRESNE ze zpravy (verze 5) u vsech {fromMsg} cyklu"
                        : $"  odhad pozy: {fromMsg} presne ze zpravy, {fromTrack} APROXIMACI "
                          + "(interpolovany RobotStateMsg - stary zaznam)");
                    if (poseGap.Count > 0)
                        Console.WriteLine("  " + poseGap.Line("m")
                                          + "  <- o tolik lhala stara aproximace");
                    Console.WriteLine("  " + poseErr.Line("m"));
                    Console.WriteLine("  " + sigFix.Line("m"));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  systematicky posun (prumer chyby): {0,8:F4} m   (bylo {1:F4})", meanFix, mean));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  SKUTECNY rozptyl (sd kolem prumeru): {0,7:F4} m   (bylo {1:F4})", sdFix, sd));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  pomer skutecny/hlaseny:              {0,7:F2}x  (bylo {1:F2}x)",
                        sdFix / Math.Max(1e-9, reported), sd / Math.Max(1e-9, reported)));
                    Console.WriteLine();
                    Console.WriteLine("  Korelator hlasi posun proti ODHADU, ne proti pravde. Co z rozdilu proti");
                    Console.WriteLine("  posunu mapy zbyde po odecteni chyby fuze, je JEHO chyba - zbytek byl vzdy");
                    Console.WriteLine("  chybou fuze, kterou korelator hlasil SPRAVNE.");
                    Console.WriteLine();

                    if (zs.Count >= 4)
                    {
                        // NEJPRISNEJSI test poctivosti: kazdy cyklus se meri VLASTNI sigmou.
                        double sdZ = Sd(zs);
                        int over2 = zs.Count(z => Math.Abs(z) > 2.0);
                        Console.WriteLine("NORMOVANA CHYBA z = chyba / sigma TOHO cyklu (nejprisnejsi test):");
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  sd(z) = {0:F2}   (poctiva sigma da 1,0; > 1 = prilis mala sigma)", sdZ));
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "  |z| > 2 u {0} z {1} cyklu ({2:F0} %) — u poctive sigmy se ceka ~5 %",
                            over2, zs.Count, 100.0 * over2 / zs.Count));
                        Console.WriteLine("  Tenhle test je prisnejsi nez pomer souhrnu vyse: sigma se cyklus od");
                        Console.WriteLine("  cyklu meni (maly oblak = velka sigma), a porovnavat souhrnny rozptyl");
                        Console.WriteLine("  s MEDIANEM sigma to zahodi.");
                        Console.WriteLine();
                    }
                }
                else if (truthTrack.Count == 0)
                {
                    Console.WriteLine("(Zaznam nenese GroundTruthMsg, takze vlastni chybu fuze nejde odecist");
                    Console.WriteLine(" - cisla vyse ji obsahuji. Viz doc.)");
                    Console.WriteLine();
                }

                // DRUHA polovina ukolu: sousedni cykly koreluji z TEHOZ nahromadeneho oblaku, takze
                // merenia nejsou nezavisla - a fuze je jako nezavisla bere.
                bool haveFix = errsFix.Count >= 4;
                Console.WriteLine(haveFix
                    ? "(nasledujici rozbor jde nad chybou PO odecteni vlastni chyby fuze)"
                    : "(nasledujici rozbor jde nad SUROVOU chybou - chyba fuze v ni zustava)");
                TimeCorrelationReport.Print(
                    TimeCorrelationReport.Compute(haveFix ? tSecFix : tSec, haveFix ? errsFix : errs),
                    reported, haveFix ? Sd(errsFix) : sd,
                    haveFix ? tSecFix : tSec, haveFix ? errsFix : errs);
            }

            // JADRO otevreneho ukolu: sigma nevi, kolik dukazu za ni stoji. Kdyz sigma s poctem
            // bunek KLESA (nebo se nemeni), je slepa - maly oblak pak hlasi vetsi jistotu nez velky.
            Console.WriteLine("Sigma podle MNOZSTVI DUKAZU (jadro ukolu honestni sigma):");
            Console.WriteLine("  bunek dukazu        n   sigma tesne p50   skore p50   chyba p50   |chyba| p50");
            double[] edges = { 0, 2000, 5000, 10000, 20000, double.MaxValue };
            for (int i = 0; i + 1 < edges.Length; i++)
            {
                double a = edges[i], b = edges[i + 1];
                var bin = ok.Where(m => m.EvidenceCells >= a && m.EvidenceCells < b).ToList();
                if (bin.Count == 0) continue;

                var st = new Stats(""); var sc = new Stats(""); var er = new Stats(""); var ae = new Stats("");
                foreach (var m in bin)
                {
                    st.Add(m.SigmaTight); sc.Add(m.Score);
                    if (haveTruth) { double e2 = AlongTight(m, trueDx, trueDy); er.Add(e2); ae.Add(Math.Abs(e2)); }
                }
                string label = b == double.MaxValue ? $"nad {a:F0}" : $"{a:F0}-{b:F0}";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-14} {1,5} {2,15:F4} {3,11:F3} {4,11:F4} {5,12:F4}",
                    label, bin.Count, st.Median, sc.Median,
                    er.Count > 0 ? er.Median : double.NaN, ae.Count > 0 ? ae.Median : double.NaN));
            }
            Console.WriteLine();
            Console.WriteLine("  Kdyby byla sigma poctiva, s rostoucim poctem bunek KLESA (vic dukazu =");
            Console.WriteLine("  vetsi jistota). Kdyz roste nebo stoji, sigma o mnozstvi dukazu nevi.");
            Console.WriteLine();
        }

        /// <summary>
        /// Chyba hlaseneho posunu <b>podel lepe urcene osy</b> [m]. Kolma slozka se ignoruje
        /// zamerne — na rovne ceste je nepozorovatelna a porovnavat ji nema smysl.
        /// </summary>
        /// <summary>
        /// Casova rada (x, y) setridena podle razitka; k lineari interpolaci v case.
        /// </summary>
        private sealed class Track
        {
            private readonly List<(double T, double X, double Y)> pts = new List<(double, double, double)>();

            public int Count => pts.Count;

            public void Add(DateTime t, DateTime t0, double x, double y)
                => pts.Add(((t - t0).TotalSeconds, x, y));

            public void Seal() => pts.Sort((a, b) => a.T.CompareTo(b.T));

            /// <summary>
            /// Linearni interpolace v case; mimo rozsah vrati <c>false</c> (extrapolovat pozu je
            /// horsi nez cyklus vynechat).
            /// </summary>
            public bool TryAt(double t, out double x, out double y)
            {
                x = y = 0;
                if (pts.Count == 0 || t < pts[0].T || t > pts[pts.Count - 1].T) return false;

                int lo = 0, hi = pts.Count - 1;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) / 2;
                    if (pts[mid].T <= t) lo = mid; else hi = mid;
                }
                double span = pts[hi].T - pts[lo].T;
                double w = span > 1e-9 ? (t - pts[lo].T) / span : 0.0;
                x = pts[lo].X + (pts[hi].X - pts[lo].X) * w;
                y = pts[lo].Y + (pts[hi].Y - pts[lo].Y) * w;
                return true;
            }
        }

        /// <summary>
        /// Plocha bunky occupancy gridu [m²] z prvniho snapshotu v zaznamu, nebo 0, kdyz zaznam
        /// zadny nenese. Slouzi jen k prevodu informativniho dukazu zpatky na POCET bunek.
        /// </summary>
        private static double CellArea(RecordFile rec)
        {
            foreach (var e in rec.Index)
                if (e.MsgName == "OccupancyGridMsg" && rec.Read(e) is OccupancyGridMsg g)
                    return g.Resolution * g.Resolution;
            return 0.0;
        }

        private static double AlongTight(MapCorrelationMsg m, double trueDx, double trueDy)
        {
            double ux = Math.Cos(m.TightAxisAngle), uy = Math.Sin(m.TightAxisAngle);
            return (m.Dx - trueDx) * ux + (m.Dy - trueDy) * uy;
        }

        private static double Sd(IEnumerable<double> xs)
        {
            var v = xs.ToList();
            if (v.Count < 2) return double.NaN;
            double mean = v.Average();
            return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
        }
    }
}
