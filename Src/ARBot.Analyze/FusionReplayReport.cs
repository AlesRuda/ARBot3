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
using ARBot.Common.Occupancy;
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
    /// [--maxedge=8] [--revisit=60] [--set=klic=hodnota;klic=hodnota]</c>. <c>--set</c> prepise
    /// hodnotu z logu (napr. <c>assocfloorlong=3</c> nad jizdou z doby pred nim). <c>--maxedge</c> vychozi podle data binarky v logu
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
            /// <summary>Normala hrany (pricne merenie) a limit kroku; NaN = bez limitu.</summary>
            public double NX, NY, MaxStep = double.NaN;
        }

        /// <summary>Ucinek jednoho odeslani koridoru na AKTUALNI pozu (pred / po vlozeni do fuze).</summary>
        private sealed class SendEffect
        {
            public DateTime T, Now;
            public double Perp, Along, DTheta, MaxLat;
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
            public readonly List<SendEffect> Effects = new List<SendEffect>();
            public readonly List<FrameCheck> Frames = new List<FrameCheck>();
            public int FramesDropped;
        }

        /// <summary>Snimek kamery v poradi streamu: pred kterou zpravou <c>msgs</c> prisel.</summary>
        private struct FrameMark
        {
            public int Pos;
            public DateTime T;
            public string Name;
        }

        /// <summary>
        /// Poza v case snimku, jak ji vidi <c>LocalNavigator</c> (<c>GetStateAt(frame.TimeStamp)</c>
        /// v miste snimku ve streamu), a verdikt <see cref="PoseJumpDetector"/> postaru (cas pozadu
        /// se nekontroluje) a ponovu.
        /// </summary>
        private sealed class FrameCheck
        {
            public DateTime T;
            public string Name;
            public double Dt, Moved, TurnedDeg, V;
            public bool OldJump, NewJump;
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
                // Od 29. 9. 2026; starsi zaznam klice nema = robot jel se starym chovanim.
                SeekBackSec = GetD(c, "corridorseekback", 0),
                PositionSlewLimit = GetB(c, "corridorposlimit", false),
                MaxEdgeDistanceM = maxEdgeM,
            };
            k.Association = new EdgeAssociationConfig
            {
                Enabled = GetB(c, "assoc", true),
                Candidates = (int)GetD(c, "assock", 4),
                VetoRad = Conversions.Deg2Rad(GetD(c, "assocveto", 45)),
                SigmaLateralFloorM = GetD(c, "assocfloorlat", 3),
                // Podelny presah je od 29. 9. 2026; starsi zaznam klic nema = robot ho nepocital.
                SigmaLongitudinalFloorM = GetD(c, "assocfloorlong", 0),
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
                if (posledniLimitCas == default || t > posledniLimitCas || JeSeek(t, posledniLimitCas))
                    posledniLimitCas = t;

                if (SendLateral)
                {
                    double value = a.NormalX * a.AxisX + a.NormalY * a.AxisY + lateral;
                    double sLat = Nafoukni(sigmaLateral, cfg.SigmaLateralExtraM);
                    engine.Enqueue(new AxisOffsetMeasurement(a.NormalX, a.NormalY, value, sLat, t, cfg.MeasurementSource)
                    { GateThreshold = gate, GateMode = cfg.GateMode, MaxStep = maxLat,
                      MaxPositionStep = cfg.PositionSlewLimit ? maxLat : null });
                    Out.Add(new Emitted { T = t, Heading = false, Z = value, Sigma = sLat,
                                          NX = a.NormalX, NY = a.NormalY, MaxStep = maxLat ?? double.NaN });
                }

                if (cfg.SendHeading)
                {
                    double d = Conversions.NormalizeHalfOrientation(a.HeadingRelRad - c.DirectionRad);
                    double heading = Conversions.NormalizePrimaryOrientation(poseTheta, poseTheta + d);
                    double sH = Nafoukni(c.SigmaDirectionRad, cfg.SigmaHeadingExtraRad);
                    engine.Enqueue(new HeadingMeasurement(heading, sH, t, cfg.MeasurementSource)
                    { GateThreshold = gate, GateMode = cfg.GateMode, MaxStep = maxHdg,
                      MaxPositionStep = cfg.PositionSlewLimit ? maxLat : null });
                    Out.Add(new Emitted { T = t, Heading = true, Z = heading, Sigma = sH });
                }
            }

            private double LimitDt(DateTime t)
            {
                double cap = Math.Max(cfg.SlewDtCapSec, cfg.SlewDtFloorSec);
                if (posledniLimitCas == default || JeSeek(t, posledniLimitCas)) return cap;
                double dt = (t - posledniLimitCas).TotalSeconds;
                return Math.Min(cap, Math.Max(cfg.SlewDtFloorSec, dt));
            }

            private bool JeSeek(DateTime t, DateTime posledni) => (posledni - t).TotalSeconds > cfg.SeekBackSec;

            private static double Nafoukni(double sigma, double prirazek)
                => prirazek > 0 ? Math.Sqrt(sigma * sigma + prirazek * prirazek) : sigma;

            private bool VydatMerenie(DateTime t)
            {
                double perioda = cfg.MinSendPeriodSec;
                if (!(perioda > 0)) return true;
                if (posledniOdeslani == default || JeSeek(t, posledniOdeslani)) { posledniOdeslani = t; return true; }
                if ((t - posledniOdeslani).TotalSeconds + 1e-9 < perioda) return false;
                posledniOdeslani = t;
                return true;
            }
        }

        private static double Sq(double x) => x * x;

        // ------------------------------------------------------------------ hlavni beh

        public static void Run(RecordFile rec, string mapOverride, double maxEdgeArg, double revisitSec,
                               string setText = null)
        {
            var c = ReadConfig(rec, out string version);
            if (c.Count == 0)
            {
                Console.WriteLine("fusionreplay: v logu zaznamu neni blok ucinne konfigurace - neni podle ceho skladat fuzi.");
                return;
            }
            Console.WriteLine(version ?? "(verze binarky v logu neni)");

            // --set=klic=hodnota[;klic=hodnota]: prehrat s JINOU konfiguraci, nez s jakou robot jel
            // (napr. assocfloorlong=3 nad zaznamem z doby pred nim). Varianta S pak se zaznamem
            // sedet nemusi - kontrola shody plati jen pro konfiguraci z logu.
            if (!string.IsNullOrWhiteSpace(setText))
            {
                foreach (var kv in setText.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) { Console.WriteLine($"fusionreplay: --set '{kv}' neni klic=hodnota."); return; }
                    c[kv.Substring(0, eq).Trim()] = (kv.Substring(eq + 1).Trim(), "--set");
                }
                Console.WriteLine($"POZOR: konfigurace prepsana --set ({setText}) - shoda varianty S se zaznamem se neceka.");
            }

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
                                      "corridorslew", "corridorheadingslew", "corridorseekback", "corridorposlimit", "corridorsingle", "corridorsinglewidthstd",
                                      "assoc", "assock", "assocveto", "assocfloorlat", "assocfloorlong", "assocfloorhdg", "assocchi2",
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
            var frames = new List<FrameMark>();
            var otherSensors = new Dictionary<string, int>();
            foreach (var e in rec.Index)
            {
                // Snimek se necte (jsou to gigabajty) - LocalNavigator z nej pro pozu potrebuje
                // jen cas porizeni, a ten nese index.
                if (e.MsgName == "CameraFrame")
                {
                    frames.Add(new FrameMark { Pos = msgs.Count, T = e.CaptureTime, Name = e.Name });
                    continue;
                }
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

            var withC = RunVariant("S koridorem", true, c, origin, net, maxEdge, msgs, frames: frames);
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

            JumpAnalysis(recorded, withC.Samples, noC.Samples);
            EffectAnalysis(withC.Effects);
            GridResetAnalysis(withC);

            // ---------------- 8) podelne meritko pozy proti kolum, 9) proc
            var noGpsPos = RunVariant("BEZ GPS polohy", false, c, origin, net, maxEdge, msgs,
                                      keep: m => m.Source != "GPS/position");
            var odoImu = RunVariant("jen kola+IMU", false, c, origin, net, maxEdge, msgs,
                                    keep: m => !m.Source.StartsWith("GPS/", StringComparison.Ordinal));
            var odoSpeed = RunVariant("jen Odo/speed", false, c, origin, net, maxEdge, msgs,
                                      keep: m => m.Source == "Odo/speed");
            ScaleAnalysis(msgs, gpsLocal, new List<(string, List<Sample>)>
            {
                ("zaznam (runtime)", recorded), (withC.Name, withC.Samples), (noC.Name, noC.Samples),
                (noGpsPos.Name, noGpsPos.Samples), (odoImu.Name, odoImu.Samples), (odoSpeed.Name, odoSpeed.Samples),
            });
            OdoTimingAnalysis(msgs, BuildFusionConfig(c, origin));
        }

        /// <summary>
        /// <b>Proc fuze z rychlosti kol ujede vic nez enkodery?</b> (<c>lok-fuze-poza-pred-koly</c>,
        /// zmereno 1. 10. 2026.) <c>SDC2160Ex</c> razitkuje vzorek na ZACATKU cteni a rychlost
        /// pocita jako <c>Δenkoder / Δrazitko</c>. Kontroler posila v pravidelne periode (enkoder
        /// pribyva rovnomerne), ale radky chodi po seriove lince v davkach, takze razitka maji
        /// vzor ~12 / 12 / 9 ms a vzorek po kratkem intervalu hlasi o ~35 % vyssi rychlost.
        /// Integral „hodnota plati ZPETNE" (jak vznikla) to vyrusi presne; EKF ale merenie drzi
        /// DOPREDU, takze vysokou rychlost pocita i pres nasledujicich 12 ms.
        /// <para>Blok tiskne: rozpad intervalu, integral rychlosti obema pravidly proti enkoderum,
        /// samotny EKF krmeny jen <c>Odo/speed</c> a protifakt: rychlost z enkoderu pres N vzorku
        /// (okno pres celou periodu davek chybu razitek vyrusi).</para>
        /// </summary>
        private static void OdoTimingAnalysis(List<Message> msgs, FusionConfig fcfg)
        {
            var raw = msgs.OfType<MotorStateBase>().Where(m => m.HasMeasurement).OrderBy(m => m.TimeStamp).ToList();
            Console.WriteLine();
            Console.WriteLine("9) RAZITKA ODOMETRIE: integral rychlosti kol proti enkoderum, EKF jen z Odo/speed:");
            if (raw.Count < 10) { Console.WriteLine("   malo vzorku motoru"); return; }
            double V(MotorStateBase m) => 0.5 * (m.LeftWheelSpeed + m.RightWheelSpeed);
            double E(MotorStateBase m) => 0.5 * (m.LeftEncoder + m.RightEncoder);

            var dts = new Stats("interval mezi vzorky [ms]");
            int nShort = 0, nLong = 0;
            var vShort = new Stats("s"); var vLong = new Stats("l");
            double back = 0, fwd = 0, enc = 0;
            for (int i = 1; i < raw.Count; i++)
            {
                double dt = (raw[i].TimeStamp - raw[i - 1].TimeStamp).TotalSeconds;
                dts.Add(dt * 1000);
                enc += E(raw[i]) - E(raw[i - 1]);
                if (dt <= 0 || dt > 1) continue;
                back += V(raw[i]) * dt;         // hodnota plati ZPETNE (jak ji driver spocetl)
                fwd += V(raw[i - 1]) * dt;      // hodnota plati DOPREDU (jak ji drzi EKF)
                // Za jizdy: rychlost po kratkem (< 10,5 ms) a po dlouhem intervalu, pomer k sousedum.
                if (i + 1 < raw.Count && V(raw[i - 1]) > 0.5 && V(raw[i + 1]) > 0.5)
                {
                    double around = 0.5 * (V(raw[i - 1]) + V(raw[i + 1]));
                    if (dt * 1000 < 10.5) { nShort++; vShort.Add(V(raw[i]) / around); }
                    else { nLong++; vLong.Add(V(raw[i]) / around); }
                }
            }
            Console.WriteLine($"   vzorku {raw.Count}; " + dts.Line("ms"));

            // Cas jednotky (MotorStateBase verze 4, radek T= ze skriptu): interval podle jednotky
            // a jak se od nej lisi interval razitek. Po oprave ma razitko jit s jednotkou (rozdil
            // ~0) a radek "pole rychlosti ze zpravy" nize vyjit ~1,000.
            int withDev = raw.Count(m => m.HasDeviceTime);
            if (withDev == 0)
                Console.WriteLine("   cas jednotky: zaznam ho nenese (skript bez radku T= nebo binarka pred 1. 10. 2026)");
            else
            {
                var dDev = new Stats("interval podle jednotky [ms]");
                var dDiff = new Stats("|interval razitek - interval jednotky| [ms]");
                for (int i = 1; i < raw.Count; i++)
                {
                    if (!raw[i].HasDeviceTime || !raw[i - 1].HasDeviceTime) continue;
                    long d = raw[i].DeviceTimeMs - raw[i - 1].DeviceTimeMs;
                    if (d < 0) d += 1_000_000_000;   // SDC2160Ex.DeviceTimeModulus
                    if (d > 1000) continue;          // mezera / restart jednotky
                    dDev.Add(d);
                    dDiff.Add(Math.Abs((raw[i].TimeStamp - raw[i - 1].TimeStamp).TotalMilliseconds - d));
                }
                Console.WriteLine($"   cas jednotky nese {withDev} z {raw.Count} vzorku");
                Console.WriteLine("   " + dDev.Line("ms"));
                Console.WriteLine("   " + dDiff.Line("ms"));
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   za jizdy: po intervalu < 10,5 ms {0} vzorku, rychlost / prumer sousedu p50 {1:F3}; po delsim {2}, p50 {3:F3}",
                nShort, vShort.Median, nLong, vLong.Median));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   draha: enkodery {0:F2} m | integral rychlosti zpetne {1:F2} m ({2:F4}) | dopredu {3:F2} m ({4:F4})",
                enc, back, back / enc, fwd, fwd / enc));

            // EKF jen z rychlosti: dnesni pole rychlosti (N = 1 je totez co Δenc/Δrazitko) a okno pres N vzorku.
            Console.WriteLine("   EKF jen z rychlosti kol (posun / enkodery):");
            foreach (int nWin in new[] { 0, 1, 2, 3, 6 })
            {
                var ekf = new EKFModel(fcfg);
                double x0 = ekf.X[EKFModel.IX];
                DateTime last = raw[0].TimeStamp;
                for (int i = Math.Max(1, nWin); i < raw.Count; i++)
                {
                    double v;
                    if (nWin == 0) v = V(raw[i]);
                    else
                    {
                        double dtw = (raw[i].TimeStamp - raw[i - nWin].TimeStamp).TotalSeconds;
                        if (dtw <= 0.001) continue;
                        v = (E(raw[i]) - E(raw[i - nWin])) / dtw;
                    }
                    double dt = (raw[i].TimeStamp - last).TotalSeconds;
                    if (dt > 0) ekf.Predict(dt);
                    last = raw[i].TimeStamp;
                    ekf.Update(ScalarStateMeasurement.Velocity(v, fcfg.OdoSpeedStd, raw[i].TimeStamp, "Odo/speed"));
                }
                double d = ekf.X[EKFModel.IX] - x0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "     {0,-44} {1,9:F2} m   {2:F4}",
                    nWin == 0 ? "pole rychlosti ze zpravy (dnes)" : $"z enkoderu pres {nWin} vzorky (okno)", d, d / enc));
            }
            Console.WriteLine("   (pomer > 1 = fuze z rychlosti ujede vic, nez kola skutecne ujela)");
        }

        /// <summary>
        /// <b>Ujede poza vic nez kola?</b> (<c>lok-fuze-poza-pred-koly</c>.) Na primych useku
        /// (okno 30 s, smer prvni a druhe poloviny podle GPS se lisi o &lt; 3 deg, tetiva GPS
        /// &gt;= 20 m) parovy pomer tetivy pozy a drahy z kol (integral <c>(vL + vR)/2</c>,
        /// overeny proti enkoderum) a integral <c>V</c> ze stavu v kazde variante prehrani.
        /// Varianta bez zdroje, ktery pomer zvedal, ma vyjit ~1,000.
        /// <para>Okna se NEVYBIRAJI podle pomeru pozy a kol (to dela <c>posegps</c>; useknuti
        /// zvedalo median, tady se ukazalo, ze jen o ~0,005).</para>
        /// </summary>
        private static void ScaleAnalysis(List<Message> msgs, List<(DateTime T, double X, double Y)> gps,
                                          List<(string Name, List<Sample> S)> variants)
        {
            Console.WriteLine();
            Console.WriteLine("8) PODELNE MERITKO: tetiva pozy / draha z kol na primych usecich (okno 30 s):");
            var wheels = msgs.OfType<MotorStateBase>().Where(m => m.HasMeasurement)
                             .Select(m => (T: m.TimeStamp, V: 0.5 * (m.LeftWheelSpeed + m.RightWheelSpeed),
                                           E: 0.5 * (m.LeftEncoder + m.RightEncoder)))
                             .OrderBy(w => w.T).ToList();
            // Enkoder (kumulativni draha) v case t: nejblizsi vzorek motoru.
            double EncAt(DateTime t)
            {
                int lo = 0, hi = wheels.Count - 1;
                while (lo < hi) { int m = (lo + hi) / 2; if (wheels[m].T < t) lo = m + 1; else hi = m; }
                if (lo > 0 && (t - wheels[lo - 1].T) < (wheels[lo].T - t)) lo--;
                return wheels[lo].E;
            }
            const double win = 30;
            var windows = new List<(DateTime A, DateTime B, double Wheel, double Gps, double Enc)>();
            for (int i = 0; i < gps.Count; i += 50)
            {
                var a = gps[i];
                int j = gps.FindIndex(i, g => g.T >= a.T.AddSeconds(win));
                if (j < 0) break;
                var b = gps[j];
                if ((b.T - a.T).TotalSeconds > win + 0.5) continue;
                var mid = gps[gps.FindIndex(i, g => g.T >= a.T.AddSeconds(win / 2))];
                double az1 = Math.Atan2(mid.Y - a.Y, mid.X - a.X), az2 = Math.Atan2(b.Y - mid.Y, b.X - mid.X);
                double dAz = Math.Abs(Math.IEEERemainder(az2 - az1, 2 * Math.PI)) * 180 / Math.PI;
                double cg = Math.Sqrt(Sq(b.X - a.X) + Sq(b.Y - a.Y));
                if (dAz > 3 || cg < 20) continue;
                double wk = 0;
                for (int k = 1; k < wheels.Count; k++)
                {
                    if (wheels[k].T <= a.T || wheels[k].T > b.T) continue;
                    double dt = (wheels[k].T - wheels[k - 1].T).TotalSeconds;
                    if (dt > 0 && dt < 0.1) wk += wheels[k].V * dt;
                }
                if (wk > 1) windows.Add((a.T, b.T, wk, cg, Math.Abs(EncAt(b.T) - EncAt(a.T))));
            }
            Console.WriteLine($"   primych oken {windows.Count}; draha z kol celkem {windows.Sum(w => w.Wheel):F0} m");
            if (windows.Count == 0) return;
            var kg = new Stats("kola / tetiva GPS");
            foreach (var w in windows) kg.Add(w.Wheel / w.Gps);
            Console.WriteLine("   " + kg.Line());
            var ew = new Stats("enkodery / integral rychlosti kol");
            foreach (var w in windows) ew.Add(w.Enc / w.Wheel);
            Console.WriteLine("   " + ew.Line());
            Console.WriteLine("   varianta             n    poza/kola p10 / p50 / p90      souhrn (soucet tetiv / soucet kol)   poza/tetiva GPS p50   integral V / kola p50");
            foreach (var (name, smp) in variants)
            {
                var r = new Stats("r"); var rg = new Stats("g"); var rv = new Stats("v");
                double sp = 0, sw = 0;
                foreach (var w in windows)
                {
                    if (!TryPoseAt(smp, w.A, out var pa) || !TryPoseAt(smp, w.B, out var pb)) continue;
                    double cp = Math.Sqrt(Sq(pb.X - pa.X) + Sq(pb.Y - pa.Y));
                    r.Add(cp / w.Wheel); rg.Add(cp / w.Gps);
                    sp += cp; sw += w.Wheel;
                    // Integral V ze stavu (lichobeznik pres vzorky varianty, ~10 Hz).
                    int i0 = Lower(smp, w.A), i1 = Lower(smp, w.B);
                    double iv = 0;
                    for (int k = Math.Max(1, i0 + 1); k <= Math.Min(i1, smp.Count - 1); k++)
                    {
                        double dt = (smp[k].T - smp[k - 1].T).TotalSeconds;
                        if (dt > 0 && dt < 0.5) iv += 0.5 * (smp[k].V + smp[k - 1].V) * dt;
                    }
                    rv.Add(iv / w.Wheel);
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-17} {1,5}    {2,6:F4} / {3,6:F4} / {4,6:F4}         {5,6:F4}                          {6,6:F4}             {7,6:F4}",
                    name, r.Count, r.Percentile(10), r.Median, r.Percentile(90), sw > 0 ? sp / sw : double.NaN, rg.Median, rv.Median));
            }
            Console.WriteLine("   (> 1 = poza ujede vic nez kola; varianta, ve ktere zmizi, ukaze zdroj.)");
        }

        /// <summary>
        /// <b>Smaze LocalNavigator pri skoku pozy grid?</b> (<c>lok-skok-pozy-nedetekce</c>.)
        /// Pro kazdy snimek kamery v poradi streamu poza <c>GetStateAt(cas snimku)</c> z varianty S
        /// (ta odpovida jizde) a verdikt <see cref="PoseJumpDetector"/> postaru a ponovu. Obe verze
        /// si pamatuji kazdou pozu, takze u snimku s <c>dt &gt; 0</c> rozhoduji shodne - lisit se
        /// mohou jen snimky s casem pozadu. Skoky pozy (blok 5) se pak paruji se smazanim gridu.
        /// <para><b>Aproximace:</b> runtime se pta az po zpracovani snimku ve fronte stupne,
        /// tedy o neco pozdeji - do fuze uz muze dorazit dalsi merenie.</para>
        /// </summary>
        private static void GridResetAnalysis(Variant v)
        {
            const double MatchSec = 0.5;
            var f = v.Frames;
            Console.WriteLine();
            Console.WriteLine("7) MAZANI GRIDU PRI SKOKU POZY (PoseJumpDetector v casech snimku, varianta S):");
            if (f.Count == 0) { Console.WriteLine("   zadny snimek"); return; }
            var back = f.Skip(1).Where(x => x.Dt <= 0).ToList();
            var backMs = new Stats("|dt| snimku s casem pozadu [ms]");
            foreach (var x in back) backMs.Add(-x.Dt * 1000);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "   snimku {0} (bez pozy zahozeno {1}), s casem pozadu (dt <= 0) {2} ({3:F1} %)",
                f.Count, v.FramesDropped, back.Count, 100.0 * back.Count / Math.Max(1, f.Count - 1)));
            if (back.Count > 0) Console.WriteLine("   " + backMs.Line("ms"));
            foreach (var g in f.Skip(1).GroupBy(x => x.Name ?? "").OrderBy(g => g.Key))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "     kamera {0,-22} snimku {1,6}, s casem pozadu {2,6}",
                    g.Key, g.Count(), g.Count(x => x.Dt <= 0)));

            int oldN = f.Count(x => x.OldJump), newN = f.Count(x => x.NewJump);
            int onlyNew = f.Count(x => x.NewJump && !x.OldJump), onlyOld = f.Count(x => x.OldJump && !x.NewJump);
            int pingPong = 0;
            for (int i = 1; i < f.Count; i++) if (f[i].NewJump && f[i - 1].NewJump) pingPong++;
            Console.WriteLine($"   smazani gridu: postaru {oldN}, ponovu {newN} (navic {onlyNew}, chybi {onlyOld}); dvakrat po sobe ponovu {pingPong}");

            // Skoky pozy podle RobotStateMsg (tez meritko jako blok 5): kolik z nich grid smazalo?
            var js = Jumps(v.Samples);
            int hitOld = js.Count(j => f.Any(x => x.OldJump && Math.Abs((x.T - j.T).TotalSeconds) <= MatchSec));
            int hitNew = js.Count(j => f.Any(x => x.NewJump && Math.Abs((x.T - j.T).TotalSeconds) <= MatchSec));
            Console.WriteLine($"   skoku pozy (blok 5) {js.Count}: grid smazan do {MatchSec:F1} s postaru u {hitOld}, ponovu u {hitNew}");

            var list = f.Where(x => x.NewJump).ToList();
            if (list.Count == 0) return;
            Console.WriteLine("   cas snimku    kamera                    dt[ms]  posun[m]  kurz[deg]   v     postaru");
            foreach (var x in list.Take(40))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "   {0:HH:mm:ss.fff}  {1,-24} {2,7:F1}  {3,7:F2}  {4,8:F1}  {5,5:F2}   {6}",
                    x.T, x.Name, x.Dt * 1000, x.Moved, x.TurnedDeg, x.V, x.OldJump ? "ano" : "NE - spolknut"));
            if (list.Count > 40) Console.WriteLine($"   ... a dalsich {list.Count - 40}");
        }

        /// <summary>
        /// <b>Drzi limit kroku na poze, kterou vidi rizeni?</b> Pro kazde odeslani koridoru: posun
        /// AKTUALNI pozy (cas posledniho RobotStateMsg) mezi stavem pred vlozenim merenia a po nem,
        /// rozlozeny KOLMO k hrane (to limit omezuje, <c>MaxStep</c>) a PODEL ni (to neomezuje nic)
        /// a zmena kurzu. Merenie ma cas SNIMKU, takze se vklada do historie a ocas se prepocita.
        /// </summary>
        private static void EffectAnalysis(List<SendEffect> e)
        {
            Console.WriteLine();
            Console.WriteLine("6) UCINEK JEDNOHO ODESLANI KORIDORU NA AKTUALNI POZU (pred / po vlozeni, varianta S):");
            if (e.Count == 0) { Console.WriteLine("   zadne odeslani"); return; }
            var perp = new Stats("kolmo k hrane [m]"); var along = new Stats("podel hrany [m]");
            var dth = new Stats("|zmena kurzu| [deg]"); var lag = new Stats("stari snimku [s]");
            int over = 0, withLim = 0;
            foreach (var x in e)
            {
                perp.Add(x.Perp); along.Add(x.Along); dth.Add(Math.Abs(x.DTheta));
                lag.Add((x.Now - x.T).TotalSeconds);
                if (!double.IsNaN(x.MaxLat)) { withLim++; if (x.Perp > x.MaxLat * 1.05 + 0.01) over++; }
            }
            Console.WriteLine("   " + perp.Line()); Console.WriteLine("   " + along.Line());
            Console.WriteLine("   " + dth.Line()); Console.WriteLine("   " + lag.Line());
            Console.WriteLine($"   kolmy posun NAD limitem (MaxStep): {over} z {withLim} odeslani s limitem");
            Console.WriteLine("   nejvetsi posuny (kolmo + podel):");
            Console.WriteLine("   cas snimku    stari[s]  kolmo  limit  podel  dKurz[deg]");
            foreach (var x in e.OrderByDescending(x => x.Perp + x.Along).Take(15))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0:HH:mm:ss.f}   {1,6:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2}   {5,7:F2}",
                    x.T, (x.Now - x.T).TotalSeconds, x.Perp, x.MaxLat, x.Along, x.DTheta));
        }

        /// <summary>
        /// <b>Skoky pozy: udelal je koridor?</b> Stejne meritko jako <c>ARBot.Analyze nav</c> blok 1
        /// (posun mezi po sobe jdoucimi pozami minus <c>|v|·dt + 0,05</c> nad 0,5 m), ale ve variante
        /// S koridorem i BEZ nej. Skok, ktery je i ve variante BEZ (do ±0,3 s), koridor nezpusobil
        /// — typicky usazovani pozy na GPS po startu. Casova souvislost s merenim koridoru nestaci:
        /// koridor posila 2× za sekundu, takze nejake jeho merenie je „tesne pred" skoro vzdy.
        /// </summary>
        private static void JumpAnalysis(List<Sample> recorded, List<Sample> withC, List<Sample> noC)
        {
            const double MatchSec = 0.3;
            var jr = Jumps(recorded); var js = Jumps(withC); var jb = Jumps(noC);
            bool In(List<(DateTime T, double D, double V, double Dir)> l, DateTime t)
                => l.Any(x => Math.Abs((x.T - t).TotalSeconds) <= MatchSec);

            Console.WriteLine();
            Console.WriteLine("5) SKOKY POZY (posun minus |v|*dt nad 0,5 m, jako nav blok 1): udelal je koridor?");
            Console.WriteLine($"   zaznam {jr.Count}, S koridorem {js.Count} (drah {js.Sum(x => x.D):F1} m), BEZ koridoru {jb.Count} (drah {jb.Sum(x => x.D):F1} m)");
            int shared = js.Count(x => In(jb, x.T));
            Console.WriteLine($"   ze skoku S je i ve variante BEZ (do {MatchSec:F1} s): {shared} -> koridor je NEZPUSOBIL; jen v S: {js.Count - shared}");
            if (js.Count == 0) return;
            Console.WriteLine("   cas           skok [m]    v    smer    i BEZ?");
            foreach (var x in js.Take(40))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   {0:HH:mm:ss.f}   {1,7:F2}  {2,5:F2}  {3,6:F0}    {4}",
                    x.T, x.D, x.V, x.Dir, In(jb, x.T) ? "ano" : "NE - koridor"));
            if (js.Count > 40) Console.WriteLine($"   ... a dalsich {js.Count - 40}");
        }

        /// <summary>Skoky pozy mezi po sobe jdoucimi vzorky: posun minus <c>|v|·dt + 0,05</c> nad 0,5 m.</summary>
        private static List<(DateTime T, double D, double V, double Dir)> Jumps(List<Sample> s)
        {
            const double JumpM = 0.5;
            var j = new List<(DateTime, double, double, double)>();
            for (int i = 1; i < s.Count; i++)
            {
                double dt = (s[i].T - s[i - 1].T).TotalSeconds;
                if (dt <= 0 || dt > 1.0) continue;
                double dx = s[i].X - s[i - 1].X, dy = s[i].Y - s[i - 1].Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d - (Math.Abs(s[i - 1].V) * dt + 0.05) > JumpM)
                    j.Add((s[i].T, d, s[i - 1].V, Math.Atan2(dy, dx) * 180 / Math.PI));
            }
            return j;
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
                                          bool lateral = true, bool heading = true, List<FrameMark> frames = null,
                                          Func<IMeasurement, bool> keep = null)
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
            DateTime lastPoseT = default;
            // Snimky: jako LocalNavigator.Process - GetStateAt(cas snimku), null = zahodit, jinak detektor.
            var detOld = new PoseJumpDetector { CheckBackwardTime = false };
            var detNew = new PoseJumpDetector();
            int fi = 0;
            RobotState prevFrame = null;
            for (int mi = 0; mi < msgs.Count; mi++)
            {
                for (; frames != null && fi < frames.Count && frames[fi].Pos <= mi; fi++)
                {
                    var fs = engine.GetStateAt(frames[fi].T);
                    if (fs == null) { v.FramesDropped++; continue; }
                    double fdx = prevFrame == null ? 0 : fs.X - prevFrame.X, fdy = prevFrame == null ? 0 : fs.Y - prevFrame.Y;
                    v.Frames.Add(new FrameCheck
                    {
                        T = frames[fi].T, Name = frames[fi].Name,
                        Dt = prevFrame == null ? double.NaN : (fs.TimeStamp - prevFrame.TimeStamp).TotalSeconds,
                        Moved = Math.Sqrt(fdx * fdx + fdy * fdy),
                        TurnedDeg = prevFrame == null ? 0 : Conversions.NormalizeOrientation(fs.Theta - prevFrame.Theta) * 180 / Math.PI,
                        V = fs.V,
                        OldJump = detOld.Check(fs.X, fs.Y, fs.Theta, fs.V, fs.Omega, fs.TimeStamp),
                        NewJump = detNew.Check(fs.X, fs.Y, fs.Theta, fs.V, fs.Omega, fs.TimeStamp),
                    });
                    prevFrame = fs;
                }
                var m = msgs[mi];
                switch (m)
                {
                    case SensorStateBase s:
                        foreach (var meas in v.Mapper.ToMeasurements(s))
                            if (keep == null || keep(meas)) engine.Enqueue(meas);
                        break;
                    case RoadCorridorMsg rc:
                    {
                        // Ucinek na AKTUALNI pozu (cas posledniho RobotStateMsg): stav pred a po
                        // vlozeni merenia. Limit kroku plati v case SNIMKU - tady je videt, co z nej
                        // zbyde na poze, kterou vidi rizeni.
                        int before = v.Corridor.Out.Count;
                        var s0 = lastPoseT == default ? null : engine.GetStateAt(lastPoseT);
                        v.Corridor.Process(rc, engine);
                        if (s0 != null && v.Corridor.Out.Count > before)
                        {
                            var lat = v.Corridor.Out.Skip(before).FirstOrDefault(o => !o.Heading);
                            var s1 = engine.GetStateAt(lastPoseT);
                            if (lat != null && s1 != null)
                            {
                                double dx = s1.X - s0.X, dy = s1.Y - s0.Y;
                                double dth = s1.Theta - s0.Theta;
                                while (dth > Math.PI) dth -= 2 * Math.PI;
                                while (dth < -Math.PI) dth += 2 * Math.PI;
                                v.Effects.Add(new SendEffect
                                {
                                    T = lat.T, Now = lastPoseT,
                                    Perp = Math.Abs(dx * lat.NX + dy * lat.NY),
                                    Along = Math.Abs(-dx * lat.NY + dy * lat.NX),
                                    DTheta = dth * 180 / Math.PI, MaxLat = lat.MaxStep,
                                });
                            }
                        }
                        break;
                    }
                    case RobotStateMsg r:
                        lastPoseT = r.TimeStamp;
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
