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

    // ====== Gate rovnobeznosti vs. rozsirujici se cesta (24. 8. 2026, ZADNA oprava) ======
    //
    // Testy nize popisuji DNESNI chovani a jeho hranice. Vznikly z otazky, jestli se merenie
    // nezahazuje na rozsirujicim se useku testovaci mapy — zahazuje, ale reálné cesty jsou
    // typicky konstantni sirky, takze to neni pripad k ladeni. Nemenit gate bez toho, ze se
    // najde reálná cesta, ktere vadí. Podrobne doc/map-correlation-localization.md.

    /// <summary>
    /// Hranice <b>rozsirujici se</b> cesty: sirka roste z <paramref name="widthFrom"/> na
    /// <paramref name="widthTo"/> na delce <paramref name="lengthM"/>. Presne tvar useku D
    /// v <c>OSM/SyntetickyKoridor.osm</c> (nalevka 1 m -&gt; 3 m na 10 m).
    /// </summary>
    private static (List<Point2D> left, List<Point2D> right) Funnel(
        double widthFrom, double widthTo, double lengthM, int count = 60)
    {
        var left = new List<Point2D>();
        var right = new List<Point2D>();
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * (lengthM / count);          // podel osy (osa = +X, robot na ose)
            double half = 0.5 * (widthFrom + (widthTo - widthFrom) * (s / lengthM));
            left.Add(new Point2D(s, half));
            right.Add(new Point2D(s, -half));
        }
        return (left, right);
    }

    /// <summary>
    /// <b>Nalevka v testovaci mape pada na gatu rovnobeznosti — a je to tak v poradku.</b>
    ///
    /// <para>Sirka 1 m -&gt; 3 m na 10 m znamena, ze se kazda hranice odklani od osy o
    /// atan(1/10) = 5,71°, tedy hranice vuci sobe o <b>11,42°</b> — nad prahem
    /// <see cref="CorridorConfig.MaxParallelErrorRad"/> = 10°. Zamitne se to VZDY, i kdyby bylo
    /// prolozeni dokonale.</para>
    ///
    /// <para><b>Neni to vada k oprave.</b> Reálné cesty jsou typicky konstantni sirky; gradient
    /// 2 m na 10 m je vlastnost <c>OSM/SyntetickyKoridor.osm</c> (usek D), ne pripad z praxe.
    /// Realisticke gradienty projdou s rezervou: 0,25 / 0,5 / 1,0 m na 10 m dá 1,4 / 2,9 / 5,7°.
    /// Na ceste konstantni sirky je nerovnobeznost <b>cisty signal kvality prolozeni</b>, takze
    /// tam gate dela presne to, co ma.</para>
    ///
    /// <para><b>Test tu je jako dokumentace</b> — aby bylo videt, ze <c>NotParallel</c> nad
    /// zaznamem <c>20260822-100403</c> (87 z 258 cyklu) ma synteticky puvod, a ten zaznam se tedy
    /// nema pouzivat jako "tezky" benchmark. Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    [Test]
    public void RozsirujiciSeCesta_padneNaGatuRovnobeznosti()
    {
        var (l, r) = Funnel(widthFrom: 1.0, widthTo: 3.0, lengthM: 10.0);

        var c = Finder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.NotParallel));
        Assert.That(c.ParallelErrorRad * 180 / Math.PI, Is.EqualTo(11.42).Within(0.5),
                    "geometricky vychazi 2*atan(1/10) = 11,42 stupne");
    }

    /// <summary>
    /// Geometrie koridoru <b>sama</b> nalevku zvlada — zamitnuti dela vylucne gate, ne vypocet.
    ///
    /// <para>Smer koridoru se pocita jako <b>prumer</b> smeru obou hranic, a u symetricke nalevky
    /// je prumer presne smer osy. Sirka i pricna poloha se odectou z offsetu primek <b>v miste
    /// robotu</b>, takze rozbihani hranic dal po ceste je nezkresluje.</para>
    ///
    /// <para><b>Nacpak to vedet, kdyz se nalevky ladit nemaji.</b> Aby bylo jasne, ze prah
    /// <see cref="CorridorConfig.MaxParallelErrorRad"/> je <b>volitelna pojistka</b>, ne
    /// predpoklad, na kterem vypocet stoji. Kdyby se nekdy nasla reálná cesta s prudkym
    /// rozsirenim, prah se da zvednout bez zasahu do geometrie — a tenhle test rika, ze to
    /// nic nerozbije.</para>
    /// </summary>
    [Test]
    public void RozsirujiciSeCesta_priZvednutemGatuJsouVysledkySpravne()
    {
        var (l, r) = Funnel(widthFrom: 1.0, widthTo: 3.0, lengthM: 10.0);

        // Sirka v miste robotu: osa zacina na s=0, body od s=1 m, takze v miste robotu (s=0)
        // je sirka 1,0 m; kolmice se ale pocita z offsetu primek, tedy sirka v pocatku.
        var c = new CorridorFinder(new CorridorConfig { MaxParallelErrorRad = 20 * Math.PI / 180 })
            .Find(l, r);

        Assert.Multiple(() =>
        {
            Assert.That(c.Reason, Is.EqualTo(CorridorReason.Ok));
            Assert.That(c.DirectionRad, Is.EqualTo(0.0).Within(0.01),
                        "prumer smeru obou hranic je smer osy, i kdyz se rozbihaji");
            Assert.That(c.Lateral, Is.EqualTo(0.0).Within(0.02),
                        "robot je na ose a ma to tak vyjit");
            Assert.That(c.Width, Is.EqualTo(1.0).Within(0.05),
                        "sirka se cte v miste robotu (s=0), tam je 1,0 m");
        });
    }

    /// <summary>
    /// Kolik rozsireni gate jeste snese: pri prahu 10° projde rozsireni do ~1,75 m na 10 m.
    /// Cislo je tu proto, aby bylo videt, ze <b>realisticke gradienty maji rezervu</b> — cesta,
    /// ktera se rozsiri o 1 m na 10 m, projde na 5,7°, tedy s prahem na polovinu.
    /// </summary>
    [Test]
    public void GateRovnobeznosti_snesejenMaleRozsireni()
    {
        // 1 m -> 2,7 m na 10 m: 2*atan(0,85/10) = 9,7 stupne, tesne pod prahem.
        var mild = Funnel(1.0, 2.7, 10.0);
        // 1 m -> 3,2 m na 10 m: 2*atan(1,1/10) = 12,5 stupne, nad prahem.
        var steep = Funnel(1.0, 3.2, 10.0);

        Assert.Multiple(() =>
        {
            Assert.That(Finder().Find(mild.left, mild.right).Reason,
                        Is.EqualTo(CorridorReason.Ok), "mirne rozsireni jeste projde");
            Assert.That(Finder().Find(steep.left, steep.right).Reason,
                        Is.EqualTo(CorridorReason.NotParallel), "prudsi uz ne");
        });
    }

    // ============ Prehradlovani konsenzualni sady (24. 8. 2026) ============

    /// <summary>
    /// Prehradlovani musi sadu <b>rozsirit</b>, ne zuzit: konsenzualni sada vznikla proti hypoteze
    /// ze tri bodu (tedy proti primce se sumem), takze cast bodu, ktere na spravne primce lezi,
    /// zustala venku. Po prolozeni je primka lepsi a pri opakovanem hradlovani je pribere.
    ///
    /// <para>Nameřeno nad zaznamy: chyba sirky proti mape klesla v p90 o 8-15 %. Viz
    /// <see cref="CorridorConfig.RegatePasses"/>.</para>
    /// </summary>
    [Test]
    public void Prehradlovani_nezuziKonsenzualniSadu()
    {
        var (left, right) = Corridor(width: 2.0, lateral: 0.2, dirRad: 0.05, count: 80, noise: 0.06);

        var without = new CorridorFinder(new CorridorConfig { RegatePasses = 0 }).Find(left, right);
        var with = new CorridorFinder(new CorridorConfig { RegatePasses = 2 }).Find(left, right);

        Assert.Multiple(() =>
        {
            Assert.That(without.Reason, Is.EqualTo(CorridorReason.Ok), "predpoklad testu");
            Assert.That(with.Reason, Is.EqualTo(CorridorReason.Ok));
            // RANSAC je nedeterministicky, takze se netvrdi "vic" - tvrdi se "ne vyrazne mene".
            Assert.That(with.InliersLeft, Is.GreaterThanOrEqualTo(without.InliersLeft - 3));
            Assert.That(with.InliersRight, Is.GreaterThanOrEqualTo(without.InliersRight - 3));
            Assert.That(with.Width, Is.EqualTo(2.0).Within(0.1));
        });
    }

    /// <summary>
    /// Nula = puvodni chovani (jeden pruchod bez prehradlovani). Pojistka, aby se dal vychozi
    /// stav vratit a aby slo merit A/B - vychozi hodnota je 2, ale nula musi zustat funkcni.
    /// </summary>
    [Test]
    public void Prehradlovani_Nula_jePuvodniChovani()
    {
        var (left, right) = Corridor(width: 2.0, lateral: 0.0, dirRad: 0.0, noise: 0.02);

        var c = new CorridorFinder(new CorridorConfig { RegatePasses = 0 }).Find(left, right);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.Ok));
        Assert.That(c.Width, Is.EqualTo(2.0).Within(0.05));
    }

    /// <summary>
    /// Prehradlovani nesmi utect: kdyz se primka pri iteraci rozjede, musi zustat platny
    /// predchozi stav. Hlida se to na datech, kde je hranice jen kratky useknuty shluk - tam
    /// je nejvic sance, ze se sada rozpadne.
    /// </summary>
    [Test]
    public void Prehradlovani_naKratkeHranici_neutece()
    {
        var (left, right) = Corridor(width: 2.0, lateral: 0.0, dirRad: 0.0, count: 30, noise: 0.15);

        var c = new CorridorFinder(new CorridorConfig { RegatePasses = 5 }).Find(left, right);

        // Bud koridor vznikne a je rozumny, nebo se poctive zamitne - ale nesmi vyjit nesmysl.
        if (c.Reason == CorridorReason.Ok)
        {
            Assert.That(c.Width, Is.EqualTo(2.0).Within(0.4));
            Assert.That(c.InliersLeft, Is.GreaterThan(0));
        }
        else
        {
            Assert.That(c.Ok, Is.False);
        }
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
