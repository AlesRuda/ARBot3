using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// <b>Sirkova brana koridoru plati az od chvile, kdy ma odhad sirky KVALITU.</b>
///
/// <para><b>Nacpak to je</b> (nalezeno rozborem 15. 9. 2026): brana
/// <c>MaxWidthDisagreementM</c> se ptala na mapovou sirku <b>drív</b>, nez se filtr mel z ceho
/// naucit — takze na ceste sirsi nez <c>roadwidth ± 1,5 m</c> se prvni merenie nikdy neprijalo,
/// filtr se nezalozil a hrana zustala <b>nema navzdy</b>. Na Hviezdoslavove (zadne <c>width</c>
/// tagy, tedy vsude default 3 m) by to znamenalo, ze koridor na vozovce nezmeri nic.</para>
///
/// <para>Lecba: <see cref="RoadWidthEstimator"/> bezi bez brany a sam rika, kdy uz se odhadu da
/// verit. Dokud kvalita neni dobra, sirkova brana <b>neplati</b> (neni s cim nesouhlasit)
/// a do fuze se <b>neposila nic</b> (bez duveryhodne sirky nechyti „chytly se jine hranice" nic).
/// Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class CorridorWidthTrustTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    private static AsyncFusionEngine EngineAt(double x, double y, double theta)
    {
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(x, y, 0.5, seed);
        engine.Enqueue(new PositionMeasurement(x, y, 0.5, 0.5, seed, "GPS"));
        engine.Enqueue(new HeadingMeasurement(theta, 0.05, seed, "Compass"));
        return engine;
    }

    private static (CameraFrame left, CameraFrame right) Frames(
        double width, double lateral, DateTime t, int count = 40)
    {
        var l = new List<PathEdge>();
        var r = new List<PathEdge>();
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * 0.15;
            l.Add(new PathEdge
            {
                Y = i,
                Left = 100 + i,
                LeftPoint = new Point4D { X = (float)s, Y = (float)(-lateral + width / 2), Z = 0, A = 1 },
            });
            r.Add(new PathEdge
            {
                Y = i,
                Right = 200 + i,
                RightPoint = new Point4D { X = (float)s, Y = (float)(-lateral - width / 2), Z = 0, A = 1 },
            });
        }
        return (new CameraFrame { Name = "Left", TimeStamp = t, PathEdges = l },
                new CameraFrame { Name = "Right", TimeStamp = t.AddMilliseconds(20), PathEdges = r });
    }

    private static CorridorLocalizer Localizer(AsyncFusionEngine engine, double mapWidth,
                                               CorridorLocalizerConfig cfg = null)
    {
        var origin = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(origin, mapWidth);
        cfg ??= new CorridorLocalizerConfig();
        // Jen OBOUSTRANNY koridor: jedna hrana (od 24. 9. 2026) by z prvniho, jeste nesparovaneho
        // snimku poslala vlastni merenie a zamichala by se do toho, co tu testy zkoumaji.
        // Jedna hrana ma vlastni soubor CorridorSingleEdgeTests.
        cfg.Corridor.SingleEdge = false;
        return new CorridorLocalizer(engine, net, origin, cfg);
    }

    /// <summary>Projede <paramref name="cycles"/> dvojic snimku o dane sirce a pricne poloze.</summary>
    private static CorridorFix Run(CorridorLocalizer loc, int cycles, double width,
                                   double lateral = 0, int firstIndex = 0)
    {
        CorridorFix last = null;
        for (int i = 0; i < cycles; i++)
        {
            var (l, r) = Frames(width, lateral, T0.AddMilliseconds((firstIndex + i) * 100));
            loc.Process(l);
            last = loc.Process(r);
        }
        return last;
    }

    [Test]
    public void DokudNeniKvalitaSirky_sirkovaBranaNeplati()
    {
        // Mapa rika 3 m, kamera vidi 6 m. Se starym chovanim by to byl WidthDisagreement uz
        // v prvnim cyklu - a nikdy nic jineho.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 3.0,
                            cfg: new CorridorLocalizerConfig { MaxWidthDisagreementM = 1.5 });

        var (l, r) = Frames(width: 6.0, lateral: 0, t: T0);
        loc.Process(l);
        loc.Process(r);

        Assert.That(loc.LastFix.Reason, Is.Not.EqualTo(CorridorFixReason.WidthDisagreement),
                    "bez odhadu neni s cim nesouhlasit - brana nesmi platit");
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.WidthNotTrusted));
    }

    [Test]
    public void DokudNeniKvalitaSirky_doFuzeNejdeNic()
    {
        // Bez duveryhodne sirky nema co chytit „prolozila se jina dvojice hranic" - proto se
        // merenie nepousti, i kdyz se jinak spocitalo.
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, mapWidth: 3.0);

        var (l, r) = Frames(width: 3.0, lateral: 0.6, t: T0);
        loc.Process(l);
        loc.Process(r);

        Assert.That(loc.EmittedCorrections, Is.Zero);
        var after = engine.GetStateAt(T0.AddMilliseconds(50));
        Assert.That(after.Y, Is.EqualTo(0).Within(0.01), "poza se nesmi pohnout");
    }

    [Test]
    public void PoUsazeniSirky_sePosila()
    {
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, mapWidth: 3.0);

        var last = Run(loc, cycles: 12, width: 3.0, lateral: 0.6);

        Assert.That(last, Is.Not.Null);
        Assert.That(last.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(loc.EmittedCorrections, Is.GreaterThan(0));
    }

    [Test]
    public void SirokaCestaNaUzkeMape_seNakonecZmeri()
    {
        // Tohle je ten nalez cely: mapa bez tagu width (vsude default), skutecna cesta dvakrat
        // sirsi. Se starym chovanim nula merenii, s estimatorem se to po rozjezdu chytne.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 3.0,
                            cfg: new CorridorLocalizerConfig { MaxWidthDisagreementM = 1.5 });

        var last = Run(loc, cycles: 12, width: 6.0);

        Assert.That(last, Is.Not.Null, "na sirsi ceste se koridor musi chytit taky");
        Assert.That(last.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(loc.Widths.TryGetWidth(last.Axis.WayId, out double w), Is.True);
        Assert.That(w, Is.EqualTo(6.0).Within(0.1), "odhad ma sedet na MERENE sirce, ne na mapove");
    }

    [Test]
    public void PoUsazeniSirky_branaZasePlati()
    {
        // Jakmile je odhad duveryhodny, prolozeni jine dvojice hranic uz se zamitnout MA.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 3.0,
                            cfg: new CorridorLocalizerConfig { MaxWidthDisagreementM = 1.0 });
        Run(loc, cycles: 12, width: 3.0);

        var (l, r) = Frames(width: 6.5, lateral: 0, t: T0.AddMilliseconds(1200));
        loc.Process(l);
        loc.Process(r);

        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.WidthDisagreement));
    }

    [Test]
    public void EstimatorSeUciIKdyzPozaNesedi()
    {
        // WidthUpdateMaxDisagreementM (0,3 m) uz sirku NEPODMINUJE: sirka je rozdil dvou primek
        // v ramci robotu, takze na poze nezavisi. Podminovat ji shodou s pozou by vyrobilo TYZ
        // zamek, jaky se prave odstranuje - pri chybe pozy 0,6 m by se odhad nezalozil nikdy.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 3.0,
                            cfg: new CorridorLocalizerConfig { WidthUpdateMaxDisagreementM = 0.3 });

        Run(loc, cycles: 12, width: 3.0, lateral: 0.6);   // 0,6 m > 0,3 m

        Assert.That(loc.Widths.TryGetWidth(1, out double w), Is.True,
                    "odhad se musi naucit i pri pricnem nesouhlasu nad WidthUpdateMaxDisagreementM");
        Assert.That(w, Is.EqualTo(3.0).Within(0.1));
    }

    [Test]
    public void PriVelkemPricnemNesouhlasu_seEstimatorUci()
    {
        // ⚠️ OTOCENO 18. 9. 2026 (drive "PriVelkemPricnemNesouhlasu_seEstimatorNeuci").
        //
        // Duvod, ktery tu stal ("nad MaxLateralDisagreementM uz neni jiste, ke KTERE hrane
        // merenie patri"), byl spravna otazka se spatnym meritkem: na "ke ktere hrane" je
        // EdgeAssociator (azimut + chi2 proti kovarianci pozy), ne pevne pravitko v metrech.
        // Ta brana byla navic tim, co odhad sirky hladovelo: pri poze 2,5-4,5 m mimo vozovku
        // (20260917-160558.rec) se estimator nenaucil nic -> WidthNotTrusted -> hrana nema.
        // Je to tyz zamek, jaky se 15. 9. 2026 odstranoval o patro niz.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 3.0,
                            cfg: new CorridorLocalizerConfig { MaxOutsideCorridorM = 5 });

        Run(loc, cycles: 12, width: 3.0, lateral: 1.2);

        Assert.That(loc.Widths.TryGetWidth(1, out double w), Is.True,
                    "odhad se musi naucit i pri velkem pricnem nesouhlasu - sirka na poze nezavisi");
        Assert.That(w, Is.EqualTo(3.0).Within(0.1));
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.Ok));
    }

    [Test]
    public void PricnyNesouhlasNadZrusenouBranou_projde()
    {
        // Jadro zmeny z 18. 9. 2026: pricny nesouhlas 2,0 m (nad byvalym stropem 1,5 m) uz
        // merenie nezahazuje. Kdyby brana zustala, skoncilo by to na LateralDisagreement
        // a fuze by o teto hrane neslysela nikdy - prave to delalo 81,8 % cyklu v zaznamu.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 3.0,
                            cfg: new CorridorLocalizerConfig { MaxOutsideCorridorM = 5 });

        // Nejdriv hrana dostane kvalitu sirky (jinak by se skoncilo na WidthNotTrusted), pak
        // teprve prijde cyklus s velkym pricnym nesouhlasem. Merit se musi TEN cyklus: kdyby se
        // jelo dvanactkrat za sebou mimo, korekce by pozu dotahly a nesouhlas by klesl k nule -
        // coz je mimochodem prave to, co tvrda brana znemoznovala.
        Run(loc, cycles: 12, width: 3.0, lateral: 0);
        long pred = loc.EmittedCorrections;
        var fix = Run(loc, cycles: 1, width: 3.0, lateral: 2.0, firstIndex: 12);

        Assert.That(fix, Is.Not.Null, "merenie s velkym pricnym nesouhlasem se ma pustit dal");
        Assert.That(loc.EmittedCorrections, Is.GreaterThan(pred));
        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(Math.Abs(fix.LateralDisagreement), Is.GreaterThan(1.5),
                    "test by nic nedokazoval, kdyby nesouhlas nebyl nad byvalym stropem");
    }
}
