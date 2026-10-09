using System;
using System.Collections.Generic;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Soustava lokalni vrstvy (<see cref="LocalFrame"/>, parametr <c>localframe=</c>, faze 2 tematu
    /// <c>lp-grid-odometricka-soustava</c>): transformace odom → svet, stav v soustave, zpravy gridu
    /// a planu s transformaci (vcetne prevodu do sveta pro zobrazeni), regulator v ridici smycce,
    /// mrkev FreeRunu a oblak korelatoru. Navigator samotny testuje <c>LocalNavigatorTest</c>.
    /// </summary>
    [TestFixture]
    public class LocalFrameTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        // --- FrameTransform / RobotState ---

        [Test]
        public void FrameTransform_ToLocalJeInverzeToWorld()
        {
            var t = new FrameTransform(10, -5, 0.7);
            var (wx, wy) = t.ToWorld(3, 4);
            var (lx, ly) = t.ToLocal(wx, wy);
            Assert.Multiple(() =>
            {
                Assert.That(lx, Is.EqualTo(3).Within(1e-12));
                Assert.That(ly, Is.EqualTo(4).Within(1e-12));
                Assert.That(t.AngleToWorld(0.1), Is.EqualTo(0.8).Within(1e-12));
                Assert.That(FrameTransform.Identity.IsIdentity, Is.True);
            });
        }

        [Test]
        public void InFrame_WorldVraciTentyzObjekt_OdomKopiiSOdometrickouPozou()
        {
            var s = new RobotState { X = 10, Y = 5, Theta = 1, V = 0.8, Omega = 0.1, OdomX = 2, OdomY = 3, OdomTheta = 0.4, Pitch = 0.05 };
            Assert.That(s.InFrame(LocalFrame.World), Is.SameAs(s));

            var o = s.InFrame(LocalFrame.Odom);
            Assert.Multiple(() =>
            {
                Assert.That(o, Is.Not.SameAs(s), "stav z fuze se nesmi mutovat");
                Assert.That((o.X, o.Y, o.Theta), Is.EqualTo((2.0, 3.0, 0.4)));
                Assert.That((o.V, o.Omega, o.Pitch), Is.EqualTo((0.8, 0.1, 0.05)));
                Assert.That(s.X, Is.EqualTo(10));
                // Transformace z odom do sveta vrati odometrickou pozu na globalni.
                var (wx, wy) = s.ToWorldTransform(LocalFrame.Odom).ToWorld(o.X, o.Y);
                Assert.That(wx, Is.EqualTo(10).Within(1e-12));
                Assert.That(wy, Is.EqualTo(5).Within(1e-12));
                Assert.That(s.ToWorldTransform(LocalFrame.World).IsIdentity, Is.True);
            });
        }

        // --- OccupancyGridMsg ---

        private static OccupancyGridMsg Grid(int n = 10)
            => new OccupancyGridMsg
            {
                Size = n, Resolution = 0.1, OriginX = 0, OriginY = 0, Scale = 0.1f,
                BlockedThreshold = 2f, FreeThreshold = -2f, TimeStamp = T0,
                Occ = new sbyte[n * n], Road = new sbyte[n * n],
            };

        [Test]
        public void GridInWorldFrame_IdentitaVraciTentyzObjekt()
        {
            var g = Grid();
            Assert.That(g.InWorldFrame(), Is.SameAs(g));
        }

        [Test]
        public void GridInWorldFrame_PrevzorkujePosunutouAOtocenouBunku()
        {
            // Lokalni bunka (7, 2) = stred (0,75; 0,25). Otoceni o 90° a posun (5, 1):
            // svet = (−0,25 + 5; 0,75 + 1) = (4,75; 1,75).
            var g = Grid();
            g.Occ[7 + 2 * 10] = 100;
            g.Road[7 + 2 * 10] = 50;
            g.Frame = LocalFrame.Odom;
            g.Transform = new FrameTransform(5, 1, Math.PI / 2);

            var w = g.InWorldFrame();
            int i = (int)Math.Floor(4.75 / 0.1) - w.OriginX, j = (int)Math.Floor(1.75 / 0.1) - w.OriginY;
            int hits = 0;
            for (int k = 0; k < w.Occ.Length; k++) if (w.Occ[k] != 0) hits++;
            Assert.Multiple(() =>
            {
                Assert.That(w.Frame, Is.EqualTo(LocalFrame.World));
                Assert.That(w.Transform.IsIdentity, Is.True);
                Assert.That(w.Occ[i + j * w.Size], Is.EqualTo(100));
                Assert.That(w.Road[i + j * w.Size], Is.EqualTo(50));
                Assert.That(hits, Is.EqualTo(1), "jedna bunka zustane jednou bunkou");
                Assert.That(w.Size, Is.GreaterThanOrEqualTo(10));
            });
        }

        [Test]
        public void GridMsg_RoundTripVerze2_NeseSoustavu()
        {
            var g = Grid(4);
            g.Frame = LocalFrame.Odom;
            g.Transform = new FrameTransform(1.5, -2, 0.3);
            var zpet = RoundTrip(g, new OccupancyGridMsg(), 2);
            Assert.Multiple(() =>
            {
                Assert.That(OccupancyGridMsg.FormatVersion, Is.EqualTo(2));
                Assert.That(zpet.Frame, Is.EqualTo(LocalFrame.Odom));
                Assert.That(zpet.Transform, Is.EqualTo(g.Transform));
            });
        }

        [Test]
        public void GridMsg_Verze1_JeVeSvete()
        {
            var g = Grid(4);
            g.Frame = LocalFrame.Odom;
            g.Transform = new FrameTransform(1.5, -2, 0.3);
            g.Verze = 1;
            var zpet = RoundTrip(g, new OccupancyGridMsg(), 1);
            Assert.That(zpet.Frame, Is.EqualTo(LocalFrame.World));
            Assert.That(zpet.Transform.IsIdentity, Is.True);
        }

        // --- LocalPlanMsg ---

        private static LocalPlanMsg Plan()
            => new LocalPlanMsg
            {
                RequestedGoalX = 3, RequestedGoalY = 0, ReachedGoalX = 2, ReachedGoalY = 0, TimeStamp = T0,
                WayPoints = new[]
                {
                    new RegulatorWayPoint { X = 0, Y = 0, Speed = 0.5 },
                    new RegulatorWayPoint { X = 2, Y = 0, Speed = 0.3, Orientation = 0.0 },
                },
                Frame = LocalFrame.Odom,
                Transform = new FrameTransform(100, 50, Math.PI / 2),
            };

        [Test]
        public void PlanInWorldFrame_PrevedeWaypointyCileIOrientaci()
        {
            var p = Plan();
            var w = p.InWorldFrame();
            Assert.Multiple(() =>
            {
                Assert.That(w, Is.Not.SameAs(p));
                Assert.That(w.Frame, Is.EqualTo(LocalFrame.World));
                Assert.That(w.WayPoints[1].X, Is.EqualTo(100).Within(1e-9));
                Assert.That(w.WayPoints[1].Y, Is.EqualTo(52).Within(1e-9));
                Assert.That(w.WayPoints[1].Orientation, Is.EqualTo(Math.PI / 2).Within(1e-9));
                Assert.That(w.RequestedGoalY, Is.EqualTo(53).Within(1e-9));
                Assert.That(w.ReachedGoalY, Is.EqualTo(52).Within(1e-9));
                Assert.That(p.WayPoints[1].X, Is.EqualTo(2), "puvodni zprava se nemeni");
            });

            p.Transform = FrameTransform.Identity;
            Assert.That(p.InWorldFrame(), Is.SameAs(p));
        }

        [Test]
        public void PlanMsg_RoundTripVerze3_NeseSoustavu_Verze2JeVeSvete()
        {
            var p = Plan();
            p.Verze = 3;
            var zpet = RoundTrip(p, new LocalPlanMsg(), 3);
            Assert.That(zpet.Frame, Is.EqualTo(LocalFrame.Odom));
            Assert.That(zpet.Transform, Is.EqualTo(p.Transform));
            Assert.That(zpet.WayPoints[1].X, Is.EqualTo(2));

            p.Verze = 2;
            var v2 = RoundTrip(p, new LocalPlanMsg(), 2);
            Assert.That(v2.Frame, Is.EqualTo(LocalFrame.World));
            Assert.That(v2.Transform.IsIdentity, Is.True);
        }

        /// <summary>
        /// Verze 4 (8. 10. 2026) nese, kde robot stoji - pro hlidac uvaznuti. Starsi verze ji nemaji
        /// a cteni z nich da „nevi se" (NaN / None), ne nulu.
        /// </summary>
        [Test]
        public void PlanMsg_RoundTripVerze4_NeseKdeRobotStoji_Verze3NeznaHo()
        {
            var p = Plan();
            p.GoalDistanceM = 7.2;
            p.StartBlock = (byte)ARBot.Common.Occupancy.CellBlockReason.Geometry;
            p.StartClearanceM = 0.05;
            Assert.That(LocalPlanMsg.FormatVersion, Is.EqualTo(4));
            Assert.That(p.Verze, Is.EqualTo(4));

            var zpet = RoundTrip(p, new LocalPlanMsg(), 4);
            Assert.Multiple(() =>
            {
                Assert.That(zpet.GoalDistanceM, Is.EqualTo(7.2));
                Assert.That(zpet.StartBlockReason, Is.EqualTo(ARBot.Common.Occupancy.CellBlockReason.Geometry));
                Assert.That(zpet.StartClearanceM, Is.EqualTo(0.05));
                Assert.That(zpet.Frame, Is.EqualTo(LocalFrame.Odom));
            });

            p.Verze = 3;
            var v3 = RoundTrip(p, new LocalPlanMsg(), 3);
            Assert.Multiple(() =>
            {
                Assert.That(double.IsNaN(v3.GoalDistanceM), Is.True);
                Assert.That(v3.StartBlock, Is.EqualTo(0));
                Assert.That(double.IsNaN(v3.StartClearanceM), Is.True);
            });
        }

        private static T RoundTrip<T>(T msg, T empty, int verze) where T : Message
        {
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                msg.ToData(bw);
            ms.Position = 0;
            empty.Verze = verze;
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                empty.FromData(br);
            Assert.That(ms.Position, Is.EqualTo(ms.Length), "precteno vse, co se zapsalo");
            return empty;
        }

        // --- EvidenceCloud (korelace s mapou) ---

        [Test]
        public void EvidenceCloud_GridVOdometrickeSoustave_JeOblakVeSvete()
        {
            var g = Grid();
            g.Road[7 + 2 * 10] = 100;   // 10 log-odds, nad prahem
            g.Frame = LocalFrame.Odom;
            g.Transform = new FrameTransform(5, 1, Math.PI / 2);

            var c = EvidenceCloud.FromGrid(g, 1f);
            Assert.That(c.Count, Is.EqualTo(1));
            Assert.That(c.X[0], Is.EqualTo(4.75).Within(1e-9));
            Assert.That(c.Y[0], Is.EqualTo(1.75).Within(1e-9));
        }

        // --- ControlLoop: regulator v soustave lokalni vrstvy ---

        private sealed class SpyRegulator : IRegulator
        {
            public IModelState Last;
            public RegulatorResult Control(IModelState state) { Last = state; return new RegulatorResult(); }
            public bool IsFinished => false;
        }

        private static AsyncFusionEngine StojiciRobotPosunutyNa100()
        {
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(2));
            for (int i = 0; i < 20; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(0, 0.05, T0.AddSeconds(i * 0.1), "Odo"));
            e.InitializePosition(100, 50, 0.5, T0.AddSeconds(0.5));
            return e;
        }

        [TestCase(LocalFrame.World, 100.0)]
        [TestCase(LocalFrame.Odom, 0.0)]
        public void ControlLoop_RegulatorDostaneSouPozuVSoustaveLokalniVrstvy(LocalFrame frame, double expectedX)
        {
            var engine = StojiciRobotPosunutyNa100();
            var scheduler = new Scheduler();
            using var loop = new ControlLoop(engine, new DummyMotors(), new VirtualClock(), scheduler,
                                             period: TimeSpan.FromMilliseconds(100));
            var spy = new SpyRegulator();
            loop.Frame = frame;
            loop.Regulator = spy;

            scheduler.PumpDue(T0.AddSeconds(1.0));

            Assert.That(spy.Last, Is.Not.Null, "takt mel probehnout");
            var st = (RobotState)spy.Last;
            Assert.That(st.X, Is.EqualTo(expectedX).Within(1e-6));
        }

        // --- FreeRun: mrkev pozou TEHOZ snimku v soustave lokalni vrstvy ---

        private sealed class OdomGoal : ILocalGoalSink
        {
            public LocalFrame Frame => LocalFrame.Odom;
            public (double X, double Y)? World, Local;
            public void SetGoal(double worldX, double worldY, double corridorWidthM = 0,
                                double goalRadiusM = double.NaN) => World = (worldX, worldY);
            public void SetLocalGoal(double x, double y, double corridorWidthM = 0,
                                     double goalRadiusM = double.NaN) => Local = (x, y);
            public void ClearGoal() { }
        }

        [Test]
        public void FreeRun_VOdometrickeSoustave_PosilaLokalniMrkev()
        {
            var engine = StojiciRobotPosunutyNa100();
            var goal = new OdomGoal();
            var cfg = new FreeRunConfig();
            var mise = new FreeRunMission(engine, goal, new CorridorSource(engine), cfg);

            // Bez hranicnich bodu neni koridor -> mrkev rovne podle kurzu (lookahead vpred).
            var r = mise.Process(new CameraFrame { Name = "Left", TimeStamp = T0.AddSeconds(1.0), PathEdges = new List<PathEdge>() });

            Assert.That(r, Is.Not.Null);
            Assert.That(goal.World, Is.Null, "svetovy cil se neposila - prevadel by se jinou transformaci");
            Assert.That(goal.Local, Is.Not.Null);
            Assert.That(goal.Local.Value.X, Is.EqualTo(cfg.LookaheadM).Within(1e-6), "mrkev v odometricke soustave");
            Assert.That(r.GoalX, Is.EqualTo(100 + cfg.LookaheadM).Within(1e-6), "do zpravy jde svetova mrkev");
        }
    }
}
