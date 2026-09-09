using System;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Missions
{
    /// <summary>
    /// Round-trip <see cref="TrackMsg"/> pres plnou serializaci. Zprava jde do zaznamu, takze
    /// rozbor po jizde stoji na tom, ze prezije zapis na disk. Viz doc/track-mission.md.
    ///
    /// <para>⚠️ Test soucasne hlida, ze je zprava <b>zaregistrovana v katalogu</b> — kdyz neni,
    /// <c>Read</c> vrati <c>null</c> a zaznam se tvari, jako by v nem mise VUBEC NEBYLA. Ta past
    /// uz stala dvakrat hodinu (<c>GPSState</c>, <c>MotorStateBase</c>; viz
    /// <see cref="MessageCatalog"/>).</para>
    /// </summary>
    public class TrackMsgSerializationTests
    {
        private static TrackMsg RoundTrip(TrackMsg msg)
        {
            var enc = Encoding.UTF8;
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(msg);
            w.Flush();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new MemoryStream(ms.ToArray()), enc, map);
            return reader.Read() as TrackMsg;
        }

        [Test]
        public void RoundTrip_ZachovaVsechnaPole()
        {
            var t = new DateTime(2026, 9, 8, 18, 30, 0, DateTimeKind.Utc);
            var src = new TrackMsg
            {
                Phase = 3,
                PointIndex = 1,
                PointCount = 3,
                Lap = 2,
                Repeat = true,
                Reached = 4,
                RawLatitude = 0.8733012,
                RawLongitude = 0.2536208,
                TargetLatitude = 0.8733100,
                TargetLongitude = 0.2536300,
                OffRoadM = 7.25,
                RouteLengthM = 143.5,
                AbortReason = "nic",
                ElapsedSec = 61.5,
                TimeStamp = t,
            };

            var back = RoundTrip(src);

            Assert.That(back, Is.Not.Null, "zprava neni v MessageCatalog - zaznam by ji zahodil");
            Assert.Multiple(() =>
            {
                Assert.That(back.Phase, Is.EqualTo(3));
                Assert.That(back.PointIndex, Is.EqualTo(1));
                Assert.That(back.PointCount, Is.EqualTo(3));
                Assert.That(back.Lap, Is.EqualTo(2));
                Assert.That(back.Repeat, Is.True);
                Assert.That(back.Reached, Is.EqualTo(4));
                Assert.That(back.RawLatitude, Is.EqualTo(0.8733012).Within(1e-12));
                Assert.That(back.RawLongitude, Is.EqualTo(0.2536208).Within(1e-12));
                Assert.That(back.TargetLatitude, Is.EqualTo(0.8733100).Within(1e-12));
                Assert.That(back.TargetLongitude, Is.EqualTo(0.2536300).Within(1e-12));
                Assert.That(back.OffRoadM, Is.EqualTo(7.25).Within(1e-9));
                Assert.That(back.RouteLengthM, Is.EqualTo(143.5).Within(1e-9));
                Assert.That(back.AbortReason, Is.EqualTo("nic"));
                Assert.That(back.ElapsedSec, Is.EqualTo(61.5).Within(1e-9));
                Assert.That(back.TimeStamp, Is.EqualTo(t));
            });
        }

        [Test]
        public void PrazdnyDuvodPreruseni_NeniNull_AleRetezec()
        {
            // BinaryWriter.Write(string) na null vyhodi; proto se pise ?? string.Empty.
            var back = RoundTrip(new TrackMsg { AbortReason = null });

            Assert.That(back, Is.Not.Null);
            Assert.That(back.AbortReason, Is.Empty);
        }
    }
}
