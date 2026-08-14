using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;
using ARBot.Common.Tests.Runtime;   // DelegateTarget
using ARBot.Common.Vision;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Testy vyssi ridici smycky (<see cref="LocalNavigator"/>): zarovnani snimku podle pozy z fuze
    /// v case porizeni, zahozeni snimku mimo okno historie, planovani jen s cilem a emitovani zprav.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public class LocalNavigatorTest
    {
        private const int W = 64, H = 64;
        private const float Hc = 0.52f;
        private const float F = 40f;
        private static readonly double Pitch = 20 * Math.PI / 180.0;
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // --- syntetická kamera (stejna jako v OccupancyIntegratorTest) ---

        private static Intrinsics MakeIntrinsics() => new Intrinsics
        {
            Width = W, Height = H, PPx = W / 2f, PPy = H / 2f, Fx = F, Fy = F,
            Model = Intrinsics.Distortion.None, Coeffs = new float[5],
        };

        private static Matrix4x4 ForwardCamera()
        {
            float s = (float)Math.Sin(Pitch), c = (float)Math.Cos(Pitch);
            return new Matrix4x4(0, -1, 0, 0, -s, 0, -c, 0, c, 0, -s, 0, 0, 0, Hc, 1);
        }

        private static CameraProjection MakeProjection()
        {
            var p = new CameraProjection(MakeIntrinsics(), MakeIntrinsics(),
                                         Matrix4x4.Identity, Matrix4x4.Identity);
            p.SetOrientation(ForwardCamera());
            return p;
        }

        private static Image<Gray16> FlatGroundDepth()
        {
            double s = Math.Sin(Pitch), c = Math.Cos(Pitch);
            var img = new Image<Gray16>(W, H);
            var d = img.Data;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double ry = (y - H / 2.0) / F;
                    double denom = ry * c + s;
                    ushort v = 0;
                    if (denom > 1e-3)
                    {
                        double dist = Hc / denom;
                        if (dist > 0 && dist < 60) v = (ushort)Math.Round(dist * 1000);
                    }
                    int idx = (y * W + x) * 2;
                    d[idx] = (byte)(v & 0xFF);
                    d[idx + 1] = (byte)(v >> 8);
                }
            return img;
        }

        private static PolarGridConfig PolarCfg() => new PolarGridConfig
        {
            ColumnsPerCell = 8, TargetPointsPerCell = 12, MinPointsPerCell = 8,
            MinRangeM = 0.3f, MaxRangeM = 5.0f, MinRadialStepM = 0.05f, AssumedValidFraction = 1.0f,
        };

        private static CameraFrame Frame(DateTime t)
        {
            var proj = MakeProjection();
            var proc = new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = proj }, PolarCfg());
            var prob = new Image<Gray>(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    prob[x, y].Value = 255;   // vsude cesta

            return new CameraFrame
            {
                Name = "Cam",
                TimeStamp = t,
                Grid = proc.BuildGrid(FlatGroundDepth(), proj),
                ImageProbability = prob,
            };
        }

        // --- fuze ---

        /// <summary>Fuze naplnena merenimi rychlosti (default 0 = robot stoji v pocatku).</summary>
        private static AsyncFusionEngine Engine(DateTime from, int count = 30, double stepS = 0.1,
                                                double speed = 0.0)
        {
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(2));
            for (int i = 0; i < count; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(speed, 0.05, from.AddSeconds(i * stepS), "Odo"));
            return e;
        }

        private static LocalNavigator MakeNavigator(AsyncFusionEngine engine, bool withPlanner = true)
        {
            var proj = MakeProjection();
            return new LocalNavigator(
                engine,
                depthProjections: _ => proj,
                colorProjections: _ => proj,
                pathPlanner: withPlanner
                    ? new PathPlanner(new TrapezoidMotionProfile(0.8, Math.PI / 6, 0.3, Profile.Rozchod))
                    : null,
                gridMessagePeriod: TimeSpan.Zero);
        }

        /// <summary>
        /// Beh navigatoru pro test: nastartuje ho, sbira jeho vystupni zpravy a umi poslat snimek
        /// a POCKAT, az ho vlakno fronty opravdu zpracuje.
        ///
        /// <para>POZOR: <see cref="MessageTarget.Stop"/> frontu <b>trvale uzavre</b> (TryComplete),
        /// takze po nem uz zadny <c>Post</c> neprojde. Proto se v ramci jednoho testu smi zastavit
        /// az na konci - drive to zpusobovalo, ze druhy "pump" tise nic nedelal a testy prochazely
        /// naprazdno.</para>
        /// </summary>
        private sealed class Session : IDisposable
        {
            public readonly LocalNavigator Nav;
            public readonly List<Message> Messages = new List<Message>();
            private readonly DelegateTarget sink;
            private readonly IDisposable connection;

            public Session(LocalNavigator nav)
            {
                Nav = nav;
                sink = new DelegateTarget(m => { lock (Messages) Messages.Add(m); });
                sink.Start();
                connection = nav.Output.Connect(sink);
                nav.Start();
            }

            /// <summary>Posle snimek a pocka, az se zpracuje (nebo zahodi).</summary>
            public void Send(CameraFrame frame)
            {
                long before = Nav.ProcessedFrames + Nav.DroppedFrames;
                Nav.Post(frame);

                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    if (Nav.ProcessedFrames + Nav.DroppedFrames > before) return;
                    Thread.Sleep(2);
                }
                Assert.Fail("navigator snimek nezpracoval do 5 s");
            }

            public List<Message> Snapshot()
            {
                lock (Messages) return new List<Message>(Messages);
            }

            public void Dispose()
            {
                Nav.Stop();
                connection.Dispose();
                sink.Stop();
            }
        }

        // ---------------- testy ----------------

        [Test]
        public void BezCile_MapuAkumulujeAlePlanNeemituje()
        {
            var nav = MakeNavigator(Engine(T0));
            using var s = new Session(nav);

            s.Send(Frame(T0.AddSeconds(1.0)));

            Assert.That(nav.ProcessedFrames, Is.EqualTo(1));
            Assert.That(nav.LastPlan, Is.Null, "bez cile se neplanuje");
            var msgs = s.Snapshot();
            Assert.That(msgs.Exists(m => m is LocalPlanMsg), Is.False, "bez cile zadny LocalPlanMsg");
            Assert.That(msgs.Exists(m => m is OccupancyGridMsg), Is.True, "mapa se ma akumulovat a emitovat");

            // Zem pred robotem musi byt v gridu videt jako volna.
            Assert.That(nav.Grid.LogOddsOcc(nav.Grid.CellX(1.5), nav.Grid.CellY(0)), Is.LessThan(0f));
        }

        [Test]
        public void SCilem_NaplanujeAEmitujePlan()
        {
            var nav = MakeNavigator(Engine(T0));
            nav.SetGoal(3.0, 0.0);
            using var s = new Session(nav);

            s.Send(Frame(T0.AddSeconds(1.0)));

            Assert.That(nav.LastPlan, Is.Not.Null, "s cilem se ma planovat");
            Assert.That(nav.LastPlan.HasPath, Is.True, "pred robotem je volno - plan ma vzniknout");

            var plan = s.Snapshot().Find(m => m is LocalPlanMsg) as LocalPlanMsg;
            Assert.That(plan, Is.Not.Null, "LocalPlanMsg se ma emitovat");
            Assert.That(plan.WayPoints, Is.Not.Null);
            Assert.That(plan.WayPoints.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(plan.RequestedGoalX, Is.EqualTo(3.0));
            Assert.That(plan.ComputeMs, Is.GreaterThan(0), "doba vypoctu se ma vyplnit");
        }

        [Test]
        public void SnimekMimoOknoHistorie_SeZahodi()
        {
            // Fuze zna cas az od T0+10 s; snimek z T0 je hluboko pred oknem -> GetStateAt vrati null.
            var nav = MakeNavigator(Engine(T0.AddSeconds(10)));
            nav.SetGoal(3.0, 0.0);
            using var s = new Session(nav);

            s.Send(Frame(T0));

            Assert.That(nav.DroppedFrames, Is.EqualTo(1), "snimek se ma zahodit");
            Assert.That(nav.ProcessedFrames, Is.EqualTo(0));
            Assert.That(nav.LastPlan, Is.Null, "snimek se spatnou pozou se nesmi zapsat ani planovat");
            Assert.That(nav.Grid.State(nav.Grid.CellX(1.5), nav.Grid.CellY(0)),
                        Is.EqualTo(CellState.Unknown), "do mapy se nesmelo nic zapsat");
        }

        [Test]
        public void PredaRegulatorNizsiSmycce()
        {
            var engine = Engine(T0);
            var nav = MakeNavigator(engine);
            using var loop = new ControlLoop(engine, new DummyMotors(), new VirtualClock(), new Scheduler(),
                                             period: TimeSpan.FromMilliseconds(100));
            nav.ControlLoop = loop;
            nav.SetGoal(3.0, 0.0);
            using var s = new Session(nav);

            Assert.That(loop.Regulator, Is.Null, "na zacatku zadny regulator");

            s.Send(Frame(T0.AddSeconds(1.0)));

            Assert.That(loop.Regulator, Is.Not.Null, "naplanovana draha se ma predat nizsi smycce");
        }

        // ---------------- kontrola rozjete drahy proti aktualni mape ----------------

        /// <summary>
        /// Scena pro kontrolu rozjete drahy: robot JEDE (~0,5 m/s), dostal cil a drahu. Pak se
        /// volitelne pred nim objevi zed a cil se zrusi, takze novy plan uz nevznikne a robot je
        /// odkazany na tu drahu, kterou uz jede.
        /// </summary>
        private sealed class DrivingScene : IDisposable
        {
            public LocalNavigator Nav;
            public ControlLoop Loop;
            public IRegulator FirstRegulator;
            public Session Session;
            public double PoseX, PoseY;

            public static DrivingScene Create()
            {
                // Kratky beh (0,5 s), aby robot neujel daleko, ale mel nenulovou rychlost.
                var engine = Engine(T0, count: 6, stepS: 0.1, speed: 0.5);
                var nav = MakeNavigator(engine);
                var loop = new ControlLoop(engine, new DummyMotors(), new VirtualClock(), new Scheduler(),
                                           period: TimeSpan.FromMilliseconds(100));
                nav.ControlLoop = loop;

                var pose = engine.GetStateAt(T0.AddSeconds(0.5));
                Assert.That(pose, Is.Not.Null);
                Assert.That(Math.Abs(pose.V), Is.GreaterThan(0.2), "predpoklad sceny: robot jede");

                nav.SetGoal(pose.X + 3.0, pose.Y);
                var session = new Session(nav);
                session.Send(Frame(T0.AddSeconds(0.5)));

                var first = loop.Regulator;
                Assert.That(first, Is.Not.Null, "predpoklad sceny: robot dostal drahu");

                return new DrivingScene
                {
                    Nav = nav, Loop = loop, FirstRegulator = first, Session = session,
                    PoseX = pose.X, PoseY = pose.Y,
                };
            }

            /// <summary>Zapise do mapy zed pres celou sirku drahy <paramref name="aheadM"/> pred robotem.</summary>
            public void BlockAhead(double aheadM)
            {
                var g = Nav.Grid;
                for (double x = aheadM; x <= aheadM + 0.2; x += g.Resolution / 2)
                    for (double y = -1.0; y <= 1.0; y += g.Resolution / 2)
                        for (int k = 0; k < 10; k++)
                            g.ObserveOccupied(g.CellX(PoseX + x), g.CellY(PoseY + y), 1f);
            }

            /// <summary>Zrusi cil (novy plan uz nevznikne) a posle dalsi snimek.</summary>
            public void RunWithoutNewPlan()
            {
                Nav.ClearGoal();
                Session.Send(Frame(T0.AddSeconds(0.55)));
            }

            public void Dispose()
            {
                Session.Dispose();
                Loop.Stop();
            }
        }

        [Test]
        public void PrekazkaNaRozjeteDraze_ZpusobiNouzoveZastaveni()
        {
            // Novy plan nevznikl a draha, po ktere robot jede, uz podle AKTUALNI mapy koliduje.
            // Rizeni se musi zahodit OKAMZITE - watchdog nizsi smycky (500 ms + brzdna draha)
            // by byl pozde.
            using var scene = DrivingScene.Create();

            // Zed musi lezet UVNITR okna zavazku, na ktere PathCollides drahu kontroluje:
            //     check = v^2/(2*MaxDeceleration) + v*Ts + rozliseni
            // Pri v = 0,5 m/s to je 0,52 m pro MaxDecceleration 0,30, ale jen 0,35 m pro 0,50 -
            // a odstup klesne pod SafeDist az od (vzdalenost zdi - SafeDist). Drive tu bylo natvrdo
            // 0,8 m, takze test spadl, jakmile se v Profile zvysila decelerace (silnejsi brzda =
            // kratsi zavazek = kratsi dohled, coz je spravne chovani). 0,5 m se vejde do obou.
            scene.BlockAhead(0.5);

            scene.RunWithoutNewPlan();

            Assert.That(scene.Loop.Regulator, Is.Null,
                        "prekazka na rozjete draze musi rizeni zahodit okamzite");
            Assert.That(scene.Nav.LastPlan.Status, Is.EqualTo(LocalPlanStatus.AbortedCollision));
            Assert.That(scene.Nav.LastPlan.MinClearanceM, Is.GreaterThanOrEqualTo(0),
                        "hlasi se vzdalenost k nalezene kolizi");
        }

        [Test]
        public void VolnaDraha_SeNezahazuje()
        {
            // Opacny smer: novy plan nevznikl, ale draha je porad volna -> rizeni se NESMI zahodit,
            // dobrzdeni resi (rizene) watchdog nizsi smycky.
            using var scene = DrivingScene.Create();

            scene.RunWithoutNewPlan();

            Assert.That(scene.Loop.Regulator, Is.SameAs(scene.FirstRegulator),
                        "volna draha se nema zahazovat - dobrzdeni resi watchdog");
        }

        [Test]
        public void VzdalenaPrekazka_NezpusobiNouzoveZastaveni()
        {
            // Prekazka daleko pred robotem (mimo brzdnou drahu) neni duvod k nouzovemu zastaveni -
            // vyresi ji priste uspesne preplanovani objezdem, pripadne rizene dobrzdeni.
            using var scene = DrivingScene.Create();
            scene.BlockAhead(3.0);

            scene.RunWithoutNewPlan();

            Assert.That(scene.Loop.Regulator, Is.SameAs(scene.FirstRegulator),
                        "vzdalena prekazka neni nouzovy stav");
        }

        [Test]
        public void StojiciRobot_NouzoveNezastavuje()
        {
            // Kdyz robot stoji (v = 0), brzdna draha je nulova - neni co nouzove zastavovat.
            var engine = Engine(T0);   // merenia rychlosti 0
            var nav = MakeNavigator(engine);
            using var loop = new ControlLoop(engine, new DummyMotors(), new VirtualClock(), new Scheduler(),
                                             period: TimeSpan.FromMilliseconds(100));
            nav.ControlLoop = loop;
            nav.SetGoal(3.0, 0.0);
            using var s = new Session(nav);

            s.Send(Frame(T0.AddSeconds(1.0)));
            var regulator = loop.Regulator;
            Assert.That(regulator, Is.Not.Null);

            var g = nav.Grid;
            for (double x = 0.8; x <= 1.0; x += g.Resolution / 2)
                for (double y = -1.0; y <= 1.0; y += g.Resolution / 2)
                    for (int k = 0; k < 10; k++)
                        g.ObserveOccupied(g.CellX(x), g.CellY(y), 1f);
            nav.ClearGoal();

            s.Send(Frame(T0.AddSeconds(1.05)));

            Assert.That(loop.Regulator, Is.SameAs(regulator), "stojici robot nema co nouzove zastavovat");
        }

        [Test]
        public void CilLzeZrusit()
        {
            var nav = MakeNavigator(Engine(T0));

            Assert.That(nav.Goal, Is.Null);
            nav.SetGoal(1, 2);
            Assert.That(nav.Goal, Is.Not.Null);
            Assert.That(nav.Goal.Value.X, Is.EqualTo(1));
            nav.ClearGoal();
            Assert.That(nav.Goal, Is.Null);
        }

        [Test]
        public void BezPathPlanneru_PlanVznikneAleRegulatorNe()
        {
            var engine = Engine(T0);
            var nav = MakeNavigator(engine, withPlanner: false);
            using var loop = new ControlLoop(engine, new DummyMotors(), new VirtualClock(), new Scheduler(),
                                             period: TimeSpan.FromMilliseconds(100));
            nav.ControlLoop = loop;
            nav.SetGoal(3.0, 0.0);
            using var s = new Session(nav);

            s.Send(Frame(T0.AddSeconds(1.0)));

            Assert.That(nav.LastPlan?.HasPath, Is.True, "plan se pocita i bez IPathPlanneru");
            Assert.That(loop.Regulator, Is.Null, "bez IPathPlanneru se regulator nesestavuje");
        }
    }
}
