using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// A/B mereni <b>estimatoru prolozeni</b> v hranove lokalizaci: co dela zmena
    /// <see cref="CorridorConfig.FitMode"/> a <see cref="CorridorConfig.RegatePasses"/>.
    ///
    /// <para><b>Dve poloviny, obe potreba.</b> <c>--synth</c> generuje hranicni body s <b>znamou
    /// pravdou</b> a namerenym modelem sumu, takze umi rict SPRAVNE/SPATNE, ne jen "jinak" — to nad
    /// zaznamem nejde, protoze tam pravdu neznáme (mapa neni pravda o tom, kde kamera videla kraj).
    /// Nad zaznamem se naopak meri to, co syntetika neumi: skutecne rozlozeni bodu, dropouty
    /// a podil zamitnutych cyklu.</para>
    ///
    /// <para><b>Proc to neni jen v testech.</b> RANSAC je nedeterministicky (neseedovany
    /// <c>Random</c>), takze jedno mereni na variantu nic neznamena — nad tymiz daty kolisa pocet
    /// prijatych koridoru o +-8. Kazda varianta se proto meri <c>--rep</c> krat a tiskne se
    /// ROZPETI, ne jedno cislo. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para><b>Nad zaznamem se NEPOCITAJI body znovu z hloubky</b> — berou se metricke body, ktere
    /// uz v zaznamu jsou (<see cref="CameraFrame.PathEdges"/>, format verze &gt;= 5). Meri se tedy
    /// presne ten stupen, ktery se meni, a nic pred nim.</para>
    /// </summary>
    public static class CorridorFitReport
    {
        /// <summary>Jedna merena varianta konfigurace.</summary>
        private sealed class Variant
        {
            public string Name;
            public Func<CorridorConfig> Make;
        }

        private static List<Variant> Variants(int regate, double huberK) => new List<Variant>
        {
            new Variant { Name = "vychozi (osova, bez prehradl.)",
                          Make = () => new CorridorConfig() },
            new Variant { Name = $"osova + prehradl. {regate}x",
                          Make = () => new CorridorConfig { RegatePasses = regate } },
            new Variant { Name = "ortogonalni",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.Orthogonal } },
            new Variant { Name = $"ortogonalni + prehradl. {regate}x",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.Orthogonal, RegatePasses = regate } },
            new Variant { Name = $"Huber k={huberK:F2}",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalHuber, HuberK = huberK } },
            new Variant { Name = $"Huber k={huberK:F2} + prehradl. {regate}x",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalHuber, HuberK = huberK,
                                                            RegatePasses = regate } },

            // Huber s meritkem z MAD rezidui, ne z tolerance. Tolerance je tak volna, ze v jejich
            // jednotkach Huber nikdy nezabere; s MAD je to skutecne robustni odhad. Ma odstranit
            // systematickou odchylku sirky, ktera vznika tim, ze nejmensi kvadraty sleduji PRUMER
            // zesikmeneho rozdeleni odchylek, ne jeho median. Viz ARBot.Analyze edgebias.
            new Variant { Name = $"Huber MAD k={huberK:F2}",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalHuber, HuberK = huberK,
                                                            HuberUsesTolerance = false } },
            new Variant { Name = $"Huber MAD k={huberK:F2} + prehradl. {regate}x",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalHuber, HuberK = huberK,
                                                            HuberUsesTolerance = false, RegatePasses = regate } },

            // Estimatory, ktere CILI MEDIAN. Huber ma v chvostu vahu omezenou, ale NENULOVOU
            // (k*s/|r|), takze jednostranny chvost porad tahne konstantni silou - proto u nej
            // zbylo 6 mm misto nuly. L1 minimalizuje soucet absolutnich odchylek (= median),
            // Tukey chvost uplne utne.
            new Variant { Name = "L1 (median)",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalL1 } },
            new Variant { Name = $"L1 (median) + prehradl. {regate}x",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalL1, RegatePasses = regate } },
            new Variant { Name = "Tukey (utne chvost)",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalTukey } },
            new Variant { Name = $"Tukey (utne chvost) + prehradl. {regate}x",
                          Make = () => new CorridorConfig { FitMode = LineFitMode.OrthogonalTukey, RegatePasses = regate } },
        };

        // ------------------------------------------------------------------ syntetika

        /// <summary>
        /// Syntetický sweep se znamou pravdou. Model sumu je <b>namereny</b> (23. 8. 2026, 12 631
        /// bodu proti mape): median hranicnich bodu sedi na okraji vozovky v kazde vzdalenosti
        /// a roste jen rozptyl — z +-0,05 m na 1 m na radove +-0,5 m na 10 m. Tedy NENI to
        /// "presne body + hrube outliery", je to nevychyleny sum rostouci se vzdalenosti.
        /// </summary>
        public static void Synth(int trials, int repeats, double grossFraction, double huberK, int regate)
        {
            Console.WriteLine($"SYNTETIKA — {trials} scen x {repeats} opakovani na variantu");
            Console.WriteLine($"sum: sigma(r) = 0,02 + 0,05*r [m] kolmo na hranici"
                              + (grossFraction > 0 ? $", hrubych outlieru {grossFraction * 100:F0} %" : ", bez hrubych outlieru"));
            Console.WriteLine("pravda: sirka 2,00 m, pricne a smer se losuji");
            Console.WriteLine();
            Console.WriteLine("  Kazde cislo je MEDIAN v ramci opakovani a v zavorce rozpeti mezi opakovanimi.");
            Console.WriteLine("  Bez toho rozpeti nejde rict, jestli je rozdil mezi variantami skutecny —");
            Console.WriteLine("  RANSAC je nedeterministicky. Prekryvajici se rozpeti = zadny prukazny rozdil.");
            Console.WriteLine();

            Console.WriteLine("  varianta                            Ok            chyba smeru [deg]      chyba pricne [m]        chyba sirky [m]      inliery");
            foreach (var v in Variants(regate, huberK))
            {
                var okCounts = new List<double>();
                var dirMed = new List<double>();
                var latMed = new List<double>();
                var widMed = new List<double>();
                var inlMed = new List<double>();

                for (int rep = 0; rep < repeats; rep++)
                {
                    int ok = 0;
                    var finder = new CorridorFinder(v.Make());
                    // Seed zavisi na opakovani, ne na variante — vsechny varianty vidi TATAZ data.
                    var rnd = new Random(1000 + rep);
                    var dir = new Stats(""); var lat = new Stats(""); var wid = new Stats(""); var inl = new Stats("");

                    for (int t = 0; t < trials; t++)
                    {
                        double trueWidth = 2.0;
                        double trueLat = (rnd.NextDouble() - 0.5) * 0.8;      // +-0,4 m od osy
                        double trueDir = (rnd.NextDouble() - 0.5) * 0.5;      // +-14 stupnu

                        var (l, r) = Scene(rnd, trueWidth, trueLat, trueDir, grossFraction);
                        var c = finder.Find(l, r);
                        if (c.Reason != CorridorReason.Ok) continue;

                        ok++;
                        dir.Add(Math.Abs(Half(c.DirectionRad - trueDir)) * 180 / Math.PI);
                        lat.Add(Math.Abs(c.Lateral - trueLat));
                        wid.Add(Math.Abs(c.Width - trueWidth));
                        inl.Add(0.5 * (c.InliersLeft + c.InliersRight));
                    }
                    okCounts.Add(ok);
                    dirMed.Add(dir.Median); latMed.Add(lat.Median);
                    widMed.Add(wid.Median); inlMed.Add(inl.Median);
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-32} {1,4} ({2:F0}-{3:F0}) {4,8:F3} ({5:F3}-{6:F3}) {7,8:F4} ({8:F4}-{9:F4}) {10,8:F4} ({11:F4}-{12:F4}) {13,5:F0}",
                    v.Name, okCounts.Average(), okCounts.Min(), okCounts.Max(),
                    dirMed.Average(), dirMed.Min(), dirMed.Max(),
                    latMed.Average(), latMed.Min(), latMed.Max(),
                    widMed.Average(), widMed.Min(), widMed.Max(),
                    inlMed.Average()));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Jedna scena: leva a prava hranice koridoru videna z pocatku, body od 1 m dal, se sumem
        /// rostoucim se vzdalenosti. Vzdalenejsi body jsou <b>ridsi</b> (perspektiva) a od nejake
        /// dalky uz vypadavaji — presne jako u kamery.
        /// </summary>
        private static (List<Point2D> left, List<Point2D> right) Scene(
            Random rnd, double width, double lateral, double dirRad, double grossFraction)
        {
            double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
            double nx = -uy, ny = ux;
            double ox = -lateral * nx, oy = -lateral * ny;

            var left = new List<Point2D>();
            var right = new List<Point2D>();

            // Podelny krok roste se vzdalenosti (perspektiva): u kamery je na 1 m radka co 2 cm,
            // na 10 m co 30 cm. Dosah se losuje 5-11 m.
            double reach = 5 + rnd.NextDouble() * 6;
            for (double s = 1.0; s < reach; s += 0.03 + 0.03 * s)
            {
                Add(left, +1); Add(right, -1);

                void Add(List<Point2D> into, int side)
                {
                    double bx = ox + ux * s + nx * side * width / 2;
                    double by = oy + uy * s + ny * side * width / 2;
                    double r = Math.Sqrt(bx * bx + by * by);

                    // Namereny model: median na miste, rozptyl roste se vzdalenosti.
                    double sigma = 0.02 + 0.05 * r;
                    double e = Gauss(rnd) * sigma;

                    // Hrube outliery (stin, trava, jina hranice) — vybocek radu metru.
                    if (grossFraction > 0 && rnd.NextDouble() < grossFraction)
                        e += (rnd.NextDouble() < 0.5 ? -1 : 1) * (0.5 + rnd.NextDouble() * 1.5);

                    into.Add(new Point2D(bx + nx * side * e, by + ny * side * e));
                }
            }
            return (left, right);
        }

        private static double Gauss(Random rnd)
        {
            double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // ------------------------------------------------------------------ nad zaznamem

        /// <summary>
        /// Prehraje <b>metricke hranicni body ze zaznamu</b> pres varianty konfigurace. Pravdu
        /// tady neznáme, takze se meri, kolik cyklu projde a jak jsou na tom rezidua, sirka
        /// a nerovnobeznost — plus proc ty ostatni propadly.
        /// </summary>
        public static void Replay(RecordFile rec, int repeats, int limit, double huberK, int regate,
                                  double trueWidth, double axisY)
        {
            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            if (limit > 0 && limit < entries.Count) entries = entries.Take(limit).ToList();
            Console.WriteLine($"CameraFrame: {entries.Count} (cte cele snimky, chvili to trva)");

            // Body se precte JEDNOU a drzi v pameti — cist gigabajtovy zaznam pro kazdou variantu
            // a kazde opakovani by bylo desetkrat pomalejsi a nic navic by to nereklo.
            var frames = new List<(string Cam, DateTime T, List<Point2D> L, List<Point2D> R)>();
            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f) || f.PathEdges == null) continue;
                var (l, r) = MetricPoints(f.PathEdges);
                frames.Add((f.Name ?? string.Empty, f.TimeStamp, l, r));
            }
            Console.WriteLine($"z toho s hranicemi: {frames.Count}");
            if (frames.Count == 0)
            {
                Console.WriteLine("Zaznam metricke hranicni body nenese (format verze < 5) — nelze merit.");
                return;
            }

            // SIRKA Z MAPY jako nezavisla reference presnosti. Bez ni jde merit jen
            // self-konzistenci (rezidua, nerovnobeznost), a ta se da "zlepsit" tim, ze se prijmou
            // jen snadne snimky — mensi nerovnobeznost pri mensim poctu Ok tedy sama o sobe
            // neznamena presnejsi merenie. Sirka z mapy je proti tomu imunni: rika, jak daleko
            // od skutecne sirky cesty vysledek je.
            var mapWidth = new Dictionary<DateTime, double>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "RoadCorridorMsg") continue;
                if (rec.Read(e) is RoadCorridorMsg m && m.MapWidth > 0) mapWidth[m.TimeStamp] = m.MapWidth;
            }
            Console.WriteLine(mapWidth.Count > 0
                ? $"sirka z mapy k dispozici u {mapWidth.Count} casu (RoadCorridorMsg)"
                : "sirka z mapy NENI (zaznam bez RoadCorridorMsg) — presnost proti mape nelze merit");

            // SKUTECNA PRAVDA, kdyz je zadana geometrie cesty. Nad OSM/SyntetickyRovny.osm je cesta
            // presne rovna podel +X, presne 2,0 m siroka a jeji osa je presne y = 0 v lokalnim ENU,
            // takze se da merit proti ZNAME hodnote misto proti mape.
            //
            // Proc to je lepsi nez RoadCorridorMsg.MapWidth: ta neni sirka z mapy, ale vystup
            // RoadWidthFilter.Estimate, tedy filtr, ktery se UCI z merenii (nad timto zaznamem dava
            // 2,017 m proti skutecnym 2,000). Merit presnost proti necemu, co merenie samo
            // ovlivnuje, je mirne kruhove. Znama sirka a ground truth kruhove nejsou vubec.
            var truth = new List<(DateTime T, double X, double Y, double Th)>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "GroundTruthMsg") continue;
                if (rec.Read(e) is GroundTruthMsg g) truth.Add((g.TimeStamp, g.X, g.Y, g.Theta));
            }
            truth.Sort((a, b) => a.T.CompareTo(b.T));

            bool haveAxis = !double.IsNaN(axisY) && truth.Count > 0;
            Console.WriteLine(trueWidth > 0
                ? $"znama sirka cesty: {trueWidth:F3} m (--truewidth) — meri se proti NI, ne proti filtru"
                : "znama sirka cesty nezadana (--truewidth) — presnost sirky se meri proti mape/filtru");
            Console.WriteLine(haveAxis
                ? $"ground truth: {truth.Count} zprav, osa cesty y = {axisY:F2} m podel +X (--axisy) "
                  + "— pricna poloha i kurz se meri proti PRAVDE"
                : "ground truth / osa nezadana (--axisy) — pricnou polohu ani kurz nelze overit");

            // Parovani presne jako CorridorLocalizer: kazdy snimek se paruje s POSLEDNIM prijatym
            // snimkem druhe kamery (dozadu). Kompenzace pohybu se tu nedela — merí se estimator,
            // a pripadna nerovnobeznost z pohybu je pro vsechny varianty tataz.
            var pairs = new List<(List<Point2D> L, List<Point2D> R, double MapW,
                                  double TrueLat, double TrueDir)>();
            var last = new Dictionary<string, (DateTime T, List<Point2D> L, List<Point2D> R)>();
            foreach (var f in frames)
            {
                last[f.Cam] = (f.T, f.L, f.R);
                (DateTime T, List<Point2D> L, List<Point2D> R)? other = null;
                double best = double.MaxValue;
                foreach (var kv in last)
                {
                    if (kv.Key == f.Cam) continue;
                    double dt = Math.Abs((kv.Value.T - f.T).TotalMilliseconds);
                    if (dt < best) { best = dt; other = kv.Value; }
                }
                if (other == null) continue;

                // Kamery renderuji z ground truth (camerapose=truth je od 22. 8. 2026 vychozi),
                // takze koridor meri SKUTECNOU polohu vuci ose - a chyba proti pravde je tedy
                // chyba merenia, ne chyba lokalizace.
                double tLat = double.NaN, tDir = double.NaN;
                if (haveAxis)
                {
                    var g = NearestTruth(truth, f.T);
                    // Cesta vede podel +X, osa je y = axisY. "+ = robot vlevo od osy" (FLU, +Y vlevo),
                    // takze pricna poloha je prosty rozdil v Y. Smer cesty v ramci robotu: cesta ma
                    // v ENU kurz 0, robot Theta, takze se jevi stocena o -Theta.
                    tLat = g.Y - axisY;
                    tDir = Half(-g.Th);
                }

                pairs.Add((f.L.Count >= other.Value.L.Count ? f.L : other.Value.L,
                           f.R.Count >= other.Value.R.Count ? f.R : other.Value.R,
                           mapWidth.TryGetValue(f.T, out double mw) ? mw : double.NaN,
                           tLat, tDir));
            }
            Console.WriteLine($"dvojic ke zpracovani: {pairs.Count}");

            // Kolik bodu vubec je — bez tohoto cisla nejde odlisit "estimator selhal" od
            // "nebylo z ceho merit" (zaznam bez metrickych bodu da nuly a vypada jako regrese).
            var ptsL = new Stats(""); var ptsR = new Stats("");
            foreach (var p in pairs) { ptsL.Add(p.L.Count); ptsR.Add(p.R.Count); }
            Console.WriteLine($"bodu na dvojici: leva p50 {ptsL.Median:F0} (min {ptsL.Min:F0}, max {ptsL.Max:F0}), "
                              + $"prava p50 {ptsR.Median:F0} (min {ptsR.Min:F0}, max {ptsR.Max:F0})");
            Console.WriteLine();

            string refLabel = trueWidth > 0 ? "|sirka-PRAVDA|" : "|sirka-mapa| ";
            Console.WriteLine($"  varianta                              Ok (rozpeti)   NotParallel   TooFewInliers   rezidua p50   inliery p50   nerovnob. p50   {refLabel} p50    p90   VYCHYL.  ROZPTYL"
                              + (haveAxis ? "     |pricne-PRAVDA| p50    p90     |kurz-PRAVDA| p50 [deg]" : "")
                              + "     ms/dvojici");
            foreach (var v in Variants(regate, huberK))
            {
                var okCounts = new List<int>();
                var np = new List<int>();
                var few = new List<int>();
                var wid = new Stats(""); var res = new Stats(""); var inl = new Stats(""); var par = new Stats("");
                var vsMap = new Stats(""); var bias = new Stats("");
                var vsLat = new Stats(""); var vsDir = new Stats("");
                var reasons = new Dictionary<CorridorReason, int>();

                var clock = new System.Diagnostics.Stopwatch();
                for (int rep = 0; rep < repeats; rep++)
                {
                    var finder = new CorridorFinder(v.Make());
                    int ok = 0, notPar = 0, tooFew = 0;
                    clock.Start();
                    foreach (var p in pairs)
                    {
                        var c = finder.Find(p.L, p.R);
                        switch (c.Reason)
                        {
                            case CorridorReason.Ok: ok++; break;
                            case CorridorReason.NotParallel: notPar++; break;
                            case CorridorReason.TooFewInliers: tooFew++; break;
                        }
                        if (rep == 0) Bump(reasons, c.Reason);
                        if (c.Reason != CorridorReason.Ok) continue;
                        wid.Add(c.Width);
                        res.Add(0.5 * (c.ResidualLeft + c.ResidualRight));
                        inl.Add(0.5 * (c.InliersLeft + c.InliersRight));
                        par.Add(Math.Abs(c.ParallelErrorRad) * 180 / Math.PI);

                        // Reference presnosti: znama sirka, kdyz je zadana, jinak mapa/filtr.
                        // ZNAMENKOVA odchylka zvlast: |odchylka| michá vychyleni s rozptylem, takze
                        // by nesla poznat vymena jednoho za druhy. A prave o tu tady jde - robustni
                        // prolozeni ma srazit VYCHYLENI, ale muze zvednout rozptyl.
                        if (trueWidth > 0) { vsMap.Add(Math.Abs(c.Width - trueWidth)); bias.Add(c.Width - trueWidth); }
                        else if (!double.IsNaN(p.MapW)) { vsMap.Add(Math.Abs(c.Width - p.MapW)); bias.Add(c.Width - p.MapW); }

                        if (!double.IsNaN(p.TrueLat))
                        {
                            vsLat.Add(Math.Abs(c.Lateral - p.TrueLat));
                            vsDir.Add(Math.Abs(Half(c.DirectionRad - p.TrueDir)) * 180 / Math.PI);
                        }
                    }
                    clock.Stop();
                    okCounts.Add(ok); np.Add(notPar); few.Add(tooFew);
                }
                double msPerPair = clock.Elapsed.TotalMilliseconds / Math.Max(1, repeats * pairs.Count);

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-34} {1,5} ({2}-{3}) {4,10:F0} {5,13:F0} {6,13:F4} {7,13:F0} {8,15:F3} {9,15:F4} {10,8:F4} {11,10:F4} {12,8:F4}",
                    v.Name, (int)Math.Round(okCounts.Average()), okCounts.Min(), okCounts.Max(),
                    np.Average(), few.Average(), res.Median, inl.Median, par.Median,
                    vsMap.Median, vsMap.Percentile(90), bias.Median, bias.Percentile(90) - bias.Percentile(10))
                    + (haveAxis ? string.Format(CultureInfo.InvariantCulture,
                        " {0,18:F4} {1,8:F4} {2,20:F3}",
                        vsLat.Median, vsLat.Percentile(90), vsDir.Median) : "")
                    + string.Format(CultureInfo.InvariantCulture, " {0,11:F3}", msPerPair));

                // Cely rozpad duvodu u prvniho opakovani — bez nej nejde poznat, jestli cykly
                // padaji na estimatoru, nebo uz na tom, ze hranice vidi jen jedna strana.
                Console.WriteLine("        duvody: " + string.Join(", ",
                    reasons.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value}")));
            }
            Console.WriteLine();
            Console.WriteLine("  (Pravdu tady neznáme, takze vic Ok neni samo o sobe lepsi — cti to spolu");
            Console.WriteLine("   s rezidui a nerovnobeznosti. Vic prijatych PRI stejnych rezidui je zlepseni,");
            Console.WriteLine("   vic prijatych pri horsich rezidui je jen povolena oprat.)");
            Console.WriteLine();

            NotParallelAnatomy(pairs);
        }

        /// <summary>
        /// Rozbor zamitnutych <see cref="CorridorReason.NotParallel"/>: <b>je to chyba prolozeni,
        /// nebo se cesta skutecne rozsiruje?</b>
        ///
        /// <para><b>Proc to je potreba.</b> Gate <c>MaxParallelErrorRad</c> porovnava levou primku
        /// s pravou — tedy predpoklada, ze hranice cesty jsou rovnobezne. Na rozsirujicim se useku
        /// to <b>neplati</b>: v <c>OSM/SyntetickyKoridor.osm</c> je usek D nalevka 1 m -&gt; 3 m na
        /// delce 10 m, takze kazda hranice se od osy odklani o atan(1/10) = 5,71° a hranice vuci
        /// sobe o <b>11,42°</b> — nad prahem 10°. Tam se koridor zamitne VZDY, i kdyby bylo
        /// prolozeni dokonale.</para>
        ///
        /// <para><b>Rozlisovaci znak jsou rezidua.</b> Skutecne rozsireni = dve dobre prolozene
        /// primky, ktere se rozbihaji (mala rezidua, velka nerovnobeznost). Spatne prolozeni =
        /// velka rezidua, kratka zakladna. Kdyz maji zamitnute snimky rezidua srovnatelna
        /// s prijatymi, gate zahazuje platna merenia.</para>
        /// </summary>
        private static void NotParallelAnatomy(List<(List<Point2D> L, List<Point2D> R, double MapW,
                                                     double TrueLat, double TrueDir)> pairs)
        {
            // Merí se s VYPNUTYM gatem (prah na 90°), aby byla videt cela populace vcetne te,
            // kterou by gate zahodil — jinak se o zamitnutych nic nedozvime.
            var finder = new CorridorFinder(new CorridorConfig { MaxParallelErrorRad = Math.PI / 2 });
            const double gate = 10 * Math.PI / 180;

            var parIn = new Stats(""); var resIn = new Stats(""); var widIn = new Stats(""); var inlIn = new Stats("");
            var parOut = new Stats(""); var resOut = new Stats(""); var widOut = new Stats(""); var inlOut = new Stats("");
            var hist = new int[10];

            foreach (var p in pairs)
            {
                var c = finder.Find(p.L, p.R);
                if (c.Reason != CorridorReason.Ok) continue;

                double deg = Math.Abs(c.ParallelErrorRad) * 180 / Math.PI;
                hist[Math.Min(hist.Length - 1, (int)(deg / 2))]++;

                double res = 0.5 * (c.ResidualLeft + c.ResidualRight);
                double inl = 0.5 * (c.InliersLeft + c.InliersRight);
                if (Math.Abs(c.ParallelErrorRad) <= gate)
                { parIn.Add(deg); resIn.Add(res); widIn.Add(c.Width); inlIn.Add(inl); }
                else
                { parOut.Add(deg); resOut.Add(res); widOut.Add(c.Width); inlOut.Add(inl); }
            }

            Console.WriteLine("ANATOMIE NotParallel — je to chyba prolozeni, nebo se cesta rozsiruje?");
            Console.WriteLine("  (pocitano s VYPNUTYM gatem, at je videt i to, co by se zahodilo)");
            Console.WriteLine();
            Console.WriteLine("  nerovnobeznost [deg]   snimku");
            for (int i = 0; i < hist.Length; i++)
            {
                if (hist[i] == 0) continue;
                string label = i == hist.Length - 1 ? $"nad {i * 2}" : $"{i * 2}-{i * 2 + 2}";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,10}  {2}{3}", label, hist[i], new string('#', Math.Min(60, hist[i])),
                    i * 2 >= 10 ? "   <- gate zahodi" : ""));
            }
            Console.WriteLine();
            Console.WriteLine("  populace          n   nerovnob. p50   rezidua p50   p90       sirka p50   inliery p50");
            Print("projde gatem", parIn, resIn, widIn, inlIn);
            Print("gate zahodi", parOut, resOut, widOut, inlOut);
            Console.WriteLine();

            // Rozpad zahozenych po pasmech. Podstatne pro rozhodnuti, jestli gate uvolnit:
            // pasmo 10-14 stupnu je presne to, co predpovida nalevka (11,42), zatimco desitky
            // stupnu jsou spis krizovatka nebo dve rozdilne cesty v zabere - tam gate smysl ma.
            Console.WriteLine("  zahozene po pasmech (rozliseni 'nalevka' vs. 'skutecny nesmysl'):");
            Console.WriteLine("  pasmo [deg]       n   rezidua p50   p90       sirka p50   inliery p50");
            foreach (var (lo, hi, name) in new[] { (10.0, 14.0, "10-14"), (14.0, 20.0, "14-20"),
                                                   (20.0, 90.0, "nad 20") })
            {
                var pr = new Stats(""); var rs = new Stats(""); var wd = new Stats(""); var il = new Stats("");
                foreach (var p in pairs)
                {
                    var c = finder.Find(p.L, p.R);
                    if (c.Reason != CorridorReason.Ok) continue;
                    double deg = Math.Abs(c.ParallelErrorRad) * 180 / Math.PI;
                    if (deg < lo || deg >= hi) continue;
                    pr.Add(deg); rs.Add(0.5 * (c.ResidualLeft + c.ResidualRight));
                    wd.Add(c.Width); il.Add(0.5 * (c.InliersLeft + c.InliersRight));
                }
                if (rs.Count == 0) continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-12} {1,5} {2,13:F4} {3,9:F4} {4,13:F3} {5,13:F0}",
                    name, rs.Count, rs.Median, rs.Percentile(90), wd.Median, il.Median));
            }
            Console.WriteLine();
            Console.WriteLine("  Kdyz ma zahozena populace rezidua SROVNATELNA s prijatou, nejsou to spatna");
            Console.WriteLine("  prolozeni — je to geometrie, kterou gate neuznava (rozsirujici se cesta).");
            Console.WriteLine("  Kdyz ma rezidua vyrazne HORSI, gate dela svou praci.");
            Console.WriteLine();

            void Print(string name, Stats par, Stats res, Stats wid, Stats inl)
                => Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-14} {1,5} {2,13:F2} {3,13:F4} {4,9:F4} {5,13:F3} {6,13:F0}",
                    name, res.Count, par.Median, res.Median, res.Percentile(90), wid.Median, inl.Median));
        }

        /// <summary>Metricke body hranic z ramce (stejny filtr jako <c>CorridorLocalizer</c>: A != 0 = platny).</summary>
        private static (List<Point2D> left, List<Point2D> right) MetricPoints(List<PathEdge> edges)
        {
            var left = new List<Point2D>();
            var right = new List<Point2D>();
            foreach (var e in edges)
            {
                if (e.LeftPoint.A != 0) left.Add(new Point2D(e.LeftPoint.X, e.LeftPoint.Y));
                if (e.RightPoint.A != 0) right.Add(new Point2D(e.RightPoint.X, e.RightPoint.Y));
            }
            return (left, right);
        }

        private static void Bump(Dictionary<CorridorReason, int> d, CorridorReason r)
            => d[r] = d.TryGetValue(r, out int n) ? n + 1 : 1;

        /// <summary>Ground truth nejblizsi danemu casu (seznam je setrideny podle casu).</summary>
        private static (DateTime T, double X, double Y, double Th) NearestTruth(
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

        private static double Half(double a)
        {
            while (a > Math.PI / 2) a -= Math.PI;
            while (a < -Math.PI / 2) a += Math.PI;
            return a;
        }
    }
}
