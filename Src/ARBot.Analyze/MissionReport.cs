using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Common.Vision.Qr;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Mise Robotour ze záznamu: co automat dělal a proč se (ne)přečetl QR kód.</b>
    ///
    /// <para><b>Nač to je:</b> 19. 9. 2026 robot v misi Robotour kód nepřečetl a v záznamu nebyla
    /// jediná <see cref="QrCodeMsg"/>. To samo o sobě nerozliší tři úplně různé příčiny:
    /// (a) skener vůbec neběžel, protože automat nebyl ve fázi <c>Servicing</c> (stop nebyl
    /// držený, mise se nezarmovala…), (b) skener běžel, ale kód v obraze nebyl čitelný
    /// (vzdálenost, ostrost, kamera), (c) dekodér selhal. Zpráva <see cref="MissionMsg"/> nese
    /// fázi a stav stopu, snímky kamer jsou v záznamu — takže (a) jde přečíst z časové osy a (b)
    /// od (c) rozliší <b>prohnání skutečných snímků živým dekodérem</b>
    /// (<see cref="QrScanner.Process"/> je veřejný přesně kvůli tomu).</para>
    ///
    /// <para>Čte celé snímky jen ve <b>vybraných oknech</b> (servisní fáze; bez ní celý záznam
    /// vzorkovaně), takže na gigabajtovém záznamu netrvá minuty. <c>--png=&lt;prefix&gt;</c> uloží
    /// snímek z každého okna, aby člověk viděl, co kamera viděla.</para>
    /// </summary>
    public static class MissionReport
    {
        /// <param name="camera">Kamera, ze které se čte (<c>null</c> = výchozí skeneru, tj. pravá;
        /// prázdný řetězec = všechny).</param>
        /// <param name="png">Prefix cesty pro uložení snímků z oken; <c>null</c> = neukládat.</param>
        /// <param name="limit">Nejvýš tolik snímků dekodérem; 0 = bez omezení.</param>
        public static void Run(RecordFile rec, string camera, string png, int limit)
        {
            // ---------- 1. Časová osa mise ----------
            var mission = rec.ReadAll<MissionMsg>("MissionMsg").ToList();
            var qr = rec.ReadAll<QrCodeMsg>("QrCodeMsg").ToList();
            Console.WriteLine($"MissionMsg {mission.Count}, QrCodeMsg {qr.Count}");
            if (mission.Count == 0)
            {
                Console.WriteLine("Zaznam neobsahuje MissionMsg - mise Robotour v nem nebezela.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== 1. CASOVA OSA MISE (radek = zmena faze / stopu / priznaku) ===");
            Console.WriteLine("  cas       faze                  stanoviste  estop  kodNevidim  precteno/zamitnuto  poznamka");
            MissionMsg prev = null;
            var servicing = new List<(DateTime from, DateTime to)>();
            DateTime? servFrom = null;
            foreach (var m in mission)
            {
                bool change = prev == null
                              || m.Phase != prev.Phase || m.Stop != prev.Stop
                              || m.EmergencyStop != prev.EmergencyStop
                              || m.CodeNotSeen != prev.CodeNotSeen
                              // Dalsi zamitnuti TEHOZ kodu z TEHOZ duvodu radek nezaklada (kod se
                              // pod stopem cte 10x/s, takze by jich bylo sto na jedno ukazani).
                              || (m.CodesRead != prev.CodesRead
                                  && ((m.RejectReason ?? "") != (prev.RejectReason ?? "")
                                      || (m.RejectedCodeText ?? "") != (prev.RejectedCodeText ?? "")
                                      || m.CodesRead != m.CodesRejected))
                              || (m.AbortReason ?? "") != (prev.AbortReason ?? "");
                if (change)
                {
                    string pozn = "";
                    if (!string.IsNullOrEmpty(m.AbortReason)) pozn += $"ABORT: {m.AbortReason} ";
                    if (!string.IsNullOrEmpty(m.RejectReason)) pozn += $"zamitnuto: {m.RejectReason} '{m.RejectedCodeText}' ";
                    if (m.HasAcceptedCode) pozn += $"prijato: '{m.AcceptedCodeText}' ";
                    if (m.HasFixInfo && (RobotourPhase)m.Phase == RobotourPhase.ArmingAtDepot)
                        pozn += $"fix ok={m.FixQualityOk} sat={m.FixSatellites} hdop={m.FixHdop:F1} n={m.FixSamples} spread={m.FixSpreadM:F2}/{m.FixSpreadLimitM:F1} m ";
                    Console.WriteLine($"  {Cas(m.TimeStamp)}  {(RobotourPhase)m.Phase,-21} {(RobotourStop)m.Stop,-11} {(m.EmergencyStop ? "DRZEN" : "-"),-6} {(m.CodeNotSeen ? "ANO" : "-"),-11} {m.CodesRead}/{m.CodesRejected,-17} {pozn}");
                }

                bool serv = (RobotourPhase)m.Phase == RobotourPhase.Servicing;
                if (serv && servFrom == null) servFrom = m.TimeStamp;
                if (!serv && servFrom != null) { servicing.Add((servFrom.Value, m.TimeStamp)); servFrom = null; }
                prev = m;
            }
            if (servFrom != null) servicing.Add((servFrom.Value, prev.TimeStamp));
            Console.WriteLine($"  {Cas(prev.TimeStamp)}  (konec zaznamu) faze {(RobotourPhase)prev.Phase}, ubehlo {prev.ElapsedSec:F0} s, "
                              + $"timeouty {prev.Timeouts}");

            Console.WriteLine();
            var byPhase = mission.GroupBy(m => (RobotourPhase)m.Phase)
                                 .Select(g => (g.Key, g.Count()))
                                 .OrderByDescending(x => x.Item2);
            Console.WriteLine("  Zprav MissionMsg podle faze: "
                              + string.Join(", ", byPhase.Select(x => $"{x.Key} {x.Item2}")));
            Console.WriteLine($"  Servisnich oken (faze Servicing, skener ZAPNUTY): {servicing.Count}"
                              + (servicing.Count > 0
                                     ? ", celkem " + servicing.Sum(w => (w.to - w.from).TotalSeconds).ToString("F1") + " s"
                                     : string.Empty));
            foreach (var w in servicing)
                Console.WriteLine($"    {Cas(w.from)} - {Cas(w.to)}  ({(w.to - w.from).TotalSeconds:F1} s)");

            // ---------- 1b. Kvalita fixu během čekání v depu ----------
            // Proc: 19. 9. 2026 mise stala 158 s v ArmingAtDepot a stranka ukazovala „sigma 60-70 m"
            // - to je ale gpsposstd x HDOP (sigma pro FUZI), ne kriterium mise. Mise chce
            // HDOP <= MaxHdop a druzice >= MinSatellites NEPRERUSENE po DepotFixSec, a pak RMS
            // rozptyl <= MaxSpreadM. Tady je videt, o kolik a jak dlouho fix prah mijel.
            var arming = new List<(DateTime from, DateTime to)>();
            DateTime? armFrom = null; MissionMsg armPrev = null;
            foreach (var m in mission)
            {
                bool a = (RobotourPhase)m.Phase == RobotourPhase.ArmingAtDepot;
                if (a && armFrom == null) armFrom = m.TimeStamp;
                if (!a && armFrom != null) { arming.Add((armFrom.Value, m.TimeStamp)); armFrom = null; }
                armPrev = m;
            }
            if (armFrom != null) arming.Add((armFrom.Value, armPrev.TimeStamp));
            if (arming.Count > 0)
            {
                var rc = new RobotourConfig();
                Console.WriteLine();
                Console.WriteLine("=== 1b. KVALITA FIXU V ArmingAtDepot ===");
                Console.WriteLine($"  kriterium mise: fix + druzic >= {rc.MinSatellites} + HDOP <= {rc.MaxHdop:F1}, "
                                  + $"NEPRERUSENE {rc.DepotFixSec:F0} s, pak RMS rozptyl <= {rc.MaxSpreadM:F1} m "
                                  + "(sigma na strance = gpsposstd x HDOP, to kriterium NENI)");
                var gpsAll = rec.ReadAll<GPSState>("GPSState").ToList();
                foreach (var w in arming)
                {
                    var g = gpsAll.Where(x => x.TimeStamp >= w.from && x.TimeStamp <= w.to).ToList();
                    Console.WriteLine($"  okno {Cas(w.from)} - {Cas(w.to)} ({(w.to - w.from).TotalSeconds:F0} s): fixu {g.Count}");
                    if (g.Count == 0) continue;
                    var hd = g.Where(x => x.Hdop > 0).Select(x => x.Hdop).OrderBy(x => x).ToList();
                    var sat = g.Select(x => (double)x.NumberOfSatellites).OrderBy(x => x).ToList();
                    Console.WriteLine($"    fix: {g.Count(x => x.IsFixed)} z {g.Count}; druzic min/p50/max {sat.First():F0}/{Q(sat, .5):F0}/{sat.Last():F0}; "
                                      + $"HDOP min/p10/p50/p90/max {(hd.Count > 0 ? $"{hd.First():F2}/{Q(hd, .1):F2}/{Q(hd, .5):F2}/{Q(hd, .9):F2}/{hd.Last():F2}" : "-")}");
                    bool Ok(GPSState x, double maxHdop) => x.IsFixed && x.NumberOfSatellites >= rc.MinSatellites && x.Hdop > 0 && x.Hdop <= maxHdop;
                    foreach (double maxHdop in new[] { rc.MaxHdop, 2.5, 3.0, 4.0 })
                    {
                        int ok = g.Count(x => Ok(x, maxHdop));
                        // nejdelsi neprerusena serie vyhovujicich fixu [s]
                        double best = 0; DateTime? s0 = null; DateTime lastOk = default;
                        foreach (var x in g)
                        {
                            if (Ok(x, maxHdop)) { s0 ??= x.TimeStamp; lastOk = x.TimeStamp; best = Math.Max(best, (lastOk - s0.Value).TotalSeconds); }
                            else s0 = null;
                        }
                        Console.WriteLine($"    HDOP <= {maxHdop:F1}: vyhovuje {100.0 * ok / g.Count,5:F1} % fixu, nejdelsi neprerusena serie {best,6:F1} s"
                                          + (maxHdop == rc.MaxHdop ? "   <- dnesni prah" : "")
                                          + (best >= rc.DepotFixSec ? "  (okno by se naplnilo)" : ""));
                    }
                }
            }

            // ---------- 1c. Zamítnuté cíle proti mapě, kterou runtime SKUTEČNĚ měl ----------
            // Proc: 19. 9. 2026 mise zamitla kod „na cil nevede po siti zadna trasa", ackoli cil je
            // presne uzel mapy a offline (ARBot.Analyze route) je z depa dosazitelny. Zaznam nese
            // MapMsg - sit tak, jak ji runtime nacetl - a GlobalNavMsg s pozou, ze ktere Probe
            // pocital. Tady se obe veci slozi: je cil v TE mape, ve stejne komponente jako robot,
            // a kde robot podle sebe stal.
            var odmitnute = mission.Where(m => !string.IsNullOrEmpty(m.RejectReason) && !string.IsNullOrEmpty(m.RejectedCodeText))
                                   .GroupBy(m => m.RejectedCodeText).Select(g => g.First()).ToList();
            if (odmitnute.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("=== 1c. ZAMITNUTE CILE PROTI MAPE ZE ZAZNAMU ===");
                var map = rec.ReadAll<MapMsg>("Map").FirstOrDefault();
                var gn = rec.ReadAll<GlobalNavMsg>("GlobalNavMsg").ToList();
                if (map == null) Console.WriteLine("  Zaznam nema MapMsg - sit runtime se porovnat neda.");
                else
                {
                    var komp = KomponentyMapy(map);
                    int pocetKomp = komp.Distinct().Count();
                    Console.WriteLine($"  MapMsg '{map.Name}': uzlu {map.Nodes.Count}, hran {map.Edges.Count}, komponent souvislosti {pocetKomp}"
                                      + (pocetKomp > 1 ? "  <- SIT JE ROZPOJENA" : ""));
                    foreach (var m in odmitnute)
                    {
                        Console.WriteLine($"  {Cas(m.TimeStamp)} '{m.RejectedCodeText}': {m.RejectReason}");
                        if (!GeoUri(m.RejectedCodeText, out double tlat, out double tlon)) { Console.WriteLine("    (text neni geo:lat,lon)"); continue; }
                        int ti = NejblizsiUzel(map, tlat, tlon, out double td);
                        Console.WriteLine($"    cil: nejblizsi uzel mapy {(ti >= 0 ? map.Nodes[ti].Id.ToString() : "-")} ve {td:F1} m, komponenta #{(ti >= 0 ? komp[ti] : -1)}");
                        var g = gn.Where(x => x.TimeStamp <= m.TimeStamp).LastOrDefault() ?? gn.FirstOrDefault();
                        if (g == null) { Console.WriteLine("    robot: zadna GlobalNavMsg (poza pro Probe neznama)"); continue; }
                        int ri = NejblizsiUzel(map, g.LatDeg, g.LonDeg, out double rd);
                        Console.WriteLine($"    robot (GlobalNavMsg {Cas(g.TimeStamp)}, stav {(ARBot.Common.Maps.OsmNav.Navigation.GlobalNavStatus)g.Status}): "
                                          + $"{g.LatDeg:F6},{g.LonDeg:F6}, nejblizsi uzel {(ri >= 0 ? map.Nodes[ri].Id.ToString() : "-")} ve {rd:F1} m, komponenta #{(ri >= 0 ? komp[ri] : -1)}");
                        if (ti >= 0 && ri >= 0)
                            Console.WriteLine(komp[ti] == komp[ri]
                                ? "    -> tataz komponenta: NoRoute NENI z rozpojene site; overit orientaci hran / pozu (ARBot.Analyze route --from=<poza robota>)"
                                : "    -> RUZNE komponenty: NoRoute je dusledek ROZPOJENE SITE v mape, kterou runtime mel");
                    }
                }
            }

            // ---------- 2. Nouzové zastavení podle motorů ----------
            Console.WriteLine();
            Console.WriteLine("=== 2. NOUZOVE ZASTAVENI PODLE MOTORU (MotorStateBase) ===");
            var motors = rec.Index.Where(e => e.MsgName == "MotorStateBase").ToList();
            int total = 0, noMeas = 0, estop = 0;
            bool? last = null; DateTime lastT = default;
            var epizody = new List<(DateTime from, DateTime to)>();
            DateTime? eFrom = null;
            foreach (var e in motors)
            {
                if (!(rec.Read(e) is MotorStateBase ms)) continue;
                total++;
                if (!ms.HasMeasurement) { noMeas++; continue; }
                if (ms.IsEmergencyStop) estop++;
                if (ms.IsEmergencyStop && eFrom == null) eFrom = ms.TimeStamp;
                if (!ms.IsEmergencyStop && eFrom != null) { epizody.Add((eFrom.Value, ms.TimeStamp)); eFrom = null; }
                last = ms.IsEmergencyStop; lastT = ms.TimeStamp;
            }
            if (eFrom != null) epizody.Add((eFrom.Value, lastT));
            Console.WriteLine($"  ramcu {total}, bez mereni (fail-ramec) {noMeas}, se stopem {estop}");
            Console.WriteLine($"  epizod drzeneho stopu: {epizody.Count}");
            foreach (var w in epizody)
                Console.WriteLine($"    {Cas(w.from)} - {Cas(w.to)}  ({(w.to - w.from).TotalSeconds:F1} s)");
            if (epizody.Count == 0 && total > 0)
                Console.WriteLine("  -> Stop nebyl za cely zaznam ani jednou stisknuty: skener se nemohl zapnout "
                                  + "(zapina se JEN ve fazi Servicing pod drzenym stopem).");

            // ---------- 3. Snímky kamer dekodérem ----------
            Console.WriteLine();
            Console.WriteLine("=== 3. SNIMKY KAMER ZIVYM DEKODEREM ===");
            var cfg = new QrScannerConfig();
            if (camera != null) cfg.CameraName = camera;
            Console.WriteLine($"  kamera: {(string.IsNullOrWhiteSpace(cfg.CameraName) ? "(vsechny)" : cfg.CameraName)}, "
                              + $"downscale {cfg.Downscale}, dekoder ZXing (tryHarder)");

            var frames = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            var byName = frames.GroupBy(e => e.Name ?? "").Select(g => $"{g.Key} {g.Count()}");
            Console.WriteLine($"  CameraFrame v zaznamu: {frames.Count} ({string.Join(", ", byName)})");

            // Okna: servisni faze; kdyz zadna nebyla, cely zaznam (vzorkovane), aby bylo videt,
            // jestli kod nekde v obraze vubec byl.
            List<(DateTime from, DateTime to)> okna = servicing.Count > 0 ? servicing
                                                    : new List<(DateTime, DateTime)> { (DateTime.MinValue, DateTime.MaxValue) };
            if (servicing.Count == 0)
                Console.WriteLine("  Zadne servisni okno -> dekodujou se snimky z CELEHO zaznamu (skener v nem "
                                  + "nebezel; meri se jen, jestli by kod sel precist, kdyby bezel).");

            var scanner = new QrScanner(new ZXingQrDecoder(), cfg) { Enabled = true };
            int dekodovano = 0, sKodem = 0, bezObrazu = 0, jinaKamera = 0;
            var texty = new Dictionary<string, int>();
            var podleKamery = new Dictionary<string, (int dekodovano, int sKodem)>();
            var ulozene = new HashSet<string>();
            long budget = limit > 0 ? limit : long.MaxValue;
            // Vzorkovani: v okne kazdy snimek, mimo okna (cely zaznam) kazdy 10.
            int krok = servicing.Count > 0 ? 1 : 10;
            for (int i = 0; i < frames.Count && dekodovano < budget; i += krok)
            {
                var e = frames[i];
                var t = new DateTime(e.CaptureTicks);
                if (!okna.Any(w => t >= w.from && t <= w.to)) continue;
                if (!QrScanner.CameraMatches(e.Name, cfg.CameraName)) { jinaKamera++; continue; }

                if (!(rec.Read(e) is CameraFrame f)) continue;
                if (f.ImageRGB == null) { bezObrazu++; continue; }

                var res = scanner.Process(f);
                dekodovano++;
                podleKamery.TryGetValue(f.Name ?? "", out var pk);
                podleKamery[f.Name ?? ""] = (pk.dekodovano + 1, pk.sKodem + (res.Length > 0 ? 1 : 0));
                if (res.Length > 0)
                {
                    sKodem++;
                    foreach (var r in res)
                    {
                        texty.TryGetValue(r.Text, out int n); texty[r.Text] = n + 1;
                        if (sKodem <= 5)
                            Console.WriteLine($"  {Cas(f.TimeStamp)} {f.Name}: KOD '{r.Text}'");
                    }
                }

                if (!string.IsNullOrWhiteSpace(png))
                {
                    // Jeden snimek z kazdeho okna a kazde kamery (+ prvni s kodem).
                    string klic = $"{okna.FindIndex(w => t >= w.from && t <= w.to)}-{f.Name}-{(res.Length > 0 ? "kod" : "bez")}";
                    if (ulozene.Add(klic))
                    {
                        string cesta = $"{png}-{f.TimeStamp:HHmmss}-{f.Name}{(res.Length > 0 ? "-kod" : "")}.png";
                        File.WriteAllBytes(cesta, ImageMsg.EncodePng(f.ImageRGB));
                        Console.WriteLine($"  ulozen {cesta} ({f.ImageRGB.Width}x{f.ImageRGB.Height})");
                    }
                }
            }
            Console.WriteLine($"  dekodovano snimku: {dekodovano}, s kodem: {sKodem}, bez obrazu RGB: {bezObrazu}, "
                              + $"jina kamera (preskoceno): {jinaKamera}");
            foreach (var kv in texty.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"    '{kv.Key}' x{kv.Value}");
            foreach (var kv in podleKamery)
                Console.WriteLine($"    {kv.Key}: dekodovano {kv.Value.dekodovano}, s kodem {kv.Value.sKodem}");

            // Past z 19. 9. 2026: nastavene jmeno „Right", snimky „Right 740112071021". Kdyz se
            // k nastaveni nehodi ZADNE jmeno v zaznamu, je to sama o sobe odpoved.
            if (!string.IsNullOrWhiteSpace(cfg.CameraName)
                && !frames.Any(e => QrScanner.CameraMatches(e.Name, cfg.CameraName)))
                Console.WriteLine($"  !! K nastavene kamere '{cfg.CameraName}' se nehodi jmeno ZADNEHO snimku v zaznamu "
                                  + "-> skener by za behu nedostal nic, at je kod v obraze nebo ne.");

            // ---------- 4. Verdikt ----------
            Console.WriteLine();
            Console.WriteLine("=== 4. VERDIKT ===");
            if (servicing.Count == 0)
            {
                bool armed = mission.Any(m => (RobotourPhase)m.Phase != RobotourPhase.Idle
                                              && (RobotourPhase)m.Phase != RobotourPhase.ArmingAtDepot);
                if (!armed)
                    Console.WriteLine("  Mise se nedostala pres ArmingAtDepot (ceka na kvalitni fix v depu) -> skener "
                                      + "se nikdy nezapnul. Kod se necetl, protoze se NECETLO NIC; kamera za to nemuze.");
                else if (epizody.Count == 0)
                    Console.WriteLine("  Mise cekala na stisk nouzoveho zastaveni (AwaitingEStop), ale stop podle motoru "
                                      + "nebyl za cely zaznam drzeny -> skener se nikdy nezapnul.");
                else
                    Console.WriteLine("  Stop byl drzeny, ale automat nebyl ve fazi AwaitingEStop v tu chvili "
                                      + "(viz casova osa) -> servisni okno se neotevrelo.");
                if (sKodem > 0)
                    Console.WriteLine($"  Pritom kod v obraze BYL ({sKodem} snimku) - kdyby skener bezel, precetl by ho.");
            }
            else if (sKodem == 0)
                Console.WriteLine($"  Skener bezel {servicing.Sum(w => (w.to - w.from).TotalSeconds):F0} s, ale v zadnem z "
                                  + $"{dekodovano} snimku {cfg.CameraName} kod nebyl citelny -> podivej se na ulozene PNG "
                                  + "(--png=): je kod v zaberu, je ostry, je dost velky?");
            else if (qr.Count == 0)
                Console.WriteLine($"  Offline dekoder kod cte ({sKodem} snimku), ale za behu zadna QrCodeMsg nevznikla -> "
                                  + "vada je v behu (skener nedostal snimky nebo spadl), ne v obraze.");
            else
                Console.WriteLine($"  Kod se cetl ({qr.Count} QrCodeMsg); podivej se na duvody zamitnuti v casove ose.");
        }

        private static string Cas(DateTime t) => t.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture);

        /// <summary>Komponenty souvislosti (neorientovane) nad hranami <see cref="MapMsg"/>; index uzlu -> id komponenty.</summary>
        private static int[] KomponentyMapy(MapMsg map)
        {
            var adj = new List<int>[map.Nodes.Count];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
            foreach (var e in map.Edges)
                if (e.From >= 0 && e.From < adj.Length && e.To >= 0 && e.To < adj.Length)
                { adj[e.From].Add(e.To); adj[e.To].Add(e.From); }
            var comp = new int[map.Nodes.Count];
            int id = 0;
            for (int s = 0; s < comp.Length; s++)
            {
                if (comp[s] != 0) continue;
                id++;
                var stack = new Stack<int>(); stack.Push(s); comp[s] = id;
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    foreach (int v in adj[u]) if (comp[v] == 0) { comp[v] = id; stack.Push(v); }
                }
            }
            return comp;
        }

        /// <summary>Index nejblizsiho uzlu mapy k bodu (stupne); vzdalenost v metrech.</summary>
        private static int NejblizsiUzel(MapMsg map, double latDeg, double lonDeg, out double distM)
        {
            int best = -1; distM = double.PositiveInfinity;
            double cos = Math.Cos(latDeg * Math.PI / 180);
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                double dy = (map.Nodes[i].LatDeg - latDeg) * 111320;
                double dx = (map.Nodes[i].LonDeg - lonDeg) * 111320 * cos;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < distM) { distM = d; best = i; }
            }
            return best;
        }

        /// <summary><c>geo:lat,lon[...]</c> -> stupne. Tolerantni parser jen pro rozbor, ne pro misi.</summary>
        private static bool GeoUri(string text, out double lat, out double lon)
        {
            lat = lon = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.Trim();
            if (s.StartsWith("geo:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(4);
            int q = s.IndexOfAny(new[] { ';', '?' }); if (q >= 0) s = s.Substring(0, q);
            var p = s.Split(',');
            return p.Length >= 2
                   && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                   && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon);
        }

        /// <summary>Kvantil setrideneho seznamu (nejblizsi prvek).</summary>
        private static double Q(List<double> sorted, double q)
            => sorted.Count == 0 ? double.NaN
                                 : sorted[Math.Min(sorted.Count - 1, Math.Max(0, (int)Math.Round(q * (sorted.Count - 1))))];
    }
}
