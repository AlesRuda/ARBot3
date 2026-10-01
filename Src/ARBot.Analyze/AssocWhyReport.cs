using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Proc je prirazeni k hrane nejednoznacne — s cim ta vitezna hrana soutezi?</b>
    /// Rozlozi <see cref="EdgeAssociator"/> nad zaznamenanymi cykly (oboustranny koridor
    /// i jedna hrana) na jednotlive hypotezy a u nejednoznacnych cyklu tiskne, kdo je DRUHY:
    /// jina cesta (skutecna nejednoznacnost), nebo sousedni usek TEZE cesty.
    ///
    /// <para><b>Nacpak to je:</b> 29. 9. 2026 (Modrany, <c>20260929-150844.rec</c>) skoncilo
    /// 51 % cyklu jako <c>AmbiguousEdge</c> na dlouhe rovne cyklostezce bez souběžne cesty.
    /// <see cref="RoadAxis.Relate"/> pocita pricnou polohu z <b>nekonecne primky</b> useku,
    /// takze sousedni usek s ohybem 1–2° dava ve vzdalenosti desitek metru osu o metr vedle —
    /// vic nez <c>SameHypothesisLateralM</c> (0,5 m), a pri podlaze pricne sigmy 3 m je to
    /// remiza.</para>
    ///
    /// <para>Protifakt <b>„podelny presah"</b>: hypoteze, na jejiz usecku se poza nepromita,
    /// se k chi-kvadratu pricte <c>(presah / sigma)²</c> se stejnou podlahou jako pricne.
    /// Usek, vedle ktereho robot nestoji, tak prestane soutezit vlastni extrapolaci. Je to
    /// merenie, ne zmena kodu.</para>
    ///
    /// <para>Pouziti: <c>ARBot.Analyze assocwhy zaznam.rec --map=OSM/x.osm [--floorhdg=5]
    /// [--margin=4] [--roadwidth=3] [--singlestd=1] [--maxedge=∞] [--floorlong=3] [--jelfloorlong=0]</c>. Do 26. 9. 2026 jel robot s <c>--maxedge=8</c>, do 29. 9. s <c>--jelfloorlong=0</c> (od te doby <c>assocfloorlong=</c> z logu). Parametry maji odpovidat jizde
    /// (vypis konfigurace v <c>log</c>).</para>
    /// </summary>
    public static class AssocWhyReport
    {
        private sealed class Hyp
        {
            public RoadAxisMatch Axis;
            public Edge Edge;
            public double Chi2;
            /// <summary>O kolik poza lezi za koncem usecky ve smeru hrany [m]; 0 = vedle ni.</summary>
            public double Overhang;
            public double VarLong;
        }

        public static void Run(RecordFile rec, string mapPath, double roadWidth, double floorHdgDeg,
                               double margin, double singleStd, double maxEdgeM = double.PositiveInfinity,
                               double floorLong = 3.0, double recFloorLong = 0.0)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
            {
                Console.Error.WriteLine("assocwhy: --map=<cesta.osm> je povinne (mapa, podle ktere robot jel).");
                return;
            }
            RoadNetwork net;
            using (var fs = File.OpenRead(mapPath))
            {
                var data = OsmXmlReader.Read(fs);
                data = NetworkIslands.Prune(data, TravelProfile.Robot(), out var ostrovy);
                net = GraphBuilder.BuildNetwork(data, TravelProfile.Robot(), roadWidth);
            }

            MapMsg map = null;
            var msgs = new List<RoadCorridorMsg>();
            var gps = new List<(DateTime T, double Lat, double Lon)>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName == "Map" && map == null) map = rec.Read(e) as MapMsg;
                else if (e.MsgName == "RoadCorridorMsg" && rec.Read(e) is RoadCorridorMsg m) msgs.Add(m);
                else if (e.MsgName == "GPSState" && rec.Read(e) is ARBot.Common.Devices.GPSState g && g.IsFixed)
                    gps.Add((g.TimeStamp, g.Latitude, g.Longitude));
            }
            gps.Sort((a, b) => a.T.CompareTo(b.T));
            var origin = map?.BuildOrigin();
            if (origin == null) { Console.WriteLine("assocwhy: zaznam nema MapMsg."); return; }
            var poses = new PoseTrack(rec);

            var cfg = new EdgeAssociationConfig
            {
                Candidates = 4,
                VetoRad = 45 * Math.PI / 180,
                SigmaLateralFloorM = 3.0,
                SigmaHeadingFloorRad = floorHdgDeg * Math.PI / 180,
                Chi2Max = 9.21,
                Chi2Margin = margin,
            };
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "parametry: floorhdg {0}, floorlat 3, margin {1}, veto 45, chi2max 9,21, sirka z mapy +- {2} m (jedna hrana), maxedge {3} m; floorlong jizda {4}, protifakt {5}",
                floorHdgDeg, margin, singleStd, maxEdgeM, recFloorLong, floorLong));

            int n = 0, same = 0;
            var mism = new Dictionary<string, int>();
            int ambRec = 0;
            var cat = new Dictionary<string, int>();
            var secDist = new Stats("druhy: vzdalenost k usecce [m]");
            var secOver = new Stats("druhy: podelny presah [m]");
            var secDLat = new Stats("|pricne viteze - druheho| [m]");
            var secDHdg = new Stats("|smer viteze - druheho| [deg]");
            var winOver = new Stats("vitez: podelny presah [m]");
            int cfOk = 0, cfAmb = 0, cfMis = 0, cfGpsHit = 0, cfGpsN = 0, cfOkWasAmb = 0;
            int baseOk = 0, baseGpsHit = 0, baseGpsN = 0;
            var cfDLat = new Stats("|pricny nesouhlas koridor - vybrana osa| [m]");
            // Pojistka: co protifakt udela s cykly, ktere uz DNES prirazeni melo.
            int implSame = 0, implN = 0;
            var implCfg = new EdgeAssociationConfig
            {
                Candidates = cfg.Candidates, VetoRad = cfg.VetoRad, SigmaLateralFloorM = cfg.SigmaLateralFloorM,
                SigmaHeadingFloorRad = cfg.SigmaHeadingFloorRad, Chi2Max = cfg.Chi2Max, Chi2Margin = cfg.Chi2Margin,
                SigmaLongitudinalFloorM = floorLong,
            };
            int keepSame = 0, keepOtherWay = 0, keepOtherAxis = 0, lostAmb = 0, lostMis = 0;
            int freshGpsHit = 0, freshGpsN = 0, chgOldHit = 0, chgNewHit = 0, chgGpsN = 0;
            var byTime = new SortedDictionary<int, (int Amb, int Cf)>();
            DateTime t0 = msgs.Count > 0 ? msgs[0].TimeStamp : default;

            foreach (var m in msgs)
            {
                if (!m.HasPose) continue;
                var recRes = Recorded((CorridorFixReason)m.FixReason);
                if (recRes == null) continue;
                var corridor = Corridor(m);
                if (corridor == null) continue;
                var st = poses.Nearest(m.TimeStamp);
                var pose = new RobotState { X = m.PoseX, Y = m.PoseY, Theta = m.PoseTheta, Covariance = st?.Covariance };

                var hyps = Hypotheses(net, origin, pose, corridor, cfg, singleStd, maxEdgeM, recFloorLong);
                var res = Verdict(hyps, cfg);
                n++;
                if (res == recRes.Value) same++;
                else
                {
                    string k = $"{recRes.Value} -> {res}";
                    mism[k] = mism.TryGetValue(k, out int c0) ? c0 + 1 : 1;
                }
                var gpsWays = GpsWays(net, gps, m.TimeStamp);
                if (res == EdgeAssocResult.Ok)
                {
                    baseOk++;
                    if (gpsWays != null) { baseGpsN++; if (gpsWays.Contains(hyps[0].Axis.WayId)) baseGpsHit++; }
                }

                if (res == EdgeAssocResult.Ambiguous && hyps.Count >= 2)
                {
                    ambRec++;
                    var w = hyps[0]; var s = hyps[1];
                    string kind;
                    if (s.Axis.WayId != w.Axis.WayId) kind = "jina cesta";
                    else if (SharesNode(s.Edge, w.Edge)) kind = "tataz cesta, SOUSEDNI usek";
                    else kind = "tataz cesta, vzdalenejsi usek";
                    cat[kind] = cat.TryGetValue(kind, out int c1) ? c1 + 1 : 1;
                    secDist.Add(s.Axis.DistanceM);
                    secOver.Add(s.Overhang);
                    winOver.Add(w.Overhang);
                    secDLat.Add(Math.Abs(w.Axis.Lateral - s.Axis.Lateral));
                    secDHdg.Add(Math.Abs(Conversions.NormalizeHalfOrientation(w.Axis.HeadingRelRad - s.Axis.HeadingRelRad)) * 180 / Math.PI);
                }

                // Protifakt: podelny presah v chi-kvadratu.
                var cf = Hypotheses(net, origin, pose, corridor, cfg, singleStd, maxEdgeM, floorLong);
                var cfRes = Verdict(cf, cfg);
                // Skutecny EdgeAssociator s touz podlahou musi dat totez co kopie v meridle.
                var impl = EdgeAssociator.Associate(net, origin, pose, corridor, implCfg, maxEdgeM,
                                                    ax => (ax.WidthM, singleStd));
                implN++;
                if (impl.Result == cfRes) implSame++;
                int bin = (int)((m.TimeStamp - t0).TotalSeconds / 30) * 30;
                byTime.TryGetValue(bin, out var bt);
                byTime[bin] = (bt.Amb + (res == EdgeAssocResult.Ambiguous ? 1 : 0),
                               bt.Cf + (cfRes == EdgeAssocResult.Ambiguous ? 1 : 0));
                switch (cfRes)
                {
                    case EdgeAssocResult.Ok:
                        cfOk++;
                        if (res == EdgeAssocResult.Ambiguous) cfOkWasAmb++;
                        if (gpsWays != null) { cfGpsN++; if (gpsWays.Contains(cf[0].Axis.WayId)) cfGpsHit++; }
                        if (res == EdgeAssocResult.Ok)
                        {
                            var o = hyps[0].Axis; var nw = cf[0].Axis;
                            if (o.WayId != nw.WayId) keepOtherWay++;
                            else if (Math.Abs(o.Lateral - nw.Lateral) > 0.5
                                     || Math.Abs(Conversions.NormalizeHalfOrientation(o.HeadingRelRad - nw.HeadingRelRad)) > 5 * Math.PI / 180)
                                keepOtherAxis++;
                            else keepSame++;
                            if (o.WayId != nw.WayId && gpsWays != null)
                            {
                                chgGpsN++;
                                if (gpsWays.Contains(o.WayId)) chgOldHit++;
                                if (gpsWays.Contains(nw.WayId)) chgNewHit++;
                            }
                        }
                        else if (gpsWays != null) { freshGpsN++; if (gpsWays.Contains(cf[0].Axis.WayId)) freshGpsHit++; }
                        double lat = corridor.Ok ? corridor.Lateral : corridor.SingleEdgeLateral(cf[0].Axis.WidthM);
                        cfDLat.Add(Math.Abs(lat - cf[0].Axis.Lateral));
                        break;
                    case EdgeAssocResult.Ambiguous: cfAmb++; if (res == EdgeAssocResult.Ok) lostAmb++; break;
                    case EdgeAssocResult.NoCandidate: cfMis++; if (res == EdgeAssocResult.Ok) lostMis++; break;
                }
            }

            Console.WriteLine();
            Console.WriteLine("OVERENI MERIDLA (prepocet proti verdiktu v zaznamu, oboustranny koridor i jedna hrana):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  shodny verdikt {0} z {1} ({2:F1} %)", same, n, n > 0 ? 100.0 * same / n : double.NaN));
            foreach (var kv in mism.OrderByDescending(k => k.Value).Take(6))
                Console.WriteLine($"  nesouhlas: {kv.Key,-28} {kv.Value,6}");
            if (n > 0 && same < 0.95 * n)
                Console.WriteLine("  POZOR: prepocet nesedi na zaznam - zbytek necist (jina sirka jedne hrany? jine parametry?).");

            Console.WriteLine();
            Console.WriteLine($"KDO JE DRUHY u nejednoznacnych (n={ambRec}):");
            foreach (var kv in cat.OrderByDescending(k => k.Value))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-32} {1,6}  ({2:F1} %)", kv.Key, kv.Value, 100.0 * kv.Value / Math.Max(1, ambRec)));
            Console.WriteLine("  " + secDist.Line());
            Console.WriteLine("  " + secOver.Line());
            Console.WriteLine("  " + winOver.Line());
            Console.WriteLine("  " + secDLat.Line());
            Console.WriteLine("  " + secDHdg.Line());
            Console.WriteLine("  presah = jak daleko za koncem usecky poza lezi; > 0 znamena, ze se druhy");
            Console.WriteLine("  prihlasil jen EXTRAPOLACI sve primky, robot vedle nej nestoji.");

            Console.WriteLine();
            Console.WriteLine("PROTIFAKT: chi2 += (podelny presah / sigma)^2, sigma = max(poza podel hrany, podlaha --floorlong):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  dnes:      Ok {0,6}   z toho cesta do 2 m od GPS {1:F1} % (n={2})", baseOk,
                baseGpsN > 0 ? 100.0 * baseGpsHit / baseGpsN : double.NaN, baseGpsN));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  protifakt: Ok {0,6}   z toho cesta do 2 m od GPS {1:F1} % (n={2}); nejednozn. {3}, nesedi {4}; Ok z drive nejednoznacnych {5}",
                cfOk, cfGpsN > 0 ? 100.0 * cfGpsHit / cfGpsN : double.NaN, cfGpsN, cfAmb, cfMis, cfOkWasAmb));
            Console.WriteLine("  " + cfDLat.Line());
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  nove prirazene (dnes ne): cesta do 2 m od GPS {0:F1} % (n={1})",
                freshGpsN > 0 ? 100.0 * freshGpsHit / freshGpsN : double.NaN, freshGpsN));
            Console.WriteLine($"  POJISTKA - dnes Ok ({baseOk}): tataz osa {keepSame}, jina osa tehoz way {keepOtherAxis}, JINA CESTA {keepOtherWay}, ztraceno {lostAmb} nejednozn. + {lostMis} nesedi");
            if (chgGpsN > 0)
                Console.WriteLine($"    u jine cesty: stara do 2 m od GPS {chgOldHit}, nova {chgNewHit} (z {chgGpsN})");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  KONTROLA IMPLEMENTACE: EdgeAssociator s floorlong {0} dava tentyz verdikt jako protifakt v {1} z {2} ({3:F2} %)",
                floorLong, implSame, implN, implN > 0 ? 100.0 * implSame / implN : double.NaN));
            Console.WriteLine("  Pozor: Ok tu neznamena poslano do fuze - za prirazenim jsou jeste sirkove brany a 'robot na ceste'.");

            Console.WriteLine();
            Console.WriteLine("NEJEDNOZNACNE PO CASE (30 s): dnes / protifakt");
            foreach (var kv in byTime)
                Console.WriteLine($"  {kv.Key,5} s   {kv.Value.Amb,5} / {kv.Value.Cf,5}");
        }

        private static RoadCorridor Corridor(RoadCorridorMsg m)
        {
            var c = new RoadCorridor
            {
                Reason = (CorridorReason)m.CorridorReason,
                Width = m.Width,
                Lateral = m.Lateral,
                DirectionRad = m.DirectionRad,
                SigmaLateral = m.SigmaLateral,
                SigmaDirectionRad = m.SigmaDirectionRad,
                SingleSide = (CorridorSide)m.SingleSide,
                EdgeOffset = m.EdgeOffset,
                EdgeSigma = m.EdgeSigma,
            };
            if (m.SingleSide == 0) c.Reason = CorridorReason.Ok;
            return c.Ok || c.HasSingleEdge ? c : null;
        }

        /// <summary>
        /// Kopie smycky z <see cref="EdgeAssociator.Associate"/> (vcetne slucovani hypotez), ktera
        /// hypotezy VRACI; <paramref name="floorLong"/> &gt; 0 = s podelnym presahem (0 = bez).
        /// </summary>
        private static List<Hyp> Hypotheses(RoadNetwork net, GeoReference origin, RobotState pose,
                                            RoadCorridor corridor, EdgeAssociationConfig cfg,
                                            double singleStd, double maxEdgeM, double floorLong)
        {
            var list = new List<Hyp>();
            var candidates = net.NearestEdges(origin.ToLLA(pose.X, pose.Y), cfg.Candidates, maxEdgeM);
            bool single = !corridor.Ok && corridor.HasSingleEdge;
            var p = pose.Covariance;
            double varTh = p != null && p.RowCount > EKFModel.ITh ? p[EKFModel.ITh, EKFModel.ITh] : 0;
            foreach (var c in candidates)
            {
                var axis = RoadAxis.Relate(origin, c.Edge, c.T, c.DistanceM, pose.X, pose.Y, pose.Theta);
                if (!axis.Found) continue;
                double dHdg = Conversions.NormalizeHalfOrientation(corridor.DirectionRad - axis.HeadingRelRad);
                if (Math.Abs(dHdg) > cfg.VetoRad) continue;
                double lateral = corridor.Lateral, sigmaLat = corridor.SigmaLateral;
                if (single)
                {
                    lateral = corridor.SingleEdgeLateral(axis.WidthM);
                    sigmaLat = Math.Sqrt(corridor.EdgeSigma * corridor.EdgeSigma + singleStd * singleStd / 4);
                }
                double dLat = lateral - axis.Lateral;
                double varLat = Math.Max(Var(p, axis.NormalX, axis.NormalY), cfg.SigmaLateralFloorM * cfg.SigmaLateralFloorM)
                                + sigmaLat * sigmaLat;
                double varHdg = Math.Max(varTh, cfg.SigmaHeadingFloorRad * cfg.SigmaHeadingFloorRad)
                                + corridor.SigmaDirectionRad * corridor.SigmaDirectionRad;
                double chi2 = dLat * dLat / varLat + dHdg * dHdg / varHdg;

                // Podelny presah: kde lezi poza podel usecky.
                var a = origin.ToLocal(c.Edge.From.Location);
                var b = origin.ToLocal(c.Edge.To.Location);
                double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
                double over = 0, varLong = 0;
                if (len > 1e-6)
                {
                    ex /= len; ey /= len;
                    double s = ex * (pose.X - a.X) + ey * (pose.Y - a.Y);
                    over = s < 0 ? -s : s > len ? s - len : 0;
                    varLong = Math.Max(Var(p, ex, ey), floorLong * floorLong);
                }
                if (floorLong > 0 && over > 0) chi2 += over * over / varLong;
                if (double.IsNaN(chi2)) continue;

                var h = new Hyp { Axis = axis, Edge = c.Edge, Chi2 = chi2, Overhang = over, VarLong = varLong };
                int same = list.FindIndex(x => Math.Abs(x.Axis.Lateral - axis.Lateral) <= cfg.SameHypothesisLateralM
                    && Math.Abs(Conversions.NormalizeHalfOrientation(x.Axis.HeadingRelRad - axis.HeadingRelRad)) <= cfg.SameHypothesisHeadingRad);
                if (same >= 0) { if (chi2 < list[same].Chi2) list[same] = h; continue; }
                list.Add(h);
            }
            list.Sort((h1, h2) => h1.Chi2.CompareTo(h2.Chi2));
            return list;
        }

        private static EdgeAssocResult Verdict(List<Hyp> h, EdgeAssociationConfig cfg)
        {
            if (h.Count == 0) return EdgeAssocResult.NoCandidate;
            if (h[0].Chi2 > cfg.Chi2Max) return EdgeAssocResult.NoCandidate;
            if (h.Count > 1 && h[1].Chi2 - h[0].Chi2 < cfg.Chi2Margin) return EdgeAssocResult.Ambiguous;
            return EdgeAssocResult.Ok;
        }

        private static bool SharesNode(Edge a, Edge b)
            => a.From.Id == b.From.Id || a.From.Id == b.To.Id || a.To.Id == b.From.Id || a.To.Id == b.To.Id;

        private static double Var(MathNet.Numerics.LinearAlgebra.Matrix<double> p, double ux, double uy)
        {
            if (p == null || p.RowCount <= EKFModel.IY) return 0;
            return ux * ux * p[EKFModel.IX, EKFModel.IX] + 2 * ux * uy * p[EKFModel.IX, EKFModel.IY]
                 + uy * uy * p[EKFModel.IY, EKFModel.IY];
        }

        private static HashSet<long> GpsWays(RoadNetwork net, List<(DateTime T, double Lat, double Lon)> gps, DateTime t)
        {
            if (gps.Count == 0) return null;
            int lo = 0, hi = gps.Count - 1;
            while (lo < hi) { int mid = (lo + hi) / 2; if (gps[mid].T < t) lo = mid + 1; else hi = mid; }
            if ((gps[lo].T - t).Duration() > TimeSpan.FromSeconds(0.5)) return null;
            var c = net.NearestEdges(new LLA(gps[lo].Lat, gps[lo].Lon), 8);
            if (c.Count == 0) return null;
            double dmin = c.Min(x => x.DistanceM);
            return new HashSet<long>(c.Where(x => x.DistanceM <= dmin + 2.0).Select(x => x.Edge.WayId));
        }

        private static EdgeAssocResult? Recorded(CorridorFixReason r) => r switch
        {
            CorridorFixReason.Ok => EdgeAssocResult.Ok,
            CorridorFixReason.WidthNotTrusted => EdgeAssocResult.Ok,
            CorridorFixReason.WidthDisagreement => EdgeAssocResult.Ok,
            CorridorFixReason.OutsideCorridor => EdgeAssocResult.Ok,
            CorridorFixReason.AmbiguousEdge => EdgeAssocResult.Ambiguous,
            CorridorFixReason.EdgeMismatch => EdgeAssocResult.NoCandidate,
            CorridorFixReason.EdgeTooFar => EdgeAssocResult.NoCandidate,
            _ => null,
        };
    }
}
