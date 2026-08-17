using System;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Telemetry;

namespace ARBot.Common.Tests.Telemetry
{
    /// <summary>
    /// Testy vytazeni rady pro graf z hotove tabulky: berou se JEN skutecne prichody (ne drzene
    /// hodnoty) a hodnota v case se cte jako schod. Viz doc/telemetry-view.md, sekce Faze 2.
    /// </summary>
    public class TelemetrySeriesTests
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

        private static IndexEntry Entry(long seq, DateTime t)
            => new IndexEntry { Seq = seq, CaptureTicks = t.Ticks, ArrivalTicks = t.Ticks };

        /// <summary>Tabulka: pomaly plan (2 prichody) a rychlejsi poza (3 prichody).</summary>
        private static TelemetryTable BuildTable()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Plan(T0, 8.0), Entry(0, T0));
            b.Add(Robot(T0.AddMilliseconds(50), 1.2), Entry(1, T0.AddMilliseconds(50)));
            b.Add(Robot(T0.AddMilliseconds(100), 1.5), Entry(2, T0.AddMilliseconds(100)));
            b.Add(Plan(T0.AddMilliseconds(150), 6.0), Entry(3, T0.AddMilliseconds(150)));
            b.Add(Robot(T0.AddMilliseconds(200), 0.9), Entry(4, T0.AddMilliseconds(200)));
            return b.Build();
        }

        [Test]
        public void Series_TakesOnlyArrivals_NotHeldValues()
        {
            var table = BuildTable();

            var plan = TelemetrySeries.From(table, table.Columns[1]);

            // Tabulka ma 5 radku, ale plan prisel jen dvakrat - rada ma dva body.
            Assert.That(table.RowCount, Is.EqualTo(5));
            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(plan.ValueAt(0), Is.EqualTo(8.0));
            Assert.That(plan.ValueAt(1), Is.EqualTo(6.0));
            Assert.That(plan.TicksAt(0), Is.EqualTo(T0.Ticks));
            Assert.That(plan.TicksAt(1), Is.EqualTo(T0.AddMilliseconds(150).Ticks));
        }

        [Test]
        public void Series_KnowsItsRangeAndBounds()
        {
            var table = BuildTable();

            var speed = TelemetrySeries.From(table, table.Columns[0]);

            Assert.That(speed.Count, Is.EqualTo(3));
            Assert.That(speed.Min, Is.EqualTo(0.9));
            Assert.That(speed.Max, Is.EqualTo(1.5));
            Assert.That(speed.FirstTicks, Is.EqualTo(T0.AddMilliseconds(50).Ticks));
            Assert.That(speed.LastTicks, Is.EqualTo(T0.AddMilliseconds(200).Ticks));
        }

        [Test]
        public void ValueAtTime_ReadsAsStep_AndIsNullBeforeFirstArrival()
        {
            var table = BuildTable();
            var plan = TelemetrySeries.From(table, table.Columns[1]);
            var speed = TelemetrySeries.From(table, table.Columns[0]);

            Assert.That(speed.ValueAtTime(T0.Ticks), Is.Null, "prvni poza prijde az v 50 ms");
            Assert.That(speed.ValueAtTime(T0.AddMilliseconds(50).Ticks), Is.EqualTo(1.2));

            // Mezi prichody plati hodnota toho predchoziho (schod), i daleko za poslednim bodem.
            Assert.That(plan.ValueAtTime(T0.AddMilliseconds(149).Ticks), Is.EqualTo(8.0));
            Assert.That(plan.ValueAtTime(T0.AddMilliseconds(150).Ticks), Is.EqualTo(6.0));
            Assert.That(plan.ValueAtTime(T0.AddHours(1).Ticks), Is.EqualTo(6.0));
        }

        [Test]
        public void InterpolatedAt_RampsBetweenArrivals_AndClampsOutsideRange()
        {
            var table = BuildTable();
            var speed = TelemetrySeries.From(table, table.Columns[0]);   // 1.2 @50ms, 1.5 @100ms, 0.9 @200ms

            // Presne v polovine mezi 50 a 100 ms -> polovina mezi 1.2 a 1.5.
            Assert.That(speed.InterpolatedAt(T0.AddMilliseconds(75).Ticks),
                        Is.EqualTo(1.35).Within(1e-9));

            // V bodech same hodnoty jako v datech.
            Assert.That(speed.InterpolatedAt(T0.AddMilliseconds(100).Ticks), Is.EqualTo(1.5));

            // Mimo rozsah se drzi krajni hodnota (kdezto schod pred prvnim prichodem nema nic).
            Assert.That(speed.InterpolatedAt(T0.Ticks), Is.EqualTo(1.2));
            Assert.That(speed.InterpolatedAt(T0.AddHours(1).Ticks), Is.EqualTo(0.9));
            Assert.That(speed.ValueAtTime(T0.Ticks), Is.Null, "schod pred prvnim prichodem neplati");
        }

        [Test]
        public void Series_IsSortedByTime_EvenWhenRecordOrderIsNot()
        {
            // Realny pripad ze zaznamu: dve sousedni zpravy TEHOZ typu s KLESAJICIM casem porizeni
            // (kazda putuje pipeline jinak dlouho). Radky tabulky jdou v poradi zaznamu, ale rada
            // je osa X grafu - musi byt setridena.
            var b = new TelemetryTableBuilder(new[] { PlanLengthColumn() });
            b.Add(Plan(T0.AddMilliseconds(243), 9.0), Entry(0, T0.AddMilliseconds(243)));
            b.Add(Plan(T0.AddMilliseconds(195), 4.0), Entry(1, T0.AddMilliseconds(195)));
            b.Add(Plan(T0.AddMilliseconds(300), 7.0), Entry(2, T0.AddMilliseconds(300)));
            var table = b.Build();

            var plan = TelemetrySeries.From(table, table.Columns[0]);

            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan.TicksAt(0), Is.EqualTo(T0.AddMilliseconds(195).Ticks));
            Assert.That(plan.TicksAt(1), Is.EqualTo(T0.AddMilliseconds(243).Ticks));
            Assert.That(plan.TicksAt(2), Is.EqualTo(T0.AddMilliseconds(300).Ticks));

            // Hodnoty se musi presunout SPOLU s casy, ne jen setridit casy.
            Assert.That(plan.ValueAt(0), Is.EqualTo(4.0));
            Assert.That(plan.ValueAt(1), Is.EqualTo(9.0));
            Assert.That(plan.ValueAt(2), Is.EqualTo(7.0));

            // A puleni pak vraci spravne hodnoty.
            Assert.That(plan.ValueAtTime(T0.AddMilliseconds(250).Ticks), Is.EqualTo(9.0));
        }

        [Test]
        public void EmptySeries_HasNoPointsAndZeroRange()
        {
            // Sloupec, ktery v zaznamu nikdy neprisel (zadna GPS zprava).
            var spec = new ColumnSpec
            {
                MsgName = "GPSState",
                Header = "GPS HDOP",
                Value = m => (double?)null,
            };
            var b = new TelemetryTableBuilder(new[] { spec });
            b.Add(Robot(T0, 1.0), Entry(0, T0));

            var series = TelemetrySeries.From(b.Build(), b.Build().Columns[0]);

            Assert.That(series.Count, Is.EqualTo(0));
            Assert.That(series.Min, Is.EqualTo(0));
            Assert.That(series.Max, Is.EqualTo(0));
            Assert.That(series.ValueAtTime(T0.Ticks), Is.Null);
            Assert.That(series.InterpolatedAt(T0.Ticks), Is.Null);
        }

        [Test]
        public void TextOf_UsesSpecTextWhenProvided()
        {
            var spec = new ColumnSpec
            {
                MsgName = "DriveCommandMsg",
                Header = "STOP",
                Format = "F0",
                Text = v => v != 0 ? "STOP" : "-",
                Value = m => (double?)null,
            };
            var b = new TelemetryTableBuilder(new[] { spec });
            b.Add(Robot(T0, 1.0), Entry(0, T0));
            var series = TelemetrySeries.From(b.Build(), b.Build().Columns[0]);

            Assert.That(series.TextOf(1.0), Is.EqualTo("STOP"));
            Assert.That(series.TextOf(0.0), Is.EqualTo("-"));
        }
    }
}
