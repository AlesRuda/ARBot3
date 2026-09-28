using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;
using ARBot.Common.Runtime;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Offline A/B hranove lokalizace nad JEDNOU jizdou:</b> prehraje fuzi ze zaznamenanych
    /// senzoru dvakrat — jednou S korekcemi z koridoru, jednou BEZ nich — a obe trajektorie
    /// porovna nezavislymi meritky (odstup od site cest, rozdil od GPS, shoda pri opakovanem
    /// pruchodu). Nahrada za A/B dvema jizdami (<c>corridorsend=false</c> po teze trati), ktere
    /// srovnatelne nejsou: jine svetlo, jina poza, jiny provoz.
    ///
    /// <para><b>Jak:</b> ucinna konfigurace se vycte z logu zaznamu (Info), fuze se sklada jako
    /// v <c>ARBotRuntime.WireRun</c> (FusionConfig + <see cref="DefaultMeasurementMapper"/> +
    /// <see cref="AsyncFusionEngine"/>) a krmi se <b>v poradi indexu</b> (= poradi prichodu do
    /// zaznamu) vsemi <see cref="SensorStateBase"/> zpravami. Koridor se bere ze zprav
    /// <see cref="RoadCorridorMsg"/> (proložení v ramci robotu, obe varianty), ale
    /// <b>prirazeni k hrane a merenie do fuze se PREPOCITAVA proti prehravane poze v case
    /// snimku</b> — kopie logiky <c>CorridorLocalizer.Process / ProcessSingleEdge / Send</c>
    /// (stupen bere <c>CameraFrame</c>, ne hotovy koridor, a produkcni kod se nemeni).</para>
    ///
    /// <para><b>Aproximace:</b> (1) kompenzace pohybu mezi snimky obou kamer uz je v prolozeni
    /// ze zaznamu, tedy s RELATIVNIM pohybem z puvodni fuze (rozdil variant na desetinach sekundy
    /// zanedbatelny); (2) poradi zprav je poradi zapisu do zaznamu, ne presne poradi, v jakem
    /// je videl runtime; (3) dotaz na pozu (RobotStateMsg, koridor) se deje v miste zpravy
    /// v indexu; (4) u mise Robotour se inicializace polohy v depu emuluje ze zpravy
    /// <see cref="MissionMsg"/> (poloha depa, sigma = max(rozptyl fixu, 0,3 m)).</para>
    ///
    /// <para>Pouziti: <c>ARBot.Analyze fusionreplay zaznam.rec [--map=OSM/x.osm]
    /// [--maxedge=8] [--revisit=60]</c>. <c>--maxedge</c> vychozi podle data binarky v logu
    /// (8 m do 25. 9. 2026 vcetne, pak nekonecno).</para>
    /// </summary>
    public static class FusionReplayReport
    {
        private struct Sample
        {
            public DateTime T;
            public double X, Y, Th, V;
            /// <summary>Sigma polohy PODEL kurzu z kovariance filtru [m]; NaN = neni.</summary>
            public double SigmaAlong;
        }

        private sealed class Emitted
        {
            public DateTime T;
            public bool Heading;
            public double Z, Sigma;
        }

        /// <summary>Jedna varianta prehrani.</summary>
        private sealed class Variant
        {
            public string Name;
            public bool Send;
            public AsyncFusionEngine Engine;
            public DefaultMeasurementMapper Mapper;
            public CorridorReplay Corridor;
            public readonly List<Sample> Samples = new List<Sample>();
            public readonly List<(DateTime T, double X, double Y)> DepotInits = new List<(DateTime, double, double)>();
        }

        // ------------------------------------------------------------------ konfigurace z logu

        /// <summary>Ucinna konfigurace z logu: klic -&gt; (hodnota, puvod).</summary>
        private static Dictionary<string, (string Value, string Origin)> ReadConfig(RecordFile rec, out string version)
        {
            var cfg = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            version = null;
            var rx = new Regex(@"^\s+([A-Za-z_0-9]+)=(.*?)\s+\((default|profil|prikazova radka|zvoleno za behu)\)\s*$");
            bool inBlock = false;
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "Info") continue;
                if (!(rec.Read(e) is Info info)) continue;
                string t = info.Message ?? string.Empty;
                if (version == null && t.StartsWith("ARBot verze:")) version = t;
                if (t.StartsWith("Konfigurace (ucinne hodnoty")) { inBlock = true; continue; }
                if (!inBlock) continue;
                var m = rx.Match(t);
                if (!m.Success) { if (cfg.Count > 0) break; continue; }
                if (!cfg.ContainsKey(m.Groups[1].Value))
                    cfg[m.Groups[1].Value] = (m.Groups[2].Value.Trim(), m.Groups[3].Value);
            }
            return cfg;
        }

        private static string Get(Dictionary<string, (string Value, string Origin)> c, string key, string fallback)
            => c.TryGetValue(key, out var v) && v.Value != "(nenastaveno)" ? v.Value : fallback;

        private static double GetD(Dictionary<string, (string Value, string Origin)> c, string key, double fallback)
            => double.TryParse(Get(c, key, null), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;

        private static bool GetB(Dictionary<string, (string Value, string Origin)> c, string key, bool fallback)
            => bool.TryParse(Get(c, key, null), out bool b) ? b : fallback;

        /// <summary>Nastaveno mimo default? (Runtime prenasi do FusionConfig jen <c>IsSet</c>.)</summary>
        private static bool IsSet(Dictionary<string, (string Value, string Origin)> c, string key)
            => c.TryGetValue(key, out var v) && v.Origin != "default";

        /// <summary>FusionConfig presne jako <c>ARBotRuntime.ApplyGpsQualityParams</c> + <c>ApplyImuHeadingParams</c>.</summary>
        private static FusionConfig BuildFusionConfig(Dictionary<string, (string Value, string Origin)> c, GeoReference origin)
        {
            var f = new FusionConfig();
            if (IsSet(c, "gpsminsat")) f.GpsMinSatellites = (int)Math.Round(GetD(c, "gpsminsat", f.GpsMinSatellites));
            if (IsSet(c, "gpsmaxdop")) f.GpsMaxDop = GetD(c, "gpsmaxdop", f.GpsMaxDop);
            if (IsSet(c, "gpsposstd")) f.GpsPosStd = GetD(c, "gpsposstd", f.GpsPosStd);
            if (IsSet(c, "gpsdopsigma")) f.GpsScaleStdByDop = GetB(c, "gpsdopsigma", f.GpsScaleStdByDop);
            if (IsSet(c, "imuheadingstd")) f.CompassHeadingStdFloor = Conversions.Deg2Rad(GetD(c, "imuheadingstd", 5));
            if (IsSet(c, "imuheadinghz"))
            {
                double hz = GetD(c, "imuheadinghz", 1);
                f.CompassHeadingMinPeriodSec = hz > 0 ? 1.0 / hz : 0.0;
            }
            f.GeoReference = origin;
            return f;
        }

        /// <summary>CorridorLocalizerConfig presne jako v <c>ARBotRuntime.WireRun</c> (hodnoty z logu).</summary>
        private static CorridorLocalizerConfig BuildCorridorConfig(Dictionary<string, (string Value, string Origin)> c,
                                                                  bool send, double maxEdgeM)
        {
            double hz = GetD(c, "corridorhz", 0);
            var k = new CorridorLocalizerConfig
            {
                SendCorrections = send,
                SigmaLateralExtraM = GetD(c, "corridorstd", 0),
                SigmaHeadingExtraRad = Conversions.Deg2Rad(GetD(c, "corridorheadingstd", 0)),
                MinSendPeriodSec = hz > 0 ? 1.0 / hz : 0,
                SlewRateMps = GetD(c, "corridorslew", 0),
                SlewRateHeadingRadPerSec = Conversions.Deg2Rad(GetD(c, "corridorheadingslew", 0)),
                MaxEdgeDistanceM = maxEdgeM,
            };
            k.Association = new EdgeAssociationConfig
            {
                Enabled = GetB(c, "assoc", true),
                Candidates = (int)GetD(c, "assock", 4),
                VetoRad = Conversions.Deg2Rad(GetD(c, "assocveto", 45)),
                SigmaLateralFloorM = GetD(c, "assocfloorlat", 3),
                SigmaHeadingFloorRad = Conversions.Deg2Rad(GetD(c, "assocfloorhdg", 10)),
                Chi2Max = GetD(c, "assocchi2", 9.21),
                Chi2Margin = GetD(c, "assocmargin", 4),
            };
            k.Corridor.SingleEdge = GetB(c, "corridorsingle", false);
            k.SingleEdgeWidthStdM = GetD(c, "corridorsinglewidthstd", 1.0);
            return k;
        }

        // ------------------------------------------------------------------ koridor

        /// <summary>
        /// Kopie mapove poloviny <c>CorridorLocalizer</c> (Process za <c>CorridorSource</c>,
        /// ProcessSingleEdge, Send) nad koridorem ze zpravy. Drzi si vlastni odhad sirek
        /// a stav skrceni/limitu kroku — jako stupen v runtime.
        /// </summary>
        private sealed class CorridorReplay
        {
            private readonly RoadNetwork net;
            private readonly GeoReference origin;
            private readonly CorridorLocalizerConfig cfg;
            private readonly RoadWidthEstimator widths;
            private DateTime posledniOdeslani, posledniLimitCas;

            public readonly Dictionary<CorridorFixReason, int> Reasons = new Dictionary<CorridorFixReason, int>();
            public readonly List<Emitted> Out = new List<Emitted>();
            public readonly Stats PoseVsMsg = new Stats("poza pri snimku vs zprava [m]");
            public readonly Stats HdgVsMsg = new Stats("kurz pri snimku vs zprava [deg]");
            /// <summary>|kamera − mapa| pricne u cyklu, ktere dostaly hranu (Ok) [m].</summary>
            public readonly Stats LatDisagree = new Stats("|kamera - mapa| pricne [m]");
            public int Usable, UsableSingle, ReasonSame, ReasonCompared, Throttled;
            public readonly Dictionary<(CorridorFixReason Rec, CorridorFixReason New), int> ReasonDiff =
                new Dictionary<(CorridorFixReason, CorridorFixReason), int>();

            /// <summary>Posilat pricnou korekci (rozbor: varianta „jen kurz" ji vypne).</summary>
            public bool SendLateral = true;

            public CorridorReplay(RoadNetwork net, GeoReference origin, CorridorLocalizerConfig cfg)
            {
                this.net = net; this.origin = origin; this.cfg = cfg;
                widths = new RoadWidthEstimator(cfg.WidthEstimator);
            }

            private void Count(CorridorFixReason r, RoadCorridorMsg m)
            {
                Reasons.TryGetValue(r, out int n); Reasons[r] = n + 1;
                var rec = (CorridorFixReason)m.FixReason;
                ReasonCompared++;
                if (rec == r) ReasonSame++;
                else { ReasonDiff.TryGetValue((rec, r), out int d); ReasonDiff[(rec, r)] = d + 1; }
            }

            public void Process(RoadCorridorMsg m, AsyncFusionEngine engine)
            {
                var rr = (CorridorFixReason)m.FixReason;
                if (rr == CorridorFixReason.NoPair || rr == CorridorFixReason.NoCorridor) return;
                bool two = m.CorridorReason == (byte)CorridorReason.Ok;
                bool single = !two && m.SingleSide != 0;
                if (!two && !single) return;   // NoPose z kompenzace apod. - koridor neni

                RoadCorridor corridor;
                if (two)
                {
                    corridor = new RoadCorridor
                    {
                        Reason = CorridorReason.Ok, Width = m.Width, Lateral = m.Lateral,
                        DirectionRad = m.DirectionRad, SigmaLateral = m.SigmaLateral,
                        SigmaDirectionRad = m.SigmaDirectionRad,
                    };
                    // CorridorSource: robot musi lezet uvnitr koridoru (na poze nezavisle).
                    if (Math.Abs(corridor.Lateral) > corridor.Width / 2 + cfg.MaxOutsideCorridorM)
                    {
                        Count(CorridorFixReason.OutsideCorridor, m);
                        return;
                    }
                }
                else
                {
                    corridor = new RoadCorridor
                    {
                        Reason = (CorridorReason)m.CorridorReason,
                        DirectionRad = m.DirectionRad, SigmaDirectionRad = m.SigmaDirectionRad,
                        SingleSide = (CorridorSide)m.SingleSide, EdgeOffset = m.EdgeOffset, EdgeSigma = m.EdgeSigma,
                    };
                    if (corridor.Reason == CorridorReason.Ok) corridor.Reason = CorridorReason.OneSideOnly;
                }
                Usable++;
                if (single) UsableSingle++;

                var pose = engine.GetStateAt(m.TimeStamp);
                if (pose == null) { Count(CorridorFixReason.NoPose, m); return; }
                if (m.HasPose)
                {
                    PoseVsMsg.Add(Math.Sqrt(Sq(pose.X - m.PoseX) + Sq(pose.Y - m.PoseY)));
                    HdgVsMsg.Add(Math.Abs(Conversions.NormalizeOrientation(pose.Theta - m.PoseTheta)) * 180 / Math.PI);
                }

                var assoc = EdgeAssociator.Associate(net, origin, pose, corridor, cfg.Association,
                                                     cfg.MaxEdgeDistanceM, single ? SingleEdgeWidth : null);
                if (assoc.Result != EdgeAssocResult.Ok)
                {
                    var a0 = assoc.Axis;
                    Count(assoc.Result switch
                    {
                        EdgeAssocResult.Ambiguous => CorridorFixReason.AmbiguousEdge,
                        EdgeAssocResult.NoCandidate => a0.Found && a0.DistanceM > cfg.MaxEdgeDistanceM
                                                       ? CorridorFixReason.EdgeTooFar : CorridorFixReason.EdgeMismatch,
                        _ => CorridorFixReason.NoEdge,
                    }, m);
                    return;
                }
                var axis = assoc.Axis;
                double lateral, sigmaLat;

                if (two)
                {
                    widths.Add(axis.WayId, corridor.Width);
                    bool trusted = widths.TryGetWidth(axis.WayId, out double estimate);
                    double mapW = trusted ? estimate : axis.WidthM;
                    if (!trusted) { Count(CorridorFixReason.WidthNotTrusted, m); return; }
                    if (Math.Abs(corridor.Width - mapW) > cfg.MaxWidthDisagreementM)
                    {
                        Count(CorridorFixReason.WidthDisagreement, m);
                        return;
                    }
                    lateral = corridor.Lateral;
                    sigmaLat = corridor.SigmaLateral;
                }
                else
                {
                    var (w, sw) = SingleEdgeWidth(axis);
                    lateral = corridor.SingleEdgeLateral(w);
                    sigmaLat = Math.Sqrt(corridor.EdgeSigma * corridor.EdgeSigma + sw * sw / 4);
                    if (Math.Abs(lateral) > w / 2 + cfg.MaxOutsideCorridorM + sw)
                    {
                        Count(CorridorFixReason.OutsideCorridor, m);
                        return;
                    }
                }

                Count(CorridorFixReason.Ok, m);
                LatDisagree.Add(Math.Abs(lateral - axis.Lateral));
                if (cfg.SendCorrections) Send(engine, m.TimeStamp, pose.Theta, axis, corridor, lateral, sigmaLat);
            }

            private (double WidthM, double StdM) SingleEdgeWidth(RoadAxisMatch axis)
            {
                if (widths.TryGetWidth(axis.WayId, out double learned))
                {
                    double mad = widths.DispersionOf(axis.WayId);
                    double std = double.IsNaN(mad) ? cfg.SingleEdgeLearnedWidthStdFloorM
                                                   : Math.Max(cfg.SingleEdgeLearnedWidthStdFloorM, mad);
                    return (learned, std);
                }
                return (axis.WidthM, cfg.SingleEdgeWidthStdM);
            }

            private void Send(AsyncFusionEngine engine, DateTime t, double poseTheta, RoadAxisMatch a,
                              RoadCorridor c, double lateral, double sigmaLateral)
            {
                if (!VydatMerenie(t)) { Throttled++; return; }
                double gate = Gating.ChiSquareThreshold(1);
                double dt = LimitDt(t);
                double? maxLat = cfg.SlewRateMps > 0 ? cfg.SlewRateMps * dt : (double?)null;
                double? maxHdg = cfg.SlewRateHeadingRadPerSec > 0 ? cfg.SlewRateHeadingRadPerSec * dt : (double?)null;
                posledniLimitCas = t;

                if (SendLateral)
                {
                    double value = a.NormalX * a.AxisX + a.NormalY * a.AxisY + lateral;
                    double sLat = Nafoukni(sigmaLateral, cfg.SigmaLateralExtraM);
                    engine.Enqueue(new AxisOffsetMeasurement(a.NormalX, a.NormalY, value, sLat, t, cfg.MeasurementSource)
                    { GateThreshold = gate, GateMode = cfg.GateMode, MaxStep = maxLat });
                    Out.Add(new Emitted { T = t, Heading = false, Z = value, Sigma = sLat });
                }

                if (cfg.SendHeading)
                {
                    double d = Conversions.NormalizeHalfOrientation(a.HeadingRelRad - c.DirectionRad);
                    double heading = Conversions.NormalizePrimaryOrientation(poseTheta, poseTheta + d);
                    double sH = Nafoukni(c.SigmaDirectionRad, cfg.SigmaHeadingExtraRad);
                    engine.Enqueue(new HeadingMeasurement(heading, sH, t, cfg.MeasurementSource)
                    { GateThreshold = gate, GateMode = cfg.GateMode, MaxStep = maxHdg });
                    Out.Add(new Emitted { T = t, Heading = true, Z = heading, Sigma = sH });
                }
            }

            private double LimitDt(DateTime t)
            {
                double cap = Math.Max(cfg.SlewDtCapSec, cfg.SlewDtFloorSec);
                if (posledniLimitCas == default || t < posledniLimitCas) return cap;
                double dt = (t - posledniLimitCas).TotalSeconds;
                return Math.Min(cap, Math.Max(cfg.SlewDtFloorSec, dt));
            }

            private static double Nafoukni(double sigma, double prirazek)
                => prirazek > 0 ? Math.Sqrt(sigma * sigma + prirazek * prirazek) : sigma;

            private bool VydatMerenie(DateTime t)
            {
                double perioda = cfg.MinSendPeriodSec;
                if (!(perioda > 0)) return true;
                if (posledniOdeslani == default || t < posledniOdeslani) { posledniOdeslani = t; return true; }
                if ((t - posledniOdeslani).TotalSeconds + 1e-9 < perioda) return false;
                posledniOdeslani = t;
                return true;
            }
        }

        private static double Sq(double x) => x * x;

        // ------------------------------------------------------------------ hlavni beh

        public static void Run(RecordFile rec, string mapOverride, double maxEdgeArg, double revisitSec)
        {
            var c = ReadConfig(rec, out string version);
            if (c.Count == 0)
            {
                Console.WriteLine("fusionreplay: v logu zaznamu neni blok ucinne konfigurace - neni podle ceho skladat fuzi.");
                return;
            }
            Console.WriteLine(version ?? "(verze binarky v logu neni)");

            // Limit odstupu hrany: 8 m do buildu 25. 9. 2026 vcetne (d193c12 ho 26. 9. vypnul).
            double maxEdge = maxEdgeArg;
            if (double.IsNaN(maxEdge))
            {
                var mb = version == null ? null : Regex.Match(version, @"build (\d{4}-\d{2}-\d{2})");
                maxEdge = mb != null && mb.Success
                          && string.CompareOrdinal(mb.Groups[1].Value, "2026-09-26") < 0 ? 8.0 : double.PositiveInfinity;
            }

            string mapPath = mapOverride ?? Get(c, "map", null);
            if (mapPath == null || !File.Exists(mapPath))
            {
                Console.WriteLine($"fusionreplay: mapa '{mapPath}' neexistuje (zadej --map=).");
                return;
            }
            double roadWidth = GetD(c, "roadwidth", 3);
            RoadNetwork net;
            using (var fs = File.OpenRead(mapPath))
            {
                var data = OsmXmlReader.Read(fs);
                // mapprune= vznikl 19. 9. 2026; binarka bez nej v logu ostrovy nezahazovala.
                if (GetB(c, "mapprune", c.ContainsKey("mapprune")))
                    data = NetworkIslands.Prune(data, TravelProfile.Robot(), out var ostrovy);
                net = GraphBuilder.BuildNetwork(data, TravelProfile.Robot(), roadWidth);
            }

            // Pocatek: ze zaznamu (MapMsg) - tim se kreslily a pocitaly vsechny lokalni souradnice.
            MapMsg mapMsg = null;
            foreach (var e in rec.Index) if (e.MsgName == "Map") { mapMsg = rec.Read(e) as MapMsg; break; }
            var origin = mapMsg?.BuildOrigin() ?? net.ToLogMessage().BuildOrigin();
            var originNet = net.ToLogMessage().BuildOrigin();
            var dO = originNet.ToLocal(origin.Origin);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "mapa {0}: {1} hran; pocatek ze zaznamu {2}, od pocatku nactene mapy {3:F3} m",
                mapPath, net.Edges.Count, mapMsg != null ? "ano" : "NE (z mapy)", Math.Sqrt(dO.X * dO.X + dO.Y * dO.Y)));

            Console.WriteLine();
            Console.WriteLine("KONFIGURACE Z LOGU (co se pouzilo):");
            foreach (var k in new[] { "gpsminsat", "gpsmaxdop", "gpsposstd", "gpsdopsigma", "imuheadingstd", "imuheadinghz",
                                      "corridor", "corridorsend", "corridorstd", "corridorheadingstd", "corridorhz",
                                      "corridorslew", "corridorheadingslew", "corridorsingle", "corridorsinglewidthstd",
                                      "assoc", "assock", "assocveto", "assocfloorlat", "assocfloorhdg", "assocchi2",
                                      "assocmargin", "mapcorr", "roadwidth", "mapprune", "mission", "start" })
                Console.WriteLine($"  {k}={Get(c, k, "(v logu neni)")}" + (c.TryGetValue(k, out var v) ? $"  ({v.Origin})" : ""));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  MaxEdgeDistanceM={0} (podle data binarky / --maxedge)", maxEdge));
            if (GetB(c, "mapcorr", false)) Console.WriteLine("  POZOR: mapcorr=true - korelace s mapou se NEPREHRAVA.");
            if (Get(c, "start", null) != null) Console.WriteLine("  POZOR: start= je nastaveno - inicializace pozy se NEPREHRAVA.");
            if (!GetB(c, "corridor", false) || !GetB(c, "corridorsend", false))
                Console.WriteLine("  POZOR: v jizde korekce z koridoru do fuze NESLY - varianta S je protifakticka.");
            Console.WriteLine();

            // Zpravy v poradi indexu.
            var wanted = new HashSet<string> { "IMUState", "GPSState", "MotorStateBase", "RoadCorridorMsg",
                                               "RobotStateMsg", "MeasurementDiagMsg", "MissionMsg" };
            var msgs = new List<Message>();
            var otherSensors = new Dictionary<string, int>();
            foreach (var e in rec.Index)
            {
                if (!wanted.Contains(e.MsgName))
                {
                    if (e.MsgName != "CameraFrame" && e.MsgName.Contains("State")) { otherSensors.TryGetValue(e.MsgName, out int n); otherSensors[e.MsgName] = n + 1; }
                    continue;
                }
                var m = rec.Read(e);
                if (m != null) msgs.Add(m);
            }
            foreach (var kv in otherSensors)
                Console.WriteLine($"  (typ {kv.Key} x{kv.Value} se neprehrava)");
            Console.WriteLine($"zprav k prehrani: {msgs.Count}");

            var recorded = msgs.OfType<RobotStateMsg>()
                               .Select(r => new Sample { T = r.TimeStamp, X = r.X, Y = r.Y, Th = r.Theta, V = r.V }).ToList();

            var withC = RunVariant("S koridorem", true, c, origin, net, maxEdge, msgs);
            var noC = RunVariant("BEZ koridoru", false, c, origin, net, maxEdge, msgs);

            // ---------------- 1) overeni meridla
            Console.WriteLine();
            Console.WriteLine("1) OVERENI MERIDLA: varianta S koridorem proti zaznamenanym RobotStateMsg");
            PrintDiff("  poloha |replay - zaznam|", withC.Samples, recorded, out var worst);
            if (worst.HasValue)
                Console.WriteLine($"     nejvetsi rozdil v {worst.Value:HH:mm:ss.fff}");
            PrintDiffTimeline(withC.Samples, recorded);
            var cr = withC.Corridor;
            Console.WriteLine("  poza v case snimku proti poze ve zprave koridoru (runtime GetStateAt v tomze miste):");
            Console.WriteLine("    " + cr.PoseVsMsg.Line("m"));
            Console.WriteLine("    " + cr.HdgVsMsg.Line("deg"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  verdikt koridoru shodny se zaznamem: {0} z {1} ({2:F1} %)",
                cr.ReasonSame, cr.ReasonCompared, cr.ReasonCompared > 0 ? 100.0 * cr.ReasonSame / cr.ReasonCompared : double.NaN));
            foreach (var kv in cr.ReasonDiff.OrderByDescending(k => k.Value).Take(6))
                Console.WriteLine($"    zaznam {kv.Key.Rec,-18} -> replay {kv.Key.New,-18} {kv.Value,6}");
            CompareDiag(msgs.OfType<MeasurementDiagMsg>().Where(d => d.Source == "Corridor").ToList(), cr.Out);
            if (withC.DepotInits.Count > 0)
                foreach (var d in withC.DepotInits)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  (emulovana inicializace polohy v depu {0:HH:mm:ss}: X={1:F1} Y={2:F1})", d.T, d.X, d.Y));

            // ---------------- 2) koridor v obou variantach
            Console.WriteLine();
            Console.WriteLine("2) KORIDOR v obou variantach (koridor ze zpravy, prirazeni proti prehravane poze):");
            foreach (var v in new[] { withC, noC })
            {
                var k = v.Corridor;
                int ok = k.Reasons.TryGetValue(CorridorFixReason.Ok, out int o) ? o : 0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-13} pouzitelny koridor {1} (z toho jedna hrana {2}), Ok {3} ({4:F1} %), odeslano mereni {5} (skrceno {6})",
                    v.Name, k.Usable, k.UsableSingle, ok, k.Usable > 0 ? 100.0 * ok / k.Usable : double.NaN, k.Out.Count, k.Throttled));
                Console.WriteLine("      duvody: " + string.Join(", ", k.Reasons.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")));
                Console.WriteLine("      " + k.LatDisagree.Line("m"));
            }
            Console.WriteLine("  (|kamera - mapa| je v S z velke casti inovace, kterou fuze sama stahuje - nezavisle je jen v BEZ.)");

            // ---------------- 3) A/B
            Console.WriteLine();
            Console.WriteLine("3) A/B - NEZAVISLA MERITKA (pravda neexistuje):");
            var gps = msgs.OfType<GPSState>().Where(g => g.IsFixed).OrderBy(g => g.TimeStamp).ToList();
            var fcfg = BuildFusionConfig(c, origin);
            var gpsOk = gps.Where(g => DefaultMeasurementMapper.PositionRejectReason(g, fcfg) == null).ToList();
            var gpsLocal = gpsOk.Select(g => { var p = origin.ToLocal(g.Latitude, g.Longitude); return (T: g.TimeStamp, X: (double)p.X, Y: (double)p.Y); }).ToList();
            Console.WriteLine($"   GPS fixu {gps.Count}, z toho projde kvalitou fuze {gpsOk.Count}");

            var rows = new List<(string Name, List<Sample> S)>
            {
                ("zaznam (runtime)", recorded), (withC.Name, withC.Samples), (noC.Name, noC.Samples),
            };
            Console.WriteLine("   Za jizdy (|v| > 0,2 m/s podle zaznamu):");
            Console.WriteLine("   varianta            n    od osy site p50/p90/max [m]   >polosirka  >2 m    |poza-GPS| p50/p90 [m]  pricne p50/p90 [m]  podelne p50/p90 [m]  opak.pruchod p50/p90 [m] (n)");
            var moving = new HashSet<DateTime>(recorded.Where(s => Math.Abs(s.V) > 0.2).Select(s => s.T));
            foreach (var (name, s) in rows)
            {
                var mv = s.Where(x => moving.Contains(x.T)).ToList();
                var road = RoadMetrics(net, origin, roadWidth, mv);
                var g = GpsMetrics(mv, gpsLocal);
                var rv = Revisit(mv, revisitSec);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-17} {1,5}   {2,5:F2} / {3,5:F2} / {4,6:F2}      {5,6:F1} %  {6,5:F1} %      {7,5:F2} / {8,5:F2}        {9,5:F2} / {10,5:F2}       {14,5:F2} / {15,5:F2}         {11,5:F2} / {12,5:F2} ({13})",
                    name, mv.Count, road.D.Median, road.D.Percentile(90), road.D.Max, road.OverHalf, road.Over2,
                    g.Tot.Median, g.Tot.Percentile(90), g.Lat.Median, g.Lat.Percentile(90),
                    rv.Median, rv.Percentile(90), rv.Count, g.Along.Median, g.Along.Percentile(90)));
            }
            // GPS sama jako reference pro opakovany pruchod a pro odstup od site.
            {
                var gs = gpsLocal.Select(g => new Sample { T = g.T, X = g.X, Y = g.Y }).ToList();
                var movingGps = gs.Where(x => NearestMoving(recorded, x.T)).ToList();
                var road = RoadMetrics(net, origin, roadWidth, movingGps);
                var rv = Revisit(movingGps, revisitSec);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-17} {1,5}   {2,5:F2} / {3,5:F2} / {4,6:F2}      {5,6:F1} %  {6,5:F1} %          -                   -                    -                {7,5:F2} / {8,5:F2} ({9})",
                    "GPS (reference)", movingGps.Count, road.D.Median, road.D.Percentile(90), road.D.Max,
                    road.OverHalf, road.Over2, rv.Median, rv.Percentile(90), rv.Count));
            }
            Console.WriteLine();
            Console.WriteLine("   S proti BEZ primo (tytez casy):");
            PrintDiff("   |S - BEZ|", withC.Samples, noC.Samples, out _);

            // Casovy prubeh: odstup od site po minutach.
            Console.WriteLine();
            Console.WriteLine("   Po minutach (za jizdy): od osy site p50 [m] a |poza-GPS| p50 [m]  -  zaznam | S | BEZ");
            var t0 = recorded.Count > 0 ? recorded[0].T : DateTime.MinValue;
            foreach (var grp in recorded.Where(s => moving.Contains(s.T)).GroupBy(s => (int)((s.T - t0).TotalSeconds / 60)))
            {
                var set = new HashSet<DateTime>(grp.Select(x => x.T));
                string cell(List<Sample> s)
                {
                    var sub = s.Where(x => set.Contains(x.T)).ToList();
                    var r = RoadMetrics(net, origin, roadWidth, sub);
                    var g = GpsMetrics(sub, gpsLocal);
                    return string.Format(CultureInfo.InvariantCulture, "{0,5:F2} {1,6:F2}", r.D.Median, g.Tot.Median);
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0,3} min  n={1,4}   {2}  |  {3}  |  {4}",
                    grp.Key, set.Count, cell(recorded), cell(withC.Samples), cell(noC.Samples)));
            }

            // ---------------- 4) rozbor podelne chyby
            var latOnly = RunVariant("jen pricne", true, c, origin, net, maxEdge, msgs, lateral: true, heading: false);
            var hdgOnly = RunVariant("jen kurz", true, c, origin, net, maxEdge, msgs, lateral: false, heading: true);
            var gpsCourse = gpsOk
                .Where(g => g.DynamicOrientation.HasValue && (g.Speed ?? g.DynamicSpeed ?? 0) >= 0.5)
                .Select(g => { var p = origin.ToLocal(g.Latitude, g.Longitude); return (T: g.TimeStamp, X: (double)p.X, Y: (double)p.Y, Course: g.DynamicOrientation.Value); })
                .ToList();
            AlongTrackAnalysis(new List<(string, List<Sample>)>
            {
                (withC.Name, withC.Samples), (latOnly.Name, latOnly.Samples),
                (hdgOnly.Name, hdgOnly.Samples), (noC.Name, noC.Samples),
            }, gpsCourse, moving);
        }

        private static bool NearestMoving(List<Sample> rec, DateTime t)
        {
            int i = Lower(rec, t);
            if (i >= rec.Count) i = rec.Count - 1;
            if (i < 0) return false;
            return Math.Abs(rec[i].V) > 0.2 && Math.Abs((rec[i].T - t).TotalSeconds) < 0.2;
        }

        private static Variant RunVariant(string name, bool send, Dictionary<string, (string Value, string Origin)> c,
                                          GeoReference origin, RoadNetwork net, double maxEdge, List<Message> msgs,
                                          bool lateral = true, bool heading = true)
        {
            var fcfg = BuildFusionConfig(c, origin);
            var engine = new AsyncFusionEngine(new EKFModel(fcfg));
            var kcfg = BuildCorridorConfig(c, send, maxEdge);
            // Rozbor podelne chyby: ktera polovina mereni koridoru co dela. SendHeading je vlastnost
            // konfigurace (runtime ji ma taky), pricnou vypina jen replay.
            kcfg.SendHeading = kcfg.SendHeading && heading;
            var v = new Variant
            {
                Name = name, Send = send, Engine = engine,
                Mapper = new DefaultMeasurementMapper(fcfg, engine),   // CERSTVY mapper (je stavovy)
                Corridor = new CorridorReplay(net, origin, kcfg) { SendLateral = lateral },
            };
            bool depotSeen = false;
            foreach (var m in msgs)
            {
                switch (m)
                {
                    case SensorStateBase s:
                        foreach (var meas in v.Mapper.ToMeasurements(s)) engine.Enqueue(meas);
                        break;
                    case RoadCorridorMsg rc:
                        v.Corridor.Process(rc, engine);
                        break;
                    case RobotStateMsg r:
                        var st = engine.GetStateAt(r.TimeStamp);
                        if (st != null) v.Samples.Add(new Sample
                        {
                            T = r.TimeStamp, X = st.X, Y = st.Y, Th = st.Theta, V = st.V,
                            SigmaAlong = SigmaAlong(st),
                        });
                        break;
                    case MissionMsg mm:
                        // RobotourMission.ArmingAtDepot: InitializePosition(prumer fixu, max(rozptyl, 0,3 m)).
                        if (mm.HasDepot && !depotSeen)
                        {
                            depotSeen = true;
                            var p = origin.ToLocal(LLA.FromDegrees(mm.DepotLatDeg, mm.DepotLonDeg));
                            double std = Math.Max(mm.HasFixInfo ? mm.FixSpreadM : 0, 0.3);
                            engine.InitializePosition(p.X, p.Y, std, mm.TimeStamp);
                            v.DepotInits.Add((mm.TimeStamp, p.X, p.Y));
                        }
                        else if (!mm.HasDepot) depotSeen = false;
                        break;
                }
            }
            return v;
        }

        /// <summary>Sigma polohy podel kurzu pozy: sqrt(uᵀ P u), u = (cos θ, sin θ).</summary>
        private static double SigmaAlong(RobotState st)
        {
            var p = st.Covariance;
            if (p == null || p.RowCount <= EKFModel.IY) return double.NaN;
            double ux = Math.Cos(st.Theta), uy = Math.Sin(st.Theta);
            double v = ux * ux * p[EKFModel.IX, EKFModel.IX] + 2 * ux * uy * p[EKFModel.IX, EKFModel.IY]
                     + uy * uy * p[EKFModel.IY, EKFModel.IY];
            return v > 0 ? Math.Sqrt(v) : double.NaN;
        }

        /// <summary>
        /// <b>Rozbor PODELNE chyby proti GPS</b> — proc vychazi s korekcemi z koridoru hur.
        ///
        /// <para>Tri veci, ktere tabulka A/B smichala:</para>
        /// <list type="bullet">
        /// <item><b>Smer rozkladu.</b> A/B rozklada rozdil podle kurzu TE KTERE pozy - varianty maji
        /// ruzny kurz, takze „podelne" neni u obou totez. Tady se rozklada podle <b>kurzu z GPS</b>
        /// (Doppler, fix nad 0,5 m/s) a se ZNAMENKEM: + = poza je PRED GPS.</item>
        /// <item><b>Latence GPS.</b> Razitko fixu je cas prijmu, poloha je starsi. Pri konstantni
        /// latenci L je poza pred GPS o v·L i kdyz je presna. Tiskne se casovy posun τ, pri kterem
        /// je varianta GPS nejbliz (GPS(t) proti poze(t − τ)): varianta, ktera GPS VERNE SLEDUJE,
        /// ma nejlepsi τ blizko 0 - ta presna ma τ ≈ L.</item>
        /// <item><b>Sila GPS.</b> Sigma polohy podel kurzu z kovariance filtru: kdyz ji koridor
        /// srazi (vazbami v P), GPS podelne tahne slabeji a chyba obvodu kola zustane.</item>
        /// </list>
        /// </summary>
        private static void AlongTrackAnalysis(List<(string Name, List<Sample> S)> variants,
                                               List<(DateTime T, double X, double Y, double Course)> gps,
                                               HashSet<DateTime> moving)
        {
            Console.WriteLine();
            Console.WriteLine("4) PODELNA CHYBA PROTI GPS - rozbor (rozklad podle KURZU Z GPS, + = poza PRED GPS, jen za jizdy):");
            Console.WriteLine("   varianta             n    podelne se znam. p50 [m]  |podelne| p50/p90   |celkem| p50   nejlepsi posun tau [s] -> |celkem| p50   sigma podel z P p50/p90 [m]");
            foreach (var (name, s) in variants)
            {
                var mv = s.Where(x => moving.Contains(x.T)).ToList();
                var signed = new Stats(""); var abs = new Stats(""); var tot = new Stats("");
                foreach (var g in gps)
                {
                    if (!TryPoseAt(mv, g.T, out var p)) continue;
                    double dx = p.X - g.X, dy = p.Y - g.Y;
                    double a = Math.Cos(g.Course) * dx + Math.Sin(g.Course) * dy;
                    signed.Add(a); abs.Add(Math.Abs(a)); tot.Add(Math.Sqrt(dx * dx + dy * dy));
                }
                double bestTau = double.NaN, bestTot = double.MaxValue;
                for (int k = -10; k <= 20; k++)
                {
                    double tau = k * 0.1;
                    var st = new Stats("");
                    foreach (var g in gps)
                    {
                        if (!TryPoseAt(mv, g.T - TimeSpan.FromSeconds(tau), out var p)) continue;
                        st.Add(Math.Sqrt(Sq(p.X - g.X) + Sq(p.Y - g.Y)));
                    }
                    if (st.Count > 20 && st.Median < bestTot) { bestTot = st.Median; bestTau = tau; }
                }
                var sig = new Stats("");
                foreach (var x in mv) sig.Add(x.SigmaAlong);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-18} {1,5}         {2,6:F2}               {3,5:F2} / {4,5:F2}      {5,5:F2}          {6,5:F1}  ->  {7,5:F2}                   {8,6:F2} / {9,6:F2}",
                    name, signed.Count, signed.Median, abs.Median, abs.Percentile(90), tot.Median,
                    bestTau, bestTot == double.MaxValue ? double.NaN : bestTot, sig.Median, sig.Percentile(90)));
            }
            Console.WriteLine("   Cist: varianta, jejiz nejlepsi tau je blizko 0, GPS VERNE SLEDUJE (i s jeji latenci) - ne nutne");
            Console.WriteLine("   spravnou polohu. Mala sigma podel = GPS podelne skoro netahne.");
        }

        /// <summary>Poza nejblizsi casu t (do 0,06 s) ze vzorku po ~0,1 s.</summary>
        private static bool TryPoseAt(List<Sample> s, DateTime t, out Sample p)
        {
            p = default;
            int i = Lower(s, t);
            int best = -1; double bd = 0.06;
            for (int k = Math.Max(0, i - 1); k <= Math.Min(s.Count - 1, i); k++)
            {
                double dt = Math.Abs((s[k].T - t).TotalSeconds);
                if (dt <= bd) { bd = dt; best = k; }
            }
            if (best < 0) return false;
            p = s[best];
            return true;
        }

        // ------------------------------------------------------------------ meritka

        private static int Lower(List<Sample> s, DateTime t)
        {
            int lo = 0, hi = s.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (s[mid].T < t) lo = mid + 1; else hi = mid; }
            return lo;
        }

        private static void PrintDiff(string label, List<Sample> a, List<Sample> b, out DateTime? worstAt)
        {
            var byT = new Dictionary<DateTime, Sample>();
            foreach (var x in b) byT[x.T] = x;
            var dp = new Stats(label + " [m]");
            var dh = new Stats(label.Replace("poloha", "kurz") + " [deg]");
            double worst = -1; worstAt = null;
            foreach (var x in a)
            {
                if (!byT.TryGetValue(x.T, out var y)) continue;
                double d = Math.Sqrt(Sq(x.X - y.X) + Sq(x.Y - y.Y));
                dp.Add(d);
                dh.Add(Math.Abs(Conversions.NormalizeOrientation(x.Th - y.Th)) * 180 / Math.PI);
                if (d > worst) { worst = d; worstAt = x.T; }
            }
            Console.WriteLine(dp.Line("m"));
            Console.WriteLine(dh.Line("deg"));
        }

        private static void PrintDiffTimeline(List<Sample> a, List<Sample> b)
        {
            var byT = new Dictionary<DateTime, Sample>();
            foreach (var x in b) byT[x.T] = x;
            if (a.Count == 0) return;
            var t0 = a[0].T;
            Console.Write("     rozdil polohy po minutach (max v minute) [m]:");
            foreach (var g in a.Where(x => byT.ContainsKey(x.T)).GroupBy(x => (int)((x.T - t0).TotalSeconds / 60)))
            {
                double mx = g.Max(x => { var y = byT[x.T]; return Math.Sqrt(Sq(x.X - y.X) + Sq(x.Y - y.Y)); });
                Console.Write(string.Format(CultureInfo.InvariantCulture, " {0}:{1:F2}", g.Key, mx));
            }
            Console.WriteLine();
        }

        private static (Stats D, double OverHalf, double Over2) RoadMetrics(RoadNetwork net, GeoReference origin,
                                                                           double roadWidth, List<Sample> s)
        {
            var d = new Stats("od osy site");
            int half = 0, two = 0, n = 0;
            foreach (var x in s)
            {
                var cand = net.NearestEdges(origin.ToLLA(x.X, x.Y), 1);
                if (cand.Count == 0) continue;
                var e = cand[0];
                double wa = e.Edge.From.Width > 0 ? e.Edge.From.Width : roadWidth;
                double wb = e.Edge.To.Width > 0 ? e.Edge.To.Width : roadWidth;
                double w = wa + (wb - wa) * Math.Max(0, Math.Min(1, e.T));
                n++;
                d.Add(e.DistanceM);
                if (e.DistanceM > w / 2) half++;
                if (e.DistanceM > 2) two++;
            }
            return (d, n > 0 ? 100.0 * half / n : double.NaN, n > 0 ? 100.0 * two / n : double.NaN);
        }

        /// <summary>|poza − GPS| celkem, pricne a podelne (vuci kurzu pozy), GPS do 0,1 s od vzorku.</summary>
        private static (Stats Tot, Stats Lat, Stats Along) GpsMetrics(List<Sample> s, List<(DateTime T, double X, double Y)> gps)
        {
            var tot = new Stats("poza-GPS"); var lat = new Stats("pricne"); var along = new Stats("podelne");
            int j = 0;
            foreach (var x in s)
            {
                while (j < gps.Count - 1 && gps[j + 1].T <= x.T) j++;
                int best = -1; double bd = 0.1;
                for (int k = Math.Max(0, j - 1); k <= Math.Min(gps.Count - 1, j + 1); k++)
                {
                    double dt = Math.Abs((gps[k].T - x.T).TotalSeconds);
                    if (dt <= bd) { bd = dt; best = k; }
                }
                if (best < 0) continue;
                double dx = x.X - gps[best].X, dy = x.Y - gps[best].Y;
                tot.Add(Math.Sqrt(dx * dx + dy * dy));
                lat.Add(Math.Abs(-Math.Sin(x.Th) * dx + Math.Cos(x.Th) * dy));
                along.Add(Math.Abs(Math.Cos(x.Th) * dx + Math.Sin(x.Th) * dy));
            }
            return (tot, lat, along);
        }

        /// <summary>
        /// <b>Opakovany pruchod:</b> pro vzorek (po 1 m drahy) vzdalenost k nejblizsimu vzorku JINEHO
        /// pruchodu (casovy odstup nad <paramref name="minGapSec"/>), jen kdyz je do 10 m — jinak to
        /// misto podruhe navstivene neni. Robot jede po teze ceste, takze velky rozestup = chyba pozy
        /// (plus skutecny rozdil v pricne poloze jizdy, radove desitky cm).
        /// </summary>
        private static Stats Revisit(List<Sample> s, double minGapSec)
        {
            var st = new Stats("opakovany pruchod");
            var pts = new List<Sample>();
            foreach (var x in s)
                if (pts.Count == 0 || Math.Sqrt(Sq(x.X - pts[^1].X) + Sq(x.Y - pts[^1].Y)) >= 1.0) pts.Add(x);
            foreach (var p in pts)
            {
                double best = double.MaxValue;
                foreach (var q in s)
                {
                    if (Math.Abs((q.T - p.T).TotalSeconds) < minGapSec) continue;
                    double d = Sq(q.X - p.X) + Sq(q.Y - p.Y);
                    if (d < best) best = d;
                }
                best = Math.Sqrt(best);
                if (best < 10) st.Add(best);
            }
            return st;
        }

        /// <summary>
        /// Zaznamenana merenia koridoru (<c>measdiag=Corridor</c>) proti prepocitanym: shoda casu
        /// a hodnoty. Parovani podle razitka snimku a druhu (kurz ma |z| ≤ π a jde po pricnem).
        /// </summary>
        private static void CompareDiag(List<MeasurementDiagMsg> diag, List<Emitted> outList)
        {
            if (diag.Count == 0)
            {
                Console.WriteLine("  (zaznam nema MeasurementDiagMsg Corridor - hodnoty merenii nejde porovnat)");
                return;
            }
            var byT = outList.GroupBy(o => o.T).ToDictionary(g => g.Key, g => g.ToList());
            var recByT = diag.GroupBy(d => d.TimeStamp).ToDictionary(g => g.Key, g => g.ToList());
            int matchedT = 0;
            var dLat = new Stats("|dz| pricne [m]"); var dHdg = new Stats("|dz| kurz [deg]");
            foreach (var kv in recByT)
            {
                if (!byT.TryGetValue(kv.Key, out var mine)) continue;
                matchedT++;
                // Poradi v ramci casu: pricne, pak kurz (Send). Diag nese totez poradi po casech.
                var rl = kv.Value.Where(d => d.Z != null && d.Z.Length == 1).ToList();
                var ml = mine;
                for (int i = 0; i < Math.Min(rl.Count, ml.Count); i++)
                {
                    // Druh u zaznamu neznam - vezmi nejblizsi z mych stejneho casu.
                    var best = ml.OrderBy(o => o.Heading
                        ? Math.Abs(Conversions.NormalizeOrientation(o.Z - rl[i].Z[0]))
                        : Math.Abs(o.Z - rl[i].Z[0])).First();
                    if (best.Heading) dHdg.Add(Math.Abs(Conversions.NormalizeOrientation(best.Z - rl[i].Z[0])) * 180 / Math.PI);
                    else dLat.Add(Math.Abs(best.Z - rl[i].Z[0]));
                }
            }
            int recTimes = recByT.Count, myTimes = byT.Count;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  merenia do fuze: zaznam {0} ({1} snimku), replay {2} ({3} snimku), shodnych snimku {4}",
                diag.Count, recTimes, outList.Count, myTimes, matchedT));
            Console.WriteLine("    " + dLat.Line("m"));
            Console.WriteLine("    " + dHdg.Line("deg"));
        }
    }
}
