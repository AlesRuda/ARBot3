using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Uvaznuti ze zaznamu</b> (prikaz <c>uvazl</c>): prehraje <see cref="StuckMonitor"/> — TYTEZ
    /// tridu, ktera od 8. 10. 2026 hlida stani na robotu (nav-uvaznuti-neohlasene) — nad
    /// <c>RobotStateMsg</c>, <c>DriveCommandMsg</c>, <c>LocalPlanMsg</c>, <c>GlobalNavMsg</c>
    /// a <c>FreeRunMsg</c> ze zaznamu.
    ///
    /// <para><b>Nacpak:</b> prahy 20 s / 60 s jsou z dat (27 zaznamu, autor 8. 10. 2026) a overit,
    /// ze hlidac na nich hlasi to, co ma — a nic navic — jde jen prehranim stejneho kodu. Zaznamy
    /// pred 8. 10. 2026 hlidac nemaji; prehrani rekne, co by hlasil. Ma-li zaznam vlastni
    /// <see cref="StuckMsg"/> (z robotu), vypise se pro srovnani.</para>
    ///
    /// <para>Pricina je u zaznamu s <c>LocalPlanMsg</c> verze &lt; 4 hrubsi: nevi se, cim je bunka
    /// pod robotem blokovana ani jak daleko je mrkev (jen stav planu).</para>
    /// </summary>
    public static class UvazlReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        private static readonly HashSet<string> Typy = new HashSet<string>
        {
            "RobotStateMsg", "DriveCommandMsg", "LocalPlanMsg", "GlobalNavMsg", "FreeRunMsg", "StuckMsg",
        };

        private sealed class Epizoda
        {
            public DateTime Prah;           // kdy hlidac poprve ohlasil (uroven >= 1)
            public DateTime Konec;
            public double MaxStani;
            public StuckLevel MaxUroven;
            public string Prvni = "", Posledni = "", Ukonceni = "(do konce zaznamu)";
            public readonly Dictionary<StuckCause, int> Priciny = new Dictionary<StuckCause, int>();
        }

        public static void Run(RecordFile rec)
        {
            var konf = LogConfig.Read(rec);
            if (konf.Version != null) Console.WriteLine(konf.Version);

            var cfg = new StuckConfig();
            DateTime tNow = default;
            var radky = new List<(DateTime T, string Text)>();
            var mon = new StuckMonitor(cfg, s => radky.Add((tNow, s)));

            var pocty = new Dictionary<string, int>();
            var verzePlanu = new Dictionary<int, int>();
            var vystup = new List<StuckMsg>();
            var zRobotu = new List<StuckMsg>();
            DateTime prvni = default, posledni = default;

            foreach (var e in rec.Index)
            {
                if (!Typy.Contains(e.MsgName)) continue;
                var m = rec.Read(e);
                if (m == null) continue;
                pocty[e.MsgName] = pocty.TryGetValue(e.MsgName, out int c) ? c + 1 : 1;
                switch (m)
                {
                    case StuckMsg z: zRobotu.Add(z); continue;   // zaznamenany vystup robotu - jen pro srovnani
                    case LocalPlanMsg lp: verzePlanu[lp.Verze] = verzePlanu.TryGetValue(lp.Verze, out int v) ? v + 1 : 1; break;
                    case RobotStateMsg s:
                        if (s.TimeStamp > tNow) tNow = s.TimeStamp;
                        if (prvni == default) prvni = s.TimeStamp;
                        posledni = s.TimeStamp;
                        break;
                }
                var o = mon.Process(m);
                if (o != null) vystup.Add(o);
            }

            Console.WriteLine(string.Format(Ci, "prahy: stoji od {0:F0} s, UVAZL od {1:F0} s, pohyb = {2:F1} m, cerstvost {3:F0} s",
                cfg.StandingSec, cfg.StuckSec, cfg.MinMotionM, cfg.FreshSec));
            Console.WriteLine("zprav: " + string.Join(", ", pocty.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            if (verzePlanu.Count > 0)
                Console.WriteLine("LocalPlanMsg verze: " + string.Join(", ", verzePlanu.OrderBy(k => k.Key).Select(k => $"v{k.Key} {k.Value}"))
                                  + (verzePlanu.Keys.Max() < 4 ? "  (pred v4: pricina jen podle stavu planu)" : ""));
            if (prvni != default)
                Console.WriteLine(string.Format(Ci, "takty {0:HH:mm:ss} - {1:HH:mm:ss} ({2:F0} s)", prvni, posledni, (posledni - prvni).TotalSeconds));
            Console.WriteLine();

            // --- epizody ze sledu StuckMsg ---
            var epizody = new List<Epizoda>();
            Epizoda cur = null;
            foreach (var m in vystup)
            {
                var lvl = (StuckLevel)m.Level;
                if (lvl == StuckLevel.None)
                {
                    if (cur != null)
                    {
                        cur.Konec = m.TimeStamp;
                        cur.MaxStani = Math.Max(cur.MaxStani, m.EpisodeSec);
                        cur.Ukonceni = m.Text.StartsWith("stání skončilo: ") ? m.Text.Substring(16) : m.Text;
                        cur = null;
                    }
                    continue;
                }
                if (cur == null)
                {
                    cur = new Epizoda { Prah = m.TimeStamp, Prvni = m.Text };
                    epizody.Add(cur);
                }
                cur.Konec = m.TimeStamp;
                cur.MaxStani = Math.Max(cur.MaxStani, m.EpisodeSec);
                if (lvl > cur.MaxUroven) cur.MaxUroven = lvl;
                cur.Posledni = m.Text;
                var pr = (StuckCause)m.Cause;
                cur.Priciny[pr] = cur.Priciny.TryGetValue(pr, out int k) ? k + 1 : 1;
            }

            Console.WriteLine($"EPIZODY STANI PRI JIZDE (prehrani dnesniho hlidace): {epizody.Count}");
            if (epizody.Count == 0) Console.WriteLine("  zadna - robot pri jizde nestal dele nez prah.");
            foreach (var ep in epizody)
            {
                var zacatek = ep.Prah.AddSeconds(-cfg.StandingSec);
                Console.WriteLine(string.Format(Ci,
                    "  {0:HH:mm:ss.f}  stal {1,6:F1} s  {2,-8}  ukonceni: {3}",
                    zacatek, ep.MaxStani, ep.MaxUroven == StuckLevel.Stuck ? "UVAZL" : "stoji", StuckDetector.Ascii(ep.Ukonceni)));
                Console.WriteLine("      priciny (zprav 1 Hz): " + string.Join(", ",
                    ep.Priciny.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
                Console.WriteLine("      prvni: " + StuckDetector.Ascii(ep.Prvni));
                if (ep.Posledni != ep.Prvni) Console.WriteLine("      posl.: " + StuckDetector.Ascii(ep.Posledni));
            }
            int nUvazl = epizody.Count(e => e.MaxUroven == StuckLevel.Stuck);
            Console.WriteLine();
            Console.WriteLine(string.Format(Ci, "SOUHRN: {0} epizod, z toho UVAZL {1}, jen stoji {2}; celkem stani {3:F0} s",
                epizody.Count, nUvazl, epizody.Count - nUvazl, epizody.Sum(e => e.MaxStani)));

            Console.WriteLine();
            Console.WriteLine($"RADKY DO TRACE ({radky.Count}):");
            foreach (var (t, text) in radky)
                Console.WriteLine(string.Format(Ci, "  {0:HH:mm:ss.f}  {1}", t, text));

            if (zRobotu.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"StuckMsg ZE ZAZNAMU (robot): {zRobotu.Count} zprav, urovne " + string.Join(", ",
                    zRobotu.GroupBy(z => z.Level).OrderBy(g => g.Key).Select(g => $"{(StuckLevel)g.Key} {g.Count()}")));
            }
        }
    }
}
