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
    /// proces" (mlci i IMU) proti „jen kamery"; (7) <b>lokalni navigace po snimcich</b> z
    /// <see cref="LocalPlanMsg"/> - doba vypoctu (chvost p99/p99,9/max), zpozdeni planu za snimkem,
    /// rozpad podle stavu planu a expandovanych bunek A*, po minutach a nejpomalejsi plany
    /// (od 13. 10. 2026, <c>lp-vykon-retezu-na-arm</c>).</para>
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
            LokalniNavigace(rec, konf, wFrom, wTo, top);
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
            Console.WriteLine("  (zahozeno = fronta stupne pretekla a politika DropOldest/DropNewest zpravu zahodila - ZPRAVY libovolneho typu,");
            Console.WriteLine("   u LocalNavigator i RobotStateMsg/DriveCommandMsg, ne jen snimky; zprava rozpracovana pri odectu se pocita");
            Console.WriteLine("   jako zahozena (artefakt +1). AvgMs je prumer na ZPRAVU vcetne tech, ktere Consume hned vrati - u LocalNavigator");
            Console.WriteLine("   je na snimek ~2x vic, spravne cislo je blok 7. MaxMs = nejdelsi zpracovani JEDNE zpravy v intervalu)");
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
        // 7. Lokalni navigace po snimcich (LocalPlanMsg)
        // ==================================================================

        /// <summary>
        /// Doba vypoctu a zpozdeni lokalni navigace po jednotlivych planech (bod 8 pruzkumu,
        /// <c>lp-vykon-retezu-na-arm</c>, 13. 10. 2026). <c>LocalNavigator</c> nebezi v taktu ridici
        /// smycky, ale po KAZDEM snimku obou kamer: zapis do gridu, klin, pole odstupu, A*, stavba
        /// regulatoru a kontrola kolize. <c>ComputeMs</c> meri tohle cele (stopky od <c>GetStateAt</c>
        /// po predani planu, <c>LocalNavigator.Process</c>); zprava vznika jen tehdy, kdyz je cil
        /// nebo rozjeta draha - snimky ve fazi cekani (jen zapis do gridu) tu nejsou a jejich dobu
        /// ukazuje jen stupen <c>LocalNavigator</c> v bloku 4. ⚠️ <c>ComputeMs</c> je wall-clock a obsahuje
        /// i <c>GetStateAt</c> pod zamkem fuze - vcetne LINEHO prepoctu EKF po merenich mimo poradi, ktery
        /// zaplati prvni dotaz (<c>AsyncFusionEngine.EnsureValid</c>) - a preempci, GC i zasek procesu.
        ///
        /// <para><b>Rozpocet.</b> Snimky nechodi, jak je kamera vyrobi: ridici smycka si na zacatku taktu
        /// (100 ms) vezme z kazde kamery nejnovejsi snimek a OBA preda navigatoru (<c>ControlLoop</c>,
        /// <c>HwCameraPullSource</c>), takze chodi po DAVKACH nejvys jeden na kameru za takt, ~18/s.
        /// Kriterium je, aby se cena davky vesla do taktu (fronta ma kapacitu 4 zpravy a pri preteceni
        /// zahazuje nejstarsi). Davky se poznaji podle prichodu snimku do streamu (forward navigatoru
        /// i do zaznamu jde v temz <c>Emit</c>).</para>
        ///
        /// <para><b>Parovani plan ↔ snimek.</b> Prichod planu do streamu se razitkuje synchronne na vlakne
        /// navigatoru hned po vypoctu, takze <c>start = prichod − ComputeMs</c>. Zpracovany snimek je
        /// nejstarsi snimek predany navigatoru (prichod do streamu) do <c>start</c>, ktery jeste nedostal
        /// plan (fronta je FIFO; podrobne pravidla u parovani v kodu). ⚠️ <b>Cas porizeni ve zprave planu
        /// do 13. 10. 2026 NEPLATIL vzdy:</b> navigator pracoval nad zivym slotem <c>CaptureFramePool</c> kamery
        /// bez kopie a razitko cetl az po A*, takze kdyz kamera mezitim slot prepsala, nesl plan cas NOVEJSIHO
        /// snimku (3-23 % planu v 30 jizdach). Zpozdeni se proto pocita ze sparovaneho snimku. Od verze 5
        /// zpravy si navigator snimek kopiruje (<c>LocalNavigator.Post</c>) a prepsanych ma byt 0 %; jeho kopie
        /// ale jde v temz <c>Emit</c> pred zaznamem, takze prichod snimku v zaznamu zaostava o ~1 ms vic.
        /// Verze 5 nese i jadro CPU (rozpad podle jadra).</para>
        ///
        /// <para><b>Po probuzeni / navazujici.</b> Navigator nema vlastni vlakno (async <c>ConsumeLoop</c>
        /// pokracuje po prazdne fronte na vlakne z poolu - odvozeno z kodu). Plan, pred kterym navigator
        /// spal (nic nepocital vic nez 2 ms), byva zhruba v polovine pripadu v pomalem modu (~4,5x pri
        /// male praci, dvouvrcholove; mediany 1,4-3,5x) proti planu zpracovanemu hned za predchozim; v kazdem
        /// taktu jde prvni leva kamera, proto se to driv jevilo jako „leva je drazsi" (13. 10. 2026,
        /// mechanismus - jadro A55 / nabeh frekvence - nepotvrzeny).</para>
        ///
        /// <para><b>Zpozdeni</b> = prichod planu − porizeni zpracovaneho snimku: zpracovani ve vizi,
        /// cekani na takt, fronta a ComputeMs. <b>Fronta</b> = start − predani snimku, tedy cekani na
        /// predchozi snimek davky. K regulatoru pak plan dojde az v dalsim taktu (0-100 ms navic, tady neni).</para>
        /// </summary>
        private static void LokalniNavigace(RecordFile rec, LogConfig konf, DateTime wFrom, DateTime wTo, int top)
        {
            Console.WriteLine("=== 7. LOKALNI NAVIGACE PO SNIMCICH (LocalPlanMsg: zapis do gridu + klin + pole odstupu + A* + regulator) ===");
            string[] klice = { "smooth", "smoothcorners", "localframe", "colorshadow", "wedgefill", "carrotradius", "backproject", "mission" };
            Console.WriteLine("  konfigurace: " + string.Join(", ", klice.Select(k => k + "=" + (konf.Text(k) ?? "?"))));

            // Snimky z indexu: kamera, porizeni a predani navigatoru (prichod do streamu, tyz Emit).
            var snimkyL = new List<(string Kam, long Por, long Prichod)>();
            foreach (var e in rec.Index)
                if (e.MsgName == "CameraFrame" && e.CaptureTicks > 0 && e.ArrivalTicks > 0)
                    snimkyL.Add((string.IsNullOrEmpty(e.Name) ? "?" : e.Name.Split(' ')[0], e.CaptureTicks, e.ArrivalTicks));
            snimkyL.Sort((x, y) => x.Prichod.CompareTo(y.Prichod));

            var plany = new List<PlanZaznam>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "LocalPlanMsg") continue;
                long tc = e.CaptureTicks > 0 ? e.CaptureTicks : e.ArrivalTicks;
                if (tc <= 0) continue;
                var t = new DateTime(tc);
                if (t < wFrom || t > wTo) continue;
                if (!(rec.Read(e) is LocalPlanMsg m)) continue;
                plany.Add(new PlanZaznam
                {
                    T = t, Ms = m.ComputeMs, Status = m.Status, Cells = m.ExpandedCells, LenM = m.LengthM,
                    CpuStart = m.CpuStart, CpuEnd = m.CpuEnd, Verze = m.Verze,
                    Prichod = e.ArrivalTicks, Start = e.ArrivalTicks > 0 ? e.ArrivalTicks - (long)(m.ComputeMs * 1e4) : 0,
                    Kam = "nesparovano", LatMs = double.NaN, FrontaMs = double.NaN, NecinnostMs = double.NaN,
                });
            }
            if (plany.Count == 0) { Console.WriteLine("  LocalPlanMsg v okne nejsou (robot nemel cil, nebo lokalni vrstva nebezela)."); Console.WriteLine(); return; }

            // Parovani se zpracovanym snimkem a necinnost pred planem.
            // (1) Presna shoda casu porizeni, pokud snimek prisel do startu vypoctu (+10 ms: zaznam snimek
            //     nejdriv kopiruje do poolu a teprve pak razitkuje, takze jeho prichod za startem navigace
            //     zaostava medianem ~0,3 ms, v chvostu az ~9 ms) - 83-95 % planu, parovani se tim samo srovna.
            // (2) Prepsany slot poolu (zprava nese cas pozdejsiho snimku): CaptureFramePool(3) pri 30 sn/s
            //     prepise slot o 3 snimky dal, takze cas ve zprave je o ~100*m ms pozdejsi nez zpracovany
            //     snimek TEZE kamery - hleda se ten.
            // (3) Jinak nejstarsi neprirazeny snimek po poslednim prirazenem. Okno 150 ms: fronta ma 4 zpravy
            //     = jeden takt, starsi snimek uz byl zahozen (DropOldest) nebo zpracovan bez planu.
            plany.Sort((x, y) => x.Start.CompareTo(y.Start));
            var podlePor = new Dictionary<long, List<int>>();
            for (int i = 0; i < snimkyL.Count; i++)
            {
                if (!podlePor.TryGetValue(snimkyL[i].Por, out var l)) podlePor[snimkyL[i].Por] = l = new List<int>();
                l.Add(i);
            }
            var prichody = snimkyL.Select(x => x.Prichod).ToList();
            int PrvniOd(long t) { int i = prichody.BinarySearch(t); return i < 0 ? ~i : i; }
            var prirazeny = new bool[snimkyL.Count];
            int posledni = -1;
            long tolShoda = TimeSpan.FromMilliseconds(10).Ticks, tolPrepis = TimeSpan.FromMilliseconds(2).Ticks,
                 zastaraly = TimeSpan.FromMilliseconds(150).Ticks;
            for (int i = 0; i < plany.Count; i++)
            {
                var pz = plany[i];
                if (pz.Start <= 0) continue;
                if (i > 0 && plany[i - 1].Prichod > 0) pz.NecinnostMs = (pz.Start - plany[i - 1].Prichod) / 1e4;
                bool prepsanyBehem = pz.T.Ticks > pz.Start + tolPrepis;
                int vyber = -1;
                if (!prepsanyBehem && podlePor.TryGetValue(pz.T.Ticks, out var kandidati))
                    foreach (int k in kandidati)
                        if (!prirazeny[k] && snimkyL[k].Prichod <= pz.Start + tolShoda) { vyber = k; break; }
                // (2) jen u zaznamu do verze 4: od verze 5 (13. 10. 2026) navigator kopiruje snimek uz v Post,
                //     zive sloty necte a CaptureFramePool ma 8 slotu, takze podpis „100*m ms" neplati.
                if (vyber < 0 && pz.Verze < 5)
                {
                    string kamT = podlePor.TryGetValue(pz.T.Ticks, out var kt) ? snimkyL[kt[0]].Kam : null;
                    int nejm = int.MaxValue;
                    for (int k = PrvniOd(pz.Start - zastaraly); k < snimkyL.Count && snimkyL[k].Prichod <= pz.Start + tolShoda; k++)
                    {
                        if (prirazeny[k] || (kamT != null && snimkyL[k].Kam != kamT)) continue;
                        double d = (pz.T.Ticks - snimkyL[k].Por) / 1e4;
                        for (int m = 1; m <= 4 && m < nejm; m++)
                            if (Math.Abs(d - 100 * m) <= 12) { nejm = m; vyber = k; }
                    }
                }
                if (vyber < 0)
                    for (int k = Math.Max(posledni + 1, PrvniOd(pz.Start - zastaraly)); k < snimkyL.Count && snimkyL[k].Prichod <= pz.Start + tolShoda; k++)
                        if (!prirazeny[k]) { vyber = k; break; }
                if (vyber >= 0)
                {
                    var sn = snimkyL[vyber];
                    prirazeny[vyber] = true;
                    posledni = Math.Max(posledni, vyber);
                    pz.Kam = sn.Kam;
                    pz.SnimekPrichod = sn.Prichod;
                    pz.SnimekPor = sn.Por;
                    pz.LatMs = (pz.Prichod - sn.Por) / 1e4;
                    pz.FrontaMs = (pz.Start - sn.Prichod) / 1e4;
                }
                // Prepsany cas: zprava nese jiny cas porizeni nez zpracovany snimek. Bez sparovani jen
                // prepis behem vypoctu (cas pozdejsi nez start) - pak je to dolni mez.
                pz.Prepsano = vyber >= 0 ? snimkyL[vyber].Por != pz.T.Ticks : prepsanyBehem;
            }
            plany.Sort((x, y) => x.T.CompareTo(y.T));

            // Rychlost robotu pro deleni jizda / stani (nejblizsi RobotStateMsg do 0,3 s).
            var stavy = new List<(DateTime T, double V)>();
            foreach (var e in rec.Index)
                if (e.MsgName == "RobotStateMsg" && rec.Read(e) is RobotStateMsg s) stavy.Add((s.TimeStamp, s.V));
            stavy.Sort((x, y) => x.T.CompareTo(y.T));
            bool? Jede(DateTime t)
            {
                if (stavy.Count == 0) return null;
                int lo = 0, hi = stavy.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (stavy[mid].T < t) lo = mid + 1; else hi = mid; }
                int best = -1; double bd = 0.3;
                for (int k = Math.Max(0, lo - 1); k <= Math.Min(stavy.Count - 1, lo); k++)
                {
                    double d = Math.Abs((stavy[k].T - t).TotalSeconds);
                    if (d <= bd) { bd = d; best = k; }
                }
                return best < 0 ? (bool?)null : Math.Abs(stavy[best].V) >= 0.2;
            }

            // Delka souvislych useku (mezera nad 1 s = cekani, vypadek kamer se nepocita).
            double trvani = 0;
            for (int i = 1; i < plany.Count; i++)
            {
                double dt = (plany[i].T - plany[i - 1].T).TotalSeconds;
                if (dt > 0 && dt <= 1.0) trvani += dt;
            }
            double zaSek = trvani > 0 ? (plany.Count - 1) / trvani : double.NaN;
            double soucet = plany.Sum(p => p.Ms);
            Console.WriteLine(F("  planu {0}, souvisle useky {1:F0} s; planu (= snimku s cilem) za sekundu {2:F1}, prumerny rozpocet na snimek {3:F0} ms",
                plany.Count, trvani, zaSek, 1000 / zaSek));
            Console.WriteLine(F("  vytizeni jednoho jadra navigaci: {0:F1} % (soucet ComputeMs / delka souvislych useku)",
                trvani > 0 ? 100.0 * soucet / 1000 / trvani : double.NaN));

            var ms = plany.Select(p => p.Ms).OrderBy(x => x).ToList();
            Console.WriteLine("  ComputeMs [ms] vse     " + Rozd4(ms));
            var jizda = plany.Where(p => Jede(p.T) == true).Select(p => p.Ms).OrderBy(x => x).ToList();
            var stani = plany.Where(p => Jede(p.T) == false).Select(p => p.Ms).OrderBy(x => x).ToList();
            if (jizda.Count > 0) Console.WriteLine("  ComputeMs za jizdy     " + Rozd4(jizda) + "   (|v| >= 0,2 m/s)");
            if (stani.Count > 0) Console.WriteLine("  ComputeMs pri stani   " + Rozd4(stani));
            foreach (double prah in new[] { 33.3, 50, 100, 500 })
                Console.WriteLine(F("  planu nad {0,5:F0} ms: {1,6} ({2:F2} %)", prah, ms.Count(x => x > prah), 100.0 * ms.Count(x => x > prah) / ms.Count));
            Console.WriteLine("  (50 ms = pulka taktu: dva snimky davky se do taktu vejdou; 500 ms = PathControlTimeOut, pak smycka brzdi)");

            // Histogram: dvouvrcholove rozdeleni se v kvantilech schova.
            double[] hrany = { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 60, 80, 100, double.PositiveInfinity };
            Console.WriteLine("  histogram ComputeMs [ms]: " + string.Join("  ", Enumerable.Range(0, hrany.Length - 1).Select(i =>
            {
                int n = ms.Count(x => x >= hrany[i] && x < hrany[i + 1]);
                return F("{0}-{1}:{2:F1}%", hrany[i], double.IsInfinity(hrany[i + 1]) ? "" : hrany[i + 1].ToString(Ci), 100.0 * n / ms.Count);
            })));

            // Hlavni deleni: pred planem navigator spal (prvni snimek po probuzeni), nebo pokracoval
            // hned za predchozim. Po kamerach az pod tim - kamera sama o sobe dobu nerozhoduje.
            var navaz = plany.Where(p => p.NecinnostMs <= 2).Select(p => p.Ms).OrderBy(x => x).ToList();
            var probuz = plany.Where(p => p.NecinnostMs > 2).Select(p => p.Ms).OrderBy(x => x).ToList();
            double medNavaz = Q(navaz, 0.5);
            Console.WriteLine("  podle toho, jestli navigator pred planem spal (necinnost > 2 ms = prvni snimek po probuzeni):");
            foreach (var (jm, l) in new[] { ("po probuzeni", probuz), ("navazujici", navaz) })
                if (l.Count > 0)
                    Console.WriteLine(F("    {0,-13} n {1,6}  ComputeMs p50 {2,6:F2}  p90 {3,6:F2}  p99 {4,6:F2}  max {5,6:F2}  nad 25 ms {6,5:F1} %  pomaly mod (> 2,5x p50 navazujicich) {7,5:F1} %",
                        jm, l.Count, Q(l, 0.5), Q(l, 0.9), Q(l, 0.99), l[^1], 100.0 * l.Count(x => x > 25) / l.Count,
                        100.0 * l.Count(x => x > 2.5 * medNavaz) / l.Count));
            Console.WriteLine("  po kamerach (sparovano se zpracovanym snimkem) a necinnosti:");
            string Necinnost(PlanZaznam p) => double.IsNaN(p.NecinnostMs) ? "?" : p.NecinnostMs > 2 ? "po probuzeni" : "navazujici";
            foreach (var g in plany.GroupBy(p => (p.Kam, Druh: Necinnost(p))).OrderBy(g => g.Key.Kam, StringComparer.Ordinal).ThenBy(g => g.Key.Druh, StringComparer.Ordinal))
            {
                var l = g.Select(p => p.Ms).OrderBy(x => x).ToList();
                Console.WriteLine(F("    {0,-12} {1,-13} n {2,6}  ComputeMs p50 {3,6:F2}  p90 {4,6:F2}  p99 {5,6:F2}  max {6,6:F2}  nad 25 ms {7,5:F1} %",
                    g.Key.Kam, g.Key.Druh, l.Count, Q(l, 0.5), Q(l, 0.9), Q(l, 0.99), l[^1],
                    100.0 * l.Count(x => x > 25) / l.Count));
            }
            // Jadro CPU (LocalPlanMsg verze 5, 13. 10. 2026): bezi pomaly prvni snimek po probuzeni na malem
            // jadru (RK3588 0-3 = A55), nebo i na velkem (4-7 = A76, pak spis nabeh frekvence)?
            var sJadrem = plany.Where(p => p.CpuStart >= 0).ToList();
            if (sJadrem.Count == 0)
                Console.WriteLine("  jadro CPU: zaznam ho nenese (LocalPlanMsg verze < 5, binarka pred 13. 10. 2026)");
            else
            {
                string Shluk(int c) => c < 0 || c > 7 ? "?" : c <= 3 ? "male A55 (0-3)" : "velke A76 (4-7)";
                Console.WriteLine(F("  podle jadra na ZACATKU vypoctu (RK3588: 0-3 A55, 4-7 A76; planu s jadrem {0}):", sJadrem.Count));
                foreach (var g in sJadrem.GroupBy(p => (Shluk(p.CpuStart), Druh: Necinnost(p))).OrderBy(g => g.Key.Item1, StringComparer.Ordinal).ThenBy(g => g.Key.Druh, StringComparer.Ordinal))
                {
                    var l = g.Select(p => p.Ms).OrderBy(x => x).ToList();
                    Console.WriteLine(F("    {0,-16} {1,-13} n {2,6} ({3,5:F1} %)  ComputeMs p50 {4,6:F2}  p90 {5,6:F2}  p99 {6,6:F2}  nad 25 ms {7,5:F1} %",
                        g.Key.Item1, g.Key.Druh, l.Count, 100.0 * l.Count / sJadrem.Count, Q(l, 0.5), Q(l, 0.9), Q(l, 0.99),
                        100.0 * l.Count(x => x > 25) / l.Count));
                }
                int prechod = sJadrem.Count(p => p.CpuEnd >= 0 && Shluk(p.CpuStart) != Shluk(p.CpuEnd));
                int jineJadro = sJadrem.Count(p => p.CpuEnd >= 0 && p.CpuStart != p.CpuEnd);
                Console.WriteLine(F("  behem vypoctu vlakno zmenilo jadro u {0:F1} % planu, z toho mezi malymi a velkymi jadry {1:F1} %",
                    100.0 * jineJadro / sJadrem.Count, 100.0 * prechod / sJadrem.Count));
                Console.WriteLine("  po jadrech (na zacatku): " + string.Join("  ", sJadrem.GroupBy(p => p.CpuStart).OrderBy(g => g.Key)
                    .Select(g => F("{0}: {1:F1} %", g.Key, 100.0 * g.Count() / sJadrem.Count))));
            }

            int prepsano = plany.Count(p => p.Prepsano);
            Console.WriteLine(F("  plany s PREPSANYM casem porizeni (zprava nese cas jineho snimku nez zpracovaneho; bez sparovani jen dolni mez): {0} ({1:F2} %)",
                prepsano, 100.0 * prepsano / plany.Count));

            // Davky: snimky predane navigatoru v temz taktu (predani do 30 ms od predchoziho).
            var sparovane = plany.Where(p => p.SnimekPrichod > 0).OrderBy(p => p.SnimekPrichod).ToList();
            var davky = new List<List<PlanZaznam>>();
            foreach (var p in sparovane)
            {
                if (davky.Count > 0 && (p.SnimekPrichod - davky[^1][^1].SnimekPrichod) <= TimeSpan.FromMilliseconds(30).Ticks)
                    davky[^1].Add(p);
                else davky.Add(new List<PlanZaznam> { p });
            }
            if (davky.Count > 0)
            {
                var velikosti = davky.GroupBy(d => d.Count).OrderBy(g => g.Key).Select(g => F("{0} snimky: {1:F1} %", g.Key, 100.0 * g.Count() / davky.Count));
                Console.WriteLine(F("  davky (snimky predane do 30 ms po sobe = jeden takt, pri dohaneni i dva takty tesne za sebou): {0}; {1}", davky.Count, string.Join(", ", velikosti)));
                var poradi = davky.Where(d => d.Count == 2).GroupBy(d => d[0].Kam + " -> " + d[1].Kam)
                                  .OrderByDescending(g => g.Count()).Select(g => F("{0} {1:F1} %", g.Key, 100.0 * g.Count() / davky.Count(d => d.Count == 2)));
                Console.WriteLine("  poradi kamer v davkach se 2 snimky: " + string.Join(", ", poradi));
                var cena = davky.Select(d => d.Sum(p => p.Ms)).OrderBy(x => x).ToList();
                Console.WriteLine("  cena davky (soucet ComputeMs) [ms] " + Rozd4(cena));
                Console.WriteLine(F("  davek nad 100 ms (nevejde se do taktu, dalsi davka zacne pozdeji; zahazuje se, jen kdyz ve fronte pri dalsim taktu jeste neco lezi): {0} ({1:F2} %), nad 50 ms: {2:F1} %",
                    cena.Count(x => x > 100), 100.0 * cena.Count(x => x > 100) / cena.Count, 100.0 * cena.Count(x => x > 50) / cena.Count));
                var fronta = plany.Select(p => p.FrontaMs).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
                if (fronta.Count > 0) Console.WriteLine("  cekani ve fronte navigatoru [ms] " + Rozd4(fronta));
            }

            var lat = plany.Select(p => p.LatMs).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (lat.Count > 0)
            {
                Console.WriteLine("  zpozdeni planu za zpracovanym snimkem [ms] " + Rozd4(lat));
                Console.WriteLine(F("  planu se zpozdenim nad 100 ms: {0} ({1:F2} %), nad 200 ms: {2}",
                    lat.Count(x => x > 100), 100.0 * lat.Count(x => x > 100) / lat.Count, lat.Count(x => x > 200)));
            }

            Console.WriteLine("  podle stavu planu:");
            foreach (var g in plany.GroupBy(p => p.Status).OrderByDescending(g => g.Count()))
            {
                var l = g.Select(p => p.Ms).OrderBy(x => x).ToList();
                var c = g.Select(p => (double)p.Cells).OrderBy(x => x).ToList();
                Console.WriteLine(F("    {0,-18} n {1,6} ({2,5:F1} %)  ComputeMs p50 {3,6:F2}  p99 {4,6:F2}  max {5,7:F2}   expandovanych bunek p50 {6,6:F0}  p99 {7,6:F0}",
                    StavJmeno(g.Key), l.Count, 100.0 * l.Count / plany.Count, Q(l, 0.5), Q(l, 0.99), l[^1], Q(c, 0.5), Q(c, 0.99)));
            }

            // Co dobu ridi: kose podle poctu expandovanych bunek A* (kvantily).
            var podleBunek = plany.OrderBy(p => p.Cells).ToList();
            Console.WriteLine("  podle expandovanych bunek A* (desetiny planu):");
            Console.WriteLine("    bunky od-do          n    ComputeMs p50    p99      max");
            int kos = Math.Max(1, podleBunek.Count / 10);
            for (int i = 0; i < podleBunek.Count; i += kos)
            {
                var l = podleBunek.Skip(i).Take(kos).ToList();
                if (l.Count < Math.Max(5, kos / 2) && i > 0) break;
                var lm = l.Select(p => p.Ms).OrderBy(x => x).ToList();
                Console.WriteLine(F("    {0,6}-{1,-6}  {2,8}   {3,10:F2}  {4,7:F2}  {5,7:F2}", l[0].Cells, l[^1].Cells, l.Count, Q(lm, 0.5), Q(lm, 0.99), lm[^1]));
            }

            Console.WriteLine("  po minutach:");
            Console.WriteLine("    minuta   planu  ComputeMs p50    p99      max   zpozdeni p50    p99      max   jizda %");
            foreach (var g in plany.GroupBy(p => new DateTime(p.T.Year, p.T.Month, p.T.Day, p.T.Hour, p.T.Minute, 0)))
            {
                var l = g.Select(p => p.Ms).OrderBy(x => x).ToList();
                var la = g.Select(p => p.LatMs).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
                int nj = g.Count(p => Jede(p.T) == true);
                Console.WriteLine(F("    {0:HH:mm}   {1,6}  {2,12:F2}  {3,6:F2}  {4,7:F2}  {5,13:F1}  {6,6:F1}  {7,7:F1}   {8,6:F0}",
                    g.Key, l.Count, Q(l, 0.5), Q(l, 0.99), l[^1], Q(la, 0.5), Q(la, 0.99), la.Count > 0 ? la[^1] : double.NaN,
                    100.0 * nj / l.Count));
            }

            Console.WriteLine($"  nejpomalejsi plany ({top}):");
            foreach (var p in plany.OrderByDescending(p => p.Ms).Take(top))
                Console.WriteLine(F("    {0:HH:mm:ss.fff}  ComputeMs {1,7:F2}  zpozdeni {2,7:F1} ms  {3,-16} bunek {4,6}  draha {5,5:F1} m  {6,-5}  {7} {8}",
                    p.T, p.Ms, p.LatMs, StavJmeno(p.Status), p.Cells, p.LenM,
                    Jede(p.T) switch { true => "jede", false => "stoji", _ => "?" }, p.Kam,
                    (double.IsNaN(p.NecinnostMs) ? "" : F("necinnost pred {0:F1} ms", p.NecinnostMs)) + (p.Prepsano ? "  PREPSANY cas" : "")));
            Console.WriteLine("  (ComputeMs = cela prace navigace nad jednim snimkem; zpozdeni = prichod planu do streamu - porizeni snimku,");
            Console.WriteLine("   tedy i zpracovani snimku pred navigaci a fronta. Snimky bez cile tu nejsou - viz stupen LocalNavigator v bloku 4.)");
            Console.WriteLine();
        }

        /// <summary>Jeden plan lokalni navigace ze zaznamu (blok 7).</summary>
        private sealed class PlanZaznam
        {
            public DateTime T;            // cas porizeni ze zpravy planu (muze byt prepsany, viz Prepsano)
            public double Ms, LatMs, LenM;
            public int Status, Cells;
            public string Kam;
            public long Prichod, Start;   // prichod planu do streamu a start vypoctu (= prichod − ComputeMs) [ticks]
            public long SnimekPrichod;    // predani zpracovaneho snimku navigatoru [ticks]
            public long SnimekPor;        // porizeni zpracovaneho snimku [ticks]
            public double FrontaMs;       // cekani ve fronte navigatoru (start − predani snimku)
            public double NecinnostMs;    // jak dlouho navigator pred planem nic nepocital (start − konec predchoziho planu)
            public bool Prepsano;         // zprava nese cas porizeni pozdejsiho snimku (prepsany slot poolu)
            public int CpuStart = -1, CpuEnd = -1;   // jadro CPU na zacatku / konci vypoctu (verze 5)
            public int Verze;                        // verze LocalPlanMsg
        }

        private static string StavJmeno(int s)
            => Enum.IsDefined(typeof(ARBot.Common.Occupancy.LocalPlanStatus), s)
                ? ((ARBot.Common.Occupancy.LocalPlanStatus)s).ToString() : s.ToString(Ci);

        /// <summary>Rozdeleni s p99,9: n, p50, p90, p99, p99,9, max.</summary>
        private static string Rozd4(List<double> serazene)
        {
            if (serazene.Count == 0) return "n=0";
            return F("n={0} p50={1:F2} p90={2:F2} p99={3:F2} p99.9={4:F2} max={5:F2}", serazene.Count,
                Q(serazene, 0.5), Q(serazene, 0.9), Q(serazene, 0.99), Q(serazene, 0.999), serazene[^1]);
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
