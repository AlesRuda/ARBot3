using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Common.Export
{
    /// <summary>Nastaveni exportu do GPX.</summary>
    public sealed class GpxExportOptions
    {
        /// <summary>Nejmensi odstup bodu stopy fuze [s] (0 = kazda zprava). Poza chodi rychleji,
        /// nez je v prohlizeci GPX potreba; 0,1 s = 10 Hz.</summary>
        public double PoseMinIntervalS = 0.1;

        /// <summary>Mezera [s], od ktere zacina novy <c>&lt;trkseg&gt;</c> - prohlizec by jinak
        /// pres vypadek fixu nebo pauzu zaznamu nakreslil primku.</summary>
        public double SegmentGapS = 2.0;
    }

    /// <summary>Vysledek exportu: GPX text a co v nem je (pro hlaseni uzivateli).</summary>
    public sealed class GpxExportResult
    {
        public string Gpx;
        public int GpsPoints, GpsSegments, GpsRejected;
        public int PosePoints, PoseSegments;
        /// <summary>Proc chybi stopa fuze (null = je tam).</summary>
        public string PoseNote;
        /// <summary>Posun razitek zaznamu proti UTC, ktery se pouzil.</summary>
        public TimeSpan UtcOffset;
        /// <summary>Byl posun odvozen z GPS (true), nebo je to mistni zona tohoto PC (false)?</summary>
        public bool UtcOffsetFromGps;

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "GPS: {0} bodů v {1} úsecích", GpsPoints, GpsSegments);
            if (GpsRejected > 0) sb.AppendFormat(CultureInfo.InvariantCulture, " (bez fixu vynecháno {0})", GpsRejected);
            sb.Append(" · Fúze: ");
            sb.Append(PoseNote ?? string.Format(CultureInfo.InvariantCulture, "{0} bodů v {1} úsecích", PosePoints, PoseSegments));
            sb.AppendFormat(CultureInfo.InvariantCulture, " · čas UTC{0}{1:hh\\:mm} ({2})",
                            UtcOffset < TimeSpan.Zero ? "-" : "+", UtcOffset.Duration(),
                            UtcOffsetFromGps ? "posun z GPS" : "zóna tohoto PC - GPS čas nemá");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Export zaznamu do GPX 1.1: stopa <b>GPS</b> (surove platne fixy z <see cref="GPSState"/>)
    /// a stopa <b>Fúze</b> (poza z <see cref="RobotStateMsg"/>). Viz doc/record-replay.md.
    ///
    /// <para><b>Fuze se prevadi pres pocatek mapy</b> (<see cref="MapMsg.BuildOrigin"/>) - tentyz,
    /// se kterym v Run pocitala fuze. Bez <see cref="MapMsg"/> v zaznamu stopa fuze chybi;
    /// dopocitat pocatek z GPS (jako nouzova varianta World pohledu) by stopu rozskakalo
    /// o sum fixu, a to by v souboru vypadalo jako chyba fuze.</para>
    ///
    /// <para><b>Cas:</b> razitka zaznamu jsou mistni cas stroje, ktery nahraval
    /// (<see cref="ARBot.Common.Common.TimeBase"/>), bez zony - a Pi muze bezet v jine zone nez
    /// PC, na kterem se exportuje. Posun proti UTC se proto odvodi z GPS (<see cref="GPSState.FixTime"/>
    /// je UTC cas dne fixu): rozdil zaokrouhleny na ctvrthodinu, na kterem se shodne vetsina fixu.
    /// Kdyz GPS cas nema (virtualni GPS), pouzije se zona tohoto PC a vysledek to rekne.</para>
    /// </summary>
    public static class GpxExport
    {
        /// <summary>Zpravy, ktere export cte (jmena v indexu zaznamu).</summary>
        public static readonly string[] MsgNames = { "GPSState", "RobotStateMsg", "Map" };

        /// <summary>
        /// Projde zaznam (jen polozky <see cref="MsgNames"/>) a sestavi GPX. Stream musi byt
        /// vyhrazeny exportu (ne ten, ze ktereho hraje replay) - meni se jeho pozice.
        /// </summary>
        public static GpxExportResult FromRecord(Stream data, IReadOnlyList<IndexEntry> index,
                                                 MessageCatalog catalog, string trackName,
                                                 GpxExportOptions options = null,
                                                 IProgress<double> progress = null,
                                                 CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            return Build(Read(data, index, catalog.ToPrototypeMap(), progress, ct), trackName, options);
        }

        private static IEnumerable<Message> Read(Stream data, IReadOnlyList<IndexEntry> index,
                                                 Dictionary<string, Message> proto,
                                                 IProgress<double> progress, CancellationToken ct)
        {
            var wanted = new HashSet<string>(MsgNames, StringComparer.Ordinal);
            int reportEvery = Math.Max(1, index.Count / 100);
            for (int i = 0; i < index.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (progress != null && i % reportEvery == 0) progress.Report((double)i / index.Count);

                var e = index[i];
                if (e.MsgName == null || !wanted.Contains(e.MsgName)) continue;

                Message msg = null;
                try
                {
                    data.Position = e.Offset;
                    msg = new MessageReader(data, Encoding.UTF8, proto).Read();
                }
                catch (Exception ex) { Debug.WriteLine(ex.ToString()); }   // poskozeny ramec export nezastavi
                if (msg != null) yield return msg;
            }
        }

        private struct Pt
        {
            public DateTime T;          // razitko zaznamu (mistni cas nahravajiciho stroje)
            public double LatDeg, LonDeg;
            public double? Ele, Hdop;
            public int? Sat;
        }

        /// <summary>Sestavi GPX ze sekvence zprav (poradi = poradi v zaznamu).</summary>
        public static GpxExportResult Build(IEnumerable<Message> messages, string trackName,
                                            GpxExportOptions options = null)
        {
            var opt = options ?? new GpxExportOptions();
            var result = new GpxExportResult();

            var gps = new List<Pt>();
            var fixes = new List<GPSState>();
            var poses = new List<(DateTime T, double X, double Y)>();
            MapMsg map = null;
            DateTime lastPose = DateTime.MinValue;

            foreach (var m in messages)
            {
                switch (m)
                {
                    case GPSState g:
                        if (!g.IsFixed) { result.GpsRejected++; break; }
                        fixes.Add(g);
                        gps.Add(new Pt
                        {
                            T = g.TimeStamp,
                            LatDeg = g.Latitude * 180.0 / Math.PI,     // GPSState drzi radiany
                            LonDeg = g.Longitude * 180.0 / Math.PI,
                            Ele = g.Altitude,
                            Sat = g.NumberOfSatellites,
                            Hdop = g.Hdop,
                        });
                        break;
                    case RobotStateMsg r:
                        // Proredeni; skok razitka zpet (novy beh v zaznamu) ho resetuje.
                        if (r.TimeStamp >= lastPose && (r.TimeStamp - lastPose).TotalSeconds < opt.PoseMinIntervalS)
                            break;
                        lastPose = r.TimeStamp;
                        poses.Add((r.TimeStamp, r.X, r.Y));
                        break;
                    case MapMsg mm:
                        map ??= mm;   // mapa je v zaznamu jedna (runtime ji nacita pri startu)
                        break;
                }
            }

            (result.UtcOffset, result.UtcOffsetFromGps) = EstimateUtcOffset(fixes, poses.Count > 0 ? poses[0].T : null);

            var posePts = new List<Pt>();
            var origin = map?.BuildOrigin();
            if (poses.Count == 0)
                result.PoseNote = "v záznamu není RobotStateMsg";
            else if (origin == null)
                result.PoseNote = "vynechána - záznam nenese mapu (MapMsg), bez ní není počátek, ke kterému póza patří";
            else
                foreach (var (t, x, y) in poses)
                {
                    var lla = origin.ToLLA(x, y);
                    posePts.Add(new Pt { T = t, LatDeg = lla.Latitude * 180.0 / Math.PI, LonDeg = lla.Longitude * 180.0 / Math.PI });
                }

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var sw = new StringWriterUtf8(sb))
            using (var w = XmlWriter.Create(sw, settings))
            {
                const string ns = "http://www.topografix.com/GPX/1/1";
                w.WriteStartDocument();
                w.WriteStartElement("gpx", ns);
                w.WriteAttributeString("version", "1.1");
                w.WriteAttributeString("creator", "ARBot3");
                w.WriteStartElement("metadata", ns);
                w.WriteElementString("name", ns, trackName ?? "ARBot");
                w.WriteEndElement();

                (result.GpsPoints, result.GpsSegments) = WriteTrack(w, ns, (trackName ?? "ARBot") + " GPS", gps, opt, result.UtcOffset);
                if (posePts.Count > 0)
                    (result.PosePoints, result.PoseSegments) = WriteTrack(w, ns, (trackName ?? "ARBot") + " Fúze", posePts, opt, result.UtcOffset);

                w.WriteEndElement();
                w.WriteEndDocument();
            }
            result.Gpx = sb.ToString();
            return result;
        }

        private static (int points, int segments) WriteTrack(XmlWriter w, string ns, string name, List<Pt> pts,
                                                             GpxExportOptions opt, TimeSpan utcOffset)
        {
            if (pts.Count == 0) return (0, 0);
            var inv = CultureInfo.InvariantCulture;

            w.WriteStartElement("trk", ns);
            w.WriteElementString("name", ns, name);
            int segments = 0;
            DateTime prev = DateTime.MinValue;
            bool open = false;
            foreach (var p in pts)
            {
                double gap = (p.T - prev).TotalSeconds;
                if (!open || gap > opt.SegmentGapS || gap < 0)
                {
                    if (open) w.WriteEndElement();
                    w.WriteStartElement("trkseg", ns);
                    open = true;
                    segments++;
                }
                prev = p.T;

                w.WriteStartElement("trkpt", ns);
                w.WriteAttributeString("lat", p.LatDeg.ToString("0.00000000", inv));
                w.WriteAttributeString("lon", p.LonDeg.ToString("0.00000000", inv));
                // Poradi prvku je v GPX 1.1 dane schematem: ele, time, ..., sat, hdop.
                if (p.Ele.HasValue) w.WriteElementString("ele", ns, p.Ele.Value.ToString("0.###", inv));
                var utc = DateTime.SpecifyKind(p.T - utcOffset, DateTimeKind.Utc);
                w.WriteElementString("time", ns, utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", inv));
                if (p.Sat.HasValue) w.WriteElementString("sat", ns, p.Sat.Value.ToString(inv));
                if (p.Hdop.HasValue) w.WriteElementString("hdop", ns, p.Hdop.Value.ToString("0.##", inv));
                w.WriteEndElement();
            }
            if (open) w.WriteEndElement();
            w.WriteEndElement();
            return (pts.Count, segments);
        }

        /// <summary>
        /// Posun razitek zaznamu proti UTC: <c>razitko − UTC</c>. Z fixu s nenulovym
        /// <see cref="GPSState.FixTime"/> (bere se jen cas dne - u-blox do nej dava i den) se spocte
        /// rozdil, zabali do ±12 h a zaokrouhli na 15 min (latence fixu je pod sekundu). Plati
        /// hodnota, na ktere se shodne aspon polovina a aspon 3 fixy; jinak zona tohoto PC.
        /// </summary>
        public static (TimeSpan offset, bool fromGps) EstimateUtcOffset(IReadOnlyList<GPSState> fixes, DateTime? recordTime = null)
        {
            var votes = new Dictionary<long, int>();
            int n = 0;
            foreach (var g in fixes)
            {
                if (g.FixTime <= TimeSpan.Zero) continue;
                double diffMin = (g.TimeStamp.TimeOfDay - TimeSpan.FromTicks(g.FixTime.Ticks % TimeSpan.TicksPerDay)).TotalMinutes;
                while (diffMin > 12 * 60) diffMin -= 24 * 60;
                while (diffMin < -12 * 60) diffMin += 24 * 60;
                long q = (long)Math.Round(diffMin / 15.0);
                votes[q] = votes.TryGetValue(q, out int c) ? c + 1 : 1;
                n++;
            }
            if (n >= 3)
            {
                var best = votes.OrderByDescending(kv => kv.Value).First();
                if (best.Value * 2 >= n)
                    return (TimeSpan.FromMinutes(best.Key * 15), true);
            }

            // Zona tohoto PC pro DATUM ZAZNAMU (letni/zimni cas), ne pro dnesek: prvni fix, jinak
            // prvni poza. Bez obojiho neni co exportovat a na posunu nezalezi.
            var at = fixes.Count > 0 ? fixes[0].TimeStamp : recordTime ?? default;
            return (TimeZoneInfo.Local.GetUtcOffset(DateTime.SpecifyKind(at, DateTimeKind.Local)), false);
        }

        // StringWriter hlasi UTF-16; deklarace v souboru musi odpovidat skutecnemu kodovani.
        private sealed class StringWriterUtf8 : StringWriter
        {
            public StringWriterUtf8(StringBuilder sb) : base(sb, CultureInfo.InvariantCulture) { }
            public override Encoding Encoding => new UTF8Encoding(false);
        }
    }
}
