using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Filtr sirky cesty per hrana. Zamerne pomaly - kdyby dohanel merenie rychle, zapsala by se do
/// nej chyba pozy a ta by se pak sama utvrzovala. Viz doc/map-correlation-localization.md.
/// </summary>
public class RoadWidthFilterTests
{
    [Test]
    public void BezMereni_vraciZalohu()
    {
        var f = new RoadWidthFilter();

        Assert.That(f.Estimate(wayId: 1, fallbackM: 3.0), Is.EqualTo(3.0));
        Assert.That(f.Samples(1), Is.Zero);
        Assert.That(f.Count, Is.Zero);
    }

    [Test]
    public void PrvniMereni_zalozOdhad()
    {
        // Prvni merenie musi odhad ZALOZIT, ne se k nemu pomalu blizit od mapove hodnoty.
        var f = new RoadWidthFilter(alpha: 0.05);

        double w = f.Update(wayId: 7, measuredWidthM: 2.2);

        Assert.That(w, Is.EqualTo(2.2).Within(1e-9));
        Assert.That(f.Estimate(7, 3.0), Is.EqualTo(2.2).Within(1e-9));
        Assert.That(f.Samples(7), Is.EqualTo(1));
    }

    [Test]
    public void DalsiMereni_seVyhlazuji()
    {
        var f = new RoadWidthFilter(alpha: 0.5);
        f.Update(1, 2.0);

        double w = f.Update(1, 3.0);

        Assert.That(w, Is.EqualTo(2.5).Within(1e-9), "alpha 0,5 = pulcesty");
    }

    [Test]
    public void MalaAlpha_reagujePomalu()
    {
        var f = new RoadWidthFilter(alpha: 0.05);
        f.Update(1, 2.0);

        for (int i = 0; i < 10; i++) f.Update(1, 3.0);

        double w = f.Estimate(1, 0);
        Assert.That(w, Is.GreaterThan(2.0));
        Assert.That(w, Is.LessThan(2.5), "po deseti merenich nesmi byt na miste - jinak neni pomaly");
    }

    /// <summary>
    /// <b>Na rozsirujici se ceste filtr trvale zaostava</b> — a ten odstup je to, co se
    /// v telemetrii hlasi jako „sirkovy nesouhlas".
    ///
    /// <para><b>Proc na tom zalezi</b> (naměřeno 23. 8. 2026): v zaznamu vyskocil
    /// <c>|sirkovy nesouhlas|</c> p50 z 0,046 na 0,230 m a vypadalo to jako regrese po rozsireni
    /// parovaciho okna. Neni. Cely rozdil je z jedine cesty testovaci mapy, kde se koridor
    /// skutecne rozsiruje z 1 na 3 m (uzel dostava MAX sirku okolnich cest, takze uzka cesta
    /// se u krizovatky rozevira) — kamera tam sirku meri spravne (proti <b>mape</b> souhlasi na
    /// centimetry), ale filtr za rampou zustava pozadu. Zmenil se jen podil takovych cyklu ve
    /// vzorku. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para>Exponencialni filtr ma na rampe <b>ustaleny</b> odstup. Stupen porovnava merenie
    /// s odhadem <b>pred</b> zapracovanim (<c>MapWidthM = Estimate(...)</c> se cte driv, nez se
    /// zavola <c>Update</c>), takze hlaseny odstup je <c>Δ/α</c> — pro α = 0,05 dvacetinasobek
    /// prirustku na krok. (Po zapracovani by to bylo <c>Δ·(1−α)/α</c>, tedy o jeden krok mensi;
    /// telemetrie ukazuje tu prvni hodnotu.) Neni to vada filtru, je to jeho definice; vada by
    /// bylo cist ten odstup jako „nesouhlas s mapou".</para>
    ///
    /// <para>Kontrola proti zaznamu: na way 104 sla sirka 1,489 → 3,049 m za 90 cyklu, tedy
    /// Δ ≈ 0,0175 m/cyklus → ocekavany odstup 0,35 m. Posledni namereny odstup byl 0,347 m.</para>
    /// </summary>
    [Test]
    public void NaRozsirujiciSeCeste_filtrTrvaleZaostava()
    {
        const double alpha = 0.05, step = 0.02;      // rampa 2 cm na merenie (jako v zaznamu)
        var f = new RoadWidthFilter(alpha);

        double measured = 1.5;
        f.Update(1, measured);
        double lag = 0;
        for (int i = 0; i < 400; i++)                // dost dlouho, aby se odstup ustalil
        {
            measured += step;
            double before = f.Estimate(1, 0);        // to, co stupen hlasi jako "sirka z mapy"
            lag = measured - before;
            f.Update(1, measured);
        }

        double expected = step / alpha;               // 0,40 m
        Assert.That(lag, Is.EqualTo(expected).Within(0.01),
                    "odstup na rampe je dany alfou, ne kvalitou merenia");
    }

    [Test]
    public void HranySeNemichaji()
    {
        var f = new RoadWidthFilter();
        f.Update(1, 2.0);
        f.Update(2, 5.0);

        Assert.That(f.Estimate(1, 0), Is.EqualTo(2.0).Within(1e-9));
        Assert.That(f.Estimate(2, 0), Is.EqualTo(5.0).Within(1e-9));
        Assert.That(f.Count, Is.EqualTo(2));
    }

    [Test]
    public void NesmyslnaSirka_odhadNezmeni()
    {
        var f = new RoadWidthFilter();
        f.Update(1, 3.0);

        f.Update(1, 0);
        f.Update(1, -2);

        Assert.That(f.Estimate(1, 0), Is.EqualTo(3.0).Within(1e-9));
        Assert.That(f.Samples(1), Is.EqualTo(1), "neplatne merenie se nepocita");
    }

    [Test]
    public void Reset_zahodiVse()
    {
        var f = new RoadWidthFilter();
        f.Update(1, 3.0);

        f.Reset();

        Assert.That(f.Count, Is.Zero);
        Assert.That(f.Estimate(1, 9.0), Is.EqualTo(9.0));
    }

    [Test]
    public void NeplatnaAlpha_jeChyba()
    {
        Assert.That(() => new RoadWidthFilter(0), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new RoadWidthFilter(1.5), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
