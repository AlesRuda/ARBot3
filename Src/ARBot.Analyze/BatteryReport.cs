using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Napeti baterie ze zaznamu</b> (prikaz <c>battery</c>): prehraje <see cref="BatteryMonitor"/>
    /// - TYTEZ tridu, ktera od 6. 10. 2026 hlida napeti na robotu (<c>batwarn=</c>,
    /// prov-baterie-na-strance) - nad <see cref="MotorStateBase.Voltage"/> ze zaznamu.
    ///
    /// <para><b>Proc prehrani a ne prumer.</b> Jednotlive vzorky z motorove jednotky jsou hlucne
    /// (5-17 V), takze o vybiti rika jen median za okno - a jestli by varovani na strance blikalo,
    /// nebo se ozvalo vcas, ukaze jen ten kod, ktery na robotu pobezi (median 5 s, hystereze 0,2 V).
    /// Zaznamy pred 6. 10. 2026 monitor nemaji; prehranim se zjisti, co by hlasil.</para>
    ///
    /// <para>Tiskne mediany po minutach, prechody varovani a napeti pri jizde proti stani a podle
    /// proudu motoru (pokles pod zatezi je vlastnost baterie a kabelu, ne vybiti). Cte jen
    /// <see cref="MotorStateBase"/>, zadne snimky.</para>
    /// </summary>
    public static class BatteryReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <param name="batWarnArg">Prah varovani [V]; NaN = <c>batwarn=</c> ze zaznamu, jinak default z kodu.</param>
        public static void Run(RecordFile rec, double batWarnArg)
        {
            var konf = LogConfig.Read(rec);
            // Default je vychozi hodnota parametru v DNESNIM kodu (ParamRegistry), protoze zaznamy
            // pred 6. 10. 2026 batwarn= nemaji - a otazka je prave, co by s nimi udelal dnesni robot.
            double batWarn = konf.Resolve("batwarn", batWarnArg, ParamRegistry.BatWarn.Value, out string puvod);
            if (konf.Version != null) Console.WriteLine(konf.Version);
            Console.WriteLine(string.Format(Ci, "batwarn = {0:F2} V {1}; okno medianu {2:F0} s, nejmene {3} vzorku, hystereze 0,2 V",
                batWarn, puvod, BatteryMonitor.DefaultWindowSec, BatteryMonitor.MinSamples));
            Console.WriteLine();

            var motory = rec.ReadAll<MotorStateBase>("MotorStateBase").ToList();
            int sMerenim = motory.Count(m => m.HasMeasurement);
            Console.WriteLine($"MotorStateBase: {motory.Count}, s merenim {sMerenim} (bez mereni = zastupny ramec driveru, zahazuje se)");
            if (sMerenim == 0) { Console.WriteLine("Neni co merit."); return; }

            var prechody = new List<(DateTime t, string s)>();
            DateTime tAkt = default;
            var mon = new BatteryMonitor(batWarn, report: s => prechody.Add((tAkt, s)));

            var syrove = new List<double>();
            var poMin = new SortedDictionary<DateTime, List<double>>();
            var konecMinuty = new SortedDictionary<DateTime, BatteryReading>();
            var jizda = new List<double>();
            var stani = new List<double>();
            var podleProudu = new SortedDictionary<int, List<double>>();
            (double v, DateTime t) nejnizsi = (double.MaxValue, default);
            DateTime posledniOdecet = DateTime.MinValue;

            foreach (var m in motory)
            {
                if (!m.HasMeasurement) continue;
                tAkt = m.TimeStamp;
                mon.Add(m);
                if (double.IsFinite(m.Voltage) && m.Voltage > 0) syrove.Add(m.Voltage);
                var min = new DateTime(tAkt.Year, tAkt.Month, tAkt.Day, tAkt.Hour, tAkt.Minute, 0);
                if (!poMin.TryGetValue(min, out var l)) poMin[min] = l = new List<double>();
                if (m.Voltage > 0) l.Add(m.Voltage);
                var r = mon.Read(tAkt);
                konecMinuty[min] = r;

                // Jednou za sekundu odecet medianu - jako stranka nahledu - a k nemu, jestli robot
                // jel (|v| > 0,3 m/s z kol) nebo stal (< 0,05), a proud motoru.
                if ((tAkt - posledniOdecet).TotalSeconds >= 1.0)
                {
                    posledniOdecet = tAkt;
                    if (!double.IsNaN(r.Volts))
                    {
                        double v = 0.5 * Math.Abs(m.LeftWheelSpeed + m.RightWheelSpeed);
                        double proud = Math.Abs(m.LeftMotorCurrent) + Math.Abs(m.RightMotorCurrent);
                        if (v > 0.3) jizda.Add(r.Volts);
                        else if (v < 0.05) stani.Add(r.Volts);
                        int kos = proud < 2 ? 0 : proud < 5 ? 1 : proud < 10 ? 2 : 3;
                        if (!podleProudu.TryGetValue(kos, out var pl)) podleProudu[kos] = pl = new List<double>();
                        pl.Add(r.Volts);
                        if (r.Volts < nejnizsi.v) nejnizsi = (r.Volts, tAkt);
                    }
                }
            }

            Console.WriteLine("  syrove vzorky [V]              " + Rozd(syrove));
            Console.WriteLine("  median 5 s pri JIZDE (>0,3 m/s) " + Rozd(jizda));
            Console.WriteLine("  median 5 s pri STANI (<0,05)    " + Rozd(stani));
            if (jizda.Count > 0 && stani.Count > 0)
                Console.WriteLine(string.Format(Ci, "  pokles pod zatezi (p50 stani - p50 jizda): {0:F2} V", Med(stani) - Med(jizda)));
            string[] kose = { "proud < 2 A", "2-5 A", "5-10 A", ">= 10 A" };
            foreach (var kv in podleProudu)
                Console.WriteLine($"  median 5 s, {kose[kv.Key],-12}       " + Rozd(kv.Value));
            if (nejnizsi.t != default)
                Console.WriteLine(string.Format(Ci, "  nejnizsi median 5 s: {0:F2} V v {1:HH:mm:ss}", nejnizsi.v, nejnizsi.t));
            Console.WriteLine();

            Console.WriteLine($"PRECHODY VAROVANI (BatteryMonitor, batwarn={batWarn.ToString("F2", Ci)}): {prechody.Count}");
            foreach (var p in prechody) Console.WriteLine(string.Format(Ci, "  {0:HH:mm:ss.fff}  {1}", p.t, p.s));
            if (prechody.Count == 0)
                Console.WriteLine(batWarn > 0 ? "  zadny - median 5 s nespadl pod prah." : "  varovani vypnute (batwarn <= 0).");
            Console.WriteLine();

            Console.WriteLine("PO MINUTACH (syrove vzorky: median / p05 / p95, monitor na konci minuty: median 5 s a stav)");
            foreach (var kv in poMin)
            {
                if (kv.Value.Count == 0) continue;
                var s = kv.Value.OrderBy(x => x).ToList();
                var r = konecMinuty[kv.Key];
                Console.WriteLine(string.Format(Ci, "  {0:HH:mm}  {1,6:F2}  {2,6:F2}  {3,6:F2}  n={4,5}   monitor {5,6} {6}",
                    kv.Key, Q(s, 0.5), Q(s, 0.05), Q(s, 0.95), s.Count,
                    double.IsNaN(r.Volts) ? "-" : r.Volts.ToString("F2", Ci), r.Level));
            }
            Console.WriteLine();
        }

        private static double Med(List<double> l) => Q(l.OrderBy(x => x).ToList(), 0.5);

        private static double Q(List<double> s, double q)
            => s.Count == 0 ? double.NaN : s[Math.Min(s.Count - 1, Math.Max(0, (int)Math.Round(q * (s.Count - 1))))];

        private static string Rozd(List<double> l)
        {
            if (l.Count == 0) return "n=0";
            var s = l.OrderBy(x => x).ToList();
            return string.Format(Ci, "n={0,6} p50={1:F2} p10={2:F2} p90={3:F2} min={4:F2} max={5:F2}",
                s.Count, Q(s, 0.5), Q(s, 0.1), Q(s, 0.9), s[0], s[^1]);
        }
    }
}
