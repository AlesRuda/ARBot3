using ARBot.Common.Common;
using ARBot.Common.Maps.OsmNav.Navigation;

namespace ARBot.Common.Tests.OsmNav.Navigation;

/// <summary>
/// Testy vyberu "mrkve" - bodu na trase, ktery se predava lokalnimu planovaci.
/// Pravidlo: posledni bod trasy jeste UVNITR lokalni mapy, pocitano od prumetu robota
/// k PRVNIMU vystupu z ni. Viz doc/global-navigation-runtime.md.
/// </summary>
public class RouteCarrotTests
{
    /// <summary>Polomer lokalni mapy pouzity v testech [m] (grid 12,8 m => 6,4).</summary>
    private const double Half = 6.4;

    private static Point2D P(double x, double y) => new Point2D(x, y);

    [Test]
    public void Carrot_RouteLeavingMap_IsOnTheBoundary()
    {
        // Trasa vede z pocatku 20 m na vychod, robot stoji v pocatku.
        var route = new[] { P(0, 0), P(20, 0) };

        var carrot = RouteCarrot.Find(route, robot: P(0, 0), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(carrot!.Value.X, Is.EqualTo(Half).Within(0.01), "mrkev ma lezet na hranici mapy");
            Assert.That(carrot.Value.Y, Is.EqualTo(0).Within(0.01));
        });
    }

    /// <summary>Cil uvnitr mapy => mrkev je primo cil (zadny zvlastni "finalni dojezd").</summary>
    [Test]
    public void Carrot_GoalInsideMap_IsTheGoalItself()
    {
        var route = new[] { P(0, 0), P(3, 0) };

        var carrot = RouteCarrot.Find(route, robot: P(0, 0), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(carrot!.Value.X, Is.EqualTo(3).Within(0.01));
            Assert.That(carrot.Value.Y, Is.EqualTo(0).Within(0.01));
        });
    }

    /// <summary>
    /// Trasa mapu opusti a zase se do ni vrati: mrkev musi byt na PRVNIM vystupu.
    /// Pozdejsi kus uvnitr mapy je s robotem nespojeny - cil na nem by lokalni planovac
    /// nedokazal poctive obslouzit.
    /// </summary>
    [Test]
    public void Carrot_RouteReenteringMap_StopsAtFirstExit()
    {
        // Vychod za hranici, kus na sever a zpatky na zapad skrz mapu.
        var route = new[] { P(0, 0), P(20, 0), P(20, 1), P(0, 1) };

        var carrot = RouteCarrot.Find(route, robot: P(0, 0), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(carrot!.Value.X, Is.EqualTo(Half).Within(0.01),
                        "mrkev ma byt na prvnim vystupu, ne na navratove vetvi");
            Assert.That(carrot.Value.Y, Is.EqualTo(0).Within(0.01));
        });
    }

    /// <summary>
    /// Robot VEDLE trasy: mrkev lezi porad pulku mapy PRED kolmym prumetem, ne v nem. Do 29. 9.
    /// 2026 se nad 5,9 m (ctverec kolem robota uz prumet neobsahoval) vracel prumet samotny a robot
    /// mel jet kolmo na trasu - nepostupoval a detektor bez postupu zavrel hranu (Modrany,
    /// ~100 s pri odstupu 6,5-9 m). Plati i daleko za OffRouteMaxM (rozhodnuti autora).
    /// </summary>
    [TestCase(3.0)]
    [TestCase(8.0)]
    [TestCase(20.0)]
    public void Carrot_RobotBesideRoute_LiesAheadOfProjection_NotAtIt(double offset)
    {
        var route = new[] { P(-50, 0), P(50, 0) };

        var carrot = RouteCarrot.Find(route, robot: P(0, offset), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(carrot!.Value.X, Is.EqualTo(Half).Within(0.01),
                        "mrkev ma byt pulku mapy pred prumetem (0, 0), ne v nem");
            Assert.That(carrot.Value.Y, Is.EqualTo(0).Within(0.01), "mrkev lezi na trase");
        });
    }

    /// <summary>
    /// Robot vedle trasy pred zatackou: pravidlo "prvni vystup ze ctverce" plati beze zmeny, jen
    /// ctverec je kolem prumetu. Trasa jde 3 m na vychod a pak na sever, takze ze ctverce kolem
    /// prumetu (0, 0) vystoupi horni hranou.
    /// </summary>
    [Test]
    public void Carrot_RobotBesideRouteBeforeTurn_FollowsTheTurn()
    {
        var route = new[] { P(-50, 0), P(3, 0), P(3, 50) };

        var carrot = RouteCarrot.Find(route, robot: P(0, -10), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(carrot!.Value.X, Is.EqualTo(3).Within(0.01));
            Assert.That(carrot.Value.Y, Is.EqualTo(Half).Within(0.01), "vystup ze ctverce kolem prumetu");
        });
    }

    /// <summary>Robot pred zacatkem trasy: prumet je prvni bod, mrkev pulku mapy po trase od nej.</summary>
    [Test]
    public void Carrot_RobotBeforeRouteStart_MeasuredFromFirstPoint()
    {
        var route = new[] { P(0, 0), P(50, 0) };

        var carrot = RouteCarrot.Find(route, robot: P(-10, 10), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.That(carrot!.Value.X, Is.EqualTo(Half).Within(0.01));
        Assert.That(carrot.Value.Y, Is.EqualTo(0).Within(0.01));
    }

    /// <summary>Robot uprostred trasy: mrkev se pocita od jeho prumetu, ne od zacatku trasy.</summary>
    [Test]
    public void Carrot_RobotMidRoute_MeasuredFromItsProjection()
    {
        var route = new[] { P(-20, 0), P(20, 0) };

        var carrot = RouteCarrot.Find(route, robot: P(0, 0), halfExtentM: Half);

        Assert.That(carrot, Is.Not.Null);
        Assert.That(carrot!.Value.X, Is.EqualTo(Half).Within(0.01));
    }
}
