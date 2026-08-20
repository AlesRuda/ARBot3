using System;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy celeho cyklu korelace (viz doc/map-correlation-localization.md).
/// Testuje se PRIMO Process(), ne pres vlakno - vlakno je zodpovednost MessageProcessoru
/// a testovat ho tady by delalo testy nedeterministickymi.
/// </summary>
public class MapCorrelatorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Fuze s pouzitelnou pozou v case T0 (poloha 0,0, kurz 0).
    ///
    /// <para>Seed je schvalne o 200 ms PRED T0: <c>AsyncFusionEngine.Enqueue</c> zahazuje merenia
    /// s casem <c>&lt;= tBase</c>, a snapshot gridu ma cas presne T0. Kdyby se seedovalo taky v T0,
    /// korelatorem poslana merenia by se do fuze nikdy nedostala a test "posle korekci do fuze" by
    /// selhaval z duvodu, ktery s korelatorem nema nic spolecneho. 200 ms je dost na to, aby T0
    /// zustalo v okne historie. Zjisteno integracnim testem 2026-08-19.</para>
    /// </summary>
    private static AsyncFusionEngine EngineAtOrigin()
    {
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(0, 0, 0.5, seed);
        engine.Enqueue(new PositionMeasurement(0, 0, 0.5, 0.5, seed, "GPS"));
        engine.Enqueue(new HeadingMeasurement(0, 0.05, seed, "Compass"));
        return engine;
    }

    private static RoadScene StraightRoad()
    {
        var origin = CorrelationTestScenes.Origin();
        return new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
    }

    [Test]
    public void Process_NajdePricnouChybu()
    {
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0.0, 0.7, 0.0);
        var result = correlator.Process(msg);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Dy, Is.EqualTo(0.7).Within(0.15));
        Assert.That(correlator.ProcessedCycles, Is.EqualTo(1));
    }

    [Test]
    public void Process_BezPozy_ZahodiSnimek()
    {
        // Snimek s casem mimo okno historie fuze -> GetStateAt vrati null, snimek se zahodi
        // (korelovat proti spatne poze je horsi). Prazdna, nikdy neinicializovana fuze vraci
        // pocatecni stav modelu i pro libovolny cas (aby se pri startu emitoval RobotStateMsg),
        // takze test musi fuzi inicializovat a dotazovat se na cas PRED jejim oknem historie.
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(0, 0, 0.5, T0);
        var scene = StraightRoad();
        var correlator = new MapCorrelator(engine, scene, CorrelationTestScenes.TestConfig());

        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        msg.TimeStamp = T0.AddSeconds(-10);
        var result = correlator.Process(msg);

        Assert.That(result, Is.Null);
        Assert.That(correlator.DroppedNoPose, Is.EqualTo(1));
        Assert.That(correlator.ProcessedCycles, Is.EqualTo(0));
    }

    [Test]
    public void Process_DrivNezMinPeriod_Preskoci()
    {
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.MinPeriod = TimeSpan.FromMilliseconds(400);
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var first = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        var second = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        second.TimeStamp = first.TimeStamp.AddMilliseconds(100);

        Assert.That(correlator.Process(first), Is.Not.Null);
        Assert.That(correlator.Process(second), Is.Null);
        Assert.That(correlator.ThrottledCycles, Is.EqualTo(1));
    }

    [Test]
    public void Process_Vypnuty_NicNeposleDoFuze()
    {
        var engine = EngineAtOrigin();
        int before = engine.Diagnostics().Count;

        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.SendCorrections = false;
        var correlator = new MapCorrelator(engine, scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.7, 0));

        Assert.That(result, Is.Not.Null, "Vypnuty korelator ma dal POCITAT a hlasit.");
        Assert.That(correlator.EmittedCorrections, Is.EqualTo(0));
        Assert.That(engine.Diagnostics().Count, Is.EqualTo(before), "Do fuze nesmelo nic prijit.");
    }

    [Test]
    public void Process_Zapnuty_PosleKorekciDoFuze()
    {
        var engine = EngineAtOrigin();
        int before = engine.Diagnostics().Count;

        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.SendCorrections = true;
        var correlator = new MapCorrelator(engine, scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.7, 0));

        Assert.That(result.Emitted, Is.True);
        Assert.That(correlator.EmittedCorrections, Is.GreaterThan(0));
        Assert.That(engine.Diagnostics().Count, Is.GreaterThan(before));
    }

    [Test]
    public void Korekce_PosunePozuKPRAVDE()
    {
        // TOHLE je test, ktery hlida SMER korekce. Ostatni testy tvrdi jen, ze se do fuze neco
        // poslalo (naroste diagnostika), takze by prosly i s obracenym znamenkem - a robot by pak
        // zatacel presne na spatnou stranu. Tvrdi se SMER, ne velikost: tu urcuje Kalmanovo
        // zesileni a je zavisla na implementaci fuze.
        var engine = EngineAtOrigin();
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.SendCorrections = true;
        var correlator = new MapCorrelator(engine, scene, cfg);

        // Grid je naplneny tak, jako by se poza mylila o +0,7 m na sever => skutecna poloha je
        // (0, 0,7), zatimco fuze si mysli (0, 0).
        const double trueY = 0.7;
        double before = engine.GetStateAt(T0).Y;

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0.0, trueY, 0.0));
        double after = engine.GetStateAt(T0).Y;

        TestContext.Out.WriteLine($"Dy={result.Dy:F3} osa={result.TightAxisAngle * 180 / Math.PI:F1} "
                                  + $"sigT={result.SigmaTight:F3} Y: {before:F4} -> {after:F4} (pravda {trueY})");

        Assert.That(result.Reason, Is.EqualTo(MapCorrelationReason.Ok));
        Assert.That(result.EmitTightAxis, Is.True, "Pricna osa je urcena, ma se poslat.");
        Assert.That(after, Is.GreaterThan(before),
                    "Poza se musi posunout SMEREM k pravde (na sever), ne od ni.");
        Assert.That(Math.Abs(after - trueY), Is.LessThan(Math.Abs(before - trueY)),
                    "Po korekci musi byt poza BLIZ skutecne poloze.");
    }

    [Test]
    public void SikmaCesta_PodelnaOsaSeNeposle()
    {
        // Na sikme PRIME ceste vychazi SigmaLoose omylem konecna (zaparkovana vada "falesna podelna
        // jistota" - doc/map-correlation-localization.md), takze pres strop sigma podelna korekce
        // PROJDE. Zastavi ji az konkurent podel volne osy: podel prime cesty je stejne dobre reseni
        // vsude, takze konkurent skoruje jako maximum. Ta souhra dvou nezavisle postavenych kusu
        // nikde otestovana nebyla - bez tohoto testu by ji zmena v prohledavani konkurenta nebo ve
        // stropech mohla tise zrusit a do fuze by tekla falesna podelna korekce.
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.DiagonalRoad(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.SendCorrections = true;
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0.0, 0.5, 0.0));

        TestContext.Out.WriteLine($"duvod={result.Reason} sigT={result.SigmaTight:F4} "
                                  + $"sigL={result.SigmaLoose:F4} konk={result.SecondBestScore:F4} "
                                  + $"konkL={result.SecondBestScoreLoose:F4} skore={result.Score:F4} "
                                  + $"osa={result.TightAxisAngle * 180 / Math.PI:F1}");

        Assert.That(result.Reason, Is.EqualTo(MapCorrelationReason.Ok));
        Assert.That(result.EmitTightAxis, Is.True, "Pricna korekce je hlavni vystup a ma se poslat.");
        Assert.That(result.EmitLooseAxis, Is.False,
                    "Podelna korekce na PRIME (byt sikme) ceste se poslat NESMI - zadnou podelnou "
                    + "informaci tam grid nenese.");
    }

    [Test]
    public void Process_CasSkocilDozadu_NeuvazneVeThrottlingu()
    {
        // Seek dozadu v prehravani zaznamu: odstup je zaporny, tedy vzdy < MinPeriod. Bez resetu
        // by korelator omezoval frekvenci navzdy a nad zaznamem uz nikdy nic nespocital - a faze 4
        // se ladi prave nad zaznamy.
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var first = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        // Skok dozadu je jen o 100 ms - dal by snapshot vypadl z okna historie fuze a snimek by se
        // zahodil kvuli chybejici poze, tedy z jineho duvodu, nez tenhle test zkouma.
        var back = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        back.TimeStamp = first.TimeStamp.AddMilliseconds(-100);

        Assert.That(correlator.Process(first), Is.Not.Null);
        Assert.That(correlator.Process(back), Is.Not.Null, "Snimek po skoku dozadu se ma spocitat.");
        Assert.That(correlator.ThrottledCycles, Is.EqualTo(0));
        Assert.That(correlator.DroppedNoPose, Is.EqualTo(0));
    }

    [Test]
    public void Process_MimoMapovanouCestu_Mlci()
    {
        // Grid tvrdi "vsude cesta", mapa tvrdi "nikde" -> zadna pouzitelna shoda.
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.SendCorrections = true;
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        // Robot 200 m severne od cesty: rastr je tam cely "neni cesta", grid rikame "cesta".
        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 200, 0, 0, 0);
        for (int i = 0; i < msg.Road.Length; i++)
            msg.Road[i] = (sbyte)Math.Round(-1.0f / msg.Scale);

        var result = correlator.Process(msg);

        Assert.That(result.Emitted, Is.False);
        Assert.That(result.Reason, Is.Not.EqualTo(MapCorrelationReason.Ok));
        Assert.That(correlator.EmittedCorrections, Is.EqualTo(0));
    }

    [Test]
    public void Process_SoubezneCesty_NeposleNic()
    {
        // Vzor se skoro opakuje s periodou 2 m, takze shoda je nejednoznacna a korigovat by
        // znamenalo riskovat preskok na vedlejsi cestu.
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.ParallelRoads(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.SendCorrections = true;
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0));

        Assert.That(result.Reason, Is.EqualTo(MapCorrelationReason.Ambiguous));
        Assert.That(correlator.EmittedCorrections, Is.EqualTo(0));
    }

    [Test]
    public void Process_VyplniDobuVypoctu()
    {
        var scene = StraightRoad();
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, CorrelationTestScenes.TestConfig());

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0));

        Assert.That(result.ProcessingTime, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(correlator.LastResult, Is.SameAs(result));
    }

    [Test]
    public void Konstruktor_NeplatnaKonfigurace_Vyhodi()
    {
        var cfg = new MapCorrelatorConfig { MapRasterMarginM = 0.1 };   // < SearchRangeM

        Assert.That(() => new MapCorrelator(EngineAtOrigin(), StraightRoad(), cfg),
                    Throws.TypeOf<ArgumentException>());
    }
}
