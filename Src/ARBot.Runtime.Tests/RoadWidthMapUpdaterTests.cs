using System;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Robot;

namespace ARBot.Runtime.Tests;

/// <summary>
/// Kdy se mapa prestavi. Prah je proti kolisani odhadu v centimetrech, odstup proti rade cest,
/// ktere se usadi tesne po sobe. Viz doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 6.
/// </summary>
public class RoadWidthMapUpdaterTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Estimator naplneny tak, ze vsechny cesty maji duveryhodnou sirku.</summary>
    private static RoadWidthEstimator Odhad(double sirka)
    {
        var e = new RoadWidthEstimator(new RoadWidthEstimatorConfig { MinSamples = 1 });
        for (long way = 0; way < 64; way++) e.Add(way, sirka);
        return e;
    }

    [Test]
    public void PrvniDuveryhodnaSirka_prestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        RoadScene scena = null;
        MapMsg mapa = null;
        var u = new RoadWidthMapUpdater(net, o, Odhad(6.0), s => scena = s, m => mapa = m,
                                        new RoadWidthMapUpdaterConfig());

        bool prestaveno = u.Zkus(T0);

        Assert.That(prestaveno, Is.True);
        Assert.That(scena, Is.Not.Null);
        Assert.That(scena.IsRoad(0, 2.5), Is.True, "nova scena ma naucenou sirku");
        Assert.That(mapa, Is.Not.Null);
        Assert.That(u.Rebuilds, Is.EqualTo(1));
    }

    [Test]
    public void PodPrahem_neprestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var odhad = Odhad(6.0);
        var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 0 });
        u.Zkus(T0);

        // Zmena o 0,10 m je pod prahem 0,25 -> prestavovat se nema.
        foreach (var e in net.Edges) for (int i = 0; i < 40; i++) odhad.Add(e.WayId, 6.10);

        Assert.That(u.Zkus(T0.AddSeconds(60)), Is.False);
        Assert.That(u.Rebuilds, Is.EqualTo(1));
    }

    [Test]
    public void NadPrahemAleVOdstupu_neprestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var odhad = Odhad(6.0);
        var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 10 });
        u.Zkus(T0);

        foreach (var e in net.Edges) for (int i = 0; i < 40; i++) odhad.Add(e.WayId, 2.0);

        Assert.That(u.Zkus(T0.AddSeconds(5)), Is.False, "odstup jeste neuplynul");
        Assert.That(u.Zkus(T0.AddSeconds(11)), Is.True, "po odstupu uz ano");
    }

    [Test]
    public void SkokCasuVzad_odstupResetuje()
    {
        // Seek v zaznamu / novy beh: bez resetu by byl rozdil zaporny, tedy vzdy pod periodou,
        // a uz by se neprestavelo nikdy.
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var odhad = Odhad(6.0);
        var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 10 });
        u.Zkus(T0);
        foreach (var e in net.Edges) for (int i = 0; i < 40; i++) odhad.Add(e.WayId, 2.0);

        Assert.That(u.Zkus(T0.AddSeconds(-30)), Is.True, "po skoku vzad se odstup nesmi drzet");
    }

    [Test]
    public void BezDuveryhodnehoOdhadu_neprestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var u = new RoadWidthMapUpdater(net, o, new RoadWidthEstimator(), _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig());

        Assert.That(u.Zkus(T0), Is.False);
        Assert.That(u.Rebuilds, Is.Zero);
    }

    [Test]
    public void DveCerstveInstance_dajiTyzPocetPrestaveb()
    {
        // Record/replay: stupen je STAVOVY, takze zaruka plati pro CERSTVE instance.
        static long Beh()
        {
            var o = TestRoadNetwork.Origin();
            var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
            var odhad = new RoadWidthEstimator(new RoadWidthEstimatorConfig { MinSamples = 1 });
            var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                            new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 1 });
            for (int i = 0; i < 20; i++)
            {
                foreach (var e in net.Edges) odhad.Add(e.WayId, 3.0 + i * 0.1);
                u.Zkus(T0.AddSeconds(i));
            }
            return u.Rebuilds;
        }

        Assert.That(Beh(), Is.EqualTo(Beh()));
    }

    [Test]
    public void VychoziHodnotaParametru_jeVypnuto()
    {
        // Stejne jako mapcorr a corridor: nova cesta se nezapina sama. Default je POLE
        // v ParamDef, ke kteremu se jde pres Param.Def (BoolParam ho sam nevystavuje).
        Assert.That(ARBot.Common.Configuration.ParamRegistry.RoadWidthMap.Def.Default,
                    Is.EqualTo("false"));
    }
}
