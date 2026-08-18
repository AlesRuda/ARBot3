using System.Linq;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.OsmNav.Navigation;

/// <summary>
/// Testy globalni navigace nad syntetickou siti (viz doc/global-navigation-runtime.md).
/// Vrstva je ciste algoritmicka - testuje se bez occupancy gridu i bez HW, jen pres
/// <see cref="ILocalGoalSink"/>.
/// </summary>
public class GlobalNavigatorTests
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Zaznamenava, co globalni vrstva predala dolu.</summary>
    private sealed class FakeLocalGoal : ILocalGoalSink
    {
        public (double X, double Y)? Goal;
        public int SetCount, ClearCount;

        public void SetGoal(double worldX, double worldY, double corridorWidthM = 0)
        {
            Goal = (worldX, worldY);
            SetCount++;
        }

        public void ClearGoal()
        {
            Goal = null;
            ClearCount++;
        }
    }

    /// <summary>
    /// Rovna obousmerna cesta 200 m na vychod, uzly po 20 m.
    /// <para>POZOR: sit je edge-based, takze prechody grafu jsou <b>odboceni</b> a musi se
    /// registrovat zvlast (<c>AddTurn</c>) - bez nich by trasa neexistovala. U-turn na teze
    /// ceste se vynechava, stejne jako to dela <c>GraphBuilder</c>.</para>
    /// </summary>
    private static RoadNetwork StraightEastRoad(GeoReference origin)
    {
        var builder = new RoadNetwork.Builder();
        var edges = new List<Edge>();
        Node prev = null;

        for (int i = 0; i <= 10; i++)
        {
            var node = new Node(i + 1, origin.ToLLA(i * 20.0, 0), 3.0);
            if (prev != null)
            {
                edges.Add(builder.AddEdge(prev, node, 20.0, wayId: 1, traversalCost: 20.0));
                edges.Add(builder.AddEdge(node, prev, 20.0, wayId: 1, traversalCost: 20.0));
            }
            prev = node;
        }

        foreach (var inEdge in edges)
            foreach (var outEdge in edges)
            {
                if (outEdge.From.Id != inEdge.To.Id) continue;
                if (outEdge.To.Id == inEdge.From.Id && outEdge.WayId == inEdge.WayId) continue;   // U-turn
                builder.AddTurn(inEdge, outEdge, 0.0);
            }

        return builder.Build();
    }

    private static GlobalNavigator Create(GeoReference origin, FakeLocalGoal sink,
                                          GlobalNavigatorConfig cfg = null)
        => new GlobalNavigator(StraightEastRoad(origin), origin, sink,
                               cfg ?? new GlobalNavigatorConfig());

    [Test]
    public void WithoutGoal_ReportsNoGoal_AndDoesNotDriveLocalLayer()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var nav = Create(origin, sink);

        var msg = nav.Step(0, 0, DateTime.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(nav.Status, Is.EqualTo(GlobalNavStatus.NoGoal));
            Assert.That(sink.SetCount, Is.EqualTo(0), "bez cile se lokalni vrstva nema krmit");
            Assert.That(msg!.HasGoal, Is.False);
        });
    }

    [Test]
    public void DistantGoal_HandsDownCarrotOnMapEdge()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig();
        var nav = Create(origin, sink, cfg);

        nav.SetGoal(origin.ToLLA(200, 0));          // cil 200 m na vychod
        nav.Step(0, 0, DateTime.UtcNow);            // robot v pocatku

        Assert.That(sink.Goal, Is.Not.Null, "lokalni vrstva ma dostat mrkev");
        Assert.Multiple(() =>
        {
            Assert.That(nav.Status, Is.EqualTo(GlobalNavStatus.Driving));
            Assert.That(sink.Goal!.Value.X, Is.EqualTo(cfg.CarrotHalfExtentM).Within(0.2),
                        "mrkev ma lezet na okraji lokalni mapy, ne par metru pred robotem");
            Assert.That(sink.Goal.Value.Y, Is.EqualTo(0).Within(0.2));
        });
    }

    [Test]
    public void NearGoal_InsideLocalMap_HandsDownTheGoalItself()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var nav = Create(origin, sink);

        nav.SetGoal(origin.ToLLA(4, 0));            // cil uvnitr lokalni mapy
        nav.Step(0, 0, DateTime.UtcNow);

        Assert.That(sink.Goal, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(nav.Status, Is.EqualTo(GlobalNavStatus.GoalInMap));
            Assert.That(sink.Goal!.Value.X, Is.EqualTo(4).Within(0.3), "mrkev = primo cil");
        });
    }

    [Test]
    public void AtGoal_ReportsArrived()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var nav = Create(origin, sink,
                         new GlobalNavigatorConfig());

        nav.SetGoal(origin.ToLLA(100, 0));
        nav.Step(100, 0, DateTime.UtcNow);          // robot uz na cili

        Assert.That(nav.Status, Is.EqualTo(GlobalNavStatus.Arrived));
    }

    [Test]
    public void FarFromNetwork_ReportsOffRoute()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig { OffRouteMaxM = 15.0 };
        var nav = Create(origin, sink, cfg);

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(0, 40, DateTime.UtcNow);           // 40 m na sever od site

        Assert.That(nav.Status, Is.EqualTo(GlobalNavStatus.OffRoute));
    }

    /// <summary>Trasa se posila jako geometrie do mapy (vrstva „Trasa / graf").</summary>
    [Test]
    public void RouteMessage_CarriesRouteGeometryAndMarkers()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var nav = Create(origin, sink);
        var t = DateTime.UtcNow;

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(0, 0, t);

        var msg = nav.BuildRouteMessageIfDue(0, 0, t);

        Assert.That(msg, Is.Not.Null, "prvni cyklus ma trasu poslat");
        Assert.Multiple(() =>
        {
            Assert.That(msg!.Vertexes.Count, Is.GreaterThan(1), "trasa ma mit vrcholy");
            Assert.That(msg.Edges.Count, Is.EqualTo(msg.Vertexes.Count - 1));
            Assert.That(msg.Edges.TrueForAll(e => e.HightLight), Is.True, "trasa ma byt zvyraznena");
            Assert.That(msg.TargetX, Is.EqualTo(200).Within(1.0), "znacka cile");
            Assert.That(msg.ResultX, Is.Not.Null, "znacka mrkve");
        });
    }

    /// <summary>Beze zmeny trasy se geometrie neposila kazdy cyklus - je to nejvetsi z techto zprav.</summary>
    [Test]
    public void RouteMessage_NotResentWhileRouteUnchanged()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var nav = Create(origin, sink);
        var t = DateTime.UtcNow;

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(0, 0, t);

        Assert.That(nav.BuildRouteMessageIfDue(0, 0, t), Is.Not.Null);
        Assert.That(nav.BuildRouteMessageIfDue(0, 0, t.AddMilliseconds(200)), Is.Null,
                    "hned potom uz ne");
    }

    // ---------------- Detektory zaseku (faze 4) ----------------

    /// <summary>
    /// Prehrazena cesta: lokalni vrstva opakovane hlasi, ze se neda projet -> hrana se uzavre.
    /// Fyzicka zabrana blokuje oba smery, takze se musi zavrit i reverzni hrana.
    /// </summary>
    [Test]
    public void RepeatedPlanFailures_CloseTheEdgeInBothDirections()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig { BlockedPlanCount = 3 };
        var nav = Create(origin, sink, cfg);
        var t = DateTime.UtcNow;

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(10, 0, t);

        for (int i = 0; i < cfg.BlockedPlanCount; i++)
            nav.OnLocalPlan(LocalPlanStatus.NoRoute);

        nav.Step(10, 0, t.AddSeconds(1));

        Assert.That(nav.Closures, Is.Not.Empty, "opakovane selhani planovani ma uzavrit hranu");
        var closure = nav.Closures.First();
        Assert.Multiple(() =>
        {
            Assert.That(closure.Reason, Is.EqualTo(ClosureReason.RoadBlocked));
            Assert.That(closure.Hard, Is.True, "prehrazeni je tvrde uzavreni, ne jen penalizace");
        });
    }


    /// <summary>
    /// UNIK z blokovane bunky NENI selhani planu: robot jede (jen kratce a pomalu), takze se serie
    /// selhani nesmi scitat - jinak by uvaznuti nakonec zavrelo hranu, ktera je v poradku.
    /// Nalez ze zaznamu 20260818-093903.rec, viz doc/occupancy-and-local-planning.md.
    /// </summary>
    [Test]
    public void EscapingBlocked_IsNotAPlanFailure()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig { BlockedPlanCount = 3 };
        var nav = Create(origin, sink, cfg);
        var t = DateTime.UtcNow;

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(10, 0, t);

        for (int i = 0; i < 10; i++)
            nav.OnLocalPlan(LocalPlanStatus.EscapingBlocked);

        nav.Step(10, 0, t.AddSeconds(1));

        Assert.That(nav.Closures, Is.Empty, "unik se nesmi scitat jako selhani planu");
    }

    /// <summary>Uspesny plan mezi selhanimi vynuluje pocitadlo - jinak by staciler nasbirat selhani kdykoli.</summary>
    [Test]
    public void PlanFailures_AreCountedOnlyWhenConsecutive()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig { BlockedPlanCount = 3 };
        var nav = Create(origin, sink, cfg);
        var t = DateTime.UtcNow;

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(10, 0, t);

        nav.OnLocalPlan(LocalPlanStatus.NoRoute);
        nav.OnLocalPlan(LocalPlanStatus.NoRoute);
        nav.OnLocalPlan(LocalPlanStatus.Ok);      // preruseni serie
        nav.OnLocalPlan(LocalPlanStatus.NoRoute);

        nav.Step(10, 0, t.AddSeconds(1));

        Assert.That(nav.Closures, Is.Empty, "prerusena serie nesmi hranu uzavrit");
    }

    /// <summary>
    /// Bloudeni: robot ujede kus cesty, ale k cili se nepriblizi. Reakce je jen zdrazeni hrany -
    /// falesny poplach nesmi znicit trasu.
    /// </summary>
    /// <summary>Jizda sem a tam: ujeta draha roste, vzdalenost k cili ne.</summary>
    private static void Wander(GlobalNavigator nav, DateTime t, int steps)
    {
        for (int i = 0; i < steps; i++)
            nav.Step(10 + (i % 2 == 0 ? 0 : 1), 0, t.AddSeconds(i));
    }

    [Test]
    public void NoProgress_FirstTime_OnlyPenalizesTheEdge()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig { ProgressWindowM = 5.0 };
        var nav = Create(origin, sink, cfg);

        nav.SetGoal(origin.ToLLA(200, 0));
        Wander(nav, DateTime.UtcNow, steps: 7);

        Assert.That(nav.Closures, Is.Not.Empty, "bloudeni ma hranu penalizovat");
        Assert.That(nav.Closures.First().Hard, Is.False,
                    "prvni poplach je jen zdrazeni - falesny nesmi znicit trasu");
    }

    [Test]
    public void NoProgress_RepeatedOnSameEdge_EscalatesToClosure()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig { ProgressWindowM = 5.0 };
        var nav = Create(origin, sink, cfg);

        nav.SetGoal(origin.ToLLA(200, 0));
        Wander(nav, DateTime.UtcNow, steps: 16);

        Assert.That(nav.Closures.First().Hard, Is.True,
                    "opakovane bloudeni na teze hrane uz znamena uzavreni");
    }

    /// <summary>Pod nouzovym zastavenim robot legitimne stoji - zasek se nesmi hlasit.</summary>
    [Test]
    public void NoMotionUnderEmergencyStop_DoesNotCloseAnything()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var cfg = new GlobalNavigatorConfig
        {
            NoMotionSec = TimeSpan.FromSeconds(1),
            EscalateSec = TimeSpan.Zero,
            MaxRecoveries = 0,
        };
        var nav = Create(origin, sink, cfg);
        var t = DateTime.UtcNow;

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.OnLocalPlan(LocalPlanStatus.Ok);          // plan je platny
        nav.OnDriveCommand(emergencyStopActive: true);

        for (int i = 0; i < 10; i++)
            nav.Step(10, 0, t.AddSeconds(i));         // robot stoji

        Assert.That(nav.Closures, Is.Empty, "pod stopem se zasek hlasit nesmi");
    }

    [Test]
    public void Cancel_ClearsLocalGoal()
    {
        var origin = Origin();
        var sink = new FakeLocalGoal();
        var nav = Create(origin, sink);

        nav.SetGoal(origin.ToLLA(200, 0));
        nav.Step(0, 0, DateTime.UtcNow);
        nav.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(nav.Status, Is.EqualTo(GlobalNavStatus.NoGoal));
            Assert.That(sink.ClearCount, Is.GreaterThan(0), "zruseni musi dojit i dolu");
        });
    }
}
