using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Localization;
using ARBot.Common.Vision;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Dopad rozliseni pravdepodobnostniho obrazu</b> (sit 128×128 proti plnemu snimku 640×480)
    /// na hranice cesty, koridor a occupancy grid — nad zaznamem, bez robota.
    ///
    /// <para><b>Nacpak</b> (registr <c>vid-segmentace-rozliseni-128</c>): sit pocita sjizdnost ve
    /// 128×128, histogram barev nad plnym snimkem. Hranice cesty (<c>FindPathEdge</c>) davaji
    /// <b>jednu dvojici hran na radek</b>, takze pri 128 radcich misto 480 je bodu 3,75× mene —
    /// a koridor mel do 29. 9. 2026 pevnou branu v poctu bodu (<c>corridormininliers</c>, od 30. 9. <c>corridorinliers=</c> v % radku). Grid vzorkuje jeden
    /// pixel na bunku 5 cm, tam rozliseni meni jen to, kolik bunek sdili jeden pixel.</para>
    ///
    /// <para><b>Jak se meri.</b> Aby se nemerila zamena sit/histogram, porovnava se <b>tyz
    /// histogram</b> (<see cref="BackProject"/>, pevna tabulka, na velikosti nezavisi) na plnem
    /// snimku a na snimku zmensenem nejblizsim sousedem na <c>size×size</c> — presne tak, jak
    /// <c>CameraFrameProcessor</c> zmensuje vstup site. Treti sloupec je sit ze zaznamu (jeji
    /// ulozeny pravdepodobnostni obraz). Vsechny tri projdou toutez cestou: <c>FindPathEdge</c>
    /// → <see cref="ColorPixelTo3D"/> → parovani kamer jako <c>CorridorLocalizer</c> →
    /// <see cref="CorridorFinder"/>.</para>
    ///
    /// <para><b>Kontrola meridla:</b> hrany prepoctene z ulozeneho obrazu site se porovnaji
    /// s hranami, ktere zapsal robot — musi sedet, jinak report meri neco jineho nez runtime.</para>
    /// </summary>
    public static class ProbResolutionReport
    {
        private const int VarFull = 0, VarSmall = 1, VarRec = 2;

        private sealed class Snimek
        {
            public string Cam;
            public DateTime T;
            public readonly List<Point2D>[] L = new List<Point2D>[3];
            public readonly List<Point2D>[] R = new List<Point2D>[3];
        }

        public static void Run(RecordFile rec, int limit, int skip, int size, int minInliers, double percent)
        {
            if (!NativeLibAvailability.JeDostupna)
            {
                Console.WriteLine("NativeLib neni k dispozici (FindPathEdge) - build pod x64 s NativeLib.dll.");
                return;
            }

            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").Skip(Math.Max(0, skip)).ToList();
            if (limit > 0 && limit < entries.Count) entries = entries.Take(limit).ToList();
            Console.WriteLine($"CameraFrame: {entries.Count} (cte cele snimky, chvili to trva)");

            var cu = new NativeComputeUnit(1, 1, 1, 0, 0, 0.1f, null);
            var hist = new BackProject(BackProject.RoadProbability);
            var projectors = new Dictionary<CameraProjectionInfo, ColorPixelTo3D>();
            string[] nazvy = { $"hist {0}", $"hist {size}x{size}", "sit (zaznam)" };

            var snimky = new List<Snimek>();
            var kontrolaPocet = new Stats("rozdil poctu bodu");
            var kontrolaPoloha = new Stats("rozdil polohy bodu [m]");
            int bezKontroly = 0;
            string recRozmer = null, fullRozmer = null;
            int fullRows = 480, recRows = 0;

            // Stopa radku po zemi: za kazdou kameru a kazdy radek barvy medián vzdalenosti
            // (pres stredni sloupce snimku, pak pres snimky). Rozdil hloubky DVOU SOUSEDNICH radku
            // jednoho snimku nejde - ve 3-4 m je sum hloubky vetsi nez stopa radku samotna
            // (prvni verze tak dala plnemu snimku 0,09 m na radek misto ~0,03).
            var dalka = new Dictionary<string, List<float>[]>();
            int rgbVyska = 0;
            double fxBarvy = double.NaN;

            Image<Gray> probFull = null, probSmall = null;
            Image<BGR32> rgbSmall = null;

            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f)) continue;
                var rgb = f.ImageRGB;
                var info = f.Projection;
                if (rgb == null || f.ImageDepth == null || info?.ColorIntrinsics == null) continue;

                if (!projectors.TryGetValue(info, out var proj))
                {
                    proj = new ColorPixelTo3D(info.ColorIntrinsics, info.Intrinsics, info.CreateProjection(),
                                              info.ColorToDepth, info.DepthToColor);
                    projectors[info] = proj;
                    fxBarvy = info.ColorIntrinsics.Fx * (double)rgb.Width / info.ColorIntrinsics.Width;
                }

                var s = new Snimek { Cam = f.Name ?? string.Empty, T = f.TimeStamp };

                // (A) histogram na plnem snimku
                if (probFull == null || probFull.Width != rgb.Width || probFull.Height != rgb.Height)
                    probFull = new Image<Gray>(rgb.Width, rgb.Height);
                hist.Process(rgb, probFull);
                fullRozmer = $"{probFull.Width}x{probFull.Height}";
                fullRows = probFull.Height;
                (s.L[VarFull], s.R[VarFull]) = Body(cu.PathEdges(probFull, 1, 1), proj, f.ImageDepth);

                // (B) tyz histogram nad snimkem zmensenym jako vstup site
                if (rgbSmall == null) { rgbSmall = new Image<BGR32>(size, size); probSmall = new Image<Gray>(size, size); }
                rgbSmall.Resize(rgb);
                hist.Process(rgbSmall, probSmall);
                (s.L[VarSmall], s.R[VarSmall]) = Body(
                    cu.PathEdges(probSmall, (double)rgb.Width / size, (double)rgb.Height / size), proj, f.ImageDepth);

                // (C) sit: ulozeny pravdepodobnostni obraz
                var pr = f.ImageProbability;
                if (pr != null)
                {
                    recRozmer = $"{pr.Width}x{pr.Height}";
                    recRows = pr.Height;
                    var hr = cu.PathEdges(pr, (double)rgb.Width / pr.Width, (double)rgb.Height / pr.Height);
                    (s.L[VarRec], s.R[VarRec]) = Body(hr, proj, f.ImageDepth);

                    // Kontrola meridla proti tomu, co zapsal robot.
                    if (f.PathEdges != null)
                    {
                        var zapsane = f.PathEdges.Where(x => x.LeftPoint.A != 0).Select(x => x.LeftPoint)
                            .Concat(f.PathEdges.Where(x => x.RightPoint.A != 0).Select(x => x.RightPoint)).ToList();
                        var moje = s.L[VarRec].Concat(s.R[VarRec]).ToList();
                        kontrolaPocet.Add(Math.Abs(zapsane.Count - moje.Count));
                        if (zapsane.Count == moje.Count)
                            for (int i = 0; i < moje.Count; i++)
                            {
                                // Poradi: nejdriv vsechny leve, pak prave - v obou seznamech stejne.
                                double dx = zapsane[i].X - moje[i].X, dy = zapsane[i].Y - moje[i].Y;
                                kontrolaPoloha.Add(Math.Sqrt(dx * dx + dy * dy));
                            }
                    }
                    else bezKontroly++;
                }
                else { s.L[VarRec] = new List<Point2D>(); s.R[VarRec] = new List<Point2D>(); }

                // Vzdalenost po radcich (kazdy 4. snimek staci - geometrie se nemeni).
                if (snimky.Count % 4 == 0)
                {
                    rgbVyska = rgb.Height;
                    if (!dalka.TryGetValue(s.Cam, out var radky))
                    {
                        radky = new List<float>[rgb.Height];
                        for (int y = 0; y < radky.Length; y++) radky[y] = new List<float>();
                        dalka[s.Cam] = radky;
                    }
                    Dalka(proj, f.ImageDepth, rgb.Width, radky);
                }

                snimky.Add(s);
            }

            if (snimky.Count == 0)
            {
                Console.WriteLine("Zadny snimek s barvou, hloubkou a barevnou projekci (format < 5?) - nelze merit.");
                return;
            }
            nazvy[VarFull] = $"hist {fullRozmer}";
            if (recRozmer != null) nazvy[VarRec] = $"sit {recRozmer} (zaznam)";
            Console.WriteLine($"zpracovano snimku: {snimky.Count}");
            Console.WriteLine();

            Console.WriteLine("0) KONTROLA MERIDLA - hrany z ulozeneho obrazu site proti hranam, ktere zapsal robot:");
            if (kontrolaPocet.Count == 0)
                Console.WriteLine($"  nelze (snimku bez zapsanych hran: {bezKontroly})");
            else
            {
                Console.WriteLine("  " + kontrolaPocet.Line("bodu"));
                Console.WriteLine("  " + kontrolaPoloha.Line("m"));
                Console.WriteLine("  Ma byt 0 / 0 - jinak report nepocita totez co runtime.");
            }
            Console.WriteLine();

            // 1) Pocet hranicnich bodu.
            Console.WriteLine("1) HRANICNI BODY NA SNIMEK (metricke, tedy s platnou hloubkou), p50 za kameru:");
            Console.WriteLine("  varianta                 kamera                  bodu L p50  bodu R p50   L=0 %   R=0 %");
            foreach (int v in new[] { VarFull, VarSmall, VarRec })
                foreach (var cam in snimky.Select(x => x.Cam).Distinct().OrderBy(c => c))
                {
                    var fc = snimky.Where(x => x.Cam == cam).ToList();
                    var l = new Stats(""); var r = new Stats("");
                    foreach (var x in fc) { l.Add(x.L[v].Count); r.Add(x.R[v].Count); }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-24} {1,-22} {2,10:F0}  {3,10:F0}   {4,5:F1}   {5,5:F1}",
                        nazvy[v], cam, l.Median, r.Median,
                        100.0 * fc.Count(x => x.L[v].Count == 0) / fc.Count,
                        100.0 * fc.Count(x => x.R[v].Count == 0) / fc.Count));
                }
            Console.WriteLine();

            // 2) Koridor.
            var pary = Paruj(snimky);
            Console.WriteLine($"2) KORIDOR (CorridorFinder, SingleEdge=true), paru snimku: {pary.Count}");
            Console.WriteLine($"  a) PEVNA brana {minInliers} bodu (obe strany i jedna hrana; do 29. 9. 2026 v provoznim profilu 20):");
            var cfg = new CorridorConfig { MinInliers = minInliers, SingleEdgeMinInliers = minInliers,
                                           MinInliersPercent = 0, SingleEdgeMinInliersPercent = 0, SingleEdge = true };
            var vysledky = new RoadCorridor[3][];
            foreach (int v in new[] { VarFull, VarSmall, VarRec })
            {
                var finder = new CorridorFinder(cfg);
                vysledky[v] = pary.Select(p => finder.Find(p.L[v], p.R[v])).ToArray();
            }
            Console.WriteLine("  varianta                   Ok %  1 hrana %  TooFewInl %  TooFewPts %  OneSide %  NotPar %  inl L p50  inl R p50");
            foreach (int v in new[] { VarFull, VarSmall, VarRec }) Radek(nazvy[v], vysledky[v]);

            // b) Brana v procentech radku (od 30. 9. 2026 za behu, corridorinliers=): tataz
            // CorridorConfig jako runtime, jen s vyskou obrazu dane varianty.
            var cfgP = new CorridorConfig { MinInliersPercent = percent, SingleEdgeMinInliersPercent = percent, SingleEdge = true };
            int[] rows = { fullRows, size, recRows };
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  b) brana {0} % RADKU (corridorinliers=, corridorsingleinliers=): {1} radku -> {2} bodu, {3} -> {4}{5}",
                percent, fullRows, cfgP.EffectiveMinInliers(fullRows), size, cfgP.EffectiveMinInliers(size),
                recRows > 0 && recRows != size ? $", sit {recRows} -> {cfgP.EffectiveMinInliers(recRows)}" : ""));
            var vysledkyP = new RoadCorridor[3][];
            foreach (int v in new[] { VarFull, VarSmall, VarRec })
            {
                var finder = new CorridorFinder(cfgP);
                vysledkyP[v] = pary.Select(p => finder.Find(p.L[v], p.R[v], rows[v])).ToArray();
            }
            foreach (int v in new[] { VarFull, VarSmall, VarRec }) Radek(nazvy[v], vysledkyP[v]);
            Console.WriteLine("  (kdyz se v b) zmenseny histogram srovna s plnym, propad v a) dela pevna brana, ne kvalita bodu)");
            var vysledkyS = vysledkyP[VarSmall];
            int scaled = cfgP.EffectiveMinInliers(size);
            Console.WriteLine();

            Console.WriteLine($"3) PRESNOST - cykly, kde je Ok plny histogram (pevna brana {minInliers}) i zmenseny (brana {scaled}, tedy {percent} %):");
            Console.WriteLine("   (se stejnou branou by zmenseny skoro nikdy neprosel, takze by nebylo co srovnat)");
            var dW = new Stats("|sirka plny - zmenseny| [m]");
            var dLat = new Stats("|pricne plny - zmenseny| [m]");
            var dDir = new Stats("|smer plny - zmenseny| [deg]");
            var wF = new Stats("sirka plny [m]"); var wS = new Stats("sirka zmenseny [m]");
            var resF = new Stats("rezidua plny [m]"); var resS = new Stats("rezidua zmenseny [m]");
            for (int i = 0; i < pary.Count; i++)
            {
                var a = vysledky[VarFull][i]; var b = vysledkyS[i];
                if (!a.Ok || !b.Ok) continue;
                dW.Add(Math.Abs(a.Width - b.Width));
                dLat.Add(Math.Abs(a.Lateral - b.Lateral));
                dDir.Add(Math.Abs(Wrap(a.DirectionRad - b.DirectionRad)) * 180 / Math.PI);
                wF.Add(a.Width); wS.Add(b.Width);
                resF.Add((a.ResidualLeft + a.ResidualRight) / 2); resS.Add((b.ResidualLeft + b.ResidualRight) / 2);
            }
            foreach (var st in new[] { dW, dLat, dDir, wF, wS, resF, resS }) Console.WriteLine("  " + st.Line());
            Console.WriteLine();

            // 4) Grid: kolik metru po zemi pokryje jeden radek pravdepodobnosti.
            Console.WriteLine("4) GRID - radialni stopa JEDNOHO radku pravdepodobnosti po zemi:");
            Console.WriteLine("   (median vzdalenosti po radcich pres stredni sloupce a snimky; bunka gridu je 0,05 m,");
            Console.WriteLine("    stopa nad ni = vic bunek za sebou dostane tentyz pixel pravdepodobnosti)");
            double[] kose = { 1, 2, 3, 4, 5, 6, 8 };
            foreach (var kv in dalka.OrderBy(k => k.Key))
            {
                var r = kv.Value.Select(l => l.Count >= 5 ? Median(l) : double.NaN).ToArray();
                Console.WriteLine($"  {kv.Key}:");
                Console.WriteLine($"    vzdalenost [m]   {rgbVyska} radku [m/radek]   {size} radku [m/radek]   bunek na radek pri {size}");
                for (int b = 0; b < kose.Length - 1; b++)
                {
                    double full = StopaV(r, rgbVyska, rgbVyska, kose[b], kose[b + 1]);
                    double small = StopaV(r, rgbVyska, size, kose[b], kose[b + 1]);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0,4:F0} .. {1,-4:F0}       {2,10:F3}             {3,10:F3}            {4,6:F1}",
                        kose[b], kose[b + 1], full, small, small / 0.05));
                }
            }
            if (!double.IsNaN(fxBarvy))
            {
                Console.WriteLine("  pricna stopa jednoho sloupce (sirka pixelu / fx * vzdalenost):");
                foreach (double d in new[] { 2.0, 4.0, 6.0, 8.0 })
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0,3:F0} m: plny {1:F3} m, {2}x{2} {3:F3} m",
                        d, d / fxBarvy, size, d * 640.0 / size / fxBarvy));
            }
        }

        private static void Radek(string nazev, RoadCorridor[] r)
        {
            int n = Math.Max(1, r.Length);
            double Pct(Func<RoadCorridor, bool> p) => 100.0 * r.Count(p) / n;
            var inlL = new Stats(""); var inlR = new Stats("");
            foreach (var c in r) { if (c.PointsLeft > 0) inlL.Add(c.InliersLeft); if (c.PointsRight > 0) inlR.Add(c.InliersRight); }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-24} {1,6:F1}  {2,9:F1}  {3,11:F1}  {4,11:F1}  {5,8:F1}  {6,7:F1}  {7,9:F0}  {8,9:F0}",
                nazev, Pct(c => c.Ok), Pct(c => !c.Ok && c.HasSingleEdge),
                Pct(c => c.Reason == CorridorReason.TooFewInliers), Pct(c => c.Reason == CorridorReason.TooFewPoints),
                Pct(c => c.Reason == CorridorReason.OneSideOnly), Pct(c => c.Reason == CorridorReason.NotParallel),
                inlL.Median, inlR.Median));
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        /// <summary>Hranice (v pixelech barvy) na metricke body - totez co CameraFrameProcessor.ProjectPathEdges.</summary>
        private static (List<Point2D>, List<Point2D>) Body(List<PathEdge> edges, ColorPixelTo3D proj, Image<Gray16> depth)
        {
            var l = new List<Point2D>(); var r = new List<Point2D>();
            foreach (var e in edges)
            {
                if (e.Left.HasValue) { var p = proj.ToRobot(e.Left.Value, e.Y, depth); if (p.A != 0) l.Add(new Point2D(p.X, p.Y)); }
                if (e.Right.HasValue) { var p = proj.ToRobot(e.Right.Value, e.Y, depth); if (p.A != 0) r.Add(new Point2D(p.X, p.Y)); }
            }
            return (l, r);
        }

        /// <summary>Median vzdalenosti v jednom snimku po radcich barvy (stredni pet desetin sloupcu).</summary>
        private static void Dalka(ColorPixelTo3D proj, Image<Gray16> depth, int w, List<float>[] radky)
        {
            var buf = new List<double>();
            for (int y = 0; y < radky.Length; y++)
            {
                buf.Clear();
                for (int x = (int)(w * 0.25); x < (int)(w * 0.75); x += 4)
                {
                    var p = proj.ToRobot(x, y, depth);
                    if (p.A != 0) buf.Add(Math.Sqrt(p.X * p.X + p.Y * p.Y));
                }
                if (buf.Count >= 10) radky[y].Add((float)Median(buf));
            }
        }

        /// <summary>
        /// Median stopy radku v kosi vzdalenosti: radek <c>k</c> pravdepodobnosti (<paramref name="rows"/>
        /// radku) odpovida radku barvy <c>(int)(k*H/rows)</c> (tak skaluje NativeComputeUnit.PathEdges);
        /// stopa = rozdil vzdalenosti sousednich radku pravdepodobnosti.
        /// </summary>
        private static double StopaV(double[] r, int rgbH, int rows, double od, double doM)
        {
            var st = new List<double>();
            double sy = (double)rgbH / rows;
            for (int k = 0; k + 1 < rows; k++)
            {
                double a = r[(int)(k * sy)], b = r[(int)((k + 1) * sy)];
                if (double.IsNaN(a) || double.IsNaN(b)) continue;
                double d = Math.Min(a, b);
                if (d >= od && d < doM) st.Add(Math.Abs(a - b));
            }
            return st.Count > 0 ? Median(st) : double.NaN;
        }

        private static double Median(IEnumerable<double> v)
        {
            var a = v.OrderBy(x => x).ToArray();
            return a.Length == 0 ? double.NaN : a[a.Length / 2];
        }

        private static double Median(List<float> v) => Median(v.Select(x => (double)x));

        /// <summary>Parovani jako CorridorLocalizer / CorridorFitReport: nejblizsi snimek druhe kamery.</summary>
        private static List<Snimek> Paruj(List<Snimek> snimky)
        {
            var pary = new List<Snimek>();
            var last = new Dictionary<string, Snimek>();
            foreach (var f in snimky)
            {
                last[f.Cam] = f;
                Snimek other = null; double best = double.MaxValue;
                foreach (var kv in last)
                {
                    if (kv.Key == f.Cam) continue;
                    double dt = Math.Abs((kv.Value.T - f.T).TotalMilliseconds);
                    if (dt < best) { best = dt; other = kv.Value; }
                }
                if (other == null) continue;
                var p = new Snimek { Cam = f.Cam, T = f.T };
                // Stejne jako CorridorFitReport (a CorridorLocalizer): na kazde strane body te
                // kamery, ktera jich ma vic - kazda D435 vidi hlavne svou hranu, nescitaji se.
                for (int v = 0; v < 3; v++)
                {
                    p.L[v] = f.L[v].Count >= other.L[v].Count ? f.L[v] : other.L[v];
                    p.R[v] = f.R[v].Count >= other.R[v].Count ? f.R[v] : other.R[v];
                }
                pary.Add(p);
            }
            return pary;
        }
    }
}
