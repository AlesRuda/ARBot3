using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// Hledani <b>systematicke odchylky hranicnich bodu</b> od skutecneho okraje vozovky.
    ///
    /// <para><b>Nacpak.</b> Nad <c>OSM/SyntetickyRovny.osm</c> vyslo 24. 8. 2026 mereni sirky
    /// koridoru 2,018 m proti skutecnym 2,000 m, tedy <b>+18 mm systematicky</b>. Proti filtru
    /// sirky to nebylo videt (0,002 m) - filtr se tu odchylku naucil. Tenhle rozbor rika, ODKUD
    /// tech 18 mm je: jestli je obe hranice posunute stejne, jestli to roste se vzdalenosti,
    /// a jestli se to lisi mezi kamerami.</para>
    ///
    /// <para><b>Jak.</b> Rovna mapa ma osu presne <c>y = 0</c> a hranice presne <c>y = +-1,0 m</c>
    /// v lokalnim ENU. Hranicni body jsou v zaznamu metricke v ramci robotu
    /// (<see cref="CameraFrame.PathEdges"/>), takze se pres <b>ground truth</b> pozu prevedou do
    /// ENU a odchylka od +-1,0 se precte priamo. Poza MUSI byt ground truth, ne
    /// <see cref="CameraFrame.PoseAtCaptureX"/> (to je odhad fuze) - virtualni kamery renderuji
    /// z ground truth (<c>camerapose=truth</c>), takze jedine proti nemu je odchylka chybou
    /// DETEKCE a ne chybou lokalizace.</para>
    ///
    /// <para><b>Znamenko:</b> kladne = bod je VEN z cesty (cesta se jevi sirsi). Obe hranice se
    /// prepocitavaji na tutez konvenci, takze soucet obou odchylek je prave chyba sirky.</para>
    /// </summary>
    public static class EdgeBiasReport
    {
        /// <summary>Polovina skutecne sirky cesty [m] - <c>OSM/SyntetickyRovny.osm</c> ma 2,0 m.</summary>
        public static void Run(RecordFile rec, double trueWidth, double axisY, int limit)
        {
            double half = trueWidth / 2;
            Console.WriteLine($"Skutecna cesta: osa y = {axisY:F2} m podel +X, sirka {trueWidth:F3} m "
                              + $"(hranice y = {axisY + half:F3} a {axisY - half:F3})");

            var truth = new List<(DateTime T, double X, double Y, double Th)>();
            foreach (var e in rec.Index)
                if (e.MsgName == "GroundTruthMsg" && rec.Read(e) is GroundTruthMsg g)
                    truth.Add((g.TimeStamp, g.X, g.Y, g.Theta));
            truth.Sort((a, b) => a.T.CompareTo(b.T));

            if (truth.Count == 0)
            {
                Console.WriteLine("Zaznam nema GroundTruthMsg — bez pravdy o poze to merit nejde.");
                return;
            }
            Console.WriteLine($"ground truth: {truth.Count} zprav");

            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            if (limit > 0 && limit < entries.Count) entries = entries.Take(limit).ToList();
            Console.WriteLine($"CameraFrame: {entries.Count} (cte cele snimky, chvili to trva)");
            Console.WriteLine();

            // Vzorky: odchylka [m], vzdalenost bodu od robotu [m], ktera kamera, ktera hranice.
            var samples = new List<(double Dev, double Range, string Cam, bool Left)>();

            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f) || f.PathEdges == null) continue;
                var g = Nearest(truth, f.TimeStamp);
                double c = Math.Cos(g.Th), s = Math.Sin(g.Th);
                string cam = f.Name ?? string.Empty;

                foreach (var pe in f.PathEdges)
                {
                    Add(pe.LeftPoint, true);
                    Add(pe.RightPoint, false);

                    void Add(ARBot.Common.Common.Point4D p, bool isLeft)
                    {
                        if (p.A == 0) return;                       // neplatny bod (stejny filtr jako runtime)
                        // Ramec robotu (X vpred, Y vlevo) -> lokalni ENU.
                        double north = g.Y + p.X * s + p.Y * c;
                        // Kladne = ven z cesty. Leva hranice ma byt na axisY+half, prava na axisY-half.
                        double dev = isLeft ? north - (axisY + half) : (axisY - half) - north;
                        samples.Add((dev, Math.Sqrt(p.X * p.X + p.Y * p.Y), cam, isLeft));
                    }
                }
            }

            if (samples.Count == 0)
            {
                Console.WriteLine("Zadne metricke hranicni body (zaznam formatu < 5?).");
                return;
            }

            Console.WriteLine($"Hranicnich bodu: {samples.Count}");
            Console.WriteLine("(kladna odchylka = bod lezi VEN z cesty, tedy cesta se jevi sirsi)");
            Console.WriteLine();

            Console.WriteLine("CELKEM podle hranice:");
            Console.WriteLine("  hranice         n   odchylka p50   p10      p90      mean");
            Print("leva", samples.Where(x => x.Left));
            Print("prava", samples.Where(x => !x.Left));
            var all = new Stats(""); foreach (var x in samples) all.Add(x.Dev);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-12} {1,5} {2,13:F4} {3,8:F4} {4,8:F4} {5,8:F4}",
                "obe", all.Count, all.Median, all.Percentile(10), all.Percentile(90), all.Mean));
            Console.WriteLine();
            Console.WriteLine("  Soucet median(leva) + median(prava) je prave chyba sirky — kdyz");
            Console.WriteLine("  vyjde +18 mm, odpovida to namerenym 2,018 m proti 2,000 m.");
            Console.WriteLine();

            // MEDIAN A PRUMER ZVLAST, a to je tu to podstatne: prolozeni metodou nejmensich
            // kvadratu sleduje PRUMER, ne median. Kdyz je rozdeleni odchylek zesikmene (dlouhy
            // chvost ven z cesty), median sedi na okraji, ale prumer je posunuty - a prave o ten
            // posun vyjde prolozena hranice vedle. Odtud +18 mm v sirce koridoru.
            Console.WriteLine("Podle KAMERY (rozlisi chybu detekce od chyby extrinsik).");
            Console.WriteLine("POZOR na rozdil MEDIAN vs PRUMER — prolozeni sleduje prumer:");
            Console.WriteLine("  kamera / hranice        n   median   PRUMER   p90");
            foreach (var cam in samples.Select(x => x.Cam).Distinct().OrderBy(x => x))
            {
                Print2($"{cam} / leva", samples.Where(x => x.Cam == cam && x.Left));
                Print2($"{cam} / prava", samples.Where(x => x.Cam == cam && !x.Left));
            }
            Console.WriteLine();

            // HISTOGRAM je tu proto, aby bylo videt, ze rozdeleni neni symetricke. Na tom cely
            // efekt stoji: prolozeni nejmensimi kvadraty klade primku na PRUMER odchylek (primka
            // prochazi teznistem, tedy prumer rezidui je nula), zatimco skutecny okraj je na jejich
            // MEDIANU. U symetrickeho rozdeleni je to totez a zadna chyba nevznikne; u zesikmeneho
            // se lisi presne o (prumer - median), a to je ta systematicka odchylka sirky.
            // POUZITE SADY: CorridorLocalizer bere pro kazdou hranici tu kameru, ktera ji vidi VIC
            // bodu. Druha sada (kde kamera koukala na protejsi hranici) do prolozeni nikdy nejde,
            // takze do histogramu ani do prumeru nepatri - jinak to mate presne tak, jak to zmatlo
            // celkovy prumer (ty zkrizene body lezi ~0,92 m dovnitr a tahnou prumer opacne).
            string camL = Dominant(samples.Where(x => x.Left));
            string camR = Dominant(samples.Where(x => !x.Left));
            var used = samples.Where(x => x.Left ? x.Cam == camL : x.Cam == camR).ToList();
            Console.WriteLine($"Pouzite sady: leva hranice z kamery '{camL}', prava z '{camR}' "
                              + $"({used.Count} bodu z {samples.Count})");
            Console.WriteLine();

            Console.WriteLine("HISTOGRAM odchylek POUZITYCH sad (proc to LS vychyli: median != prumer):");
            Console.WriteLine("  odchylka [mm]       n   podil");
            double[] hEdges = { -1000, -50, -20, -10, -5, -2, 2, 5, 10, 20, 50, 100, 1000 };
            for (int i = 0; i + 1 < hEdges.Length; i++)
            {
                double a = hEdges[i] / 1000.0, b = hEdges[i + 1] / 1000.0;
                int n = used.Count(x => x.Dev >= a && x.Dev < b);
                if (n == 0) continue;
                double pct = 100.0 * n / used.Count;
                string label = i == 0 ? $"pod {hEdges[i + 1]:F0}"
                             : i + 2 == hEdges.Length ? $"nad {hEdges[i]:F0}"
                             : $"{hEdges[i]:F0} az {hEdges[i + 1]:F0}";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-14} {1,7} {2,6:F2} %  {3}", label, n, pct,
                    new string('#', (int)Math.Round(pct))));
            }
            Console.WriteLine();
            Console.WriteLine("  Vsimni si asymetrie: chvost VEN z cesty (kladne) je dlouhy, dovnitr");
            Console.WriteLine("  kratky. Median proto zustava na okraji, ale prumer je vytazeny ven.");
            Console.WriteLine();

            Console.WriteLine("Podle VZDALENOSTI bodu (roste-li to, je to pixelova kvantizace;");
            Console.WriteLine("je-li to konstantni, je to metricky offset v definici okraje):");
            Console.WriteLine("  vzdalenost [m]      n   odchylka p50   p10      p90      leva p50   prava p50");
            double[] edges = { 0, 1.5, 2.5, 3.5, 5, 7, 9, 12, double.MaxValue };
            for (int i = 0; i + 1 < edges.Length; i++)
            {
                double a = edges[i], b = edges[i + 1];
                var bin = samples.Where(x => x.Range >= a && x.Range < b).ToList();
                if (bin.Count == 0) continue;

                var st = new Stats(""); var l = new Stats(""); var r = new Stats("");
                foreach (var x in bin) { st.Add(x.Dev); if (x.Left) l.Add(x.Dev); else r.Add(x.Dev); }
                string label = b == double.MaxValue ? $"nad {a:F0}" : $"{a:F1}-{b:F1}";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,7} {2,13:F4} {3,8:F4} {4,8:F4} {5,10:F4} {6,10:F4}",
                    label, bin.Count, st.Median, st.Percentile(10), st.Percentile(90),
                    l.Count > 0 ? l.Median : double.NaN, r.Count > 0 ? r.Median : double.NaN));
            }
            Console.WriteLine();

            void Print(string name, IEnumerable<(double Dev, double Range, string Cam, bool Left)> src)
            {
                var st = new Stats(""); foreach (var x in src) st.Add(x.Dev);
                if (st.Count == 0) return;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,5} {2,13:F4} {3,8:F4} {4,8:F4} {5,8:F4}",
                    name, st.Count, st.Median, st.Percentile(10), st.Percentile(90), st.Mean));
            }

            void Print2(string name, IEnumerable<(double Dev, double Range, string Cam, bool Left)> src)
            {
                var st = new Stats(""); foreach (var x in src) st.Add(x.Dev);
                if (st.Count == 0) return;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-20} {1,7} {2,8:F4} {3,8:F4} {4,8:F4}",
                    name, st.Count, st.Median, st.Mean, st.Percentile(90)));
            }
        }

        /// <summary>Kamera s nejvic body v dane sade — tu si <c>CorridorLocalizer</c> vybere.</summary>
        private static string Dominant(IEnumerable<(double Dev, double Range, string Cam, bool Left)> src)
            => src.GroupBy(x => x.Cam).OrderByDescending(g => g.Count()).First().Key;

        private static (DateTime T, double X, double Y, double Th) Nearest(
            List<(DateTime T, double X, double Y, double Th)> truth, DateTime t)
        {
            int lo = 0, hi = truth.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (truth[mid].T < t) lo = mid + 1; else hi = mid;
            }
            if (lo > 0 && (t - truth[lo - 1].T) <= (truth[lo].T - t)) return truth[lo - 1];
            return truth[lo];
        }
    }
}
