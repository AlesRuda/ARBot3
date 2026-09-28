using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Jak presna je poza fuze proti PRAVDE</b> — chyba rozlozena na <b>pricnou a podelnou
    /// vuci skutecnemu smeru jizdy</b> a chyba kurzu, s podilem casu nad prahy. K tomu souhrn
    /// toho, co do fuze poslala hranova lokalizace (koridor), aby slo A/B porovnat beh
    /// s korekcemi a bez nich (<c>corridorsend=true|false</c>).
    ///
    /// <para><b>Proc vlastni report a ne <c>corrections</c>.</b> Ten rozklada chybu podle
    /// <i>odhadnuteho</i> kurzu, nedava podelnou slozku ani znamenko a nic neodrezava. Tady se
    /// rozklada podle kurzu <b>pravdy</b> (smer, kterym robot skutecne jel), protoze otazka zni
    /// „jak daleko vedle cesty si robot mysli, ze je" — a to je kolmo na skutecnou jizdu.</para>
    ///
    /// <para><b>Usazovani.</b> Odrezava se <c>--skip</c> sekund (vychozi 20) od <b>rozjezdu</b>
    /// (prvni vzorek pravdy s |v| &gt; 0,1 m/s), ne od zacatku zaznamu — cekani na stisk stopu
    /// se do chyby nepocita vubec. Jen <see cref="GroundTruthMsg"/> (simulace).</para>
    /// </summary>
    public static class TruthReport
    {
        public static void Run(RecordFile rec, double skipS, double binS)
        {
            var states = new List<RobotStateMsg>();
            var truth = new List<GroundTruthMsg>();
            var corr = new List<RoadCorridorMsg>();
            var diag = new List<MeasurementDiagMsg>();
            foreach (var e in rec.Index)
            {
                switch (e.MsgName)
                {
                    case "RobotStateMsg": if (rec.Read(e) is RobotStateMsg s) states.Add(s); break;
                    case "GroundTruthMsg": if (rec.Read(e) is GroundTruthMsg g) truth.Add(g); break;
                    case "RoadCorridorMsg": if (rec.Read(e) is RoadCorridorMsg c) corr.Add(c); break;
                    case "MeasurementDiagMsg": if (rec.Read(e) is MeasurementDiagMsg d) diag.Add(d); break;
                }
            }
            states.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            truth.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            corr.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));

            Console.WriteLine($"RobotStateMsg {states.Count}, GroundTruthMsg {truth.Count}, "
                              + $"RoadCorridorMsg {corr.Count}, MeasurementDiagMsg {diag.Count}");
            if (truth.Count == 0 || states.Count == 0)
            {
                Console.WriteLine("Zaznam nenese GroundTruthMsg (jen virtualni HW) nebo RobotStateMsg — neni co merit.");
                return;
            }

            var move = truth.FirstOrDefault(g => Math.Abs(g.V) > 0.1);
            if (move == null)
            {
                Console.WriteLine("Robot se v zaznamu nerozjel (|v| pravdy nikdy > 0,1 m/s).");
                return;
            }
            DateTime t0 = move.TimeStamp;
            DateTime tCut = t0.AddSeconds(skipS);
            DateTime tEnd = truth[truth.Count - 1].TimeStamp;
            double path = 0;
            for (int i = 1; i < truth.Count; i++)
                if (truth[i].TimeStamp >= tCut)
                    path += Math.Sqrt(Sq(truth[i].X - truth[i - 1].X) + Sq(truth[i].Y - truth[i - 1].Y));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "Rozjezd {0:HH:mm:ss.f}, odrezano {1:F0} s usazovani, meri se {2:F0} s, ujeto (pravda) {3:F0} m",
                t0, skipS, (tEnd - tCut).TotalSeconds, path));
            Console.WriteLine();

            // --- chyba pozy ---------------------------------------------------------------
            var lat = new Stats("|pricna| [m]");
            var lon = new Stats("|podelna| [m]");
            var pos = new Stats("|poloha| [m]");
            var hdg = new Stats("|kurz| [deg]");
            var latS = new Stats("pricna se znamenkem [m]");
            var lonS = new Stats("podelna se znamenkem [m]");
            var hdgS = new Stats("kurz se znamenkem [deg]");
            int n = 0, over05 = 0, over1 = 0, over2 = 0, lonOver1 = 0;
            var bins = new SortedDictionary<int, (Stats lat, Stats lon, Stats hdg, Stats v)>();
            var latM = new Stats("|pricna| jen za jizdy [m]");
            var lonM = new Stats("|podelna| jen za jizdy [m]");
            var hdgM = new Stats("|kurz| jen za jizdy [deg]");
            int nM = 0, over05M = 0, over1M = 0;

            int ti = 0;
            foreach (var s in states)
            {
                if (s.TimeStamp < tCut) continue;
                while (ti + 1 < truth.Count
                       && Math.Abs((truth[ti + 1].TimeStamp - s.TimeStamp).TotalSeconds)
                          <= Math.Abs((truth[ti].TimeStamp - s.TimeStamp).TotalSeconds))
                    ti++;
                var g = truth[ti];
                if (Math.Abs((g.TimeStamp - s.TimeStamp).TotalSeconds) > 0.2) continue;

                // pravda minus odhad, rozlozeno podle SKUTECNEHO smeru jizdy (pri couvani otocene)
                double dx = g.X - s.X, dy = g.Y - s.Y;
                double dir = g.V < -0.05 ? g.Theta + Math.PI : g.Theta;
                double cl = Math.Cos(dir), sl = Math.Sin(dir);
                double eLon = cl * dx + sl * dy;          // + = pravda je PRED odhadem
                double eLat = -sl * dx + cl * dy;         // + = pravda je VLEVO od odhadu
                double eH = Wrap(g.Theta - s.Theta) * 180.0 / Math.PI;

                lat.Add(Math.Abs(eLat)); lon.Add(Math.Abs(eLon)); pos.Add(Math.Sqrt(dx * dx + dy * dy));
                hdg.Add(Math.Abs(eH)); latS.Add(eLat); lonS.Add(eLon); hdgS.Add(eH);
                n++;
                if (Math.Abs(eLat) > 0.5) over05++;
                if (Math.Abs(eLat) > 1.0) over1++;
                if (Math.Abs(eLat) > 2.0) over2++;
                if (Math.Abs(eLon) > 1.0) lonOver1++;

                int b = (int)Math.Floor((s.TimeStamp - t0).TotalSeconds / binS);
                if (!bins.TryGetValue(b, out var bs))
                    bins[b] = bs = (new Stats("lat"), new Stats("lon"), new Stats("hdg"), new Stats("v"));
                bs.lat.Add(Math.Abs(eLat)); bs.lon.Add(Math.Abs(eLon)); bs.hdg.Add(Math.Abs(eH)); bs.v.Add(Math.Abs(g.V));
                if (Math.Abs(g.V) > 0.2)
                {
                    latM.Add(Math.Abs(eLat)); lonM.Add(Math.Abs(eLon)); hdgM.Add(Math.Abs(eH)); nM++;
                    if (Math.Abs(eLat) > 0.5) over05M++;
                    if (Math.Abs(eLat) > 1.0) over1M++;
                }
            }

            Console.WriteLine("CHYBA POZY PROTI PRAVDE (pravda - odhad; rozklad podle skutecneho smeru jizdy)");
            foreach (var st in new[] { lat, lon, pos, hdg, latS, lonS, hdgS })
                Console.WriteLine("  " + st.Line());
            if (n > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  podil casu: |pricna| > 0,5 m {0:F1} %, > 1 m {1:F1} %, > 2 m {2:F1} %; |podelna| > 1 m {3:F1} %",
                    100.0 * over05 / n, 100.0 * over1 / n, 100.0 * over2 / n, 100.0 * lonOver1 / n));
            foreach (var st in new[] { latM, lonM, hdgM })
                Console.WriteLine("  " + st.Line());
            if (nM > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  za jizdy (|v| pravdy > 0,2 m/s, {0:F1} % vzorku): |pricna| > 0,5 m {1:F1} %, > 1 m {2:F1} %",
                    100.0 * nM / Math.Max(1, n), 100.0 * over05M / nM, 100.0 * over1M / nM));
            Console.WriteLine();

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PRUBEH PO {0:F0} s (od rozjezdu; p50 / max)", binS));
            Console.WriteLine("     od [s]   pricna p50    max   podelna p50    max   kurz p50   max [deg]   |v| p50");
            foreach (var kv in bins)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,8:F0}   {1,9:F3} {2,7:F3}   {3,10:F3} {4,7:F3}   {5,7:F2} {6,6:F2}   {7,7:F2}",
                    kv.Key * binS, kv.Value.lat.Median, kv.Value.lat.Max, kv.Value.lon.Median,
                    kv.Value.lon.Max, kv.Value.hdg.Median, kv.Value.hdg.Max, kv.Value.v.Median));
            Console.WriteLine();

            // --- koridor ------------------------------------------------------------------
            var cAfter = corr.Where(c => c.TimeStamp >= tCut).ToList();
            Console.WriteLine($"KORIDOR (po odriznuti usazovani; celkem v zaznamu {corr.Count} cyklu)");
            if (cAfter.Count == 0)
                Console.WriteLine("  zadny RoadCorridorMsg (corridor=false?)");
            else
            {
                int ok = cAfter.Count(c => c.FixReason == (byte)CorridorFixReason.Ok);
                int eLat = cAfter.Count(c => c.EmittedLateral), eHd = cAfter.Count(c => c.EmittedHeading);
                int single = cAfter.Count(c => c.FixReason == (byte)CorridorFixReason.Ok && c.SingleSide != 0);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  cyklu {0}, Ok {1} ({2:F1} %), z toho jednohranovych {3}", cAfter.Count, ok,
                    100.0 * ok / cAfter.Count, single));
                Console.WriteLine($"  do fuze odeslano: pricnych {eLat}, kurzovych {eHd}  (vsech cyklu v zaznamu: "
                                  + $"{corr.Count(c => c.EmittedLateral)} / {corr.Count(c => c.EmittedHeading)})");
                foreach (var grp in cAfter.GroupBy(c => (CorridorFixReason)c.FixReason).OrderByDescending(x => x.Count()))
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    {0,-22} {1,6} {2,6:F1} %",
                        grp.Key, grp.Count(), 100.0 * grp.Count() / cAfter.Count));
            }
            var dc = diag.Where(d => d.Source != null && d.Source.IndexOf("Corridor", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (dc.Count > 0)
            {
                Console.WriteLine("  MeasurementDiagMsg (co fuze s merenim udelala):");
                foreach (var grp in dc.GroupBy(d => d.Source).OrderBy(x => x.Key))
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0,-20} n={1,6}  prijato {2,6}  s limitem kroku {3,6}  NIS p50 {4:F2}",
                        grp.Key, grp.Count(), grp.Count(d => d.Accepted), grp.Count(d => d.StepLimited),
                        Median(grp.Select(d => d.Nis))));
            }
            else
                Console.WriteLine("  MeasurementDiagMsg od koridoru: zadne (nic neslo do fuze, nebo measdiag vypnute)");
        }

        private static double Median(IEnumerable<double> xs)
        {
            var s = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            return s.Count == 0 ? double.NaN : s[s.Count / 2];
        }

        private static double Sq(double x) => x * x;

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }
    }
}
