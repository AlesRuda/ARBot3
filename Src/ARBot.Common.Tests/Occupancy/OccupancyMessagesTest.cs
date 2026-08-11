using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;
using ARBot.Common.Tests.Runtime;   // TestHelpers, DelegateTarget
using NUnit.Framework;
using System;
using System.IO;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Round-trip zprav lokalni navigace (<see cref="OccupancyGridMsg"/>, <see cref="LocalPlanMsg"/>)
    /// pres zaznam a replay - aby slo ve View zpetne videt, co robot videl a kudy chtel jet.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public class OccupancyMessagesTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Zapise a znovu precte zpravu pres zaznam/replay.</summary>
        private static T RoundTrip<T>(T msg) where T : Message
        {
            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(msg); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults();
            T result = null;
            var sink = new DelegateTarget(m => { if (m is T t) result = t; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(result, Is.Not.Null, $"{typeof(T).Name} se neprecetl (chybi v katalogu?)");
            return result;
        }

        // ---------------- OccupancyGridMsg ----------------

        [Test]
        public void OccupancyGridMsg_RoundTrip()
        {
            var grid = new OccupancyGrid(new OccupancyGridConfig { Size = 16, Resolution = 0.1 });
            grid.Recenter(1.0, -2.0);
            for (int k = 0; k < 5; k++) grid.ObserveOccupied(grid.CellX(1.2), grid.CellY(-2.0), 1f);
            for (int k = 0; k < 5; k++)
            {
                grid.ObserveFree(grid.CellX(0.9), grid.CellY(-2.0), 1f);
                grid.ObserveRoad(grid.CellX(0.9), grid.CellY(-2.0), 1f, 1f);
            }

            var msg = grid.ToLogMessage(T0);
            var r = RoundTrip(msg);

            Assert.That(r.Size, Is.EqualTo(16));
            Assert.That(r.Resolution, Is.EqualTo(0.1));
            Assert.That(r.OriginX, Is.EqualTo(grid.OriginX));
            Assert.That(r.OriginY, Is.EqualTo(grid.OriginY));
            Assert.That(r.Scale, Is.EqualTo(grid.Config.Scale));
            Assert.That(r.TimeStamp, Is.EqualTo(T0));
            Assert.That(r.Occ, Is.EqualTo(msg.Occ));
            Assert.That(r.Road, Is.EqualTo(msg.Road));
        }

        [Test]
        public void OccupancyGridMsg_StavBunkySedisGridem()
        {
            var grid = new OccupancyGrid(new OccupancyGridConfig { Size = 16, Resolution = 0.1 });
            grid.Recenter(0, 0);
            for (int k = 0; k < 5; k++) grid.ObserveOccupied(2, 3, 1f);
            for (int k = 0; k < 5; k++)
            {
                grid.ObserveFree(-2, -3, 1f);
                grid.ObserveRoad(-2, -3, 1f, 1f);
            }

            var r = RoundTrip(grid.ToLogMessage(T0));

            // Zprava drzi kanaly v LOKALNIM poradi - stav musi vyjit stejne jako z gridu.
            for (int i = 0; i < r.Size; i++)
                for (int j = 0; j < r.Size; j++)
                {
                    int cx = r.OriginX + i, cy = r.OriginY + j;
                    Assert.That(r.State(i, j), Is.EqualTo(grid.State(cx, cy)), $"bunka ({cx},{cy})");
                    Assert.That(r.CenterX(i), Is.EqualTo(grid.CenterX(cx)).Within(1e-9));
                    Assert.That(r.CenterY(j), Is.EqualTo(grid.CenterY(cy)).Within(1e-9));
                }
        }

        [Test]
        public void OccupancyGridMsg_MimoRozsah_JeUnknown()
        {
            var grid = new OccupancyGrid(new OccupancyGridConfig { Size = 16, Resolution = 0.1 });
            grid.Recenter(0, 0);
            var msg = grid.ToLogMessage(T0);

            Assert.That(msg.State(-1, 0), Is.EqualTo(CellState.Unknown));
            Assert.That(msg.State(0, 16), Is.EqualTo(CellState.Unknown));
        }

        // ---------------- LocalPlanMsg ----------------

        [Test]
        public void LocalPlanMsg_RoundTrip()
        {
            var plan = new LocalPlanResult
            {
                Status = LocalPlanStatus.Partial,
                RequestedGoalX = 12.5,
                RequestedGoalY = -3.25,
                ReachedGoalX = 5.5,
                ReachedGoalY = -1.5,
                CostSeconds = 7.25,
                LengthM = 5.75,
                MinClearanceM = 0.62,
                ExpandedCells = 1234,
                ComputeMs = 3.5,
                TimeStamp = T0.AddSeconds(1),
                WayPoints = new[]
                {
                    new RegulatorWayPoint { X = 0, Y = 0, Speed = 0.4, MaxPositionError = 0.07 },
                    new RegulatorWayPoint { X = 2, Y = 0.5, Speed = 0.8, MaxPositionError = 0.15,
                                            Orientation = 0.25, MaxOrientationError = 0.5 },
                    new RegulatorWayPoint { X = 5.5, Y = -1.5, Speed = 0.2, MaxPositionError = 0.03 },
                },
            };

            var r = RoundTrip(plan.ToLogMessage());

            Assert.That(r.PlanStatus, Is.EqualTo(LocalPlanStatus.Partial));
            Assert.That(r.RequestedGoalX, Is.EqualTo(12.5));
            Assert.That(r.RequestedGoalY, Is.EqualTo(-3.25));
            Assert.That(r.ReachedGoalX, Is.EqualTo(5.5));
            Assert.That(r.CostSeconds, Is.EqualTo(7.25));
            Assert.That(r.LengthM, Is.EqualTo(5.75));
            Assert.That(r.MinClearanceM, Is.EqualTo(0.62));
            Assert.That(r.ExpandedCells, Is.EqualTo(1234));
            Assert.That(r.ComputeMs, Is.EqualTo(3.5));
            Assert.That(r.TimeStamp, Is.EqualTo(T0.AddSeconds(1)));

            Assert.That(r.WayPoints.Length, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(r.WayPoints[i].X, Is.EqualTo(plan.WayPoints[i].X), $"X[{i}]");
                Assert.That(r.WayPoints[i].Y, Is.EqualTo(plan.WayPoints[i].Y), $"Y[{i}]");
                Assert.That(r.WayPoints[i].Speed, Is.EqualTo(plan.WayPoints[i].Speed), $"Speed[{i}]");
                Assert.That(r.WayPoints[i].MaxPositionError,
                            Is.EqualTo(plan.WayPoints[i].MaxPositionError), $"eps[{i}]");
                Assert.That(r.WayPoints[i].Orientation,
                            Is.EqualTo(plan.WayPoints[i].Orientation), $"Orientation[{i}]");
            }
        }

        [Test]
        public void LocalPlanMsg_BezDrahy_RoundTrip()
        {
            var plan = new LocalPlanResult
            {
                Status = LocalPlanStatus.NoRoute,
                RequestedGoalX = 1,
                RequestedGoalY = 2,
                TimeStamp = T0,
            };

            var r = RoundTrip(plan.ToLogMessage());

            Assert.That(r.PlanStatus, Is.EqualTo(LocalPlanStatus.NoRoute));
            Assert.That(r.WayPoints, Is.Empty, "plan bez drahy se precte jako prazdne pole");
        }
    }
}
