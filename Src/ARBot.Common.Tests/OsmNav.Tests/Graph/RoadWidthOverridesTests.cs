using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Tests.Localization;

namespace ARBot.Common.Tests.OsmNav.Graph;

/// <summary>
/// Prevod per-way odhadu sirky na sirku UZLU. Pravidlo je TOTEZ, jake uz pouziva
/// <c>GraphBuilder</c> pri stavbe site (maximum pres cesty uzlem) — viz
/// doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 2.
/// </summary>
public class RoadWidthOverridesTests
{
    [Test]
    public void NaucenaSirka_prepiseMapovou()
    {
        // StraightEastRoad ma vsechny uzly na mapovych 3 m; cesta se naucila 6 m.
        var net = CorrelationTestScenes.StraightEastRoad(CorrelationTestScenes.Origin(), 3.0);
        long wayId = net.Edges[0].WayId;

        var o = RoadWidthOverrides.Build(net, w => w == wayId ? 6.0 : (double?)null);

        Assert.That(o.TryGet(net.Edges[0].From.Id, out double w0), Is.True);
        Assert.That(w0, Is.EqualTo(6.0).Within(1e-9));
    }

    [Test]
    public void CestaBezOdhadu_prispejeMapovouSirkou()
    {
        var net = CorrelationTestScenes.StraightEastRoad(CorrelationTestScenes.Origin(), 3.0);

        var prekryv = RoadWidthOverrides.Build(net, _ => null);

        Assert.That(prekryv.Count, Is.Zero, "bez jedineho odhadu nesmi prekryv nic nest");
    }

    [Test]
    public void SdilenyUzel_dostaneMAXIMUM()
    {
        // T-krizovatka: dve cesty sdili prostredni uzel. Nauci se jen ta sirsi -> uzel dostane
        // jeji sirku, protoze GraphBuilder pouziva pri stavbe site TOTEZ pravidlo.
        // ⚠️ Je to ZNAMA MEZ (chodnik u vozovky podedi jeji sirku), ne vada - viz spec.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.TJunction(o, 2.0);
        long sirsi = net.Edges[0].WayId;

        var prekryv = RoadWidthOverrides.Build(net, w => w == sirsi ? 6.0 : (double?)null);

        long sdileny = net.Edges[0].To.Id;
        Assert.That(prekryv.TryGet(sdileny, out double w), Is.True);
        Assert.That(w, Is.EqualTo(6.0).Within(1e-9), "maximum, i kdyz druha cesta zustala uzka");
    }

    [Test]
    public void UzsiOdhadNaSdilenemUzlu_seNEZTRATI()
    {
        // ⚠️ Tohle je ta vada, kterou nasel autor 15. 9. 2026 na dvoumapovem rigu (vizualni mapa
        // 2 m, jizdni 3 m): nauci se UZSI cesta, ale jeji koncovy uzel sdili jina, NEZMERENA cesta.
        // Pravidlo maxima pak vezme mapovych 3 m te nezmerene a naucene zuzeni v uzlu ZMIZI -
        // takze pas cesty zustane siroky presne tam, kde se stykaji cesty, tedy skoro vsude.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.TJunction(o, 3.0);   // mapa: vsude 3 m
        long mereny = net.Edges[0].WayId;                    // way 1 = a-c-b; way 2 (c-d) nezmerena

        var prekryv = RoadWidthOverrides.Build(net, w => w == mereny ? 2.0 : (double?)null);

        long sdileny = net.Edges[0].To.Id;                   // uzel c
        Assert.That(prekryv.TryGet(sdileny, out double w), Is.True,
                    "zmerena cesta ma ve sdilenem uzlu prosadit svou sirku");
        Assert.That(w, Is.EqualTo(2.0).Within(1e-9),
                    "nezmerena cesta nesmi zuzeni prehlasit svou MAPOVOU hodnotou - to neni dukaz");
    }

    [Test]
    public void UzsiOdhadNezMapa_uzelZuzi()
    {
        // Prekryv NENI jednosmerna racna: kdyz je cesta uzsi, nez rika mapa, uzel se ma zuzit.
        var net = CorrelationTestScenes.StraightEastRoad(CorrelationTestScenes.Origin(), 4.0);
        long wayId = net.Edges[0].WayId;

        var prekryv = RoadWidthOverrides.Build(net, w => w == wayId ? 2.0 : (double?)null);

        Assert.That(prekryv.TryGet(net.Edges[0].From.Id, out double w2), Is.True);
        Assert.That(w2, Is.EqualTo(2.0).Within(1e-9));
    }
}
