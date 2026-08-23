using System;
using System.Collections.Generic;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Koridor z hranic cesty: RANSAC prolozi hranicni body primkou vlevo a vpravo, v miste robotu
/// se spocita kolmice na cestu a z pruseciku vyjde <b>sirka cesty, pricna poloha robotu
/// a odchylka osy</b>. Varianta, ktera se na starem robotu osvedcila (na rozdil od
/// <c>PathMapCorelator</c>, ktery se odladit nepodarilo).
///
/// <para>Merene nad zaznamem 21. 8. 2026: pricna poloha sd 3 cm, smer sd 0,77°, sirka 2,01 m
/// proti 2,00 m v mape. Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class CorridorFinderTests
{
    /// <summary>
    /// Body na primce v ramci robotu (X vpred, Y vlevo): koridor sirky <paramref name="width"/>,
    /// robot <paramref name="lateral"/> vlevo od osy, cesta stocena o <paramref name="dirRad"/>.
    /// </summary>
    private static (List<Point2D> left, List<Point2D> right) Corridor(
        double width, double lateral, double dirRad, int count = 40, double noise = 0)
    {
        var left = new List<Point2D>();
        var right = new List<Point2D>();
        // Osa koridoru prochazi bodem (0, -lateral) ve smeru dirRad; hranice jsou od ni +-width/2.
        double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
        double nx = -uy, ny = ux;                     // leva normala
        var rnd = new Random(12345);
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * 0.15;                 // podel cesty od 1 m dal
            double ox = -lateral * nx, oy = -lateral * ny;
            double jitter() => noise == 0 ? 0 : (rnd.NextDouble() - 0.5) * 2 * noise;
            left.Add(new Point2D(ox + ux * s + nx * (width / 2) + jitter(),
                                 oy + uy * s + ny * (width / 2) + jitter()));
            right.Add(new Point2D(ox + ux * s - nx * (width / 2) + jitter(),
                                  oy + uy * s - ny * (width / 2) + jitter()));
        }
        return (left, right);
    }

    private static CorridorFinder Finder(CorridorConfig cfg = null) => new CorridorFinder(cfg);

    [Test]
    public void PriamyKoridor_daSirkuPricnouPolohuISmer()
    {
        var (l, r) = Corridor(width: 3.0, lateral: 0.0, dirRad: 0.0);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.Ok));
        Assert.That(c.Width, Is.EqualTo(3.0).Within(0.01));
        Assert.That(c.Lateral, Is.EqualTo(0.0).Within(0.01));
        Assert.That(c.DirectionRad, Is.EqualTo(0.0).Within(0.01));
    }

    [Test]
    public void RobotVlevoOdOsy_maKladnePricne()
    {
        // Znamenkova konvence: + = robot je vlevo od osy cesty (FLU, +Y vlevo).
        var (l, r) = Corridor(width: 3.0, lateral: 0.5, dirRad: 0.0);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.Ok));
        Assert.That(c.Lateral, Is.EqualTo(0.5).Within(0.01));
        Assert.That(c.Width, Is.EqualTo(3.0).Within(0.01), "posun robotu nesmi zmenit sirku");
    }

    [Test]
    public void RobotVpravoOdOsy_maZapornePricne()
    {
        var (l, r) = Corridor(width: 2.0, lateral: -0.4, dirRad: 0.0);

        var c = Finder().Find(l, r);

        Assert.That(c.Lateral, Is.EqualTo(-0.4).Within(0.01));
    }

    [Test]
    public void StocenaCesta_daSmer()
    {
        double dir = 10 * Math.PI / 180;
        var (l, r) = Corridor(width: 2.5, lateral: 0.2, dirRad: dir);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.Ok));
        Assert.That(c.DirectionRad, Is.EqualTo(dir).Within(0.02));
        Assert.That(c.Width, Is.EqualTo(2.5).Within(0.02));
        Assert.That(c.Lateral, Is.EqualTo(0.2).Within(0.02));
    }

    [Test]
    public void SumNaBodech_seProjeviVSigma_alePolohaDrzi()
    {
        var (l, r) = Corridor(width: 3.0, lateral: 0.3, dirRad: 0, count: 60, noise: 0.05);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.Ok));
        Assert.That(c.Lateral, Is.EqualTo(0.3).Within(0.05));
        Assert.That(c.SigmaLateral, Is.GreaterThan(0), "sigma musi byt kladna");
        Assert.That(c.SigmaLateral, Is.LessThan(0.1), "pri 5 cm sumu nesmi sigma utect");
        Assert.That(c.Lateral, Is.EqualTo(0.3).Within(0.02), "prolozeni pres inliery drzi polohu");
        Assert.That(c.ResidualLeft, Is.GreaterThan(0));
    }

    [Test]
    public void CistaData_majiMensiSigmaNezSumna()
    {
        var cleanPts = Corridor(3.0, 0, 0, 60, 0);
        var clean = Finder().Find(cleanPts.left, cleanPts.right);
        var noisy = Corridor(3.0, 0, 0, 60, 0.12);
        var dirty = Finder().Find(noisy.left, noisy.right);

        // Honestni sigma musi rozliset kvalitu dat, ne vracet konstantu.
        Assert.That(clean.SigmaLateral, Is.LessThan(dirty.SigmaLateral));
        Assert.That(clean.SigmaLateral, Is.EqualTo(0.03).Within(1e-9),
                    "u cistych dat sigma sedi na podlaze = namerena opakovatelnost");
    }

    [Test]
    public void SigmaSeNEDELISqrtN()
    {
        // Sousedni hranicni body pochazi ze sousednich radku tehoz obrazu a chybu detekce si
        // SDILEJI - delenim sqrt(n) by z 200 bodu vysla milimetrova jistota. Presne tuhle vadu
        // ma estimator odstranit, takze vic bodu tehoz sumu nesmi sigma srazit.
        var few = Corridor(3.0, 0, 0, count: 40, noise: 0.08);
        var many = Corridor(3.0, 0, 0, count: 240, noise: 0.08);

        var a = Finder().Find(few.left, few.right);
        var b = Finder().Find(many.left, many.right);

        Assert.That(a.Reason, Is.EqualTo(CorridorReason.Ok), "oba koridory musi projit gaty");
        Assert.That(b.Reason, Is.EqualTo(CorridorReason.Ok));
        Assert.That(a.SigmaLateral, Is.GreaterThan(0.03), "sigma ma byt z dat, ne z podlahy");
        Assert.That(b.InliersLeft, Is.GreaterThan(a.InliersLeft * 2), "kontrola, ze bodu je vic");
        Assert.That(b.SigmaLateral, Is.EqualTo(a.SigmaLateral).Within(0.02),
                    "sigma se pocitem bodu nesmi zmensit");
    }

    [Test]
    public void MaloBodu_koridorNevznikne()
    {
        var (l, r) = Corridor(3.0, 0, 0, count: 3);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.TooFewPoints));
        Assert.That(c.Ok, Is.False);
    }

    [Test]
    public void JenJednaHranice_seHlasi()
    {
        var (l, _) = Corridor(3.0, 0, 0);

        var c = Finder().Find(l, new List<Point2D>());

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.OneSideOnly));
        Assert.That(c.Ok, Is.False);
    }

    [Test]
    public void NeparalelniHranice_seZahodi()
    {
        // Prava hranice stocena o 40 stupnu proti leve - to neni koridor.
        var (l, _) = Corridor(3.0, 0, 0);
        var (_, r) = Corridor(3.0, 0, 40 * Math.PI / 180);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.NotParallel));
    }

    [Test]
    public void NesmyslnaSirka_seZahodi()
    {
        var (l, r) = Corridor(width: 15.0, lateral: 0, dirRad: 0);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.WidthOutOfRange));
    }

    [Test]
    public void PocetInlieruJeVeVysledku()
    {
        var (l, r) = Corridor(3.0, 0, 0, count: 50);

        var c = Finder().Find(l, r);

        Assert.That(c.InliersLeft, Is.GreaterThan(20));
        Assert.That(c.InliersRight, Is.GreaterThan(20));
    }

    // ============ Prah inlieru zavisly na vzdalenosti (23. 8. 2026) ============

    /// <summary>
    /// Hranice s <b>rostoucim rozptylem</b>: blizke body presne, vzdalene rozhazene umerne
    /// vzdalenosti. Presne takhle se chova skutecny detektor (nameřeno: medián sedi na okraji
    /// vozovky v kazde vzdalenosti, ale p10/p90 roste z ±5 cm na 1 m na −0,63/+0,40 m na 10 m).
    ///
    /// <para>S jednim prahem pro vsechny body vypadnou vzdalene jako outliery a koridor prijde
    /// o dosah; prah umerny nejistote je udrzi. Viz
    /// <see cref="CorridorConfig.InlierThresholdPerMeter"/>.</para>
    /// </summary>
    [Test]
    public void RangeDependentThreshold_KeepsFarPointsThatUniformThresholdDrops()
    {
        // Hranice od 1 do 9 m, rozptyl 4 cm na metr vzdalenosti (deterministicky, at test nekmita).
        var rnd = new Random(4242);
        var left = new List<Point2D>();
        var right = new List<Point2D>();
        for (int i = 0; i < 60; i++)
        {
            double x = 1.0 + i * 0.135;
            double spread = 0.04 * x;
            double j1 = (rnd.NextDouble() - 0.5) * 2 * spread;
            double j2 = (rnd.NextDouble() - 0.5) * 2 * spread;
            left.Add(new Point2D(x, 1.0 + j1));
            right.Add(new Point2D(x, -1.0 + j2));
        }

        var uniform = new CorridorFinder(new CorridorConfig { InlierThresholdPerMeter = 0 })
            .Find(left, right);
        var scaled = new CorridorFinder(new CorridorConfig { InlierThresholdPerMeter = 0.05 })
            .Find(left, right);

        Assert.Multiple(() =>
        {
            Assert.That(uniform.Reason, Is.EqualTo(CorridorReason.Ok), "predpoklad testu");
            Assert.That(scaled.Reason, Is.EqualTo(CorridorReason.Ok));
            Assert.That(scaled.InliersLeft + scaled.InliersRight,
                        Is.GreaterThan(uniform.InliersLeft + uniform.InliersRight),
                        "prah rostouci se vzdalenosti musi udrzet vic vzdalenych bodu");
            Assert.That(scaled.Width, Is.EqualTo(2.0).Within(0.15));
        });
    }

    /// <summary>
    /// Velikost vzorku pro hypotezu nesmi rozhodovat o vysledku: RANSAC sice z nej model spocita,
    /// ale vysledna primka se pak <b>prolozi pres celou konsenzualni sadu</b>, takze sum vzorku
    /// se do ni nepromitne. Zmereno i nad zaznamem: vzorek 2 az 50 dava <c>Ok</c> 149-167
    /// a <c>NotParallel</c> 236-254, tedy nic. Test hlida, ze to tak zustane - kdyby na vzorku
    /// zaleželo, znamena to, ze se prolozeni konsenzualni sady rozbilo.
    /// </summary>
    [Test]
    public void ModelSamplePoints_DoesNotChangeTheResult()
    {
        var (left, right) = Corridor(width: 2.0, lateral: 0.3, dirRad: 0.05, noise: 0.03);

        var small = new CorridorFinder(new CorridorConfig { ModelSamplePoints = 3 }).Find(left, right);
        var big = new CorridorFinder(new CorridorConfig { ModelSamplePoints = 20 }).Find(left, right);

        Assert.Multiple(() =>
        {
            Assert.That(small.Reason, Is.EqualTo(CorridorReason.Ok), "predpoklad testu");
            Assert.That(big.Reason, Is.EqualTo(CorridorReason.Ok));
            Assert.That(big.Width, Is.EqualTo(small.Width).Within(0.05));
            Assert.That(big.Lateral, Is.EqualTo(small.Lateral).Within(0.05));
            Assert.That(big.DirectionRad, Is.EqualTo(small.DirectionRad).Within(0.02));
        });
    }

    /// <summary>
    /// Nula = puvodni chovani. Pojistka proti tomu, aby se novy parametr tise projevil i tam,
    /// kde ho nikdo nechce.
    /// </summary>
    [Test]
    public void RangeDependentThreshold_Zero_BehavesLikeUniform()
    {
        var (left, right) = Corridor(width: 2.0, lateral: 0.0, dirRad: 0.0, noise: 0.02);

        var a = new CorridorFinder(new CorridorConfig { InlierThresholdPerMeter = 0 }).Find(left, right);
        var b = new CorridorFinder(new CorridorConfig { InlierThresholdM = 0.10, InlierThresholdPerMeter = 0 })
            .Find(left, right);

        Assert.Multiple(() =>
        {
            Assert.That(b.Reason, Is.EqualTo(a.Reason));
            Assert.That(b.Width, Is.EqualTo(a.Width).Within(1e-9));
            Assert.That(b.Lateral, Is.EqualTo(a.Lateral).Within(1e-9));
        });
    }
}
