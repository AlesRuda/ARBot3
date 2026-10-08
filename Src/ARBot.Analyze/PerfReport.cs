using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Stihalo rizeni za jizdy?</b> (prikaz <c>perf</c>, faze 4 mereni vykonu, registr
    /// <c>prov-perf-monitoring</c>). Rozbor <see cref="PerfMsg"/> ze zaznamu - panel Tools -> Vykon
    /// ukazuje jen aktualni sekundu, po jizde je potreba rozdeleni, casy a nejhorsi mista. Cte se
    /// jen <b>index a PerfMsg</b> (zadne snimky), takze to jde pustit i nad 70GB zaznamem.
    ///
    /// <para><b>Co tiskne.</b> (1) rozdeleni (p50/p90/p99/max) taktu, zameskanych taktu,
    /// obsazenosti periody, zpozdeni taktu a CPU procesu po sekundach, podil sekund se
    /// zameskanymi takty a verdikty; (2) totez po minutach; (3) nejhorsi sekundy; (4) stupne
    /// pipeline (zpracovano, ZAHOZENO, fronta, doba zpracovani); (5) jadra; (6) <b>mezery
    /// v proudech</b> - pro kazdy typ zpravy nejdelsi mezery v case porizeni (T_in) i prichodu
    /// (T_out), ticho vsech proudu najednou a soubezne vypadky kamer s rozlisenim „stoji cely
    /// proces" (mlci i IMU) proti „jen kamery".</para>
    ///
    /// <para><b>Proc i mezery v proudech, kdyz jde o PerfMsg.</b> Zasek celeho procesu PerfMsg
    /// sam neukaze dobre: sberac bezi na vlastnim casovaci, takze zaseknuta sekunda se projevi jen
    /// jako jeden delsi interval a navic dohanene takty (Track 1. 10. 2026, 15:15:41,8: interval
    /// 1,43 s s 11 zameskanymi takty a hned po nem 17 taktu a zpozdeni 1 131 ms). Ze zpravy samotne
    /// se neda poznat, jestli stal proces, nebo jen ridici smycka; z indexu ano - kdyz mlci i IMU
    /// (cas porizeni razitkuje driver na svem vlakne), stal proces nebo stroj.</para>
    ///
    /// <para>Prototyp vznikl 7. 10. 2026 mimo repozitar nad zaznamy z 1. 10. 2026 (bloky P a I);
    /// tady je prepsany do meridla. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public static class PerfReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <param name="fromArg">Zacatek okna: <c>HH:MM:SS</c> (hodiny ze zaznamu) nebo sekundy od zacatku; null = zacatek.</param>
        /// <param name="toArg">Konec okna, tytez formy; null = konec zaznamu.</param>
        /// <param name="top">Kolik nejhorsich sekund / mezer vypsat.</param>
        /// <param name="tichoMin">Od jake delky [s] se hlasi ticho vsech proudu.</param>
        public static void Run(RecordFile rec, string fromArg, string toArg, int top, double tichoMin)
        {
            var idx = rec.Index;
            long minT = long.MaxValue, maxT = long.MinValue;
            foreach (var e in idx)
            {
                long t = e.ArrivalTicks > 0 ? e.ArrivalTicks : e.CaptureTicks;
                if (t <= 0) continue;
                if (t < minT) minT = t;
                if (t > maxT) maxT = t;
            }
            if (minT == long.MaxValue) { Console.WriteLine("Index nenese zadne casy."); return; }
            var t0 = new DateTime(minT);
            var tEnd = new DateTime(maxT);
            DateTime wFrom = Cas(fromArg, t0, t0), wTo = Cas(toArg, t0, tEnd);

            var konf = LogConfig.Read(rec);
            double perfWarn = konf.Num("perfwarn") ?? 70;
            if (konf.Version != null) Console.WriteLine(konf.Version);
            Console.WriteLine(F("zaznam {0:HH:mm:ss.fff} - {1:HH:mm:ss.fff} ({2:F0} s), okno {3:HH:mm:ss} - {4:HH:mm:ss} ({5:F0} s); "
                                + "perf={6}, perfwarn={7:F0} %{8}",
                t0, tEnd, (tEnd - t0).TotalSeconds, wFrom, wTo, (wTo - wFrom).TotalSeconds,
                konf.Text("perf") ?? "?", perfWarn, konf.Num("perfwarn").HasValue ? "" : " (v zaznamu neni, default)"));
            Console.WriteLine();

            var perf = new List<PerfMsg>();
            foreach (var e in idx)
            {
                if (e.MsgName != "PerfMsg") continue;
                if (rec.Read(e) is PerfMsg p && p.To > wFrom && p.From < wTo) perf.Add(p);
            }
            perf.Sort((a, b) => a.From.CompareTo(b.From));

            if (perf.Count == 0)
                Console.WriteLine("PerfMsg v okne nejsou (perf=false, nebo binarka pred 1. 9. 2026) - jen mezery v proudech.\n");
            else
            {
                Souhrn(perf, perfWarn);
                PoMinutach(perf, perfWarn);
                Nejhorsi(perf, top);
                Stupne(perf);
                Jadra(perf);
            }
            Mezery(idx, wFrom, wTo, top, tichoMin);
        }

        // ==================================================================
        // 1. Souhrn
        // ==================================================================

        private static void Souhrn(List<PerfMsg> perf, double perfWarn)
        {
            Console.WriteLine("=== 1. PerfMsg PO SEKUNDACH (rozdeleni pres vsechny intervaly sberu) ===");
            var delky = perf.Select(p => (p.To - p.From).TotalSeconds).ToList();
            double pokryto = delky.Sum();
            Console.WriteLine(F("  zprav {0}, pokryto {1:F0} s, delka intervalu {2}", perf.Count, pokryto, Rozd(delky, "F3")));
            Console.WriteLine("  takty za interval        " + Rozd(perf.Select(p => (double)p.TickCount), "F0"));
            Console.WriteLine("  takty za sekundu         " + Rozd(perf.Where(p => p.To > p.From)
                                                                     .Select(p => p.TickCount / (p.To - p.From).TotalSeconds), "F1"));
            Console.WriteLine("  zameskane takty          " + Rozd(perf.Select(p => (double)p.MissedTicks), "F0"));
            Console.WriteLine("  obsazenost periody AVG % " + Rozd(perf.Select(p => p.OccupancyAvgPct), "F1"));
            Console.WriteLine("  obsazenost periody MAX % " + Rozd(perf.Select(p => p.OccupancyMaxPct), "F1"));
            Console.WriteLine("  zpozdeni taktu AVG [ms]  " + Rozd(perf.Select(p => p.DelayAvgMs), "F1"));
            Console.WriteLine("  zpozdeni taktu MAX [ms]  " + Rozd(perf.Select(p => p.DelayMaxMs), "F1"));
            Console.WriteLine("  CPU procesu [% stroje]   " + Rozd(perf.Select(p => p.ProcessCpuPct).Where(x => x >= 0), "F1"));
            var stroj = perf.Select(p => p.MachineCpuPct).Where(x => x >= 0).ToList();
            Console.WriteLine("  CPU stroje [%]           " + (stroj.Count > 0 ? Rozd(stroj, "F1") : "neznamo (faze 3 mereni neni hotova)"));

            int sZamesk = perf.Count(p => p.MissedTicks > 0);
            Console.WriteLine(F("  zameskanych taktu celkem {0}; intervalu se zameskanym taktem {1} z {2} ({3:F1} %)",
                perf.Sum(p => (long)p.MissedTicks), sZamesk, perf.Count, 100.0 * sZamesk / perf.Count));
            Console.WriteLine("  verdikt: " + string.Join(", ", perf.GroupBy(p => p.Verdict).OrderBy(g => g.Key)
                                                                   .Select(g => F("{0} {1} ({2:F1} %)", g.Key, g.Count(), 100.0 * g.Count() / perf.Count))));
            foreach (double prah in new[] { 50.0, perfWarn, 90.0 }.Distinct().OrderBy(x => x))
                Console.WriteLine(F("  intervalu s obsazenosti MAX >= {0:F0} %: {1}, AVG >= {0:F0} %: {2}",
                    prah, perf.Count(p => p.OccupancyMaxPct >= prah), perf.Count(p => p.OccupancyAvgPct >= prah)));

            // Delsi interval sberu = casovac sberace se opozdil. Sberac bezi mimo ridici smycku,
            // takze to je stopa zaseku celeho procesu, ne jen smycky.
            double medDelka = Q(delky.OrderBy(x => x).ToList(), 0.5);
            var dlouhe = perf.Where(p => (p.To - p.From).TotalSeconds > Math.Max(1.3, 1.3 * medDelka)).ToList();
            Console.WriteLine(F("  intervalu sberu delsich nez 1,3x median ({0:F2} s) - casovac sberace se opozdil: {1}", medDelka, dlouhe.Count));
            foreach (var p in dlouhe.OrderByDescending(p => (p.To - p.From).TotalSeconds).Take(10))
                Console.WriteLine(F("    {0:HH:mm:ss.fff} - {1:HH:mm:ss.fff} ({2:F2} s)  takty {3}, zamesk. {4}, zpozdeni max {5:F0} ms",
                    p.From, p.To, (p.To - p.From).TotalSeconds, p.TickCount, p.MissedTicks, p.DelayMaxMs));
            Console.WriteLine("  (zameskany takt = takt vydany az po svem planovanem case + perioda; verdikt Error je kazda sekunda");
            Console.WriteLine("   s aspon jednim, Warning obsazenost MAX >= perfwarn. Obsazenost = doba prace taktu / perioda.)");
            Console.WriteLine();
        }

        // ==================================================================
        // 2. Po minutach
        // ==================================================================

        private static void PoMinutach(List<PerfMsg> perf, double perfWarn)
        {
            Console.WriteLine("=== 2. PO MINUTACH ===");
            Console.WriteLine("  minuta  zprav  takty  zamesk.  s>0  obsaz.AVG prum  obsaz.MAX max  zpozd.MAX p50/max [ms]  CPU proc. prum/max  Error Warn  zahozeno (stupne)");
            foreach (var g in perf.GroupBy(p => new DateTime(p.From.Year, p.From.Month, p.From.Day, p.From.Hour, p.From.Minute, 0)))
            {
                var l = g.ToList();
                var cpu = l.Select(p => p.ProcessCpuPct).Where(x => x >= 0).ToList();
                var zpozd = l.Select(p => p.DelayMaxMs).OrderBy(x => x).ToList();
                Console.WriteLine(F("  {0:HH:mm}   {1,5}  {2,5}  {3,7}  {4,3}  {5,14:F1}  {6,13:F1}  {7,10:F0} / {8,6:F0}       {9,8} / {10,5}  {11,5} {12,4}  {13}",
                    g.Key, l.Count, l.Sum(p => p.TickCount), l.Sum(p => p.MissedTicks), l.Count(p => p.MissedTicks > 0),
                    l.Average(p => p.OccupancyAvgPct), l.Max(p => p.OccupancyMaxPct), Q(zpozd, 0.5), zpozd[^1],
                    cpu.Count > 0 ? cpu.Average().ToString("F1", Ci) : "-", cpu.Count > 0 ? cpu.Max().ToString("F1", Ci) : "-",
                    l.Count(p => p.Verdict == PerfVerdict.Error), l.Count(p => p.Verdict == PerfVerdict.Warning),
                    l.SelectMany(p => p.Stages).Sum(s => s.Dropped)));
            }
            Console.WriteLine();
        }

        // ==================================================================
        // 3. Nejhorsi sekundy
        // ==================================================================

        private static void Nejhorsi(List<PerfMsg> perf, int top)
        {
            Console.WriteLine($"=== 3. NEJHORSI INTERVALY ({top}) ===");
            Console.WriteLine("  podle zpozdeni taktu MAX:");
            foreach (var p in perf.OrderByDescending(p => p.DelayMaxMs).Take(top)) Radek(p);
            Console.WriteLine("  podle zameskanych taktu:");
            foreach (var p in perf.Where(p => p.MissedTicks > 0).OrderByDescending(p => p.MissedTicks)
                                  .ThenByDescending(p => p.DelayMaxMs).Take(top)) Radek(p);
            Console.WriteLine("  podle obsazenosti MAX:");
            foreach (var p in perf.OrderByDescending(p => p.OccupancyMaxPct).Take(top)) Radek(p);
            Console.WriteLine();
        }

        private static void Radek(PerfMsg p)
        {
            string zahoz = string.Join(", ", p.Stages.Where(s => s.Dropped > 0).Select(s => $"{s.Name}:{s.Dropped}"));
            string fronta = string.Join(", ", p.Stages.Where(s => s.QueueLength > 0).Select(s => $"{s.Name}:{s.QueueLength}"));
            var nejdelsi = p.Stages.Count > 0 ? p.Stages.OrderByDescending(s => s.MaxMs).First() : default;
            Console.WriteLine(F("    {0:HH:mm:ss.fff}-{1:HH:mm:ss.fff}  takty {2,2} zamesk {3,2}  obsaz avg/max {4,4:F1}/{5,5:F1} %  zpozd avg/max {6,6:F1}/{7,6:F1} ms (nejhorsi takt {8:HH:mm:ss.fff}, jadro {9})  CPU {10,5:F1} %"
                                + "{11}{12}{13}",
                p.From, p.To, p.TickCount, p.MissedTicks, p.OccupancyAvgPct, p.OccupancyMaxPct, p.DelayAvgMs, p.DelayMaxMs,
                p.WorstTickTime, p.WorstProcessorId, p.ProcessCpuPct,
                nejdelsi.Name != null ? F("  nejdelsi zprava {0} {1:F0} ms", nejdelsi.Name, nejdelsi.MaxMs) : "",
                zahoz.Length > 0 ? "  zahozeno " + zahoz : "", fronta.Length > 0 ? "  fronta " + fronta : ""));
        }

        // ==================================================================
        // 4. Stupne pipeline
        // ==================================================================

        private static void Stupne(List<PerfMsg> perf)
        {
            Console.WriteLine("=== 4. STUPNE PIPELINE (Processed/Dropped jsou PRIRUSTKY za interval, fronta je stav) ===");
            Console.WriteLine("  stupen                  zprac.celkem  zprac/s p50  ZAHOZENO  (% prichozich)  s>0  fronta p50/p99/max  AvgMs p50/p90/max     MaxMs p99/max  (kdy max)");
            var jmena = perf.SelectMany(p => p.Stages.Select(s => s.Name)).Distinct().ToList();
            foreach (var j in jmena)
            {
                var st = perf.SelectMany(p => p.Stages.Where(s => s.Name == j).Select(s => (p, s))).ToList();
                long proc = st.Sum(x => x.s.Processed), drop = st.Sum(x => x.s.Dropped);
                var rychl = st.Where(x => x.p.To > x.p.From).Select(x => x.s.Processed / (x.p.To - x.p.From).TotalSeconds).OrderBy(x => x).ToList();
                var fronta = st.Select(x => (double)x.s.QueueLength).OrderBy(x => x).ToList();
                var avg = st.Where(x => x.s.Processed > 0).Select(x => x.s.AvgMs).OrderBy(x => x).ToList();
                var max = st.Select(x => x.s.MaxMs).OrderBy(x => x).ToList();
                var kdy = st.OrderByDescending(x => x.s.MaxMs).First();
                Console.WriteLine(F("  {0,-22} {1,12}  {2,11:F1}  {3,8}  ({4,6:F2} %)     {5,4}  {6,4:F0}/{7,3:F0}/{8,4:F0}      {9,5:F2}/{10,5:F2}/{11,6:F1}   {12,6:F1}/{13,6:F0}  ({14:HH:mm:ss})",
                    j, proc, Q(rychl, 0.5), drop, proc + drop > 0 ? 100.0 * drop / (proc + drop) : 0,
                    st.Count(x => x.s.Dropped > 0), Q(fronta, 0.5), Q(fronta, 0.99), fronta.Count > 0 ? fronta[^1] : double.NaN,
                    Q(avg, 0.5), Q(avg, 0.9), avg.Count > 0 ? avg[^1] : double.NaN, Q(max, 0.99), max.Count > 0 ? max[^1] : double.NaN,
                    kdy.p.From));
            }
            Console.WriteLine("  (zahozeno = fronta stupne pretekla a politika DropOldest/DropNewest zpravu zahodila - u LocalNavigator");
            Console.WriteLine("   to jsou snimky, ktere se do gridu nezapsaly; MaxMs = nejdelsi zpracovani JEDNE zpravy v intervalu)");
            Console.WriteLine();
        }

        // ==================================================================
        // 5. Jadra
        // ==================================================================

        private static void Jadra(List<PerfMsg> perf)
        {
            var c = perf.SelectMany(p => p.Cores).ToList();
            if (c.Count == 0) return;
            Console.WriteLine("=== 5. TAKTY PO JADRECH (RK3588 ma nestejna jadra: 0-3 A55, 4-7 A76) ===");
            long celkem = c.Sum(x => (long)x.TickCount);
            foreach (var g in c.GroupBy(x => x.ProcessorId).OrderBy(g => g.Key))
            {
                var avg = g.Where(x => x.TickCount > 0).Select(x => x.AvgMs).OrderBy(x => x).ToList();
                Console.WriteLine(F("  jadro {0,2}: taktu {1,7} ({2,5:F1} %), doba taktu p50 {3:F2} ms, p90 {4:F2} ms, max {5:F2} ms",
                    g.Key, g.Sum(x => (long)x.TickCount), 100.0 * g.Sum(x => (long)x.TickCount) / Math.Max(1, celkem),
                    Q(avg, 0.5), Q(avg, 0.9), avg.Count > 0 ? avg[^1] : double.NaN));
            }
            Console.WriteLine();
        }

        // ==================================================================
        // 6. Mezery v proudech (jen index)
        // ==================================================================

        private sealed class Proud
        {
            public string Klic;
            public string MsgName;
            public List<long> In = new List<long>();
            public List<long> Out = new List<long>();
        }

        private static void Mezery(List<IndexEntry> idx, DateTime wFrom, DateTime wTo, int top, double tichoMin)
        {
            Console.WriteLine("=== 6. MEZERY V PROUDECH (jen index: T_in = cas porizeni, T_out = prichod do streamu) ===");
            long a = wFrom.Ticks, b = wTo.Ticks;
            var proudy = new Dictionary<string, Proud>();
            var vsechnyOut = new List<long>();
            foreach (var e in idx)
            {
                long t = e.ArrivalTicks > 0 ? e.ArrivalTicks : e.CaptureTicks;
                if (t < a || t > b) continue;
                string klic = string.IsNullOrEmpty(e.Name) ? e.MsgName : e.MsgName + " [" + e.Name + "]";
                if (!proudy.TryGetValue(klic, out var pr)) proudy[klic] = pr = new Proud { Klic = klic, MsgName = e.MsgName };
                if (e.CaptureTicks > 0) pr.In.Add(e.CaptureTicks);
                if (e.ArrivalTicks > 0)
                {
                    pr.Out.Add(e.ArrivalTicks);
                    // Kamery a Info se do „ticha vseho" nepocitaji: kamery maji vlastni vypadky
                    // (USB) a Info chodi nepravidelne. Ticho zbytku = nic nedoslo do streamu.
                    if (e.MsgName != "CameraFrame" && e.MsgName != "Info") vsechnyOut.Add(e.ArrivalTicks);
                }
            }
            Console.WriteLine("  proud                                    cas    n        perioda p50  prah [s]  mezer  nejdelsi (zacatek +delka)");
            foreach (var pr in proudy.Values.OrderByDescending(p => Math.Max(p.In.Count, p.Out.Count)))
            {
                foreach (var (druh, l) in new[] { ("T_in ", pr.In), ("T_out", pr.Out) })
                {
                    if (l.Count < 20) continue;
                    l.Sort();
                    var m = Mezery(l, out double perioda, out double prah, out bool nepravidelny);
                    Console.WriteLine(F("  {0,-40} {1}  {2,7}  {3,9:F1} ms  {4,7:F2}  {5,5}  {6}{7}",
                        pr.Klic.Length > 40 ? pr.Klic.Substring(0, 40) : pr.Klic, druh, l.Count, perioda * 1000, prah, m.Count,
                        string.Join(", ", m.Take(Math.Min(top, 4)).Select(x => F("{0:HH:mm:ss.fff} +{1:F2}s", x.od, x.dt))),
                        nepravidelny ? "  (nepravidelny proud - mezery nejsou vypadky)" : ""));
                }
            }
            Console.WriteLine("  (prah = max(0,3 s; 5x perioda p50). Nepravidelny = prumerny rozestup > 5x median: proud chodi v davkach,");
            Console.WriteLine("   napr. MeasurementDiagMsg jen pri mereni koridoru - jeho 'mezera' je jen ticho, ne vypadek.)");
            Console.WriteLine();

            // Ticho vsech proudu krome kamer a Info.
            vsechnyOut.Sort();
            var ticho = new List<(DateTime od, double dt)>();
            for (int i = 1; i < vsechnyOut.Count; i++)
            {
                double dt = (vsechnyOut[i] - vsechnyOut[i - 1]) / 1e7;
                if (dt > tichoMin) ticho.Add((new DateTime(vsechnyOut[i - 1]), dt));
            }
            Console.WriteLine(F("  TICHO VSECH proudu krome kamer a Info (T_out) delsi nez {0:F2} s: {1}", tichoMin, ticho.Count));
            foreach (var x in ticho.OrderByDescending(x => x.dt).Take(top))
                Console.WriteLine(F("    {0:HH:mm:ss.fff} +{1:F2} s", x.od, x.dt));
            Console.WriteLine("  (do streamu nedoslo NIC - ani IMU 100 Hz, ani motory ~90 Hz: stal cely proces, nebo vlakno zaznamu)");
            Console.WriteLine();

            SoubezneKamery(proudy, top);
        }

        /// <summary>
        /// Soubezne vypadky kamer: kdy mlci (T_in) VSECHNY kamery zaroven, a jestli zaroven mlci
        /// i IMU. Cas porizeni IMU razitkuje driver na svem vlakne pri prijmu ramce, takze kdyz mlci
        /// i on, nestaly kamery, ale proces (nebo stroj) - a to je jina porucha nez USB.
        /// </summary>
        private static void SoubezneKamery(Dictionary<string, Proud> proudy, int top)
        {
            var kamery = proudy.Values.Where(p => p.MsgName == "CameraFrame" && p.In.Count >= 20).ToList();
            Console.WriteLine($"  SOUBEZNE VYPADKY KAMER (T_in, vsech {kamery.Count} kamer zaroven) a co delal zbytek:");
            if (kamery.Count < 2) { Console.WriteLine("    mene nez dve kamery - nema smysl."); Console.WriteLine(); return; }

            // Pruniky mezer: zacne se mezerami prvni kamery a postupne se zuzi o kazdou dalsi.
            List<(long a, long b)> prunik = null;
            foreach (var k in kamery)
            {
                var m = Mezery(k.In, out _, out _, out _)
                        .Select(x => (a: x.od.Ticks, b: x.od.Ticks + (long)(x.dt * 1e7))).ToList();
                if (prunik == null) { prunik = m; continue; }
                var novy = new List<(long a, long b)>();
                foreach (var p in prunik)
                    foreach (var q in m)
                    {
                        long s = Math.Max(p.a, q.a), e = Math.Min(p.b, q.b);
                        if (e > s) novy.Add((s, e));
                    }
                prunik = novy;
            }
            var imu = proudy.Values.Where(p => p.MsgName == "IMUState").OrderByDescending(p => p.In.Count).FirstOrDefault();
            var motor = proudy.Values.Where(p => p.MsgName == "MotorStateBase").OrderByDescending(p => p.In.Count).FirstOrDefault();
            Console.WriteLine(F("    nalezeno {0}", prunik.Count));
            foreach (var (s, e) in prunik.OrderByDescending(x => x.b - x.a).Take(top).OrderBy(x => x.a))
            {
                double d = (e - s) / 1e7;
                double imuTicho = NejdelsiTicho(imu?.In, s, e);
                double motTicho = NejdelsiTicho(motor?.In, s, e);
                string verdikt = !double.IsNaN(imuTicho) && imuTicho >= 0.5 * d ? "STAL PROCES (mlci i IMU)" : "jen kamery (IMU bezi)";
                Console.WriteLine(F("    {0:HH:mm:ss.fff} +{1:F2} s  nejdelsi ticho v nem: IMU T_in {2:F2} s, motory T_in {3:F2} s  -> {4}",
                    new DateTime(s), d, imuTicho, motTicho, verdikt));
            }
            Console.WriteLine();
        }

        /// <summary>Nejdelsi mezera serazenych casu, ktera se prekryva s intervalem [s, e] [s]; NaN = proud neni.</summary>
        private static double NejdelsiTicho(List<long> l, long s, long e)
        {
            if (l == null || l.Count == 0) return double.NaN;
            int i = l.BinarySearch(s);
            if (i < 0) i = ~i;
            i = Math.Max(1, i);
            double best = 0;
            for (; i < l.Count; i++)
            {
                if (l[i - 1] > e) break;
                if (l[i] < s) continue;
                long a = Math.Max(l[i - 1], s), b = Math.Min(l[i], e);
                if (b > a) best = Math.Max(best, (b - a) / 1e7);
            }
            return best;
        }

        /// <summary>
        /// Mezery v serazenych casech nad prahem <c>max(0,3 s; 5 x perioda p50)</c>, od nejdelsi.
        /// <paramref name="nepravidelny"/> = prumerny rozestup je vic nez 5x median (davkovy proud).
        /// </summary>
        private static List<(DateTime od, double dt)> Mezery(List<long> serazene, out double perioda, out double prah, out bool nepravidelny)
        {
            var dts = new List<double>(Math.Max(0, serazene.Count - 1));
            for (int i = 1; i < serazene.Count; i++) dts.Add((serazene[i] - serazene[i - 1]) / 1e7);
            var s = dts.OrderBy(x => x).ToList();
            perioda = Q(s, 0.5);
            double prumer = dts.Count > 0 ? dts.Average() : double.NaN;
            nepravidelny = perioda > 0 ? prumer > 5 * perioda : prumer > 0.01;
            prah = Math.Max(0.3, 5 * (double.IsNaN(perioda) ? 0 : perioda));
            var m = new List<(DateTime, double)>();
            for (int i = 1; i < serazene.Count; i++)
            {
                double dt = (serazene[i] - serazene[i - 1]) / 1e7;
                if (dt > prah) m.Add((new DateTime(serazene[i - 1]), dt));
            }
            return m.OrderByDescending(x => x.Item2).ToList();
        }

        // ==================================================================
        // Pomocne
        // ==================================================================

        /// <summary>Cas z prepinace: <c>HH:MM:SS</c> (hodiny ze zaznamu, den z jeho zacatku) nebo sekundy od zacatku.</summary>
        private static DateTime Cas(string arg, DateTime t0, DateTime vychozi)
        {
            if (string.IsNullOrWhiteSpace(arg)) return vychozi;
            if (arg.Contains(":"))
            {
                if (!TimeSpan.TryParse(arg, Ci, out var tod))
                    throw new ArgumentException($"Cas '{arg}' nejde precist (cekam HH:MM:SS nebo sekundy).");
                return t0.Date + tod;
            }
            return t0.AddSeconds(double.Parse(arg, Ci));
        }

        /// <summary>Kvantil serazeneho seznamu (nejblizsi rad); NaN pro prazdny.</summary>
        private static double Q(List<double> serazene, double q)
            => serazene.Count == 0 ? double.NaN
             : serazene[Math.Min(serazene.Count - 1, Math.Max(0, (int)Math.Round(q * (serazene.Count - 1))))];

        /// <summary>Rozdeleni: n, p50, p90, p99, max, min.</summary>
        private static string Rozd(IEnumerable<double> xs, string f)
        {
            var s = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (s.Count == 0) return "n=0";
            return F("n={0} p50={1} p90={2} p99={3} max={4} min={5}", s.Count,
                Q(s, 0.5).ToString(f, Ci), Q(s, 0.9).ToString(f, Ci), Q(s, 0.99).ToString(f, Ci),
                s[^1].ToString(f, Ci), s[0].ToString(f, Ci));
        }

        private static string F(string fmt, params object[] a) => string.Format(Ci, fmt, a);
    }
}
