using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// Rozbor <b>hranove lokalizace</b> ze zaznamu: proc cykly propadly, jak presna jsou prijata
    /// merenia a — hlavne — jak to vypada <b>v zavislosti na parovacim rozestupu snimku</b>.
    ///
    /// <para><b>Proc rozpad podle rozestupu.</b> Sirkovy nesouhlas vyskocil z 0,046 na 0,230 m
    /// a vypadalo to jako cena rozsireni parovaciho okna z 60 na 400 ms. Rozpad to vyvratil:
    /// <b>velky rozestup je lepsi</b>, ne horsi, a zlom je ostry u 120 ms — podpis zavadejici
    /// promenne. Tou promennou je misto na trase (faze kamer se prubehem behu posouva, takze
    /// pasma rozestupu jsou ve skutecnosti pasma casu). Skutecna pricina byla zaostavani filtru
    /// sirky na ceste, ktera se rozsiruje — proto tu je i rozpad podle <b>OSM cesty</b>, ktery to
    /// ukazal. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para><b>Rozestup v zaznamu neni</b> (<see cref="RoadCorridorMsg"/> ho nenese), ale jde
    /// spolehlive zrekonstruovat z indexu: <see cref="CorridorLocalizer"/> si drzi posledni snimek
    /// od kazde kamery a paruje jen <b>dozadu</b>, a pro kazdy zpracovany snimek vznikne prave
    /// jedna <see cref="RoadCorridorMsg"/>. Mnozina jejich casu je tedy presne mnozina snimku,
    /// ktere do stupne dosly (frontou DropOldest se cast snimku zahodi) — a nad ni se parovani
    /// prehraje presne.</para>
    /// </summary>
    public static class CorridorReport
    {
        public static void Run(RecordFile rec, double oldWindowMs)
        {
            var msgs = new List<RoadCorridorMsg>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "RoadCorridorMsg") continue;
                if (rec.Read(e) is RoadCorridorMsg m) msgs.Add(m);
            }

            Console.WriteLine($"RoadCorridorMsg: {msgs.Count} zprav");
            if (msgs.Count == 0) return;
            Console.WriteLine($"verze zpravy:    {string.Join(", ", msgs.Select(m => m.Verze).Distinct().OrderBy(x => x))}");
            Console.WriteLine($"delka useku:     {(msgs[msgs.Count - 1].TimeStamp - msgs[0].TimeStamp).TotalSeconds:F1} s");
            Console.WriteLine();

            Console.WriteLine("Duvod (FixReason):");
            foreach (var g in msgs.GroupBy(m => (CorridorFixReason)m.FixReason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-22} {g.Count(),5}  ({100.0 * g.Count() / msgs.Count,4:F1} %)");

            Console.WriteLine("Duvod koridoru (CorridorReason) tam, kde koridor nevznikl:");
            foreach (var g in msgs.Where(m => m.FixReason == (byte)CorridorFixReason.NoCorridor)
                                  .GroupBy(m => (CorridorReason)m.CorridorReason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"  {g.Key,-22} {g.Count(),5}");
            Console.WriteLine();

            var skew = PairingSkew(rec, msgs);
            var ok = msgs.Where(m => m.FixReason == (byte)CorridorFixReason.Ok).ToList();
            Console.WriteLine($"Prijatych merenii (Ok): {ok.Count}");
            Console.WriteLine($"Rozestup zrekonstruovan u {ok.Count(m => skew.ContainsKey(m.TimeStamp))} z nich");
            Console.WriteLine();

            Report("VSECHNA prijata merenia", ok);

            var narrow = ok.Where(m => skew.TryGetValue(m.TimeStamp, out double s) && s <= oldWindowMs).ToList();
            var wide = ok.Where(m => skew.TryGetValue(m.TimeStamp, out double s) && s > oldWindowMs).ToList();
            Report($"jen rozestup do {oldWindowMs:F0} ms (co by proslo starym oknem)", narrow);
            Report($"jen rozestup nad {oldWindowMs:F0} ms (co pribylo rozsirenim)", wide);

            Console.WriteLine("Nesouhlas podle parovaciho rozestupu (hleda se TREND, ne jedno cislo):");
            Console.WriteLine("  pasmo [ms]      n   abs sirka p50   sirka p50   abs pricne p50   nerovnobez. p50 [deg]");
            double[] edges = { 0, 20, 60, 120, 200, 300, 400, double.MaxValue };
            for (int i = 0; i + 1 < edges.Length; i++)
            {
                double a = edges[i], b = edges[i + 1];
                var bin = ok.Where(m => skew.TryGetValue(m.TimeStamp, out double s) && s >= a && s < b).ToList();
                if (bin.Count == 0) continue;
                var aw = new Stats(""); var sw = new Stats(""); var al = new Stats(""); var pe = new Stats("");
                foreach (var m in bin)
                {
                    aw.Add(Math.Abs(m.WidthDisagreement)); sw.Add(m.WidthDisagreement);
                    al.Add(Math.Abs(m.LateralDisagreement)); pe.Add(Math.Abs(m.ParallelErrorRad) * 180 / Math.PI);
                }
                string label = b == double.MaxValue ? $"nad {a:F0}" : $"{a:F0}-{b:F0}";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,4}       {2,8:F3}    {3,8:F3}         {4,8:F3}            {5,8:F2}",
                    label, bin.Count, aw.Median, sw.Median, al.Median, pe.Median));
            }
            Console.WriteLine();

            GeometryCheck(ok);
            ByPose(rec, ok, msgs);
        }

        /// <summary>
        /// Rozpad prijatych merenii podle <b>rychlosti robotu</b> a podle <b>casu v behu</b>
        /// (= misto na trase) — a chyba lokalizace proti ground truth.
        ///
        /// <para><b>Proc to tu je.</b> Nesouhlas sirky vypadal, jako by zavisel na parovacim
        /// rozestupu, ale zlom byl ostry (do 120 ms spatne, nad 120 ms dobre) — to je podpis
        /// <b>zavadejici promenne</b>, ne plynule ceny okna. Rychlost a misto na trase jsou
        /// prvni dva kandidati: <c>SyntetickyKoridor.osm</c> ma krizovatku i slepy konec, kde
        /// koridor existovat nema, a robot v cili stoji.</para>
        /// </summary>
        private static void ByPose(RecordFile rec, List<RoadCorridorMsg> ok, List<RoadCorridorMsg> all)
        {
            var poses = new PoseTrack(rec);
            var t0 = all[0].TimeStamp;

            // KDE robot vlastne byl. Bez toho se "koridor prestal vznikat" pletlo s "dojel na konec
            // cesty" — nad rovnou mapou dlouhou 80 m to pri 1,2 m/s nemuze byt totez, a rozdil je
            // videt jen z trajektorie. Kresli se z tiku RobotStateMsg v casech cyklu koridoru.
            {
                double minX = double.MaxValue, maxX = double.MinValue;
                double minY = double.MaxValue, maxY = double.MinValue;
                double? px = null, py = null; double path = 0;
                foreach (var m in all)
                {
                    var p = poses.Nearest(m.TimeStamp);
                    if (p == null) continue;
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    if (px.HasValue)
                        path += Math.Sqrt((p.X - px.Value) * (p.X - px.Value) + (p.Y - py.Value) * (p.Y - py.Value));
                    px = p.X; py = p.Y;
                }
                if (minX <= maxX)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "Trajektorie (lokalni ENU): X {0:F1}..{1:F1} m, Y {2:F1}..{3:F1} m, ujeto {4:F1} m",
                        minX, maxX, minY, maxY, path));
                    Console.WriteLine();
                }
            }

            Console.WriteLine("Prijata merenia podle RYCHLOSTI robotu:");
            Console.WriteLine("  rychlost [m/s]    n   abs sirka p50   abs pricne p50   nerovnobez. p50 [deg]");
            double[] vEdges = { -0.01, 0.05, 0.3, 0.8, 1.1, double.MaxValue };
            foreach (var b in Bins(vEdges))
            {
                var bin = ok.Where(m => { var p = poses.Nearest(m.TimeStamp);
                                          return p != null && p.V >= b.a && p.V < b.b; }).ToList();
                if (bin.Count == 0) continue;
                PrintBin(b.b == double.MaxValue ? $"nad {b.a:F2}" : $"{b.a:F2}-{b.b:F2}", bin);
            }
            Console.WriteLine();

            // Rozsah pasem se bere z DELKY ZAZNAMU, ne natvrdo. Bylo tu 40 s (delka tehdejsich
            // behu), takze nad 70s jizdou po OSM/SyntetickyRovny.osm se poslednich 30 s vubec
            // netisklo - a prave tam koridor propadal. Pasmo se drzi na ~8 radcich, at je to
            // citelne i u dlouheho behu.
            double runSeconds = Math.Ceiling((all[all.Count - 1].TimeStamp - t0).TotalSeconds);
            double binSeconds = Math.Max(5, Math.Ceiling(runSeconds / 8 / 5) * 5);

            Console.WriteLine($"Prijata merenia podle CASU v behu (= misto na trase, {binSeconds:F0}s pasma):");
            Console.WriteLine("  cas [s]           n   abs sirka p50   abs pricne p50   nerovnobez. p50 [deg]");
            for (double s = 0; s < runSeconds; s += binSeconds)
            {
                double a = s, b = s + binSeconds;
                var bin = ok.Where(m => { double dt = (m.TimeStamp - t0).TotalSeconds;
                                          return dt >= a && dt < b; }).ToList();
                if (bin.Count == 0) continue;
                PrintBin($"{a:F0}-{b:F0}", bin);
            }
            Console.WriteLine();

            Console.WriteLine("Podil zamitnutych (NotParallel) po casu — kde koridor vubec nevznika:");
            Console.WriteLine("  cas [s]        cyklu    Ok   NotParallel");
            for (double s = 0; s < runSeconds; s += binSeconds)
            {
                double a = s, b = s + binSeconds;
                var bin = all.Where(m => { double dt = (m.TimeStamp - t0).TotalSeconds;
                                           return dt >= a && dt < b; }).ToList();
                if (bin.Count == 0) continue;
                int okN = bin.Count(m => m.FixReason == (byte)CorridorFixReason.Ok);
                int np = bin.Count(m => m.FixReason == (byte)CorridorFixReason.NoCorridor
                                     && m.CorridorReason == (byte)CorridorReason.NotParallel);
                Console.WriteLine($"  {a,2:F0}-{b,-2:F0}          {bin.Count,5} {okN,5}         {np,5}");
            }
            Console.WriteLine();

            // Rozpad po OSM cestach. SyntetickyKoridor.osm ma cesty sirky 1, 2 a 3 m, takze
            // "nesouhlas sirky" muze byt cely o tom, ze poza uz je na jine ceste, nez na kterou
            // se kamery koukaji — a to bez jakekoli chyby polohy (pricne to sedi).
            Console.WriteLine("Prijata merenia podle OSM cesty (way):");
            Console.WriteLine("  wayId              n   sirka p50   mapa p50   abs sirka p50   abs pricne p50");
            foreach (var g in ok.GroupBy(m => m.WayId).OrderByDescending(g => g.Count()))
            {
                var w = new Stats(""); var mw = new Stats(""); var aw = new Stats(""); var al = new Stats("");
                foreach (var m in g)
                {
                    w.Add(m.Width); mw.Add(m.MapWidth);
                    aw.Add(Math.Abs(m.WidthDisagreement)); al.Add(Math.Abs(m.LateralDisagreement));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,6}    {2,8:F3}   {3,8:F3}        {4,8:F3}        {5,8:F3}",
                    g.Key, g.Count(), w.Median, mw.Median, aw.Median, al.Median));
            }
            Console.WriteLine();

            if (!poses.HasTruth)
            {
                Console.WriteLine("Ground truth v zaznamu neni — chybu lokalizace nelze spocitat.");
                return;
            }
            var ep = new Stats("chyba polohy [m]");
            var eh = new Stats("chyba kurzu [deg]");
            foreach (var m in all)
            {
                if (!poses.TryError(m.TimeStamp, out double dx, out double dy, out double dth)) continue;
                ep.Add(Math.Sqrt(dx * dx + dy * dy));
                eh.Add(Math.Abs(dth) * 180 / Math.PI);
            }
            Console.WriteLine("Chyba lokalizace (ground truth minus odhad) v casech cyklu koridoru:");
            Console.WriteLine("  " + ep.Line());
            Console.WriteLine("  " + eh.Line());
            Console.WriteLine();
        }

        private static IEnumerable<(double a, double b)> Bins(double[] edges)
        {
            for (int i = 0; i + 1 < edges.Length; i++) yield return (edges[i], edges[i + 1]);
        }

        private static void PrintBin(string label, List<RoadCorridorMsg> bin)
        {
            var aw = new Stats(""); var al = new Stats(""); var pe = new Stats("");
            foreach (var m in bin)
            {
                aw.Add(Math.Abs(m.WidthDisagreement));
                al.Add(Math.Abs(m.LateralDisagreement));
                pe.Add(Math.Abs(m.ParallelErrorRad) * 180 / Math.PI);
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-14} {1,4}       {2,8:F3}         {3,8:F3}            {4,8:F2}",
                label, bin.Count, aw.Median, al.Median, pe.Median));
        }

        /// <summary>
        /// CSV radek za kazdy cyklus koridoru — pro pripady, kdy percentily nestaci a je potreba
        /// videt <b>casovy prubeh</b> (napr. jestli filtr sirky konverguje, nebo stoji).
        /// </summary>
        public static void Dump(RecordFile rec)
        {
            var msgs = new List<RoadCorridorMsg>();
            foreach (var e in rec.Index)
                if (e.MsgName == "RoadCorridorMsg" && rec.Read(e) is RoadCorridorMsg m) msgs.Add(m);
            if (msgs.Count == 0) return;

            var poses = new PoseTrack(rec);
            var t0 = msgs[0].TimeStamp;
            Console.WriteLine("t;fix;corr;way;width;mapWidth;filtered;dWidth;dLat;parErr_deg;v;inlL;inlR;resL;resR;"
                              + "dirL_deg;dirR_deg;farL;farR");
            foreach (var m in msgs)
            {
                var p = poses.Nearest(m.TimeStamp);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F3};{1};{2};{3};{4:F3};{5:F3};{6:F3};{7:F3};{8:F3};{9:F2};{10:F2};{11};{12};{13:F3};{14:F3};"
                    + "{15:F2};{16:F2};{17:F2};{18:F2}",
                    (m.TimeStamp - t0).TotalSeconds, (CorridorFixReason)m.FixReason,
                    (CorridorReason)m.CorridorReason, m.WayId, m.Width, m.MapWidth, m.FilteredWidth,
                    m.WidthDisagreement, m.LateralDisagreement, m.ParallelErrorRad * 180 / Math.PI,
                    p?.V ?? double.NaN, m.InliersLeft, m.InliersRight, m.ResidualLeft, m.ResidualRight,
                    m.DirectionLeftRad * 180 / Math.PI, m.DirectionRightRad * 180 / Math.PI,
                    Reach(m.HasLeftLine, m.LeftFromX, m.LeftFromY, m.LeftToX, m.LeftToY),
                    Reach(m.HasRightLine, m.RightFromX, m.RightFromY, m.RightToX, m.RightToY)));
            }
        }

        /// <summary>
        /// Jak daleko od robotu dosahuje vzdalenejsi konec prolozene usecky [m]. <b>Klicovy udaj
        /// pri hledani spatneho prolozeni:</b> odchylka hranicnich bodu od okraje vozovky ma
        /// nulovy median v kazde vzdalenosti, ale rozptyl roste (±0,05 m na 1 m, −0,63/+0,40 m na
        /// 10 m; naměřeno 22. 8. 2026), takze usecka tahnouci se do 8-10 m je skoro urcite
        /// nahodne zarovnani rozstriknutych vzdalenych bodu.
        /// </summary>
        private static double Reach(bool has, double ax, double ay, double bx, double by)
        {
            if (!has) return double.NaN;
            return Math.Max(Math.Sqrt(ax * ax + ay * ay), Math.Sqrt(bx * bx + by * by));
        }

        private static void Report(string title, List<RoadCorridorMsg> ok)
        {
            Console.WriteLine($"--- {title} (n={ok.Count}) ---");
            if (ok.Count == 0) { Console.WriteLine(); return; }

            var stats = new[]
            {
                new Stats("sirka merena [m]"),
                new Stats("sirka z mapy/filtru [m]"),
                new Stats("sirkovy nesouhlas [m]"),
                new Stats("abs sirkovy nesouhlas [m]"),
                new Stats("pricny nesouhlas [m]"),
                new Stats("abs pricny nesouhlas [m]"),
                new Stats("nerovnobeznost [deg]"),
                new Stats("abs nesouhlas kurzu [deg]"),
                new Stats("rezidua (L+R)/2 [m]"),
                new Stats("inliery L"),
                new Stats("inliery R"),
                new Stats("sigma pricne [m]"),
            };
            foreach (var m in ok)
            {
                stats[0].Add(m.Width);
                stats[1].Add(m.MapWidth);
                stats[2].Add(m.WidthDisagreement);
                stats[3].Add(Math.Abs(m.WidthDisagreement));
                stats[4].Add(m.LateralDisagreement);
                stats[5].Add(Math.Abs(m.LateralDisagreement));
                stats[6].Add(Math.Abs(m.ParallelErrorRad) * 180 / Math.PI);
                stats[7].Add(Math.Abs(m.HeadingDisagreementRad) * 180 / Math.PI);
                stats[8].Add(0.5 * (m.ResidualLeft + m.ResidualRight));
                stats[9].Add(m.InliersLeft);
                stats[10].Add(m.InliersRight);
                stats[11].Add(m.SigmaLateral);
            }
            foreach (var s in stats) Console.WriteLine("  " + s.Line());
            Console.WriteLine();
        }

        /// <summary>
        /// Kontrola, jestli hlasena sirka odpovida <b>zaznamenanym useckam</b> prolozeni: sirka ma
        /// byt odstup obou primek. Kdyby se lisily, chyba je ve vypoctu, ne v datech. Zaroven se
        /// meri <b>podelny prekryv</b> usecek — kdyz hranice nepokryvaji tentyz usek cesty, je
        /// sirka dopoctena extrapolaci a chyba smeru se do ni prenasi nasobene.
        /// </summary>
        private static void GeometryCheck(List<RoadCorridorMsg> ok)
        {
            var diff = new Stats("sirka hlasena - z usecek [m]");
            var overlap = new Stats("podelny prekryv usecek [m]");
            var lenL = new Stats("delka usecky L [m]");
            var lenR = new Stats("delka usecky R [m]");
            int both = 0;
            foreach (var m in ok)
            {
                if (!m.HasLeftLine || !m.HasRightLine) continue;
                both++;

                double lmx = 0.5 * (m.LeftFromX + m.LeftToX), lmy = 0.5 * (m.LeftFromY + m.LeftToY);
                double rmx = 0.5 * (m.RightFromX + m.RightToX), rmy = 0.5 * (m.RightFromY + m.RightToY);
                double dir = m.DirectionRad;
                double nx = -Math.Sin(dir), ny = Math.Cos(dir);
                double w = (lmx - rmx) * nx + (lmy - rmy) * ny;
                diff.Add(m.Width - Math.Abs(w));

                double c = Math.Cos(dir), s = Math.Sin(dir);
                double l1 = m.LeftFromX * c + m.LeftFromY * s, l2 = m.LeftToX * c + m.LeftToY * s;
                double r1 = m.RightFromX * c + m.RightFromY * s, r2 = m.RightToX * c + m.RightToY * s;
                overlap.Add(Math.Min(Math.Max(l1, l2), Math.Max(r1, r2)) - Math.Max(Math.Min(l1, l2), Math.Min(r1, r2)));
                lenL.Add(Math.Abs(l2 - l1));
                lenR.Add(Math.Abs(r2 - r1));
            }
            Console.WriteLine($"Geometrie prolozeni (usecky jsou u {both} z {ok.Count} prijatych):");
            Console.WriteLine("  " + diff.Line());
            Console.WriteLine("  " + overlap.Line());
            Console.WriteLine("  " + lenL.Line());
            Console.WriteLine("  " + lenR.Line());
            Console.WriteLine();
        }

        /// <summary>
        /// Prehraje parovani snimku a vrati pro kazdy zpracovany snimek jeho rozestup [ms]
        /// k partnerovi. Klic je cas snimku = <see cref="RoadCorridorMsg.TimeStamp"/>.
        /// </summary>
        private static Dictionary<DateTime, double> PairingSkew(RecordFile rec, List<RoadCorridorMsg> msgs)
        {
            var processed = new HashSet<DateTime>(msgs.Select(m => m.TimeStamp));
            var result = new Dictionary<DateTime, double>();
            var last = new Dictionary<string, DateTime>();

            foreach (var e in rec.Index)
            {
                if (e.MsgName != "CameraFrame") continue;
                var t = e.CaptureTime;
                if (!processed.Contains(t)) continue;

                string cam = e.Name ?? string.Empty;
                last[cam] = t;

                double best = double.MaxValue;
                foreach (var kv in last)
                {
                    if (kv.Key == cam) continue;
                    double dt = Math.Abs((kv.Value - t).TotalMilliseconds);
                    if (dt < best) best = dt;
                }
                if (best < double.MaxValue) result[t] = best;
            }
            return result;
        }
    }
}
