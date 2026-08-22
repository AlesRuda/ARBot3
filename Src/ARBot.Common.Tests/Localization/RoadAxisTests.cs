using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Mapova protistrana koridoru: vztah pozy k ose nejblizsi hrany. Teprve rozdil proti
/// <see cref="RoadCorridor"/> z kamer je merenie do fuze.
/// Viz doc/map-correlation-localization.md.
/// </summary>
public class RoadAxisTests
{
    // Prima cesta na vychod (uzly na -30 a +30 m), sirka 4 m.
    private static (Maps.OsmNav.Graph.RoadNetwork net, Coordinates.GeoReference origin) EastRoad(double width = 4.0)
    {
        var origin = CorrelationTestScenes.Origin();
        return (CorrelationTestScenes.StraightEastRoad(origin, width), origin);
    }

    [Test]
    public void NaOse_daNuluAStejnySmer()
    {
        var (net, origin) = EastRoad();

        var m = RoadAxis.Match(net, origin, x: 0, y: 0, theta: 0);

        Assert.That(m.Found, Is.True);
        Assert.That(m.Lateral, Is.EqualTo(0).Within(0.01));
        Assert.That(m.HeadingRelRad, Is.EqualTo(0).Within(0.001));
        Assert.That(m.WidthM, Is.EqualTo(4.0).Within(0.01));
    }

    [Test]
    public void VlevoOdOsy_maKladnyOdstup()
    {
        // Cesta vede na vychod, robot je 0,7 m NA SEVER od osy. Pri kurzu na vychod je sever vlevo.
        var (net, origin) = EastRoad();

        var m = RoadAxis.Match(net, origin, x: 5, y: 0.7, theta: 0);

        Assert.That(m.Lateral, Is.EqualTo(0.7).Within(0.02));
    }

    [Test]
    public void VpravoOdOsy_maZapornyOdstup()
    {
        var (net, origin) = EastRoad();

        var m = RoadAxis.Match(net, origin, x: 5, y: -0.5, theta: 0);

        Assert.That(m.Lateral, Is.EqualTo(-0.5).Within(0.02));
    }

    [Test]
    public void JizdaProtiSmeruHrany_neprohodiStrany()
    {
        // Hrany site jsou ORIENTOVANE. Kdyz robot jede proti smeru hrany, musi "vlevo" zustat
        // vlevo z pohledu robotu - jinak by znamenko pricne korekce preskakovalo.
        var (net, origin) = EastRoad();

        // Kurz na zapad (pi); robot je na SEVER od osy, coz je pri jizde na zapad VPRAVO.
        var m = RoadAxis.Match(net, origin, x: 5, y: 0.7, theta: Math.PI);

        Assert.That(m.Found, Is.True);
        Assert.That(m.Lateral, Is.EqualTo(-0.7).Within(0.02),
                    "pri jizde na zapad je sever vpravo, tedy zaporny odstup");
        Assert.That(m.HeadingRelRad, Is.EqualTo(0).Within(0.001), "sklon je stejny, jen opacny smer");
    }

    [Test]
    public void StoceniRobotu_seProjeviVeSmeru()
    {
        var (net, origin) = EastRoad();

        // Robot stoceny o 10 stupnu vlevo -> cesta se vuci nemu jevi stocena o -10 stupnu.
        var m = RoadAxis.Match(net, origin, x: 0, y: 0, theta: 10 * Math.PI / 180);

        Assert.That(m.HeadingRelRad * 180 / Math.PI, Is.EqualTo(-10).Within(0.5));
    }

    [Test]
    public void SirkaSeBereZMapy()
    {
        var (net, origin) = EastRoad(width: 2.5);

        var m = RoadAxis.Match(net, origin, 0, 0, 0);

        Assert.That(m.WidthM, Is.EqualTo(2.5).Within(0.01));
    }

    [Test]
    public void NormalaAOsaJsouKonzistentni()
    {
        var (net, origin) = EastRoad();
        double x = 5, y = 0.7;

        var m = RoadAxis.Match(net, origin, x, y, 0);

        // Prumet pozy na osu + odstup podel normaly musi dat zpatky pozu.
        Assert.That(m.AxisX + m.Lateral * m.NormalX, Is.EqualTo(x).Within(0.02));
        Assert.That(m.AxisY + m.Lateral * m.NormalY, Is.EqualTo(y).Within(0.02));
    }

    [Test]
    public void BezSite_neniNalezeno()
    {
        var m = RoadAxis.Match(null, CorrelationTestScenes.Origin(), 0, 0, 0);

        Assert.That(m.Found, Is.False);
    }
}
