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
        public static void Run(RecordFile rec, double trueDx, double trueDy)
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
            var wInf = new Stats("VAHA INFORMATIVNIHO dukazu");
            var errTight = new Stats("chyba podel tesne osy [m]");
            foreach (var m in ok)
            {
                sTight.Add(m.SigmaTight);
                if (!double.IsInfinity(m.SigmaLoose)) sLoose.Add(m.SigmaLoose);
                cells.Add(m.EvidenceCells);
                wInf.Add(m.InformativeWeight);
                if (haveTruth) errTight.Add(AlongTight(m, trueDx, trueDy));
            }

            Console.WriteLine("  " + sTight.Line("m"));
            Console.WriteLine("  " + sLoose.Line("m"));
            Console.WriteLine("  " + cells.Line());
            Console.WriteLine("  " + wInf.Line());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  podil informativniho dukazu: {0:F1} % z bunek", 100.0 * wInf.Median / Math.Max(1, cells.Median)));
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
            var truth = new List<(DateTime T, double Y)>();
            foreach (var e in rec.Index)
                if (e.MsgName == "GroundTruthMsg" && rec.Read(e) is GroundTruthMsg g)
                    truth.Add((g.TimeStamp, g.Y));
            truth.Sort((a, b) => a.T.CompareTo(b.T));

            if (truth.Count > 0)
            {
                var ty = new Stats("skutecne Y robotu [m]");
                foreach (var t in truth) ty.Add(t.Y);
                Console.WriteLine("Kde robot SKUTECNE jel (ground truth) — kontrola, ze vnuceny posun je stály:");
                Console.WriteLine("  " + ty.Line("m"));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  na zacatku {0:F3} m, na konci {1:F3} m -> ujel pricne {2:F3} m",
                    truth[0].Y, truth[truth.Count - 1].Y, truth[truth.Count - 1].Y - truth[0].Y));
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
