using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Cely retez hranove lokalizace: hranicni body v ramci robotu → koridor → srovnani s mapou →
/// merenia do fuze. Testuje se PRIMO <c>Process()</c>, ne pres vlakno.
/// Viz doc/map-correlation-localization.md.
/// </summary>
public class CorridorLocalizerTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Fuze s pouzitelnou pozou v case T0. Seed je 200 ms PRED T0 - merenia s casem &lt;= tBase
    /// se zahazuji, takze seedovat presne v T0 by test "poslalo se merenie" shodilo z duvodu,
    /// ktery s lokalizaci nema nic spolecneho (viz MapCorrelatorTests).
    /// </summary>
    private static AsyncFusionEngine EngineAt(double x, double y, double theta)
    {
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(x, y, 0.5, seed);
        engine.Enqueue(new PositionMeasurement(x, y, 0.5, 0.5, seed, "GPS"));
        engine.Enqueue(new HeadingMeasurement(theta, 0.05, seed, "Compass"));
        return engine;
    }

    /// <summary>
    /// Snimek s hranicemi cesty: koridor sirky <paramref name="width"/>, robot
    /// <paramref name="lateral"/> vlevo od jeho osy, cesta stocena o <paramref name="dirRad"/>.
    /// Jedna kamera nese LEVOU hranici, druha PRAVOU (jako na robotu).
    /// </summary>
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

    private static CorridorLocalizer Localizer(AsyncFusionEngine engine, double mapWidth = 4.0,
                                               CorridorLocalizerConfig cfg = null)
    {
        var origin = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(origin, mapWidth);
        return new CorridorLocalizer(engine, net, origin, cfg);
    }

    [Test]
    public void PrvniSnimek_jeBezDvojice()
    {
        // Koridor potrebuje obe strany, tedy obe kamery - prvni snimek nemuze stacit.
        var loc = Localizer(EngineAt(0, 0, 0));
        var (left, _) = Frames(4.0, 0, 0, T0);

        var fix = loc.Process(left);

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.NoPair));
    }

    [Test]
    public void PozaNaOse_nemaCoOpravovat()
    {
        // Robot presne na ose cesty (mapa i kamera se shoduji) -> nesouhlas ~0.
        var loc = Localizer(EngineAt(0, 0, 0));
        var (left, right) = Frames(width: 4.0, lateral: 0, dirRad: 0, t: T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix, Is.Not.Null);
        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(fix.LateralDisagreement, Is.EqualTo(0).Within(0.05));
        Assert.That(fix.Corridor.Width, Is.EqualTo(4.0).Within(0.05));
    }

    [Test]
    public void ChybaPricnePolohy_seNajde()
    {
        // Fuze si mysli, ze je robot na ose (y = 0), ale kamera vidi, ze je 0,6 m vlevo od osy
        // koridoru. Nesouhlas MUSI vyjit 0,6 m - to je chyba lokalizace.
        var loc = Localizer(EngineAt(0, 0, 0));
        var (left, right) = Frames(width: 4.0, lateral: 0.6, dirRad: 0, t: T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(fix.LateralDisagreement, Is.EqualTo(0.6).Within(0.05));
    }

    [Test]
    public void KdyzPozaOdpovidaKamere_nesouhlasJeNula()
    {
        // Robot je SKUTECNE 0,6 m vlevo od osy (fuze to vi) a kamera to tak i vidi -> nic k oprave.
        var loc = Localizer(EngineAt(0, 0.6, 0));
        var (left, right) = Frames(width: 4.0, lateral: 0.6, dirRad: 0, t: T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(fix.LateralDisagreement, Is.EqualTo(0).Within(0.05));
    }

    [Test]
    public void MerenieJdeDoFuze_aPosuneOdhad()
    {
        // Fuze zna pozu na ose, kamera hlasi 0,6 m vlevo -> po zapracovani se odhad musi posunout
        // tim smerem. (Ne nutne cely - vaha proti GPS.)
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine);
        var (left, right) = Frames(4.0, 0.6, 0, T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix.EmittedLateral, Is.True);
        Assert.That(loc.EmittedCorrections, Is.GreaterThan(0));
        var after = engine.GetStateAt(T0.AddMilliseconds(50));
        Assert.That(after, Is.Not.Null);
        Assert.That(after.Y, Is.GreaterThan(0.05), "odhad se musi posunout vlevo (na sever)");
    }

    [Test]
    public void SendCorrectionsFalse_pocitaAleNeposila()
    {
        // A/B se stejnou zatezi: vypocet bezi, do fuze nejde nic.
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, cfg: new CorridorLocalizerConfig { SendCorrections = false });
        var (left, right) = Frames(4.0, 0.6, 0, T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok), "vysledek se pocita dal");
        Assert.That(fix.LateralDisagreement, Is.EqualTo(0.6).Within(0.05));
        Assert.That(fix.EmittedLateral, Is.False);
        Assert.That(loc.EmittedCorrections, Is.Zero);
        var after = engine.GetStateAt(T0.AddMilliseconds(50));
        Assert.That(after.Y, Is.EqualTo(0).Within(0.01), "poza se nesmi pohnout");
    }

    [Test]
    public void VelkyNesouhlas_seNepusti()
    {
        // Strop na nesouhlas s mapou: nejspis koreluje na jinou cestu nebo je hranice falesna.
        var engine = EngineAt(0, 0, 0);
        var cfg = new CorridorLocalizerConfig { MaxLateralDisagreementM = 0.3, MaxWidthDisagreementM = 5 };
        var loc = Localizer(engine, cfg: cfg);
        var (left, right) = Frames(4.0, 1.2, 0, T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.LateralDisagreement));
        Assert.That(loc.EmittedCorrections, Is.Zero);
    }

    [Test]
    public void NesouhlasSirky_seNepusti()
    {
        // Mapa rika 4 m, kamera vidi 2 m -> prolozila se jina dvojice hranic, ne ta cesta.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0,
                            cfg: new CorridorLocalizerConfig { MaxWidthDisagreementM = 0.5 });
        var (left, right) = Frames(width: 2.0, lateral: 0, dirRad: 0, t: T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.WidthDisagreement));
    }

    [Test]
    public void SirkaSeUciZMereni()
    {
        // Mapa rika 4 m, kamera konzistentne vidi 3,6 m -> filtr se ma k merene hodnote priblizovat.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0);

        for (int i = 0; i < 20; i++)
        {
            var (l, r) = Frames(width: 3.6, lateral: 0, dirRad: 0, t: T0.AddMilliseconds(i * 100));
            loc.Process(l);
            loc.Process(r);
        }

        Assert.That(loc.Widths.Count, Is.EqualTo(1));
        double w = loc.Widths.Estimate(1, 4.0);
        Assert.That(w, Is.LessThan(4.0), "odhad se musi hnout k merene sirce");
        Assert.That(w, Is.EqualTo(3.6).Within(0.3));
    }

    [Test]
    public void ChybaKurzu_seNajde()
    {
        // Fuze si mysli, ze robot miri na vychod; kamera vidi cestu stocenou o 5 stupnu.
        var loc = Localizer(EngineAt(0, 0, 0));
        var (left, right) = Frames(4.0, 0, 5 * Math.PI / 180, T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(fix.HeadingDisagreementRad * 180 / Math.PI, Is.EqualTo(5).Within(0.5));
        Assert.That(fix.EmittedHeading, Is.True);
    }

    [Test]
    public void SnimkyMimoCasoveOkno_seNesparuji()
    {
        var cfg = new CorridorLocalizerConfig { MaxCameraSkewMs = 10 };
        var loc = Localizer(EngineAt(0, 0, 0), cfg: cfg);
        var (left, right) = Frames(4.0, 0, 0, T0);   // rozestup 20 ms

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.NoPair));
    }

    [Test]
    public void RobotMimoKoridor_seNepusti()
    {
        // Nalezeno merením 22. 8. 2026: bez teto kontroly hlasil stupen platna merenia i kdyz byl
        // robot 2,1 m od osy koridoru sirokeho 2 m, tedy metr MIMO cestu. To s tvrzenim „jsem na
        // teto ceste" nejde dohromady - bud se prolozila jina dvojice hranic, nebo robot sjel.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 2.0,
                            cfg: new CorridorLocalizerConfig
                            {
                                MaxOutsideCorridorM = 0.5,
                                MaxLateralDisagreementM = 10,   // ať to nespadne na jiném gatu
                                MaxWidthDisagreementM = 10,
                            });
        var (left, right) = Frames(width: 2.0, lateral: 2.1, dirRad: 0, t: T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.OutsideCorridor));
        Assert.That(loc.EmittedCorrections, Is.Zero);
    }

    [Test]
    public void RobotUKrajeKoridoru_jeJesteVPorade()
    {
        // Robot u kraje (ale uvnitr + rezerva) merit smi - jinak by se korekce ztratila prave tam,
        // kde je nejvic potreba.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 2.0);
        var (left, right) = Frames(width: 2.0, lateral: 0.9, dirRad: 0, t: T0);

        loc.Process(left);
        var fix = loc.Process(right);

        Assert.That(fix, Is.Not.Null);
        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
    }

    [Test]
    public void VychoziGating_jeSoft()
    {
        // Namereno 22. 8. 2026: s Reject zahodil gating 77 % korekci (merenie tvrdi 3 cm jistoty
        // a nesouhlasi o 55 cm, coz JE odlehla hodnota) a nesouhlas s mapou pak neklesal vubec.
        // Soft misto zahozeni nafoukne R, takze se korekce uplatni. Viz decisions.md 20. 8. 2026.
        Assert.That(new CorridorLocalizerConfig().GateMode, Is.EqualTo(ARBot.Common.Fusion.GateMode.Soft));
    }

    [Test]
    public void SnimekBezMetrickychBodu_nicNerozbije()
    {
        var loc = Localizer(EngineAt(0, 0, 0));
        var frame = new CameraFrame
        {
            Name = "Left",
            TimeStamp = T0,
            PathEdges = new List<PathEdge> { new PathEdge { Y = 1, Left = 5 } },   // bod neplatny
        };

        Assert.That(() => loc.Process(frame), Throws.Nothing);
        Assert.That(loc.Frames, Is.EqualTo(1));
    }
}
