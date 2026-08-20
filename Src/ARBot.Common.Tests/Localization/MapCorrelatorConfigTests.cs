using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>Testy konfigurace korelatoru (viz doc/map-correlation-localization.md).</summary>
public class MapCorrelatorConfigTests
{
    /// <summary>
    /// Stav prepinace korekci je VEDOME rozhodnuti, ne nahoda - proto je tady pripichnuty.
    ///
    /// <para>Faze 1-3 bezely s <c>SendCorrections = false</c> (korelator pocital a hlasil, ale
    /// nekorigoval) kvuli otevrene vade "falesna podelna jistota". <b>20. 8. 2026 autor korekce
    /// zapnul</b> a soucasne prodlouzil okno historie EKF na 3 s. Kdyby se sem nekdo vratil
    /// s <c>false</c>, ma to byt zase vedome.</para>
    ///
    /// <para><b>Nepletnout si to s "pocita se to vubec".</b> Tenhle prepinac rozhoduje jen o tom,
    /// jestli se merenia POSILAJI do fuze; vypocet probehne tak jako tak. Na "nepocitat" je parametr
    /// prikazove radky <c>mapcorr=true</c> (vychozi <b>false</b>, tedy korelator se vubec nezaklada) -
    /// pri <c>mapcorr=false</c> je tato hodnota bezpredmetna.</para>
    ///
    /// <para>Otevrene vady tim NEZMIZELY - viz doc/map-correlation-localization.md
    /// (sigma slepa k mnozstvi dukazu, vychylena <c>TightAxisAngle</c>, chybejici tvrdy limit
    /// korekce za cyklus).</para>
    /// </summary>
    [Test]
    public void Vychozi_PosilaKorekce()
    {
        Assert.That(new MapCorrelatorConfig().SendCorrections, Is.True);
    }

    [Test]
    public void Vychozi_ProjdeValidaci()
    {
        Assert.That(() => new MapCorrelatorConfig().Validate(), Throws.Nothing);
    }

    [Test]
    public void Vychozi_UrovneJdouOdHrubeKJemne()
    {
        var levels = new MapCorrelatorConfig().Levels;

        Assert.That(levels.Length, Is.EqualTo(3));
        for (int i = 1; i < levels.Length; i++)
        {
            Assert.That(levels[i].StepM, Is.LessThan(levels[i - 1].StepM), $"Uroven {i} neni jemnejsi.");
            Assert.That(levels[i].HalfRangeM, Is.LessThan(levels[i - 1].HalfRangeM),
                        $"Uroven {i} nema uzsi okno.");
        }
    }

    [Test]
    public void SearchRangeM_JePulokruhNejhrubsiUrovne()
    {
        var cfg = new MapCorrelatorConfig();

        Assert.That(cfg.SearchRangeM, Is.EqualTo(cfg.Levels[0].HalfRangeM));
    }

    [Test]
    public void Validate_MarzeRastruMensiNezRozsahHledani_Vyhodi()
    {
        // Kdyby byla marze mensi, kandidat by sahal mimo rastr a odhad by se tlacil dovnitr.
        var cfg = new MapCorrelatorConfig { MapRasterMarginM = 1.0 };

        Assert.That(() => cfg.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_NekladneAlfa_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { Alpha = 0 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_DolniHraniceSigmaNadHorni_Vyhodi()
    {
        var cfg = new MapCorrelatorConfig { SigmaFloorM = 9.0, SigmaCeilingM = 5.0 };

        Assert.That(() => cfg.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Vychozi_KrokHessianuJeHrubsiNezNejjemnejsiSken()
    {
        // Skore je kvuli rastru schodovite; na kroku skenu by druha derivace merila kvantizacni sum.
        var cfg = new MapCorrelatorConfig();

        Assert.That(cfg.HessianStepM, Is.GreaterThan(cfg.Levels[^1].StepM * 2));
        Assert.That(cfg.HessianStepHeadingRad, Is.GreaterThan(cfg.Levels[^1].StepHeadingRad * 2));
    }

    [Test]
    public void Validate_NekladnyKrokHessianu_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { HessianStepM = 0 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void RequiredRasterMarginM_ZapocitavaROTACI()
    {
        // Kandidat oblak nejen posune, ale i OTOCI kolem robotu - rohova bunka je od robotu daleko,
        // takze ji uz 8 stupnu odnese o 1,26 m. Holy SearchRangeM proto nestaci a dukazy padaly mimo
        // rastr (a preskocene nesouhlasne bunky skore extremnich kandidatu ZVEDALY).
        var cfg = new MapCorrelatorConfig();
        double required = cfg.RequiredRasterMarginM(256, 0.05);   // produkcni grid

        Assert.That(required, Is.GreaterThan(cfg.SearchRangeM + cfg.HessianStepM),
                    "Bez clenu za rotaci by marze byla podhodnocena.");
        Assert.That(required, Is.EqualTo(3.96).Within(0.02));
        Assert.That(cfg.MapRasterMarginM, Is.GreaterThanOrEqualTo(required),
                    "Vychozi marze ma na produkcni grid stacit bez rozsirovani.");
    }

    [Test]
    public void Validate_ZapornaMarzeNejednoznacnosti_Vyhodi()
    {
        // Zaporna marze by prah dostala NAD maximum a test nejednoznacnosti by se obratil.
        Assert.That(() => new MapCorrelatorConfig { AmbiguityMargin = -0.01 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_ZapornaMinPeriod_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { MinPeriod = TimeSpan.FromMilliseconds(-1) }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_MinScoreMimoRozsahSkore_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { MinScore = 1.5 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_ZadnaUroven_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { Levels = new ScanLevel[0] }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }
}
