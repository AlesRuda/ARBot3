using System;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Telemetry;

namespace ARBot.Common.Tests.Telemetry
{
    /// <summary>
    /// Testy skladani telemetricke tabulky (bez I/O): drzeni hodnot z pomalejsich zprav,
    /// priznak "prislo prave teto radce", slevani radku a strop. Viz doc/telemetry-view.md.
    /// </summary>
    public class TelemetryTableBuilderTests
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

        private static RobotStateMsg Robot(DateTime t, double v)
            => new RobotStateMsg { TimeStamp = t, V = v };

        private static LocalPlanMsg Plan(DateTime t, double len)
            => new LocalPlanMsg { TimeStamp = t, LengthM = len };

        /// <summary>Zaznam indexu pro zpravu s vlastnim casem (T_in = T_out).</summary>
        private static IndexEntry Entry(long seq, DateTime t)
            => new IndexEntry { Seq = seq, CaptureTicks = t.Ticks, ArrivalTicks = t.Ticks };

        [Test]
        public void SlowColumn_HoldsValueOnFollowingRows()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Plan(T0, 8.0), Entry(0, T0));
            b.Add(Robot(T0.AddMilliseconds(50), 1.2), Entry(1, T0.AddMilliseconds(50)));
            b.Add(Robot(T0.AddMilliseconds(100), 1.3), Entry(2, T0.AddMilliseconds(100)));

            var t = b.Build();
            var plan = t.Columns[1];

            Assert.That(t.RowCount, Is.EqualTo(3));
            Assert.That(plan.ValueAt(1), Is.EqualTo(8.0));
            Assert.That(plan.ValueAt(2), Is.EqualTo(8.0));
            Assert.That(plan.TimeAt(2), Is.EqualTo(T0), "drzena hodnota si nese cas SVE zpravy");
        }

        [Test]
        public void Fresh_IsTrueOnlyOnRowWhereValueArrived()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Plan(T0, 8.0), Entry(0, T0));
            b.Add(Robot(T0.AddMilliseconds(50), 1.2), Entry(1, T0.AddMilliseconds(50)));
            b.Add(Plan(T0.AddMilliseconds(200), 6.5), Entry(2, T0.AddMilliseconds(200)));

            var t = b.Build();
            var plan = t.Columns[1];

            Assert.That(plan.IsFresh(0), Is.True);
            Assert.That(plan.IsFresh(1), Is.False);
            Assert.That(plan.IsFresh(2), Is.True);
        }

        [Test]
        public void BeforeFirstMessageOfType_CellIsEmpty()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Robot(T0, 1.0), Entry(0, T0));

            var t = b.Build();
            var plan = t.Columns[1];

            Assert.That(plan.HasValue(0), Is.False);
            Assert.That(plan.ValueAt(0), Is.Null);
            Assert.That(plan.IsFresh(0), Is.False);
            Assert.That(plan.TextAt(0), Is.Empty);
        }

        [Test]
        public void RowTime_FallsBackToArrivalWhenCaptureMissing()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn() });
            var arrival = T0.AddSeconds(3);
            b.Add(Robot(T0, 1.0), new IndexEntry { Seq = 7, CaptureTicks = 0, ArrivalTicks = arrival.Ticks });

            var t = b.Build();

            Assert.That(t.RowTime(0), Is.EqualTo(arrival));
            Assert.That(t.RowSeq(0), Is.EqualTo(7));
        }

        [Test]
        public void Row_KeepsBothTimes_CaptureAndArrival()
        {
            // T_out je vetsi nez T_in (mereni putovalo pipeline) - detail radku ukazuje oboji.
            var b = new TelemetryTableBuilder(new[] { SpeedColumn() });
            var arrival = T0.AddMilliseconds(12);
            b.Add(Robot(T0, 1.0), new IndexEntry { Seq = 3, CaptureTicks = T0.Ticks, ArrivalTicks = arrival.Ticks });

            var t = b.Build();

            Assert.That(t.RowTime(0), Is.EqualTo(T0));
            Assert.That(t.RowArrivalTime(0), Is.EqualTo(arrival));
        }

        [Test]
        public void MessagesWithEqualTime_MergeIntoOneRow()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Robot(T0, 1.2), Entry(0, T0));
            b.Add(Plan(T0, 8.0), Entry(1, T0));      // tentyz cas -> tentyz radek

            var t = b.Build();

            Assert.That(t.RowCount, Is.EqualTo(1));
            Assert.That(t.Columns[0].ValueAt(0), Is.EqualTo(1.2));
            Assert.That(t.Columns[1].ValueAt(0), Is.EqualTo(8.0));
            Assert.That(t.RowSeq(0), Is.EqualTo(0), "radek si drzi Seq prvni zpravy");
        }

        [Test]
        public void MaxRows_StopsAddingAndReportsTruncated()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn() }, maxRows: 2);
            for (int i = 0; i < 5; i++)
                b.Add(Robot(T0.AddMilliseconds(i * 10), i), Entry(i, T0.AddMilliseconds(i * 10)));

            var t = b.Build();

            Assert.That(t.RowCount, Is.EqualTo(2));
            Assert.That(t.Truncated, Is.True);
            Assert.That(b.IsFull, Is.True);
        }

        [Test]
        public void TextAt_UsesSpecTextWhenProvided_OtherwiseFormat()
        {
            var status = new ColumnSpec
            {
                MsgName = "LocalPlanMsg",
                Header = "stav planu",
                Value = m => m is LocalPlanMsg p ? p.Status : (double?)null,
                Text = v => ((ARBot.Common.Occupancy.LocalPlanStatus)(int)v).ToString(),
            };
            var len = PlanLengthColumn();
            len.Format = "F1";

            var b = new TelemetryTableBuilder(new[] { status, len });
            b.Add(new LocalPlanMsg { TimeStamp = T0, LengthM = 8.25, Status = 1 }, Entry(0, T0));

            var t = b.Build();

            Assert.That(t.Columns[0].TextAt(0), Is.EqualTo("Partial"));
            Assert.That(t.Columns[1].TextAt(0), Is.EqualTo(8.25.ToString("F1")));
        }
    }
}
