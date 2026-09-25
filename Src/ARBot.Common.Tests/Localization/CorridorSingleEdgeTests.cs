using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// <b>Merenie z JEDNE hrany cesty</b> (od 24. 9. 2026, <c>corridorsingle=</c>).
///
/// <para><b>Nacpak to je:</b> na siroke cyklostezce v Modranech (23. 9. 2026) nedal oboustranny
/// koridor ze ctyr zaznamu ani jedno merenie — vzdalenejsi hranici kamera vidi ridce, takze Track
/// se nemel podle ceho korigovat a FreeRun jel po ujizdejicim kurzu. Kurz z jedne hrany na sirce
/// nezavisi; pricna poloha se pocita pres predpokladanou sirku (naucenou, jinak mapovou s vetsi
/// sigmou). Viz doc/map-correlation-localization.md.</para>
/// </summary>
public class CorridorSingleEdgeTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------- CorridorFinder

    /// <summary>Body leve a prave hranice koridoru v ramci robotu (jako v CorridorFinderTests).</summary>
    private static (List<Point2D> left, List<Point2D> right) Edges(
        double width, double lateral, double dirRad, int count = 40)
    {
        var left = new List<Point2D>();
        var right = new List<Point2D>();
        double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
        double nx = -uy, ny = ux;
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * 0.15;
            double ox = -lateral * nx, oy = -lateral * ny;
            left.Add(new Point2D(ox + ux * s + nx * (width / 2), oy + uy * s + ny * (width / 2)));
            right.Add(new Point2D(ox + ux * s - nx * (width / 2), oy + uy * s - ny * (width / 2)));
        }
        return (left, right);
    }

    [Test]
    public void JenLevaHrana_daSmerAOdstup()
    {
        var (l, _) = Edges(width: 4.0, lateral: 0.5, dirRad: 0.1);

        var c = new CorridorFinder().Find(l, new List<Point2D>());

        Assert.That(c.Ok, Is.False, "oboustranny koridor z jedne hrany nevznika");
        Assert.That(c.Reason, Is.EqualTo(CorridorReason.OneSideOnly), "duvod se nemeni");
        Assert.That(c.SingleSide, Is.EqualTo(CorridorSide.Left));
        Assert.That(c.DirectionRad, Is.EqualTo(0.1).Within(0.01));
        Assert.That(c.EdgeOffset, Is.EqualTo(1.5).Within(0.02), "leva hrana je W/2 - lateral vlevo");
        Assert.That(c.SingleEdgeLateral(4.0), Is.EqualTo(0.5).Within(0.02));
        Assert.That(c.Width, Is.Zero, "sirka z jedne hrany neni");
    }

    [Test]
    public void JenPravaHrana_daSmerAOdstup()
    {
        var (_, r) = Edges(width: 3.0, lateral: -0.3, dirRad: 0);

        var c = new CorridorFinder().Find(new List<Point2D>(), r);

        Assert.That(c.SingleSide, Is.EqualTo(CorridorSide.Right));
        Assert.That(c.EdgeOffset, Is.EqualTo(-1.2).Within(0.02), "prava hrana je vpravo = zaporny odstup");
        Assert.That(c.SingleEdgeLateral(3.0), Is.EqualTo(-0.3).Within(0.02));
    }

    [Test]
    public void ChybaSirky_jdeDoPricnePolohyPolovinou()
    {
        // Skutecna cesta 5 m, mapa rika 3 m (Modrany bez tagu width): poloha vyjde o 1 m vedle.
        var (l, _) = Edges(width: 5.0, lateral: 0, dirRad: 0);

        var c = new CorridorFinder().Find(l, new List<Point2D>());

        Assert.That(c.SingleEdgeLateral(3.0), Is.EqualTo(-1.0).Within(0.02));
    }

    [Test]
    public void SlabaDruhaStrana_jednaHranaZeSilne()
    {
        // Track 23. 9.: vlevo medián 55 inlieru, vpravo 10 -> TooFewInliers a nic. Ted jedna hrana.
        var (l, r) = Edges(width: 4.0, lateral: 0, dirRad: 0);
        var weak = r.GetRange(0, 10);

        var c = new CorridorFinder().Find(l, weak);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.TooFewInliers));
        Assert.That(c.SingleSide, Is.EqualTo(CorridorSide.Left));
    }

    [Test]
    public void DveSilneStranyNesedi_zJedneNevznikaNic()
    {
        // Obe se prolozily a nesedi na sebe - jedna je spatne a nevime ktera.
        var (l, _) = Edges(width: 4.0, lateral: 0, dirRad: 0);
        var (_, r) = Edges(width: 4.0, lateral: 0, dirRad: 30 * Math.PI / 180);

        var c = new CorridorFinder().Find(l, r);

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.NotParallel));
        Assert.That(c.HasSingleEdge, Is.False);
    }

    [Test]
    public void OboustrannyKoridor_nemaJednuHranu()
    {
        var (l, r) = Edges(width: 4.0, lateral: 0, dirRad: 0);

        var c = new CorridorFinder().Find(l, r);

        Assert.That(c.Ok, Is.True);
        Assert.That(c.HasSingleEdge, Is.False);
    }

    [Test]
    public void Vypnuto_vraciPuvodniChovani()
    {
        var (l, _) = Edges(width: 4.0, lateral: 0, dirRad: 0);

        var c = new CorridorFinder(new CorridorConfig { SingleEdge = false }).Find(l, new List<Point2D>());

        Assert.That(c.Reason, Is.EqualTo(CorridorReason.OneSideOnly));
        Assert.That(c.HasSingleEdge, Is.False);
        Assert.That(c.HasLeftLine, Is.False, "vypnuto = ani se neprokládá, jako drive");
    }

    // ---------------------------------------------------------------- CorridorLocalizer

    private static AsyncFusionEngine EngineAt(double x, double y, double theta)
    {
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(x, y, 0.5, seed);
        engine.Enqueue(new PositionMeasurement(x, y, 0.5, 0.5, seed, "GPS"));
        engine.Enqueue(new HeadingMeasurement(theta, 0.05, seed, "Compass"));
        return engine;
    }

    /// <summary>Snimek s JEDNOU hranici (leva kamera nese levou, prava pravou).</summary>
    private static CameraFrame Frame(bool left, double width, double lateral, double dirRad,
                                     DateTime t, int count = 40)
    {
        double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
        double nx = -uy, ny = ux;
        double side = left ? width / 2 : -width / 2;
        var edges = new List<PathEdge>();
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * 0.15;
            double ox = -lateral * nx, oy = -lateral * ny;
            var p = new Point4D
            {
                X = (float)(ox + ux * s + nx * side),
                Y = (float)(oy + uy * s + ny * side),
                Z = 0, A = 1,
            };
            edges.Add(left ? new PathEdge { Y = i, Left = 100 + i, LeftPoint = p }
                           : new PathEdge { Y = i, Right = 200 + i, RightPoint = p });
        }
        return new CameraFrame { Name = left ? "Left" : "Right", TimeStamp = t, PathEdges = edges };
    }

    private static CorridorLocalizer Localizer(AsyncFusionEngine engine, double mapWidth,
                                               CorridorLocalizerConfig cfg = null)
    {
        var origin = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(origin, mapWidth);
        return new CorridorLocalizer(engine, net, origin, cfg ?? new CorridorLocalizerConfig());
    }

    [Test]
    public void JednaHrana_posleKurzIPricnouPolohu()
    {
        // Fuze si mysli, ze je robot na ose; leva hrana rika, ze je 0,6 m vlevo.
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, mapWidth: 4.0);

        var fix = loc.Process(Frame(left: true, width: 4.0, lateral: 0.6, dirRad: 0, T0));

        Assert.That(fix, Is.Not.Null);
        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(fix.SingleSide, Is.EqualTo(CorridorSide.Left));
        Assert.That(fix.SingleLateral, Is.EqualTo(0.6).Within(0.03));
        Assert.That(fix.LateralDisagreement, Is.EqualTo(0.6).Within(0.05));
        Assert.That(fix.EmittedLateral, Is.True);
        Assert.That(fix.EmittedHeading, Is.True);
        Assert.That(loc.EmittedCorrections, Is.EqualTo(2));
    }

    [Test]
    public void NenaucenaSirka_jeMapovaSNejistotouVSigme()
    {
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0,
                            new CorridorLocalizerConfig { SingleEdgeWidthStdM = 1.0 });

        var fix = loc.Process(Frame(left: false, width: 4.0, lateral: 0, dirRad: 0, T0));

        Assert.That(fix.MapWidthM, Is.EqualTo(4.0).Within(1e-6));
        Assert.That(fix.SingleWidthStdM, Is.EqualTo(1.0));
        // sqrt(0,03² + (1/2)²) - podlaha hrany a polovina nejistoty sirky.
        Assert.That(fix.SingleSigmaLateral, Is.EqualTo(Math.Sqrt(0.03 * 0.03 + 0.25)).Within(0.005));
    }

    [Test]
    public void NaucenaSirka_maPrednostPredMapovou()
    {
        // Oboustranne cykly naucí 3 m (mapa rika 4 m), pak vypadne prava kamera.
        var engine = EngineAt(0, 0, 0);
        var loc = Localizer(engine, mapWidth: 4.0);
        for (int i = 0; i < 12; i++)
        {
            var t = T0.AddMilliseconds(i * 100);
            loc.Process(Frame(left: true, width: 3.0, lateral: 0, dirRad: 0, t));
            loc.Process(Frame(left: false, width: 3.0, lateral: 0, dirRad: 0, t.AddMilliseconds(20)));
        }
        Assert.That(loc.Widths.TryGetWidth(loc.LastFix.Axis.WayId, out _), Is.True, "sirka se mela naucit");

        // Samotna leva kamera az za parovacim oknem -> NoPair, jedna hrana.
        var fix = loc.Process(Frame(left: true, width: 3.0, lateral: 0, dirRad: 0, T0.AddSeconds(3)));

        Assert.That(fix, Is.Not.Null);
        Assert.That(fix.SingleSide, Is.EqualTo(CorridorSide.Left));
        Assert.That(fix.MapWidthM, Is.EqualTo(3.0).Within(0.02), "naucena sirka, ne mapova");
        Assert.That(fix.SingleWidthStdM, Is.EqualTo(new CorridorLocalizerConfig().SingleEdgeLearnedWidthStdFloorM));
    }

    [Test]
    public void ChybaKurzu_zJedneHrany_seNajde()
    {
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0);

        var fix = loc.Process(Frame(left: true, width: 4.0, lateral: 0, dirRad: 5 * Math.PI / 180, T0));

        Assert.That(fix.Reason, Is.EqualTo(CorridorFixReason.Ok));
        Assert.That(fix.HeadingDisagreementRad * 180 / Math.PI, Is.EqualTo(5).Within(0.5));
        Assert.That(fix.EmittedHeading, Is.True);
    }

    [Test]
    public void LevaHranaVpravoOdRobotu_seNepouzije()
    {
        // "Leva" hranice 1 m VPRAVO od robotu: robot je mimo cestu, nebo se prolozilo neco jineho.
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0);

        var fix = loc.Process(Frame(left: true, width: 4.0, lateral: 3.0, dirRad: 0, T0));

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.NoPair));
        Assert.That(loc.EmittedCorrections, Is.Zero);
    }

    [Test]
    public void HranaDalNezSirkaCesty_jeMimoKoridor()
    {
        // Mapa 2 m, leva hrana 5 m vlevo: pricna poloha -4 m, daleko za polosirkou + rezervou
        // + nejistotou sirky (1 + 0,5 + 1). Cizi hrana (jina cesta za travnikem).
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 2.0,
                            new CorridorLocalizerConfig
                            {
                                Association = new EdgeAssociationConfig { Enabled = false },
                            });

        var fix = loc.Process(Frame(left: true, width: 10.0, lateral: 0, dirRad: 0, T0));

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.OutsideCorridor));
        Assert.That(loc.EmittedCorrections, Is.Zero);
    }

    [Test]
    public void Vypnuto_zadneMereniZJedneHrany()
    {
        var cfg = new CorridorLocalizerConfig();
        cfg.Corridor.SingleEdge = false;
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0, cfg);

        var fix = loc.Process(Frame(left: true, width: 4.0, lateral: 0.6, dirRad: 0, T0));

        Assert.That(fix, Is.Null);
        Assert.That(loc.LastFix.Reason, Is.EqualTo(CorridorFixReason.NoPair));
        Assert.That(loc.EmittedCorrections, Is.Zero);
    }

    [Test]
    public void OdhadSirky_seZJedneHranyNeuci()
    {
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0);

        for (int i = 0; i < 20; i++)
            loc.Process(Frame(left: true, width: 4.0, lateral: 0, dirRad: 0, T0.AddSeconds(i)));

        Assert.That(loc.Widths.Count, Is.Zero, "z jedne hrany sirka nevznika");
    }

    // ---------------------------------------------------------------- FreeRun a zprava

    [Test]
    public void CorridorSource_jednaHranaNeniOk()
    {
        // FreeRun bere Result.Ok a z nej sirku a pricnou polohu. Jedna hrana je ma nulove,
        // takze se k nemu dostat nesmi.
        var src = new CorridorSource(EngineAt(0, 0, 0));

        var r = src.Process(Frame(left: true, width: 4.0, lateral: 0, dirRad: 0, T0));

        Assert.That(r.Ok, Is.False);
        Assert.That(r.SingleEdgeUsable, Is.True);
    }

    [Test]
    public void Zprava_verze7_neseJednuHranu()
    {
        var loc = Localizer(EngineAt(0, 0, 0), mapWidth: 4.0);
        var fix = loc.Process(Frame(left: true, width: 4.0, lateral: 0.6, dirRad: 0, T0));

        var msg = RoundTrip(fix.ToLogMessage());

        Assert.That(msg.Verze, Is.EqualTo(7));
        Assert.That(msg.SingleSide, Is.EqualTo((byte)CorridorSide.Left));
        Assert.That(msg.SingleLateral, Is.EqualTo(fix.SingleLateral).Within(1e-12));
        Assert.That(msg.SingleSigmaLateral, Is.EqualTo(fix.SingleSigmaLateral).Within(1e-12));
        Assert.That(msg.SingleWidthStd, Is.EqualTo(fix.SingleWidthStdM).Within(1e-12));
        Assert.That(msg.EdgeOffset, Is.EqualTo(fix.Corridor.EdgeOffset).Within(1e-12));
        Assert.That(msg.FixReason, Is.EqualTo((byte)CorridorFixReason.Ok));
    }

    private static RoadCorridorMsg RoundTrip(RoadCorridorMsg msg)
    {
        var enc = Encoding.UTF8;
        var ms = new MemoryStream();
        var w = new MessageWriter(ms, enc);
        w.Write(msg);
        w.Flush();

        var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
        var reader = new MessageReader(new MemoryStream(ms.ToArray()), enc, map);
        return reader.Read() as RoadCorridorMsg;
    }
}
