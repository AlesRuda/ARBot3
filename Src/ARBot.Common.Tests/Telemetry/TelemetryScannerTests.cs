using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Telemetry;
using ARBot.Common.Tests.Runtime;   // TestHelpers (Enc, MakeImu)

namespace ARBot.Common.Tests.Telemetry
{
    /// <summary>
    /// Testy skenu zaznamu do telemetricke tabulky: zaznam se slozi pres RecordingTarget do
    /// MemoryStreamu (data + sidecar index) a pak se preskenuje. Viz doc/telemetry-view.md.
    /// </summary>
    public class TelemetryScannerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ColumnSpec SpeedColumn() => new ColumnSpec
        {
            MsgName = "RobotStateMsg",
            Header = "v [m/s]",
            Value = m => m is RobotStateMsg r ? r.V : (double?)null,
        };

        private static ColumnSpec PlanLengthColumn() => new ColumnSpec
        {
            MsgName = "LocalPlanMsg",
            Header = "delka planu [m]",
            Value = m => m is LocalPlanMsg p ? p.LengthM : (double?)null,
        };

        /// <summary>Katalog pro cteni zaznamu - tentyz jako pro replay.</summary>
        private static MessageCatalog Catalog() => MessageCatalog.CommonDefaults();

        /// <summary>Zapise zpravy do zaznamu (data + sidecar index) pres RecordingTarget.</summary>
        private static (byte[] data, List<IndexEntry> index) Record(IEnumerable<Message> msgs)
        {
            using var dataMs = new MemoryStream();
            using var idxMs = new MemoryStream();

            var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc);
            rec.Start();
            foreach (var m in msgs) rec.Post(m);
            rec.Stop();

            using var idxRead = new MemoryStream(idxMs.ToArray());
            return (dataMs.ToArray(), MessageIndex.Read(idxRead, TestHelpers.Enc));
        }

        /// <summary>Zaznam: plan, dve pozy a mezi nimi NEregistrovana IMU zprava.</summary>
        private static List<Message> Sequence() => new List<Message>
        {
            new LocalPlanMsg { TimeStamp = T0, LengthM = 8.0 },
            new RobotStateMsg { TimeStamp = T0.AddMilliseconds(50), V = 1.2 },
            TestHelpers.MakeImu(T0.AddMilliseconds(60), yaw: 0.1, omega: 0.0),
            new RobotStateMsg { TimeStamp = T0.AddMilliseconds(100), V = 1.3 },
        };

        [Test]
        public void Scan_BuildsRowsForRegisteredMessagesOnly()
        {
            var (data, index) = Record(Sequence());
            var columns = new[] { SpeedColumn(), PlanLengthColumn() };

            using var ms = new MemoryStream(data);
            var t = TelemetryScanner.Scan(ms, index, Catalog(), columns, TestHelpers.Enc);

            Assert.That(t.RowCount, Is.EqualTo(3), "IMU zprava neni v registru, radek nedela");
            Assert.That(t.Columns[0].ValueAt(0), Is.Null, "pred prvni pozou je rychlost prazdna");
            Assert.That(t.Columns[0].ValueAt(1), Is.EqualTo(1.2));
            Assert.That(t.Columns[1].ValueAt(2), Is.EqualTo(8.0), "delka planu se drzi z minula");
            Assert.That(t.RowMsgName(0), Is.EqualTo("LocalPlanMsg"));
        }

        [Test]
        public void Scan_RowTimesFollowRecordOrder()
        {
            var (data, index) = Record(Sequence());
            using var ms = new MemoryStream(data);

            var t = TelemetryScanner.Scan(ms, index, Catalog(),
                                          new[] { SpeedColumn(), PlanLengthColumn() }, TestHelpers.Enc);

            Assert.That(t.RowTime(0), Is.EqualTo(T0));
            Assert.That(t.RowTime(1), Is.EqualTo(T0.AddMilliseconds(50)));
            Assert.That(t.RowTime(2), Is.EqualTo(T0.AddMilliseconds(100)));
        }

        [Test]
        public void Scan_SeekIsUsed_SoRowsAreCompleteEvenWithBigSkippedFrames()
        {
            // Mezi telemetrickymi zpravami je velka neregistrovana zprava. Sken ji ma preskocit
            // (necist) a presto poskladat spravne radky - overuje se pres pozice v indexu.
            var big = new Info(new string('x', 5000));
            var msgs = new List<Message>
            {
                new RobotStateMsg { TimeStamp = T0, V = 1.0 },
                big,
                new RobotStateMsg { TimeStamp = T0.AddMilliseconds(50), V = 2.0 },
            };
            var (data, index) = Record(msgs);

            using var ms = new MemoryStream(data);
            var t = TelemetryScanner.Scan(ms, index, Catalog(), new[] { SpeedColumn() }, TestHelpers.Enc);

            Assert.That(t.RowCount, Is.EqualTo(2));
            Assert.That(t.Columns[0].ValueAt(0), Is.EqualTo(1.0));
            Assert.That(t.Columns[0].ValueAt(1), Is.EqualTo(2.0));
        }

        [Test]
        public void Scan_MaxRows_TruncatesAndReports()
        {
            var (data, index) = Record(Sequence());
            using var ms = new MemoryStream(data);

            var t = TelemetryScanner.Scan(ms, index, Catalog(),
                                          new[] { SpeedColumn(), PlanLengthColumn() },
                                          TestHelpers.Enc, maxRows: 2);

            Assert.That(t.RowCount, Is.EqualTo(2));
            Assert.That(t.Truncated, Is.True);
        }

        [Test]
        public void Scan_CanBeCancelled()
        {
            var (data, index) = Record(Sequence());
            using var ms = new MemoryStream(data);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                TelemetryScanner.Scan(ms, index, Catalog(), new[] { SpeedColumn() },
                                      TestHelpers.Enc, ct: cts.Token));
        }

        [Test]
        public void Scan_WithoutIndex_Throws()
        {
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() =>
                TelemetryScanner.Scan(ms, null, Catalog(), new[] { SpeedColumn() }, TestHelpers.Enc));
        }
    }
}
