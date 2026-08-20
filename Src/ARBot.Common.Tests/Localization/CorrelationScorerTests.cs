using System;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>Testy skore shody dukazniho oblaku s mapou (viz doc/map-correlation-localization.md).</summary>
public class CorrelationScorerTests
{
    private const double RobotX = 0.0;
    private const double RobotY = 0.0;

    /// <summary>Postavi scorer pro primou cestu a grid s danou chybou pozy.</summary>
    private static CorrelationScorer Build(double dx0, double dy0, double phi0, out MapCorrelatorConfig cfg)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
        cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, RobotX, RobotY, dx0, dy0, phi0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        return new CorrelationScorer(cloud, raster, RobotX, RobotY);
    }

    [Test]
    public void Score_BezChybyPozy_JeJednickaVNule()
    {
        var scorer = Build(0, 0, 0, out _);

        // Grid je presna kopie mapy => v (0,0,0) musi souhlasit KAZDA bunka.
        Assert.That(scorer.Score(0, 0, 0, stride: 1), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void Score_JeVMezichMinusJednaAzJedna()
    {
        var scorer = Build(0, 0, 0, out _);

        foreach (double d in new[] { -2.0, -0.5, 0.0, 0.5, 2.0 })
        {
            double s = scorer.Score(0, d, 0, stride: 1);
            Assert.That(s, Is.InRange(-1.0, 1.0), $"Skore mimo mez pri dy = {d}.");
        }
    }

    [Test]
    public void Score_MaximumJeVeSkutecnePricneChybe()
    {
        // Robot je ve skutecnosti 0,8 m severne od toho, kde si mysli.
        var scorer = Build(0.0, 0.8, 0.0, out _);

        double atTruth = scorer.Score(0.0, 0.8, 0.0, stride: 1);
        double atZero = scorer.Score(0.0, 0.0, 0.0, stride: 1);

        Assert.That(atTruth, Is.EqualTo(1.0).Within(0.02));
        Assert.That(atTruth, Is.GreaterThan(atZero));
    }

    [Test]
    public void Score_PodelPrimeCesty_JePlocheSkore()
    {
        // Klicove tvrzeni navrhu: podel prime cesty korelace NIC nerika.
        var scorer = Build(0, 0, 0, out _);

        double s0 = scorer.Score(0.0, 0.0, 0.0, stride: 1);
        double s1 = scorer.Score(1.5, 0.0, 0.0, stride: 1);

        Assert.That(s1, Is.EqualTo(s0).Within(0.02),
                    "Posun podel prime cesty nesmi skore menit - jinak by odhad predstiral podelnou informaci.");
    }

    [Test]
    public void Score_ChybaKurzu_SnizujeSkoreVNule()
    {
        var scorer = Build(0.0, 0.0, 5.0 * Math.PI / 180.0, out _);

        double atTruth = scorer.Score(0.0, 0.0, 5.0 * Math.PI / 180.0, stride: 1);
        double atZero = scorer.Score(0.0, 0.0, 0.0, stride: 1);

        Assert.That(atTruth, Is.GreaterThan(atZero + 0.05));
    }

    [Test]
    public void Score_Stride_DavaPodobnyVysledekJakoBezNeho()
    {
        var scorer = Build(0.0, 0.5, 0.0, out _);

        double full = scorer.Score(0.0, 0.5, 0.0, stride: 1);
        double sub = scorer.Score(0.0, 0.5, 0.0, stride: 4);

        Assert.That(sub, Is.EqualTo(full).Within(0.05));
    }

    [Test]
    public void Score_PrazdnyOblak_JeNula()
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();
        var msg = CorrelationTestScenes.GridFromScene(scene, RobotX, RobotY, 0, 0, 0);
        msg.Road = null;

        var scorer = new CorrelationScorer(EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold),
                                           CorrelationTestScenes.RasterFor(scene, msg, cfg),
                                           RobotX, RobotY);

        Assert.That(scorer.Score(0, 0, 0, stride: 1), Is.EqualTo(0.0));
    }
}
