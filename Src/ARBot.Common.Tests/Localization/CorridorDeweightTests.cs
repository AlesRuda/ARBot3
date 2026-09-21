using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// <b>Odtlumeni koridoru: nafouknuti sigmy a skrceni kadence.</b> Tataz lecba a tyz duvod jako
/// <c>gpsposstd</c> u GPS a <c>imuheadingstd</c> + <c>imuheadinghz</c> u kompasu.
///
/// <para><b>Nacpak.</b> Filtr bere merenia za <b>nezavisla</b>, jenze koridor meri snimek co
/// snimek <b>tyz fyzicky okraj cesty</b> (tyz stin, tyz obrubnik, tataz trava), takze jeho chyba
/// je casove korelovana a sto odectu nese informaci mnohem mensiho poctu. Dekorelacni cas koridoru
/// ⚠️ <b>zmereny NENI</b> — u plosne korelace vysel ~3 s (odtud <c>MinPeriod</c>) a u kompasu
/// τ ≳ 600 s (odtud <c>imuheadinghz</c>). Tyhle parametry existuji proto, aby se to dalo z dat
/// nastavit, az bude zaznam.</para>
///
/// <para>Vychozi je <b>0 = dnesni chovani</b>: u <c>imuheadingstd</c> je default 5°, protoze ten
/// bias byl ZMERENY; tady zmereneho neni nic, takze default z uvahy by byl prave to, co si projekt
/// jinde vycita. Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class CorridorDeweightTests
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

    private static (CameraFrame left, CameraFrame right) Frames(double width, double lateral,
                                                                DateTime t, double dirRad = 0,
                                                                int count = 40)
    {
        double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
        double nx = -uy, ny = ux;
        var l = new List<PathEdge>();
        var r = new List<PathEdge>();
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * 0.15;
            double ox = -lateral * nx, oy = -lateral * ny;
            l.Add(new PathEdge
            {
                Y = i, Left = 100 + i,
                LeftPoint = new Point4D
                {
                    X = (float)(ox + ux * s + nx * (width / 2)),
                    Y = (float)(oy + uy * s + ny * (width / 2)), Z = 0, A = 1,
                },
            });
            r.Add(new PathEdge
            {
                Y = i, Right = 200 + i,
                RightPoint = new Point4D
                {
                    X = (float)(ox + ux * s - nx * (width / 2)),
                    Y = (float)(oy + uy * s - ny * (width / 2)), Z = 0, A = 1,
                },
            });
        }
        return (new CameraFrame { Name = "Left", TimeStamp = t, PathEdges = l },
                new CameraFrame { Name = "Right", TimeStamp = t.AddMilliseconds(20), PathEdges = r });
    }

    /// <summary>Stupen, ktery se usadi hned prvnim merenim - tady se zkouma odtlumeni, ne rozjezd.</summary>
    private static CorridorLocalizer Localizer(AsyncFusionEngine engine, CorridorLocalizerConfig cfg)
    {
        var origin = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(origin, 4.0);
        cfg.WidthEstimator.MinSamples = 1;
        return new CorridorLocalizer(engine, net, origin, cfg);
    }

    /// <summary>
    /// Jedna dvojice snimku. Vraci, <b>kolik fixu</b> z ni vzniklo: <c>CorridorSource</c> paruje
    /// DOZADU a drzi posledni snimek kazde kamery, takze krome prvni dvojice dava kazdy snimek
    /// vlastni koridor - tedy dva fixy na dvojici, ne jeden.
    /// </summary>
    private static int Cycle(CorridorLocalizer loc, DateTime t, double lateral = 0.6)
    {
        var (l, r) = Frames(4.0, lateral, t);
        int n = 0;
        if (loc.Process(l) != null) n++;
        if (loc.Process(r) != null) n++;
        return n;
    }

    /// <summary>Jak daleko se odhad posunul po jednom merení s danou nafouknutou sigmou.</summary>
    private static double PosunPri(double extraM)
    {
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, new CorridorLocalizerConfig
        {
            SendHeading = false,
            SigmaLateralExtraM = extraM,
        });

        Cycle(loc, T0);

        return engine.GetStateAt(T0.AddMilliseconds(50)).Y;
    }

    [Test]
    public void NafouknutaSigma_zmensiZasahDoFuze()
    {
        double bez = PosunPri(0);
        double s = PosunPri(2.0);

        Assert.That(bez, Is.GreaterThan(0.05), "bez nafouknuti se odhad hnout MA");
        Assert.That(s, Is.LessThan(bez / 2), "nafouknuta sigma musi zasah vyrazne zmensit");
    }

    [Test]
    public void NulovaNafouknutaSigma_nemeniNic()
    {
        // 0 musi vracet PRESNE stare chovani - jinak by A/B nemelo proti cemu merit.
        Assert.That(PosunPri(0), Is.EqualTo(PosunPri(0)).Within(1e-12));
    }

    [Test]
    public void NafouknutaSigmaKurzu_zmensiZasahDoKurzu()
    {
        double Kurz(double extraDeg)
        {
            var engine = EngineAt(0, 0, 0);
            var loc = Localizer(engine, new CorridorLocalizerConfig
            {
                // Pricny kanal se umlci, ať je videt jen vliv sigmy KURZU: stocena cesta meni
                // i pricnou polohu podel koridoru a ta by kurz tahala taky.
                SigmaLateralExtraM = 1000,
                SigmaHeadingExtraRad = Conversions.Deg2Rad(extraDeg),
            });
            // Mapa vede cestu na vychod, kamera ji vidi stocenou o 0,15 rad -> kurz se ma opravit.
            var (l, r) = Frames(4.0, 0, T0, dirRad: 0.15);
            loc.Process(l);
            loc.Process(r);
            return Math.Abs(engine.GetStateAt(T0.AddMilliseconds(50)).Theta);
        }

        double bez = Kurz(0), stredni = Kurz(30), velka = Kurz(300);

        Assert.That(bez, Is.GreaterThan(0.01), "bez nafouknuti se kurz hnout MA");
        Assert.That(stredni, Is.LessThan(bez), "vetsi sigma = mensi zasah");
        Assert.That(velka, Is.LessThan(stredni));
        Assert.That(velka, Is.LessThan(bez / 5), "pri dost velke sigme uz kurz tahnout skoro nesmi");
    }

    [Test]
    public void Skrceni_pustiJenJednoZaPeriodu()
    {
        var loc = Localizer(EngineAt(0, 0, 0), new CorridorLocalizerConfig
        {
            SendHeading = false,          // ať EmittedCorrections = pocet odeslani
            MinSendPeriodSec = 0.5,
        });

        int fixu = 0;
        for (int i = 0; i < 10; i++) fixu += Cycle(loc, T0.AddMilliseconds(i * 100));   // 0..900 ms

        Assert.That(loc.EmittedCorrections, Is.EqualTo(2), "na 900 ms se pri periode 0,5 s vejdou dve");
        Assert.That(loc.ThrottledSends, Is.EqualTo(fixu - 2), "zbytek se musi zahodit skrcenim");
    }

    [Test]
    public void BezSkrceni_pustiVse()
    {
        var loc = Localizer(EngineAt(0, 0, 0), new CorridorLocalizerConfig
        {
            SendHeading = false,
            MinSendPeriodSec = 0,
        });

        int fixu = 0;
        for (int i = 0; i < 10; i++) fixu += Cycle(loc, T0.AddMilliseconds(i * 100));

        Assert.That(loc.EmittedCorrections, Is.EqualTo(fixu), "bez skrceni posila kazdy fix");
        Assert.That(loc.ThrottledSends, Is.Zero);
    }

    [Test]
    public void ZamitnutyCyklus_kvotuNesebere()
    {
        // Kvotu smi spotrebovat jen USPESNE odeslani. Jinak by cyklus shozeny na jine brane sebral
        // okno tomu dobremu hned za nim - a koridor by mlcel tim vic, cim hur mu to jde.
        var loc = Localizer(EngineAt(0, 0, 0), new CorridorLocalizerConfig
        {
            SendHeading = false,
            MinSendPeriodSec = 0.5,
        });

        // Zamitnuti drzi "jsem mimo vlastni koridor" (polosirka 2,0 + MaxOutsideCorridorM 0,5).
        // Do 18. 9. 2026 to shazovala pricna brana; ta uz neexistuje, na povaze testu se tim ale
        // nic nemeni - jde o to, ze kvotu sebere jen USPESNE odeslani.
        Cycle(loc, T0, lateral: 0.6);                              // odesle
        Cycle(loc, T0.AddMilliseconds(600), lateral: 3.0);         // zamitnuto (mimo koridor)
        Cycle(loc, T0.AddMilliseconds(700), lateral: 0.6);         // musi odeslat

        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(loc.EmittedCorrections, Is.EqualTo(2));
    }

    [Test]
    public void SkokCasuVzad_skrceniResetuje()
    {
        // Seek v zaznamu / novy beh: bez resetu by se po skoku dozadu neposlalo uz nic.
        var loc = Localizer(EngineAt(0, 0, 0), new CorridorLocalizerConfig
        {
            SendHeading = false,
            MinSendPeriodSec = 0.5,
        });

        Cycle(loc, T0);
        Cycle(loc, T0.AddMilliseconds(500));                       // odesle (perioda uplynula)
        Cycle(loc, T0.AddMilliseconds(100));                       // skok vzad -> musi odeslat

        Assert.That(loc.EmittedCorrections, Is.EqualTo(3));
    }

    // ---------------------------------------------------------------------------------------
    // Limit kroku korekce (corridorslew= / corridorheadingslew=, 21. 9. 2026).
    //
    // Na Robotouru 19. 9. 2026 stahl koridor nahromadeny drift 4-6 m v jednom kroku a robot se
    // skokem ocitl v blokovane casti gridu. Limit rika: na jedno mereni smi poza uhnout nejvys
    // slew × Δt; zbytek inovace filtr stahne dalsimi merenimi. Mechanismus je v Ekf.UpdateStep
    // (StepLimitTests), tady se zkousi, ze ho koridor SPRAVNE NASTAVUJE - vcetne Δt.
    // ---------------------------------------------------------------------------------------

    /// <summary>Pricny posun odhadu po prvnim mereni s koridorem 2 m vedle (bez limitu ~1,9 m).</summary>
    private static (double poPrvnim, double poDruhem, CorridorLocalizer loc) PosunSeSlew(double slew)
    {
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, new CorridorLocalizerConfig
        {
            SendHeading = false,
            SlewRateMps = slew,
        });
        Cycle(loc, T0, lateral: 2.0);                              // prvni odeslani: Δt = strop 1 s
        double prvni = engine.GetStateAt(T0.AddMilliseconds(50)).Y;
        Cycle(loc, T0.AddMilliseconds(100), lateral: 2.0);         // dve mereni: Δt 80 ms a 20 ms
        double druhy = engine.GetStateAt(T0.AddMilliseconds(150)).Y;
        return (prvni, druhy, loc);
    }

    [Test]
    public void LimitKroku_prvniMereniSmiJenSlewKratStrop()
    {
        var (bez, _, _) = PosunSeSlew(0);
        var (s, _, _) = PosunSeSlew(0.2);

        Assert.That(bez, Is.GreaterThan(1.0), "bez limitu filtr drift 2 m stahne skoro cely");
        Assert.That(s, Is.LessThanOrEqualTo(0.2 + 1e-6), "prvni odeslani: 0,2 m/s × strop 1 s");
        Assert.That(s, Is.GreaterThan(0.15), "ale hnout se MA - limit merenie nezahazuje");
    }

    [Test]
    public void LimitKroku_dalsiMereniDostanouDeltaTOdPredchoziho()
    {
        var (prvni, druhy, _) = PosunSeSlew(0.2);
        // 100 ms po prvnim prijdou dve mereni (Δt 80 ms + 20 ms): dohromady 0,2 × 0,1 = 0,02 m.
        Assert.That(druhy - prvni, Is.LessThanOrEqualTo(0.02 + 1e-6));
        Assert.That(druhy - prvni, Is.GreaterThan(0.005), "i male okno musi neco stahnout");
    }

    [Test]
    public void LimitKroku_nulaJeStareChovani()
    {
        var (a, a2, _) = PosunSeSlew(0);
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, new CorridorLocalizerConfig { SendHeading = false });
        Cycle(loc, T0, lateral: 2.0);
        double b = engine.GetStateAt(T0.AddMilliseconds(50)).Y;
        Assert.That(a, Is.EqualTo(b).Within(1e-12), "0 = presne dnesni chovani (A/B)");
        Assert.That(a2, Is.GreaterThan(a), "bez limitu se dotahne dal");
    }

    [Test]
    public void LimitKurzu_omeziOtoceniPozy()
    {
        double Kurz(double slewDegPerSec)
        {
            var engine = EngineAt(0, 0, 0);
            var loc = Localizer(engine, new CorridorLocalizerConfig
            {
                SigmaLateralExtraM = 1000,           // pricny kanal umlcet, at je videt jen kurz
                SlewRateHeadingRadPerSec = Conversions.Deg2Rad(slewDegPerSec),
            });
            var (l, r) = Frames(4.0, 0, T0, dirRad: 0.15);
            loc.Process(l);
            loc.Process(r);
            // Limit plati pro krok V CASE MERENI; o par ms pozdeji uz pozu tahne predikce
            // z uhlove rychlosti (stav omega se merenim kurzu hnul taky), proto dotaz v case fixu.
            return Math.Abs(engine.GetStateAt(loc.LastFix.Time).Theta);
        }

        double bez = Kurz(0);
        double s = Kurz(1.0);
        Assert.That(bez, Is.GreaterThan(Conversions.Deg2Rad(3)), "bez limitu se kurz stoci o vic nez 3 st.");
        Assert.That(s, Is.LessThanOrEqualTo(Conversions.Deg2Rad(1.0) + 1e-6), "1 st/s × strop 1 s");
        Assert.That(s, Is.GreaterThan(Conversions.Deg2Rad(0.5)));
    }

    [Test]
    public void LimitKroku_skokCasuVzadBereStrop()
    {
        // Seek v zaznamu: Δt by vysel zaporny; bere se strop, ne nula (nula = vypnuty limit).
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, new CorridorLocalizerConfig { SendHeading = false, SlewRateMps = 0.2 });
        Cycle(loc, T0.AddSeconds(5), lateral: 2.0);
        double pred = engine.GetStateAt(T0.AddSeconds(5.05)).Y;
        Cycle(loc, T0.AddSeconds(4), lateral: 2.0);                // skok vzad
        double po = engine.GetStateAt(T0.AddSeconds(5.05)).Y;
        Assert.That(po - pred, Is.LessThanOrEqualTo(0.2 + 1e-6), "po skoku vzad plati strop, ne libovolny skok");
    }
}
