using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Export;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Export
{
    /// <summary>Testy <see cref="GpxExport"/> - export zaznamu do GPX (stopa GPS a stopa fuze).</summary>
    public class GpxExportTest
    {
        private static readonly XNamespace Ns = "http://www.topografix.com/GPX/1/1";
        private static readonly DateTime T0 = new DateTime(2026, 9, 19, 14, 0, 0);   // mistni cas zaznamu (CEST)

        private static GPSState Fix(double sec, double latDeg, double lonDeg, bool valid = true, TimeSpan? fixTime = null)
            => new GPSState
            {
                TimeStamp = T0.AddSeconds(sec),
                Latitude = latDeg * Math.PI / 180,
                Longitude = lonDeg * Math.PI / 180,
                Altitude = 251.5,
                NumberOfSatellites = 9,
                Hdop = 1.2,
                Quality = valid ? GPSState.FixQuality.GpsFix : GPSState.FixQuality.Invalid,
                // UTC = mistni − 2 h, fix o 0,3 s starsi nez razitko (latence).
                FixTime = fixTime ?? (T0.AddSeconds(sec - 0.3).TimeOfDay - TimeSpan.FromHours(2)),
            };

        private static RobotStateMsg Pose(double sec, double x, double y)
            => new RobotStateMsg { TimeStamp = T0.AddSeconds(sec), X = x, Y = y };

        // Ctverec 0,002° kolem (50,001; 14,001) + uzel mimo hrany, ktery se do pocatku nesmi zapocitat.
        private static MapMsg Map()
        {
            var m = new MapMsg();
            m.Nodes.Add(new MapMsg.MapNode { Id = 1, LatDeg = 50.000, LonDeg = 14.000 });
            m.Nodes.Add(new MapMsg.MapNode { Id = 2, LatDeg = 50.002, LonDeg = 14.002 });
            m.Nodes.Add(new MapMsg.MapNode { Id = 3, LatDeg = 51.000, LonDeg = 15.000 });   // bez hrany
            m.Edges.Add(new MapMsg.MapEdge { From = 0, To = 1 });
            return m;
        }

        private static XElement[] Tracks(GpxExportResult r) => XDocument.Parse(r.Gpx).Root!.Elements(Ns + "trk").ToArray();

        [Test]
        public void Gps_DegreesAttributesAndInvalidFixSkipped()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("cs-CZ");   // desetinna carka nesmi do XML
            try
            {
                var r = GpxExport.Build(new Message[]
                {
                    Fix(0, 50.08, 14.42), Fix(1, 0, 0, valid: false), Fix(2, 50.0801, 14.4201),
                }, "test");

                Assert.That(r.GpsPoints, Is.EqualTo(2));
                Assert.That(r.GpsRejected, Is.EqualTo(1));
                var pts = Tracks(r)[0].Descendants(Ns + "trkpt").ToArray();
                Assert.That(pts.Length, Is.EqualTo(2));
                Assert.That(double.Parse(pts[0].Attribute("lat")!.Value, CultureInfo.InvariantCulture), Is.EqualTo(50.08).Within(1e-9));
                Assert.That(double.Parse(pts[0].Attribute("lon")!.Value, CultureInfo.InvariantCulture), Is.EqualTo(14.42).Within(1e-9));
                Assert.That(pts[0].Element(Ns + "ele")!.Value, Is.EqualTo("251.5"));
                Assert.That(pts[0].Element(Ns + "sat")!.Value, Is.EqualTo("9"));
                Assert.That(pts[0].Element(Ns + "hdop")!.Value, Is.EqualTo("1.2"));
                // Poradi prvku dle schematu GPX 1.1: ele, time, sat, hdop.
                Assert.That(pts[0].Elements().Select(e => e.Name.LocalName),
                            Is.EqualTo(new[] { "ele", "time", "sat", "hdop" }));
            }
            finally { Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Test]
        public void Time_IsUtcFromGpsOffset()
        {
            var r = GpxExport.Build(new Message[] { Fix(0, 50, 14), Fix(1, 50, 14), Fix(2, 50, 14) }, "t");
            Assert.That(r.UtcOffsetFromGps, Is.True);
            Assert.That(r.UtcOffset, Is.EqualTo(TimeSpan.FromHours(2)));
            var time = Tracks(r)[0].Descendants(Ns + "time").First().Value;
            Assert.That(time, Is.EqualTo("2026-09-19T12:00:00.000Z"));
        }

        [Test]
        public void UtcOffset_UbloxFixTimeWithDays_UsesTimeOfDayOnly()
        {
            // u-blox dava do FixTime i den v mesici.
            var fixes = Enumerable.Range(0, 5)
                .Select(i => Fix(i, 50, 14, fixTime: new TimeSpan(19, 12, 0, i, 0) - TimeSpan.FromMilliseconds(300)))
                .ToList();
            var (off, fromGps) = GpxExport.EstimateUtcOffset(fixes);
            Assert.That(fromGps, Is.True);
            Assert.That(off, Is.EqualTo(TimeSpan.FromHours(2)));
        }

        [Test]
        public void UtcOffset_WithoutGpsTime_FallsBackToLocalZone()
        {
            var fixes = Enumerable.Range(0, 5).Select(i => Fix(i, 50, 14, fixTime: TimeSpan.Zero)).ToList();
            var (_, fromGps) = GpxExport.EstimateUtcOffset(fixes);
            Assert.That(fromGps, Is.False, "virtualni GPS cas nema - nesmi se tvarit, ze posun zna");
        }

        [Test]
        public void Segments_SplitOnGap()
        {
            var r = GpxExport.Build(new Message[] { Fix(0, 50, 14), Fix(1, 50, 14), Fix(10, 50, 14), Fix(11, 50, 14) }, "t");
            Assert.That(r.GpsSegments, Is.EqualTo(2));
            Assert.That(Tracks(r)[0].Elements(Ns + "trkseg").Count(), Is.EqualTo(2));
        }

        [Test]
        public void Pose_ConvertedThroughMapOrigin()
        {
            var map = Map();
            var r = GpxExport.Build(new Message[] { map, Fix(0, 50, 14), Pose(0, 0, 0), Pose(1, 100, 0) }, "t");

            Assert.That(r.PoseNote, Is.Null);
            var trks = Tracks(r);
            Assert.That(trks.Length, Is.EqualTo(2));
            var pts = trks[1].Descendants(Ns + "trkpt").ToArray();
            Assert.That(pts.Length, Is.EqualTo(2));

            // Pocatek = stred obalky uzlu NA HRANACH (uzel 3 bez hrany se nepocita).
            double lat0 = double.Parse(pts[0].Attribute("lat")!.Value, CultureInfo.InvariantCulture);
            double lon0 = double.Parse(pts[0].Attribute("lon")!.Value, CultureInfo.InvariantCulture);
            Assert.That(lat0, Is.EqualTo(50.001).Within(1e-7));
            Assert.That(lon0, Is.EqualTo(14.001).Within(1e-7));

            // 100 m na vychod: zpet pres tentyz pocatek musi vyjit (100, 0).
            double lat1 = double.Parse(pts[1].Attribute("lat")!.Value, CultureInfo.InvariantCulture);
            double lon1 = double.Parse(pts[1].Attribute("lon")!.Value, CultureInfo.InvariantCulture);
            var local = map.BuildOrigin().ToLocal(lat1 * Math.PI / 180, lon1 * Math.PI / 180);
            Assert.That(local.X, Is.EqualTo(100).Within(0.01));
            Assert.That(local.Y, Is.EqualTo(0).Within(0.01));
        }

        [Test]
        public void Pose_WithoutMap_OmittedWithReason()
        {
            var r = GpxExport.Build(new Message[] { Fix(0, 50, 14), Pose(0, 0, 0), Pose(1, 1, 0) }, "t");
            Assert.That(r.PosePoints, Is.EqualTo(0));
            Assert.That(r.PoseNote, Does.Contain("mapu"));
            Assert.That(Tracks(r).Length, Is.EqualTo(1));
        }

        [Test]
        public void Pose_ThinnedToInterval()
        {
            var msgs = new List<Message> { Map() };
            for (int i = 0; i < 1000; i++) msgs.Add(Pose(i * 0.01, i * 0.01, 0));   // 100 Hz po 10 s
            var r = GpxExport.Build(msgs, "t");
            Assert.That(r.PosePoints, Is.InRange(95, 101));
            Assert.That(r.PoseSegments, Is.EqualTo(1));
        }

        [Test]
        public void Empty_IsStillValidGpx()
        {
            var r = GpxExport.Build(Array.Empty<Message>(), "t");
            Assert.That(XDocument.Parse(r.Gpx).Root!.Name, Is.EqualTo(Ns + "gpx"));
            Assert.That(r.GpsPoints, Is.EqualTo(0));
        }

        // --- Volba stop (24. 9. 2026): rada prohlizecu ukaze jen prvni stopu souboru -----------

        private static Message[] GpsIFuze()
            => new Message[] { Map(), Fix(0, 50.001, 14.001), Fix(1, 50.0011, 14.0011), Pose(0, 0, 0), Pose(1, 1, 0) };

        [Test]
        public void JenGps_zapiseJenStopuGps()
        {
            var r = GpxExport.Build(GpsIFuze(), "t", new GpxExportOptions { Tracks = GpxTracks.GpsOnly });

            var trk = Tracks(r);
            Assert.That(trk, Has.Length.EqualTo(1));
            Assert.That(trk[0].Element(Ns + "name")!.Value, Is.EqualTo("t GPS"));
            Assert.That(r.PosePoints, Is.Zero);
            Assert.That(r.Summary(), Does.Not.Contain("Fúze"));
        }

        [Test]
        public void JenFuze_zapiseJenStopuFuze()
        {
            var r = GpxExport.Build(GpsIFuze(), "t", new GpxExportOptions { Tracks = GpxTracks.PoseOnly });

            var trk = Tracks(r);
            Assert.That(trk, Has.Length.EqualTo(1));
            Assert.That(trk[0].Element(Ns + "name")!.Value, Is.EqualTo("t Fúze"));
            Assert.That(r.GpsPoints, Is.Zero);
            Assert.That(r.Summary(), Does.Not.Contain("GPS:"));
        }

        [Test]
        public void Vychozi_obeStopyVJednomSouboru()
        {
            var r = GpxExport.Build(GpsIFuze(), "t");

            Assert.That(Tracks(r).Select(t => t.Element(Ns + "name")!.Value), Is.EqualTo(new[] { "t GPS", "t Fúze" }));
        }

        [Test]
        public void DvaSoubory_zJednohoCteniZprav_daJakoSamostatneExporty()
        {
            // Export "do dvou souboru" cte zaznam jednou a Build vola dvakrat nad tymz seznamem -
            // vysledek musi byt stejny, jako by se kazda stopa exportovala zvlast.
            var zpravy = GpsIFuze().ToList();
            var gps = GpxExport.Build(zpravy, "t", new GpxExportOptions { Tracks = GpxTracks.GpsOnly });
            var fuze = GpxExport.Build(zpravy, "t", new GpxExportOptions { Tracks = GpxTracks.PoseOnly });
            var obe = GpxExport.Build(zpravy, "t");

            Assert.That(gps.GpsPoints, Is.EqualTo(obe.GpsPoints));
            Assert.That(fuze.PosePoints, Is.EqualTo(obe.PosePoints));
            Assert.That(gps.UtcOffset, Is.EqualTo(fuze.UtcOffset), "cas se musi posunout stejne v obou souborech");
        }
    }
}
