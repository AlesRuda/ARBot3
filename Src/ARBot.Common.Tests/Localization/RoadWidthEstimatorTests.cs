using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Odhad sirky cesty per hrana <b>s verdiktem kvality</b>. Bezi BEZ brany — prijme kazdou merenou
/// sirku, ktera prosla geometrickou pricetnosti <c>CorridorFinder</c>u — a sam rika, kdy uz se
/// vysledku da verit.
///
/// <para><b>Proc ne <see cref="RoadWidthFilter"/>:</b> ten se zaklada PRVNIM merenim a sirkova
/// brana koridoru se ho pak drzi, takze jedno spatne prolozeni je LEPKAVE navzdy. Tenhle drzi
/// okno poslednich merení, polohu bere MEDIANEM (odlehla hodnota ji neutrhne) a dokud si merenia
/// nesednou, rekne „nevim" misto toho, aby vnutil spatne cislo. Viz
/// doc/map-correlation-localization.md.</para>
/// </summary>
public class RoadWidthEstimatorTests
{
    private const long Way = 1;

    private static RoadWidthEstimator Estimator(int window = 20, int minSamples = 10,
                                                double maxDispersion = 0.10)
        => new RoadWidthEstimator(new RoadWidthEstimatorConfig
        {
            WindowSize = window,
            MinSamples = minSamples,
            MaxDispersionM = maxDispersion,
        });

    private static void Add(RoadWidthEstimator e, int count, double width, long way = Way)
    {
        for (int i = 0; i < count; i++) e.Add(way, width);
    }

    [Test]
    public void BezMereni_sirkuNeda()
    {
        var e = Estimator();

        Assert.That(e.TryGetWidth(Way, out _), Is.False);
        Assert.That(e.Samples(Way), Is.Zero);
    }

    [Test]
    public void MaloVzorku_sirkuNeda()
    {
        // Merenia si sedi, ale je jich min nez MinSamples - na verdikt to nestaci.
        var e = Estimator(minSamples: 10);

        Add(e, 9, 2.0);

        Assert.That(e.TryGetWidth(Way, out _), Is.False, "devet vzorku na verdikt nestaci");
        Assert.That(e.Samples(Way), Is.EqualTo(9));
    }

    [Test]
    public void KonzistentniMereni_dajiSirku()
    {
        var e = Estimator(minSamples: 10);

        Add(e, 10, 2.0);

        Assert.That(e.TryGetWidth(Way, out double w), Is.True);
        Assert.That(w, Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    public void RozptyleneMereni_sirkuNedaji()
    {
        // Prolozila se pokazde jina dvojice hranic -> merenia si nesednou. Prave tohle ma
        // kvalita chytit: bez vnejsi reference je rozptyl jedine merítko, ktere na to je.
        var e = Estimator(minSamples: 10, maxDispersion: 0.10);

        for (int i = 0; i < 20; i++) e.Add(Way, i % 2 == 0 ? 2.0 : 5.0);

        Assert.That(e.TryGetWidth(Way, out _), Is.False);
    }

    [Test]
    public void JedenOdlehlyVzorek_neutrhneOdhad()
    {
        // Median, ne prumer: jedno spatne prolozeni nesmi posunout vysledek.
        var e = Estimator(minSamples: 10);

        Add(e, 19, 2.0);
        e.Add(Way, 7.5);

        Assert.That(e.TryGetWidth(Way, out double w), Is.True, "jeden odlehly vzorek nesmi shodit kvalitu");
        Assert.That(w, Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    public void ZeSpatnehoZacatku_seSamOpravi()
    {
        // TO je duvod, proc tahle trida vznikla. RoadWidthFilter se zaloznim prvnim merenim
        // by na 7 m uvizl, protoze sirkova brana by vsechna dalsi merenia zamitla.
        var e = Estimator(window: 20, minSamples: 10);

        Add(e, 5, 7.0);
        Add(e, 20, 2.0);

        Assert.That(e.TryGetWidth(Way, out double w), Is.True);
        Assert.That(w, Is.EqualTo(2.0).Within(1e-9), "spatny zacatek musi vypadnout z okna");
    }

    [Test]
    public void RozsirujiciSeCesta_zustaneDuveryhodna()
    {
        // Cesta se muze skutecne rozsirovat (nálevka). Pres celou historii by rozptyl rostl
        // a kvalita by nebyla dobra NIKDY; v okne je i rozsirujici se cesta lokalne konzistentni.
        // 0,01 m na vzorek = pri 10 Hz a 1 m/s rozsireni o 1 m na 10 m drahy.
        var e = Estimator(window: 20, minSamples: 10, maxDispersion: 0.10);

        for (int i = 0; i < 40; i++) e.Add(Way, 2.0 + i * 0.01);

        Assert.That(e.TryGetWidth(Way, out double w), Is.True);
        Assert.That(w, Is.GreaterThan(2.2), "odhad musi rozsirovani sledovat, ne zustat na zacatku");
    }

    [Test]
    public void HranySeNemichaji()
    {
        var e = Estimator(minSamples: 10);

        Add(e, 10, 2.0, way: 1);
        Add(e, 10, 5.0, way: 2);

        Assert.That(e.TryGetWidth(1, out double w1), Is.True);
        Assert.That(w1, Is.EqualTo(2.0).Within(1e-9));
        Assert.That(e.TryGetWidth(2, out double w2), Is.True);
        Assert.That(w2, Is.EqualTo(5.0).Within(1e-9));
        Assert.That(e.Count, Is.EqualTo(2));
    }

    [Test]
    public void NesmyslnaSirka_seNeprijme()
    {
        // Zaporna nebo nulova sirka neni merenie, je to vada volajiciho.
        var e = Estimator(minSamples: 1);

        e.Add(Way, 0);
        e.Add(Way, -1);

        Assert.That(e.Samples(Way), Is.Zero);
    }
}
