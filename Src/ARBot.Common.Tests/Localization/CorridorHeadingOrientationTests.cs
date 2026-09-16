using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// <b>Primka nema orientaci.</b> Kamera vidi cestu, ale ne kterym smerem po ni jedeme — smer
/// <c>x</c> a <c>x + 180°</c> jsou totez. Rozhodnout to umi jen kurz robotu.
///
/// <para>Do 16. 9. 2026 se merenie kurzu pocitalo jako
/// <c>(PoseTheta + HeadingRelRad) − DirectionRad</c> <b>bez slozeni a bez rozhodnuti o smyslu</b>.
/// Obe veliciny jsou slozene na ±90°, takze staci, aby se rozesly pres okraj toho intervalu,
/// a do fuze jde kurz otoceny o 180°. Naměřeno nad <c>records/test/20260916-164926.rec</c>:
/// tykalo by se to 40 ze 424 prijatych cyklu (9,4 %), a <c>GateMode.Soft</c> takove merenie
/// NEZAHODI, jen odtlumi.</para>
///
/// <para>Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class CorridorHeadingOrientationTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void RozdilSmeruDvouPrimek_seSKLADAnaPlusMinus90()
    {
        // Presne ta dvojice, na ktere se to lame: mapa rika +89 stupnu, kamera -87.
        // Jako SIPKY se lisi o 176 stupnu, jako PRIMKY o 4.
        double mapa = Conversions.Deg2Rad(89);
        double kamera = Conversions.Deg2Rad(-87);

        double surovy = mapa - kamera;
        double slozeny = Conversions.NormalizeHalfOrientation(mapa - kamera);

        Assert.That(Conversions.Rad2Deg(surovy), Is.EqualTo(176).Within(0.001),
                    "premisa: surovy rozdil je 176 stupnu");
        Assert.That(Conversions.Rad2Deg(slozeny), Is.EqualTo(-4).Within(0.001),
                    "slozeny rozdil dvou PRIMEK jsou 4 stupne");
    }

    [Test]
    public void SmerCesty_rozhodujeKURZrobotu()
    {
        // Vodorovna primka: robot po ni muze jet na vychod i na zapad. Kurz je jedina reference.
        double primka = 0;

        Assert.That(Conversions.Rad2Deg(Conversions.NormalizePrimaryOrientation(0, primka)),
                    Is.EqualTo(0).Within(0.001));
        Assert.That(Math.Abs(Conversions.Rad2Deg(
                        Conversions.NormalizePrimaryOrientation(Math.PI, primka))),
                    Is.EqualTo(180).Within(0.001));
    }

    [Test]
    public void HeadingDisagreement_seUKLADAslozeny()
    {
        // Diagnostika musi rikat "nesouhlas 4 stupne", ne "176". Driv se ukladal surovy rozdil.
        var engine = EngineAt(0, 0, Conversions.Deg2Rad(-89));
        var loc = Localizer(engine, assoc: false);
        var (l, r) = Frames(4.0, 0, Conversions.Deg2Rad(-87), T0);

        loc.Process(l);
        loc.Process(r);

        Assert.That(Math.Abs(Conversions.Rad2Deg(loc.LastFix.HeadingDisagreementRad)),
                    Is.LessThanOrEqualTo(90.0001),
                    "nesouhlas dvou PRIMEK nemuze byt vetsi nez 90 stupnu");
    }

    [Test]
    public void KurzKolmyNaCestu_NEPREKLOPIodhadO180()
    {
        // Regresni test na tu vadu. Robot mid kurz -89 stupnu (skoro kolmo na cestu, ktera vede
        // na vychod) a kamera vidi cestu pod -87 stupni. Stara formule z toho udelala
        // -89 + 176 = +87, tedy kurz otoceny o 180 stupnu; spravne je -89 + (-4) = -93.
        //
        // Prirazeni se tu VYPINA schvalne: s nim by tenhle cyklus padl uz na veto azimutu, takze
        // by se do Send() vubec nedostal - a prave tim se ta vada dnes maskuje. Test ma hlidat
        // Send() sam o sobe, ne soucin obou pojistek.
        double kurz = Conversions.Deg2Rad(-89);
        var engine = EngineAt(0, 0, kurz);
        var loc = Localizer(engine, assoc: false);
        var (l, r) = Frames(4.0, 0, Conversions.Deg2Rad(-87), T0);

        loc.Process(l);
        loc.Process(r);

        Assert.That(loc.EmittedCorrections, Is.GreaterThan(0), "premisa: neco se poslat musi");

        double po = engine.GetStateAt(T0.AddMilliseconds(50)).Theta;
        double odchylka = Math.Abs(Conversions.Rad2Deg(Conversions.NormalizeOrientation(po - kurz)));

        Assert.That(odchylka, Is.LessThan(90),
                    $"odhad kurzu se preklopil: z {Conversions.Rad2Deg(kurz):F1} na "
                    + $"{Conversions.Rad2Deg(po):F1} stupnu");
    }

    [Test]
    public void BeznaCesta_odhadSeTAHNEkeSMERUcesty()
    {
        // Kontrola, ze oprava nerozbila znamenko v beznem pripade. Robot ma kurz +8 stupnu, cesta
        // vede na vychod (0), takze mapa ji v ramci robotu vidi pod -8. Kamera ji ale vidi pod -4,
        // tedy tvrdi "kurz je +4" - odhad se ma hnout z 8 dolu ke 4.
        //
        // ⚠️ Kdyby kamera videla presne -8, merenie by rikalo TOTEZ co poza a neopravilo by nic;
        // presne na tom tenhle test poprve spadl.
        double kurz = Conversions.Deg2Rad(8);
        var engine = EngineAt(0, 0, kurz);
        var loc = Localizer(engine);
        var (l, r) = Frames(4.0, 0, Conversions.Deg2Rad(-4), T0);

        loc.Process(l);
        loc.Process(r);

        Assert.That(loc.EmittedCorrections, Is.GreaterThan(0), "premisa: neco se poslat musi");

        double po = Conversions.Rad2Deg(engine.GetStateAt(T0.AddMilliseconds(50)).Theta);
        Assert.That(po, Is.LessThan(8.0), "odhad se ma hnout smerem ke kurzu, ktery rika kamera");
        Assert.That(po, Is.GreaterThan(0.0), "ale ne pres nej na druhou stranu");
    }

    // -------------------------------------------------------------------------------------------

    private static AsyncFusionEngine EngineAt(double x, double y, double theta)
    {
        // Kurz se musi INICIALIZOVAT, ne poslat jako merenie: merenia s casem <= tBase se
        // zahazuji, takze HeadingMeasurement presne v seedu by se ztratilo a filtr by zustal
        // na theta = 0. (Na te premise uz jednou spadl tenhle test.)
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(x, y, 0.5, seed);
        engine.InitializeHeading(theta, 0.05, seed);
        engine.Enqueue(new PositionMeasurement(x, y, 0.5, 0.5, seed, "GPS"));
        return engine;
    }

    private static CorridorLocalizer Localizer(AsyncFusionEngine engine, bool assoc = true)
    {
        var origin = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(origin, 4.0);
        var cfg = new CorridorLocalizerConfig();
        cfg.WidthEstimator.MinSamples = 1;
        cfg.Association.Enabled = assoc;
        return new CorridorLocalizer(engine, net, origin, cfg);
    }

    private static (CameraFrame left, CameraFrame right) Frames(
        double width, double lateral, double dirRad, DateTime t, int count = 40)
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
                Y = i,
                Left = 100 + i,
                LeftPoint = new Point4D
                {
                    X = (float)(ox + ux * s + nx * (width / 2)),
                    Y = (float)(oy + uy * s + ny * (width / 2)),
                    Z = 0, A = 1,
                },
            });
            r.Add(new PathEdge
            {
                Y = i,
                Right = 200 + i,
                RightPoint = new Point4D
                {
                    X = (float)(ox + ux * s - nx * (width / 2)),
                    Y = (float)(oy + uy * s - ny * (width / 2)),
                    Z = 0, A = 1,
                },
            });
        }
        return (new CameraFrame { Name = "Left", TimeStamp = t, PathEdges = l },
                new CameraFrame { Name = "Right", TimeStamp = t.AddMilliseconds(20), PathEdges = r });
    }
}
