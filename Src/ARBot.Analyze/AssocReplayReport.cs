using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Co by udelalo prirazeni k hrane s jinymi parametry?</b> Prepocita
    /// <see cref="EdgeAssociator"/> nad zaznamenanymi cykly koridoru — tentyz koridor, tataz poza,
    /// tataz mapa, meni se jen podlaha sigmy kurzu (<c>assocfloorhdg=</c>) a odstup od druheho
    /// kandidata (<c>assocmargin=</c>).
    ///
    /// <para><b>Nacpak to je:</b> na Robotouru 19. 9. 2026 zahodila nejednoznacnost ~37 %
    /// prolozenych koridoru a kurzova slozka chi-kvadratu mela p50 jen ~0,02 — podlaha 10° je po
    /// oprave magnetometru volna. Opakovat jizdu by A/B neslo (jine svetlo, jina poza, jina cesta
    /// mezi lidmi), prepocet nad temz zaznamem ano.</para>
    ///
    /// <para><b>Meridlo se nejdriv overuje proti zname odpovedi:</b> s parametry, se kterymi robot
    /// jel, musi prepocet vratit tytez verdikty a tatáz skore, jaka jsou ve zprave. Kdyz nesedi,
    /// je spatne rekonstrukce (sit, pocatek, kovariance) a zbytek se nema cist.</para>
    ///
    /// <para>⚠️ <b>Je to prepocet prvniho radu:</b> kdyby se prijalo vic merenii, sla by fuze
    /// jinudy a dalsi pozy by byly jine. Kovariance pozy se bere z nejblizsiho
    /// <see cref="RobotStateMsg"/> (zprava koridoru ji nenese); rozhoduje ale vetsinou podlaha.
    /// Sirkove brany za prirazenim (stav odhadu sirky) se neprehravaji — tiskne se tedy, kolik
    /// cyklu dostane hranu, ne kolik jich projde az do fuze.</para>
    ///
    /// <para>Pouziti: <c>ARBot.Analyze assocreplay zaznam.rec --map=OSM/x.osm [--floors=10,7,5,3]
    /// [--margins=4] [--maxedge=8] [--roadwidth=3]</c>. Vychozi <c>--maxedge=8</c> je hodnota
    /// z doby Robotouru (od 26. 9. 2026 je v kodu nekonecno).</para>
    /// </summary>
    public static class AssocReplayReport
    {
        private sealed class Cycle
        {
            public RoadCorridorMsg Msg;
            public RobotState Pose;
            public EdgeAssocResult Recorded;
            /// <summary>
            /// Cesty, ke kterym ma GPS fix nejvyse o <see cref="GpsToleranceM"/> dal nez k nejblizsi
            /// (nezavisla reference); <c>null</c> = fix neni.
            /// </summary>
            public HashSet<long> GpsWays;
            /// <summary>Kurz nad zemi z GPS [rad, matematicky]; NaN = robot stal nebo fix neni.</summary>
            public double GpsCourse = double.NaN;
        }

        /// <summary>
        /// Osa odchylena od kurzu z GPS o vic nez tolik je <b>jina cesta</b> (pricna ulice, odbocka),
        /// ne sum: sd kurzu z GPS za jizdy je ~4–5°, zatacky mesta 30° a vic.
        /// </summary>
        private const double WrongRoadDeg = 30.0;

        /// <summary>Pod touhle rychlosti je kurz z Doppleru sum [m/s].</summary>
        private const double MinCourseSpeedMps = 0.5;

        /// <summary>
        /// Kurz z GPS nejblizsi cyklu (do 0,5 s). U KRIZOVATKY je to rozhodujici reference: pricna
        /// ulice prochazi mistem, kde robot je, takze vzdalenost od primky ji neodlisi - smer ano.
        /// </summary>
        private static double NearestCourse(List<(DateTime T, double Course)> c, DateTime t)
        {
            if (c.Count == 0) return double.NaN;
            int lo = 0, hi = c.Count - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (c[mid].T < t) lo = mid + 1; else hi = mid; }
            int best = lo;
            if (lo > 0 && (t - c[lo - 1].T).Duration() < (c[lo].T - t).Duration()) best = lo - 1;
            return (c[best].T - t).Duration() > TimeSpan.FromSeconds(0.5) ? double.NaN : c[best].Course;
        }

        /// <summary>
        /// Odchylka smeru osy od kurzu z GPS [deg], jako PRIMKY (0–90°). Svetovy smer osy je
        /// kurz pozy + <see cref="RoadAxisMatch.HeadingRelRad"/>.
        /// </summary>
        private static double AxisVsCourseDeg(Cycle c, RoadAxisMatch ax)
            => double.IsNaN(c.GpsCourse) ? double.NaN
             : Math.Abs(Conversions.NormalizeHalfOrientation(c.Pose.Theta + ax.HeadingRelRad - c.GpsCourse)) * 180 / Math.PI;

        /// <summary>
        /// Tolerance GPS shody [m]. U krizovatky (a prave tam nejednoznacnost vznika) je „nejblizsi
        /// cesta" podle GPS nahoda mezi sousednimi useky - shoda podle jedineho id by trestala
        /// spravny vyber. Proto: vybrana cesta nesmi byt od GPS o vic nez tolik dal nez nejblizsi.
        /// </summary>
        private const double GpsToleranceM = 2.0;

        /// <summary>
        /// Cesty blizke platnemu GPS fixu do 0,5 s od cyklu. Nezavisla reference „po ktere ceste
        /// jedu": na fuzi ani na koridoru nezavisi. Hruba - GPS ve meste ma chybu metru, takze se
        /// ctou <b>podily</b> proti cyklum prijatym uz s parametry z jizdy, ne jednotlive cykly.
        /// </summary>
        private static HashSet<long> GpsWays(RoadNetwork net, List<(DateTime T, double Lat, double Lon)> gps, DateTime t)
        {
            if (gps.Count == 0) return null;
            int lo = 0, hi = gps.Count - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (gps[mid].T < t) lo = mid + 1; else hi = mid; }
            int best = lo;
            if (lo > 0 && (t - gps[lo - 1].T).Duration() < (gps[lo].T - t).Duration()) best = lo - 1;
            if ((gps[best].T - t).Duration() > TimeSpan.FromSeconds(0.5)) return null;
            var c = net.NearestEdges(new ARBot.Common.Coordinates.LLA(gps[best].Lat, gps[best].Lon), 8);
            if (c.Count == 0) return null;
            double dmin = c.Min(x => x.DistanceM);
            return new HashSet<long>(c.Where(x => x.DistanceM <= dmin + GpsToleranceM).Select(x => x.Edge.WayId));
        }

        public static void Run(RecordFile rec, string mapPath, double roadWidth, double maxEdgeM,
                               string floorsText, string marginsText)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
            {
                Console.Error.WriteLine("assocreplay: --map=<cesta.osm> je povinne (mapa, podle ktere robot jel).");
                return;
            }
            RoadNetwork net;
            using (var fs = File.OpenRead(mapPath))
            {
                var data = OsmXmlReader.Read(fs);
                data = NetworkIslands.Prune(data, TravelProfile.Robot(), out var ostrovy);
                Console.WriteLine($"mapa {Path.GetFileName(mapPath)}: {NetworkIslands.Describe(ostrovy)}");
                net = GraphBuilder.BuildNetwork(data, TravelProfile.Robot(), roadWidth);
            }

            MapMsg map = null;
            var msgs = new List<RoadCorridorMsg>();
            var gps = new List<(DateTime T, double Lat, double Lon)>();
            var course = new List<(DateTime T, double Course)>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName == "Map" && map == null) map = rec.Read(e) as MapMsg;
                else if (e.MsgName == "RoadCorridorMsg" && rec.Read(e) is RoadCorridorMsg m) msgs.Add(m);
                else if (e.MsgName == "GPSState" && rec.Read(e) is ARBot.Common.Devices.GPSState g && g.IsFixed)
                {
                    gps.Add((g.TimeStamp, g.Latitude, g.Longitude));
                    // Kurz nad zemi (Doppler) - na fuzi ani kompasu nezavisly, ale jen za jizdy.
                    if (g.DynamicOrientation.HasValue && (g.Speed ?? g.DynamicSpeed ?? 0) >= MinCourseSpeedMps)
                        course.Add((g.TimeStamp, g.DynamicOrientation.Value));
                }
            }
            gps.Sort((a, b) => a.T.CompareTo(b.T));
            course.Sort((a, b) => a.T.CompareTo(b.T));
            var origin = map?.BuildOrigin();
            if (origin == null) { Console.WriteLine("assocreplay: zaznam nema MapMsg - pocatek neni odkud vzit."); return; }
            if (msgs.Count == 0 || msgs.All(m => double.IsNaN(m.AssocChi2)))
            {
                Console.WriteLine("assocreplay: zaznam nema skore prirazeni (RoadCorridorMsg pred verzi 6).");
                return;
            }

            var poses = new PoseTrack(rec);
            var cycles = new List<Cycle>();
            foreach (var m in msgs)
            {
                if (!m.HasPose || m.SingleSide != 0) continue;   // jen oboustranny koridor
                var rec0 = Recorded((CorridorFixReason)m.FixReason);
                if (rec0 == null) continue;                      // do prirazeni nedosel
                var st = poses.Nearest(m.TimeStamp);
                cycles.Add(new Cycle
                {
                    Msg = m,
                    Recorded = rec0.Value,
                    Pose = new RobotState { X = m.PoseX, Y = m.PoseY, Theta = m.PoseTheta, Covariance = st?.Covariance },
                    GpsWays = GpsWays(net, gps, m.TimeStamp),
                    GpsCourse = NearestCourse(course, m.TimeStamp),
                });
            }
            Console.WriteLine($"cyklu koridoru {msgs.Count}, do prirazeni doslo {cycles.Count} (oboustranny koridor s pozou)");
            Console.WriteLine();
            if (cycles.Count == 0) return;

            // 1) Overeni meridla: parametry, se kterymi robot jel.
            // Parametry Z JIZDY, ne dnesni default (ten se meni - 27. 9. 2026 floorhdg 10 -> 5).
            var baseCfg = JizdaCfg(10, 4);
            var baseline = cycles.Select(c => Associate(net, origin, c, baseCfg, maxEdgeM)).ToList();
            int same = 0;
            var dChi = new Stats("|chi2 prepocet - zaznam|");
            for (int i = 0; i < cycles.Count; i++)
            {
                if (baseline[i].Result == cycles[i].Recorded) same++;
                if (!double.IsNaN(baseline[i].Chi2) && !double.IsNaN(cycles[i].Msg.AssocChi2))
                    dChi.Add(Math.Abs(baseline[i].Chi2 - cycles[i].Msg.AssocChi2));
            }
            Console.WriteLine("OVERENI MERIDLA (parametry z jizdy: floorhdg 10, floorlat 3, margin 4, veto 45, chi2max 9,21):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  shodny verdikt {0} z {1} ({2:F1} %)", same, cycles.Count, 100.0 * same / cycles.Count));
            Console.WriteLine("  " + dChi.Line());
            foreach (var g in cycles.Select((c, i) => (c.Recorded, New: baseline[i].Result))
                                    .Where(p => p.Recorded != p.New)
                                    .GroupBy(p => p).OrderByDescending(g => g.Count()).Take(6))
                Console.WriteLine($"  nesouhlas: zaznam {g.Key.Recorded,-11} -> prepocet {g.Key.New,-11} {g.Count(),6}");
            if (same < 0.95 * cycles.Count)
                Console.WriteLine("  POZOR: prepocet nesedi na zaznam - rekonstrukce je vadna, varianty nize necist.");
            var (refHit, refN) = GpsAgreement(cycles.Select((c, i) => (c, baseline[i])).Where(p => p.Item2.Ok));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  REFERENCE: prijate uz s parametry z jizdy - vybrana cesta do 2 m od nejblizsi GPS v {0:F1} % (n={1})",
                refN > 0 ? 100.0 * refHit / refN : double.NaN, refN));
            var refCourse = new Stats("");
            int refWrong = 0;
            for (int i = 0; i < cycles.Count; i++)
            {
                if (!baseline[i].Ok) continue;
                double d = AxisVsCourseDeg(cycles[i], baseline[i].Axis);
                if (double.IsNaN(d)) continue;
                refCourse.Add(d);
                if (d > WrongRoadDeg) refWrong++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  REFERENCE: |smer vybrane osy - kurz z GPS| p50 {0:F1}, p90 {1:F1} deg, nad {2:F0} deg {3:F1} % (n={4}, jen za jizdy)",
                refCourse.Median, refCourse.Percentile(90), WrongRoadDeg,
                refCourse.Count > 0 ? 100.0 * refWrong / refCourse.Count : double.NaN, refCourse.Count));
            Console.WriteLine();

            // 2) Varianty.
            var floors = Parse(floorsText, new[] { 10.0, 7, 5, 3 });
            var margins = Parse(marginsText, new[] { 4.0 });
            var flipReports = new List<(double Floor, double Margin, List<(int I, RoadAxisMatch Old, RoadAxisMatch New)> Flips)>();
            Console.WriteLine("VARIANTY (verdikty nad temiz cykly):");
            Console.WriteLine("  'nove'      = s parametry z jizdy NEPRIJATO, ted Ok;");
            Console.WriteLine("  'jiny vitez' = Ok, ale jina osa nez nejlepsi kandidat s parametry z jizdy (jina cesta");
            Console.WriteLine("                 nebo osa o > 0,5 m / 5 deg vedle) - podlaha tu PREKLOPILA poradi;");
            Console.WriteLine("  |dHdg|, |dLat| = nesouhlas koridoru s vybranou osou, jen u 'nove'.");
            Console.WriteLine("  nove GPS % = u 'nove' vybrana cesta do 2 m od nejblizsi GPS (srovnat s REFERENCE vys).");
            Console.WriteLine("  nove osa-kurz = u 'nove' |smer osy - kurz z GPS| p50/p90 [deg] a podil nad 30 deg (jina cesta).");
            Console.WriteLine("  floorhdg  margin      Ok   nejednozn.  nesedi   Ok %    nove  jiny vitez  nove GPS %   nove |dHdg| p50/p90 [deg]   nove |dLat| p50/p90 [m]   nove osa-kurz p50/p90, >30");
            foreach (double margin in margins)
                foreach (double floor in floors)
                {
                    var cfg = JizdaCfg(floor, margin);
                    int ok = 0, amb = 0, mis = 0, fresh = 0, changed = 0, gpsHit = 0, gpsN = 0, wrong = 0;
                    var dc = new Stats("");
                    var dh = new Stats(""); var dl = new Stats("");
                    var flips = new List<(int I, RoadAxisMatch Old, RoadAxisMatch New)>();
                    for (int i = 0; i < cycles.Count; i++)
                    {
                        var a = Associate(net, origin, cycles[i], cfg, maxEdgeM);
                        switch (a.Result)
                        {
                            case EdgeAssocResult.Ok:
                                ok++;
                                var b = baseline[i];
                                if (b.Axis.Found && !SameAxis(b.Axis, a.Axis)) { changed++; flips.Add((i, b.Axis, a.Axis)); }
                                if (!b.Ok)
                                {
                                    fresh++;
                                    if (cycles[i].GpsWays != null)
                                    {
                                        gpsN++;
                                        if (cycles[i].GpsWays.Contains(a.Axis.WayId)) gpsHit++;
                                    }
                                    var cor = cycles[i].Msg;
                                    dh.Add(Math.Abs(Conversions.NormalizeHalfOrientation(cor.DirectionRad - a.Axis.HeadingRelRad)) * 180 / Math.PI);
                                    dl.Add(Math.Abs(cor.Lateral - a.Axis.Lateral));
                                    double vc = AxisVsCourseDeg(cycles[i], a.Axis);
                                    if (!double.IsNaN(vc)) { dc.Add(vc); if (vc > WrongRoadDeg) wrong++; }
                                }
                                break;
                            case EdgeAssocResult.Ambiguous: amb++; break;
                            case EdgeAssocResult.NoCandidate: mis++; break;
                        }
                    }
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,6:F1}  {1,6:F1}  {2,6}  {3,9}  {4,7}  {5,6:F1}  {6,6}  {7,10}  {8,10:F1}          {9,6:F2} / {10,6:F2}              {11,6:F2} / {12,6:F2}        {13,5:F1} / {14,5:F1}, {15,5:F1} %",
                        floor, margin, ok, amb, mis, 100.0 * ok / cycles.Count, fresh, changed,
                        gpsN > 0 ? 100.0 * gpsHit / gpsN : double.NaN,
                        dh.Median, dh.Percentile(90), dl.Median, dl.Percentile(90),
                        dc.Median, dc.Percentile(90), dc.Count > 0 ? 100.0 * wrong / dc.Count : double.NaN));
                    if (flips.Count > 0) flipReports.Add((floor, margin, flips));
                }
            Console.WriteLine();
            Console.WriteLine("  Cist: vic Ok je zisk jen tehdy, kdyz 'jiny vitez' zustane u nuly - jinak podlaha");
            Console.WriteLine("  nerozsekla nejednoznacnost, ale PREHODILA cestu. Sirkove brany za prirazenim se");
            Console.WriteLine("  neprehravaji, do fuze jde mene nez 'Ok'.");
            Console.WriteLine();

            // 3) Zmeneny vitez: k lepsimu, nebo k horsimu?
            var gpsLocal = gps.Select(g => { var p = origin.ToLocal(g.Lat, g.Lon); return (g.T, (double)p.X, (double)p.Y); }).ToList();
            foreach (var (floor, margin, flips) in flipReports)
                FlipVerdicts(floor, margin, flips, cycles, gpsLocal);
        }

        /// <summary>Okno GPS drahy pro posouzeni zmeneneho viteze (± s).</summary>
        private const double FlipWindowSec = 5.0;

        /// <summary>O kolik musi byt jedna osa GPS draze bliz, aby rozhodla [m].</summary>
        private const double FlipDecisiveM = 1.0;

        /// <summary>O kolik musi jedna osa sedet na kurz z GPS lip, aby rozhodla [deg] (sd kurzu ~4–5°).</summary>
        private const double FlipDecisiveDeg = 5.0;

        /// <summary>
        /// <b>Zmeneny vitez — k lepsimu, nebo k horsimu?</b> Nezavisla reference je <b>GPS draha</b>
        /// v okne ±<see cref="FlipWindowSec"/> s: median vzdalenosti fixu od OSY stare a nove hrany
        /// (primka hrany v miste prumetu pozy). Jeden fix ma ve meste chybu metru, median pres
        /// ~100 fixu jizdy podel cesty uz rozhodne, kdyz se obe osy lisi o vic nez
        /// <see cref="FlipDecisiveM"/>. Sousedni cykly se slucuji do <b>epizod</b> (mezera nad 2 s),
        /// protoze jedno misto dava desitky cyklu za sebou a pocitat je zvlast by vazilo delku stani.
        /// </summary>
        private static void FlipVerdicts(double floor, double margin,
                                         List<(int I, RoadAxisMatch Old, RoadAxisMatch New)> flips,
                                         List<Cycle> cycles,
                                         List<(DateTime T, double X, double Y)> gps)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "ZMENENY VITEZ - floorhdg {0:F1}, margin {1:F1}: {2} cyklu. K lepsimu? (GPS draha +-{3:F0} s, rozhoduje rozdil nad {4:F1} m)",
                floor, margin, flips.Count, FlipWindowSec, FlipDecisiveM));

            var rows = new List<(DateTime T, double DOld, double DNew, int N, RoadAxisMatch Old, RoadAxisMatch New)>();
            foreach (var (i, oldAx, newAx) in flips)
            {
                var t = cycles[i].Msg.TimeStamp;
                var dOld = new List<double>(); var dNew = new List<double>();
                foreach (var g in gps)
                {
                    if (Math.Abs((g.T - t).TotalSeconds) > FlipWindowSec) continue;
                    dOld.Add(Math.Abs(oldAx.NormalX * (g.X - oldAx.AxisX) + oldAx.NormalY * (g.Y - oldAx.AxisY)));
                    dNew.Add(Math.Abs(newAx.NormalX * (g.X - newAx.AxisX) + newAx.NormalY * (g.Y - newAx.AxisY)));
                }
                rows.Add((t, Med(dOld), Med(dNew), dOld.Count, oldAx, newAx));
            }

            int better = 0, worse = 0, undecided = 0, noGps = 0;
            foreach (var r in rows)
            {
                if (r.N < 5) noGps++;
                else if (r.DNew < r.DOld - FlipDecisiveM) better++;
                else if (r.DNew > r.DOld + FlipDecisiveM) worse++;
                else undecided++;
            }
            Console.WriteLine($"  podle POLOHY (GPS draha od osy):   k lepsimu {better}, k horsimu {worse}, nerozhodnuto {undecided}, bez GPS {noGps}");

            // Podle SMERU: u krizovatky pricna ulice prochazi mistem, kde robot je, takze vzdalenost
            // ji neodlisi - rozhoduje, kterym smerem robot skutecne jel (kurz z GPS).
            int cb = 0, cw = 0, cu = 0, cn = 0;
            var courseOld = new Dictionary<int, double>(); var courseNew = new Dictionary<int, double>();
            for (int k = 0; k < flips.Count; k++)
            {
                var c = cycles[flips[k].I];
                double o = AxisVsCourseDeg(c, flips[k].Old), n = AxisVsCourseDeg(c, flips[k].New);
                courseOld[k] = o; courseNew[k] = n;
                if (double.IsNaN(o)) cn++;
                else if (n < o - FlipDecisiveDeg) cb++;
                else if (n > o + FlipDecisiveDeg) cw++;
                else cu++;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  podle SMERU (osa proti kurzu z GPS, rozhoduje nad {0:F0} deg): k lepsimu {1}, k horsimu {2}, nerozhodnuto {3}, stani/bez kurzu {4}",
                FlipDecisiveDeg, cb, cw, cu, cn));

            // Epizody: sousedni cykly (mezera do 2 s) = jedno misto.
            var eps = new List<List<int>>();
            for (int k = 0; k < rows.Count; k++)
            {
                if (k == 0 || (rows[k].T - rows[k - 1].T).TotalSeconds > 2) eps.Add(new List<int>());
                eps[eps.Count - 1].Add(k);
            }
            Console.WriteLine($"  epizod: {eps.Count}");
            Console.WriteLine("    cas       cyklu   stara cesta / nova cesta     GPS od stare / nove [m]  verdikt    osa-kurz GPS stare / nove [deg]  verdikt");
            foreach (var ep in eps.Take(25))
            {
                var first = rows[ep[0]];
                double o = Med(ep.Select(k => rows[k].DOld).Where(v => !double.IsNaN(v)).ToList());
                double n = Med(ep.Select(k => rows[k].DNew).Where(v => !double.IsNaN(v)).ToList());
                string verdikt = double.IsNaN(o) ? "bez GPS"
                               : n < o - FlipDecisiveM ? "LEPSI"
                               : n > o + FlipDecisiveM ? "HORSI" : "nerozh.";
                double co = Med(ep.Select(k => courseOld[k]).Where(v => !double.IsNaN(v)).ToList());
                double cnn = Med(ep.Select(k => courseNew[k]).Where(v => !double.IsNaN(v)).ToList());
                string vk = double.IsNaN(co) ? "stani"
                          : cnn < co - FlipDecisiveDeg ? "LEPSI"
                          : cnn > co + FlipDecisiveDeg ? "HORSI" : "nerozh.";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0:HH:mm:ss}  {1,5}   {2,10} / {3,-10}      {4,6:F2} / {5,6:F2}       {6,-8}   {7,6:F1} / {8,6:F1}              {9}",
                    first.T, ep.Count, first.Old.WayId, first.New.WayId, o, n, verdikt, co, cnn, vk));
            }
            if (eps.Count > 25) Console.WriteLine($"    ... a dalsich {eps.Count - 25} epizod");
            Console.WriteLine();
        }

        private static double Med(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            v.Sort();
            return v.Count % 2 == 1 ? v[v.Count / 2] : 0.5 * (v[v.Count / 2 - 1] + v[v.Count / 2]);
        }

        private static EdgeAssociation Associate(RoadNetwork net, ARBot.Common.Coordinates.GeoReference origin,
                                                 Cycle c, EdgeAssociationConfig cfg, double maxEdgeM)
        {
            var m = c.Msg;
            var corridor = new RoadCorridor
            {
                Reason = CorridorReason.Ok,
                Width = m.Width,
                Lateral = m.Lateral,
                DirectionRad = m.DirectionRad,
                SigmaLateral = m.SigmaLateral,
                SigmaDirectionRad = m.SigmaDirectionRad,
            };
            return EdgeAssociator.Associate(net, origin, c.Pose, corridor, cfg, maxEdgeM);
        }

        private static (int Hit, int N) GpsAgreement(IEnumerable<(Cycle C, EdgeAssociation A)> items)
        {
            int hit = 0, n = 0;
            foreach (var (c, a) in items)
            {
                if (c.GpsWays == null) continue;
                n++;
                if (c.GpsWays.Contains(a.Axis.WayId)) hit++;
            }
            return (hit, n);
        }

        /// <summary>Tatáz osa: tataz cesta, pricne do 0,5 m a smer do 5° (jako SameHypothesis).</summary>
        private static bool SameAxis(RoadAxisMatch a, RoadAxisMatch b)
            => a.WayId == b.WayId
            && Math.Abs(a.Lateral - b.Lateral) <= 0.5
            && Math.Abs(Conversions.NormalizeHalfOrientation(a.HeadingRelRad - b.HeadingRelRad)) <= 5 * Math.PI / 180;

        /// <summary>
        /// Nastaveni prirazeni, se kterym robot jel na Robotouru 19. 9. 2026 (log zaznamu:
        /// assock=4, assocveto=45, assocfloorlat=3, assocfloorhdg=10, assocchi2=9.21, assocmargin=4),
        /// s moznosti zmenit podlahu kurzu a odstup. VYSLOVNE, ne z dnesniho defaultu - jinak by
        /// se po zmene defaultu meridlo tise overovalo proti jinym parametrum, nez s jakymi se jelo.
        /// </summary>
        private static EdgeAssociationConfig JizdaCfg(double floorHdgDeg, double margin) => new EdgeAssociationConfig
        {
            Candidates = 4,
            VetoRad = 45 * Math.PI / 180,
            SigmaLateralFloorM = 3.0,
            SigmaHeadingFloorRad = floorHdgDeg * Math.PI / 180,
            Chi2Max = 9.21,
            Chi2Margin = margin,
        };

        /// <summary>Verdikt prirazeni podle zaznamenaneho duvodu; <c>null</c> = do prirazeni nedosel.</summary>
        private static EdgeAssocResult? Recorded(CorridorFixReason r) => r switch
        {
            CorridorFixReason.Ok => EdgeAssocResult.Ok,
            CorridorFixReason.WidthNotTrusted => EdgeAssocResult.Ok,
            CorridorFixReason.WidthDisagreement => EdgeAssocResult.Ok,
            CorridorFixReason.AmbiguousEdge => EdgeAssocResult.Ambiguous,
            CorridorFixReason.EdgeMismatch => EdgeAssocResult.NoCandidate,
            CorridorFixReason.EdgeTooFar => EdgeAssocResult.NoCandidate,
            CorridorFixReason.NoEdge => EdgeAssocResult.NoEdge,
            _ => null,
        };

        private static double[] Parse(string text, double[] fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            return text.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        }
    }
}
