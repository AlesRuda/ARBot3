using System;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Telemetry;

namespace ARBot.Common.Tests.Telemetry
{
    /// <summary>
    /// Prezentace uhlovych udaju: ulozeno je vzdy MATEMATICKY ve stupnich (kurz 0 = vychod, +CCW;
    /// rychlost + = doleva) a teprve zobrazeni to prepocita na svetovou konvenci (azimut 0 = sever,
    /// po smeru hodinovych rucicek; rychlost + = doprava). Viz doc/telemetry-view.md.
    /// </summary>
    public class AnglePresentationTests
    {
        [TestCase(0.0, 90.0)]      // vychod -> azimut 90
        [TestCase(90.0, 0.0)]      // sever  -> azimut 0
        [TestCase(180.0, 270.0)]   // zapad  -> azimut 270
        [TestCase(-90.0, 180.0)]   // jih    -> azimut 180
        [TestCase(45.0, 45.0)]     // severovychod
        public void Heading_World_IsAzimuth(double mathDeg, double azimuth)
        {
            Assert.That(AnglePresentation.Present(mathDeg, AngleKind.Heading, AngleMode.World),
                        Is.EqualTo(azimuth).Within(1e-9));
        }

        [TestCase(-170.0, -170.0)]
        [TestCase(190.0, -170.0)]    // normalizace do (-180, 180]
        [TestCase(360.0, 0.0)]
        public void Heading_Math_IsNormalized(double stored, double shown)
        {
            Assert.That(AnglePresentation.Present(stored, AngleKind.Heading, AngleMode.Math),
                        Is.EqualTo(shown).Within(1e-9));
        }

        [Test]
        public void Rate_World_FlipsSign()
        {
            // Matematicky je kladna rychlost doleva; ve svetove konvenci je kladne doprava.
            Assert.That(AnglePresentation.Present(30.0, AngleKind.Rate, AngleMode.World),
                        Is.EqualTo(-30.0).Within(1e-9));
            Assert.That(AnglePresentation.Present(30.0, AngleKind.Rate, AngleMode.Math),
                        Is.EqualTo(30.0).Within(1e-9));
        }

        [Test]
        public void None_IsNeverTouched()
        {
            foreach (var mode in new[] { AngleMode.Math, AngleMode.World })
                Assert.That(AnglePresentation.Present(123.45, AngleKind.None, mode),
                            Is.EqualTo(123.45).Within(1e-9), $"rezim {mode}");
        }

        // ---------------- napojeni na tabulku a radu ----------------

        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static IndexEntry Entry(long seq, DateTime t)
            => new IndexEntry { Seq = seq, CaptureTicks = t.Ticks, ArrivalTicks = t.Ticks };

        /// <summary>Tabulka s jednim kurzem a jednou uhlovou rychlosti (obe ulozene matematicky).</summary>
        private static TelemetryTable BuildTable()
        {
            var heading = new ColumnSpec
            {
                MsgName = "RobotStateMsg",
                Header = "theta [°]",
                Format = "F1",
                Angle = AngleKind.Heading,
                Value = m => m is RobotStateMsg r ? ARBot.Common.Common.Conversions.Rad2Deg(r.Theta) : (double?)null,
            };
            var rate = new ColumnSpec
            {
                MsgName = "RobotStateMsg",
                Header = "omega [°/s]",
                Format = "F1",
                Angle = AngleKind.Rate,
                Value = m => m is RobotStateMsg r ? ARBot.Common.Common.Conversions.Rad2Deg(r.Omega) : (double?)null,
            };

            var b = new TelemetryTableBuilder(new[] { heading, rate });
            // theta = 0 rad (vychod), omega = +0,5236 rad/s (30 °/s doleva)
            b.Add(new RobotStateMsg { TimeStamp = T0, Theta = 0, Omega = Math.PI / 6 }, Entry(0, T0));
            return b.Build();
        }

        [Test]
        public void Table_MathMode_ShowsStoredValues()
        {
            var t = BuildTable();
            t.AngleMode = AngleMode.Math;

            Assert.Multiple(() =>
            {
                Assert.That(t.Columns[0].ValueAt(0), Is.EqualTo(0.0).Within(1e-6));
                Assert.That(t.Columns[1].ValueAt(0), Is.EqualTo(30.0).Within(1e-6));
            });
        }

        [Test]
        public void Table_WorldMode_ConvertsBothKinds()
        {
            var t = BuildTable();
            t.AngleMode = AngleMode.World;

            Assert.Multiple(() =>
            {
                Assert.That(t.Columns[0].ValueAt(0), Is.EqualTo(90.0).Within(1e-6),
                            "vychod je azimut 90");
                Assert.That(t.Columns[1].ValueAt(0), Is.EqualTo(-30.0).Within(1e-6),
                            "otaceni doleva je ve svetove konvenci zaporne");
                Assert.That(t.Columns[0].TextAt(0), Is.EqualTo(90.0.ToString("F1")));
            });
        }

        [Test]
        public void Series_FollowsTableMode()
        {
            var t = BuildTable();

            t.AngleMode = AngleMode.Math;
            Assert.That(TelemetrySeries.From(t, t.Columns[0]).ValueAt(0), Is.EqualTo(0.0).Within(1e-6));

            t.AngleMode = AngleMode.World;
            Assert.That(TelemetrySeries.From(t, t.Columns[0]).ValueAt(0), Is.EqualTo(90.0).Within(1e-6),
                        "rada do grafu se kresli v teze konvenci jako tabulka");
        }
    }
}
