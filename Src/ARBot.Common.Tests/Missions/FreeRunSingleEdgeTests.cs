using System;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Missions;
using ARBot.Common.Tests.Localization;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// Mrkev FreeRunu podle <b>JEDINE</b> viditelne hrany cesty (od 26. 9. 2026, viz
/// <see cref="FreeRunConfig.UseSingleEdge"/> a doc/mission-freerun.md).
///
/// <para>Ve FreeRun <c>20260925-144658.rec</c> byla jedna hrana v 86 % snimku, obe jen v 2,7 %,
/// a mise pritom 97 % casu jela rovne podle kurzu. Jadro je znamenko: <c>EdgeOffset</c> je kladny,
/// kdyz je hrana VLEVO, a „prava polovina" je v FLU ZAPORNE Y.</para>
/// </summary>
public class FreeRunSingleEdgeTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Koridor jen z jedne hrany; <paramref name="edgeOffset"/> kladne = hrana vlevo.</summary>
    private static RoadCorridor Edge(CorridorSide side, double edgeOffset, double directionRad = 0)
        => new RoadCorridor
        {
            SingleSide = side,
            EdgeOffset = edgeOffset,
            DirectionRad = directionRad,
            Reason = CorridorReason.OneSideOnly,
        };

    private static readonly FreeRunConfig Cfg = new FreeRunConfig { LookaheadM = 3.0 };

    // ---------------- se sirkou z mapy: stred prave poloviny ----------------

    /// <summary>
    /// Prava hrana 2 m vpravo, cesta 4 m: osa je na robotu, stred prave poloviny 1 m vpravo
    /// (= Width/4 od osy, tedy Width/4 dovnitr od prave hrany).
    /// </summary>
    [Test]
    public void PravaHrana_SeSirkou_MiriDoStreduPravePoloviny()
    {
        var (x, y) = FreeRunMission.CarrotBodySingleEdge(Edge(CorridorSide.Right, -2.0), 4.0, Cfg);
        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(y, Is.EqualTo(-1.0).Within(1e-9), "cil je Width/4 = 1 m dovnitr od prave hrany");
        });
    }

    /// <summary>Robot uz stoji 1 m od prave hrany ve 4m ceste = presne na pozadovane care -> rovne.</summary>
    [Test]
    public void PravaHrana_NaPozadovaneCare_MiriRovne()
    {
        var (_, y) = FreeRunMission.CarrotBodySingleEdge(Edge(CorridorSide.Right, -1.0), 4.0, Cfg);
        Assert.That(y, Is.EqualTo(0.0).Within(1e-9));
    }

    /// <summary>
    /// Leva hrana 1 m vlevo, cesta 4 m: pozadovana cara je 3 m od leve hrany (Width·(1 − 1/4)),
    /// tedy 2 m vpravo od robotu.
    /// </summary>
    [Test]
    public void LevaHrana_SeSirkou_MiriTriCtvrtinySirkyOdHrany()
    {
        var (_, y) = FreeRunMission.CarrotBodySingleEdge(Edge(CorridorSide.Left, 1.0), 4.0, Cfg);
        Assert.That(y, Is.EqualTo(-2.0).Within(1e-9));
    }

    /// <summary>Se sirkou vede jedna hrana na TOTEZ jako oboustranny koridor se stejnou geometrii.</summary>
    [Test]
    public void SeSirkou_SouhlasiSOboustrannymKoridorem()
    {
        var edge = Edge(CorridorSide.Right, -1.3, 0.1);
        double w = 3.6;
        var both = new RoadCorridor
        {
            Width = w, Lateral = edge.SingleEdgeLateral(w), DirectionRad = 0.1,
            HasLeftLine = true, HasRightLine = true, Reason = CorridorReason.Ok,
        };
        var a = FreeRunMission.CarrotBodySingleEdge(edge, w, Cfg);
        var b = FreeRunMission.CarrotBody(both, Cfg);
        Assert.Multiple(() =>
        {
            Assert.That(a.bodyX, Is.EqualTo(b.bodyX).Within(1e-9));
            Assert.That(a.bodyY, Is.EqualTo(b.bodyY).Within(1e-9));
        });
    }

    // ---------------- bez sirky: smer hrany, zachovany odstup ----------------

    /// <summary>
    /// Bez sirky jde mrkev rovnobezne s hranou - pricne se neuhyba (prava hrana je tu dal nez
    /// MinRightEdgeClearanceM; blize viz PravaHrana_BezSirky_BlizkoKraje_OdsuneDoleva).
    /// </summary>
    [Test]
    public void BezSirky_DrziZmerenyOdstup()
    {
        foreach (var (side, off) in new[] { (CorridorSide.Right, -0.6), (CorridorSide.Right, -2.5), (CorridorSide.Left, 1.7) })
        {
            var (x, y) = FreeRunMission.CarrotBodySingleEdge(Edge(side, off), null, Cfg);
            Assert.That(x, Is.EqualTo(3.0).Within(1e-9), $"{side} {off}");
            Assert.That(y, Is.EqualTo(0.0).Within(1e-9), $"{side} {off}");
        }
    }

    /// <summary>Bez sirky jde mrkev PO SMERU hrany, ne po kurzu robotu - to je cely prinos proti jizde rovne.</summary>
    [Test]
    public void BezSirky_MrkevJdePoSmeruHrany()
    {
        double phi = 0.3;
        var (x, y) = FreeRunMission.CarrotBodySingleEdge(Edge(CorridorSide.Right, -1.0, phi), null, Cfg);
        Assert.Multiple(() =>
        {
            Assert.That(x, Is.EqualTo(3.0 * Math.Cos(phi)).Within(1e-9));
            Assert.That(y, Is.EqualTo(3.0 * Math.Sin(phi)).Within(1e-9));
        });
    }

    [Test]
    public void KoridorBezJedneHrany_JeChyba()
    {
        var both = new RoadCorridor { Width = 3, Lateral = 0, HasLeftLine = true, HasRightLine = true, Reason = CorridorReason.Ok };
        Assert.Throws<ArgumentException>(() => FreeRunMission.CarrotBodySingleEdge(both, 3.0, Cfg));
    }

    // ---------------- odstup od praveho kraje (pravidlo autora 26. 9. 2026) ----------------

    /// <summary>
    /// Prava polovina jen s odstupem aspon SafeDist + EdgeMarginM (0,55 m) od praveho kraje; jinak
    /// k ose, nejdal na stred. Siroka cesta = ctvrtina sirky jako dosud, uzka = stred.
    /// </summary>
    [Test]
    public void OdstupOdPravehoKraje_OmezujePravouPolovinu()
    {
        var cfg = new FreeRunConfig();   // vychozi 0,55 m
        Assert.Multiple(() =>
        {
            Assert.That(cfg.MinRightEdgeClearanceM, Is.EqualTo(0.55).Within(1e-9), "SafeDist 0,40 + EdgeMarginM 0,15");
            Assert.That(FreeRunMission.RightOffsetFromAxis(4.0, cfg), Is.EqualTo(1.0).Within(1e-9), "siroka: ctvrtina sirky");
            Assert.That(FreeRunMission.RightOffsetFromAxis(2.2, cfg), Is.EqualTo(0.55).Within(1e-9), "hranice: obe pravidla davaji totez");
            Assert.That(FreeRunMission.RightOffsetFromAxis(2.0, cfg), Is.EqualTo(0.45).Within(1e-9), "uzsi: drzi 0,55 m od kraje");
            Assert.That(FreeRunMission.RightOffsetFromAxis(1.0, cfg), Is.EqualTo(0.0).Within(1e-9), "uzka: stred cesty");
        });
    }

    /// <summary>
    /// Omezeni je SPOJITE v sirce - odhad sirky kolisa snimek od snimku a skok mezi „pravou polovinou"
    /// a „stredem" by mrkev hazel o ctvrtinu sirky.
    /// </summary>
    [Test]
    public void OdstupOdPravehoKraje_JeSpojityVSirce()
    {
        var cfg = new FreeRunConfig();
        double prev = FreeRunMission.RightOffsetFromAxis(0.01, cfg);
        for (double w = 0.02; w < 6.0; w += 0.01)
        {
            double v = FreeRunMission.RightOffsetFromAxis(w, cfg);
            Assert.That(Math.Abs(v - prev), Is.LessThanOrEqualTo(0.01 * 0.5 + 1e-9), $"w={w:F2}");
            prev = v;
        }
    }

    /// <summary>Oboustranny koridor 2 m, robot na ose: mrkev 0,45 m vpravo (drzi 0,55 m od kraje), ne 0,5.</summary>
    [Test]
    public void Koridor_UzkaCesta_DrziOdstupOdKraje()
    {
        var corridor = new RoadCorridor { Width = 2.0, Lateral = 0, HasLeftLine = true, HasRightLine = true, Reason = CorridorReason.Ok };
        var (_, y) = FreeRunMission.CarrotBody(corridor, new FreeRunConfig { LookaheadM = 3.0 });
        Assert.That(y, Is.EqualTo(-0.45).Within(1e-9));
    }

    /// <summary>Prava hrana bez sirky blize nez 0,55 m: cara se odsune doleva na 0,55 m.</summary>
    [Test]
    public void PravaHrana_BezSirky_BlizkoKraje_OdsuneDoleva()
    {
        var (_, y) = FreeRunMission.CarrotBodySingleEdge(Edge(CorridorSide.Right, -0.3), null, new FreeRunConfig { LookaheadM = 3.0 });
        Assert.That(y, Is.EqualTo(0.25).Within(1e-9), "0,55 - 0,30 = 0,25 m doleva");
    }

    /// <summary>Leva hrana bez sirky o pravem kraji nic nerika - drzi se zmereny odstup.</summary>
    [Test]
    public void LevaHrana_BezSirky_OdstupOdPravehoKrajeNeplati()
    {
        var (_, y) = FreeRunMission.CarrotBodySingleEdge(Edge(CorridorSide.Left, 0.3), null, new FreeRunConfig { LookaheadM = 3.0 });
        Assert.That(y, Is.EqualTo(0.0).Within(1e-9));
    }

    // ---------------- sirka z mapy ----------------

    /// <summary>Robot na primé ceste 4 m, hrana s ni rovnobezna -> sirka z mapy.</summary>
    [Test]
    public void SirkaZMapy_BlizkaRovnobeznaCesta()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, width: 4.0);
        var pose = new RobotState { X = 0, Y = 0.5, Theta = 0, TimeStamp = T0 };
        double? w = FreeRunMission.MapWidthAt(net, o, pose, Edge(CorridorSide.Right, -1.5, 0.05), new FreeRunConfig());
        Assert.That(w, Is.EqualTo(4.0).Within(1e-6));
    }

    /// <summary>
    /// Hrana kolmo na mapovou cestu (robot stoji u krizovatky a vidi hranu PRICNE ulice) - sirka
    /// by patrila jine ceste, takze se nebere a mise drzi odstup.
    /// </summary>
    [Test]
    public void SirkaZMapy_NerovnobeznaHrana_NeniSirka()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, width: 4.0);
        var pose = new RobotState { X = 0, Y = 0, Theta = 0, TimeStamp = T0 };
        Assert.That(FreeRunMission.MapWidthAt(net, o, pose, Edge(CorridorSide.Right, -1.0, Math.PI / 2 * 0.9),
                                              new FreeRunConfig()), Is.Null);
    }

    /// <summary>Mapova cesta daleko od pozy - nejspis to neni ta, po ktere robot jede.</summary>
    [Test]
    public void SirkaZMapy_VzdalenaCesta_NeniSirka()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, width: 4.0);
        var pose = new RobotState { X = 0, Y = 20, Theta = 0, TimeStamp = T0 };
        Assert.That(FreeRunMission.MapWidthAt(net, o, pose, Edge(CorridorSide.Right, -1.0), new FreeRunConfig()), Is.Null);
    }

    [Test]
    public void SirkaZMapy_BezMapy_NeniSirka()
    {
        var pose = new RobotState { X = 0, Y = 0, Theta = 0, TimeStamp = T0 };
        Assert.That(FreeRunMission.MapWidthAt(null, CorrelationTestScenes.Origin(), pose,
                                              Edge(CorridorSide.Right, -1.0), new FreeRunConfig()), Is.Null);
    }

    // ---------------- naucena sirka (od 8. 10. 2026) ----------------
    //
    // Do 8. 10. 2026 brala mise u jedine hrany VZDY mapovou sirku - a kde OSM nema tag width,
    // je to roadwidth= (3 m), ackoli lokalizace tutez sirku znala a RoadWidthMapUpdater ji
    // propisoval do mapy. Ted mise cte TENTYZ odhad (CorridorLocalizer.Widths) a sama do nej nepise.

    private sealed class FakeGoal : ARBot.Common.Runtime.ILocalGoalSink
    {
        public void SetGoal(double worldX, double worldY, double corridorWidthM = 0,
                            double goalRadiusM = double.NaN) { }
        public void ClearGoal() { }
    }

    private static AsyncFusionEngine EngineAt(double x, double y, double theta)
    {
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(x, y, 0.5, seed);
        engine.Enqueue(new PositionMeasurement(x, y, 0.5, 0.5, seed, "GPS"));
        engine.Enqueue(new HeadingMeasurement(theta, 0.05, seed, "Compass"));
        return engine;
    }

    /// <summary>Snimek s JEDNOU hranici (leva kamera nese levou, prava pravou) - jako v CorridorSingleEdgeTests.</summary>
    private static ARBot.Common.Devices.CameraFrame Frame(bool left, double width, double lateral, double dirRad,
                                                          DateTime t, int count = 40)
    {
        double ux = Math.Cos(dirRad), uy = Math.Sin(dirRad);
        double nx = -uy, ny = ux;
        double side = left ? width / 2 : -width / 2;
        var edges = new System.Collections.Generic.List<ARBot.Common.Common.PathEdge>();
        for (int i = 0; i < count; i++)
        {
            double s = 1.0 + i * 0.15;
            double ox = -lateral * nx, oy = -lateral * ny;
            var p = new ARBot.Common.Common.Point4D
            {
                X = (float)(ox + ux * s + nx * side),
                Y = (float)(oy + uy * s + ny * side),
                Z = 0, A = 1,
            };
            edges.Add(left ? new ARBot.Common.Common.PathEdge { Y = i, Left = 100 + i, LeftPoint = p }
                           : new ARBot.Common.Common.PathEdge { Y = i, Right = 200 + i, RightPoint = p });
        }
        return new ARBot.Common.Devices.CameraFrame { Name = left ? "Left" : "Right", TimeStamp = t, PathEdges = edges };
    }

    /// <summary>
    /// Lokalizace a mise nad tymz enginem a toutez rovnou vychodni cestou sirky
    /// <paramref name="mapWidth"/> (mapa bez tagu = 3 m) — jako v runtime, kde oba stupne dostavaji
    /// tytez snimky. <paramref name="sdilet"/> = mise dostane odhad lokalizace (runtime s
    /// <c>corridor=true</c>); <c>false</c> = lokalizace nebezi (<c>corridor=false</c>).
    /// </summary>
    private static (CorridorLocalizer loc, FreeRunMission mise) Sestava(double mapWidth, bool sdilet = true)
    {
        var engine = EngineAt(0, 0, 0);
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, width: mapWidth);
        var loc = new CorridorLocalizer(engine, net, o, new CorridorLocalizerConfig());
        var cfg = new FreeRunConfig { LookaheadM = 3.0 };
        var mise = new FreeRunMission(engine, new FakeGoal(), new CorridorSource(engine), cfg,
                                      mapRoad: (p, c) => FreeRunMission.MapRoadAt(net, o, p, c, cfg),
                                      learnedWidths: sdilet ? loc.Widths : null);
        return (loc, mise);
    }

    /// <summary>Snimek do obou stupnu (v runtime je oba dostavaji z ridici smycky).</summary>
    private static FreeRunResult Oboum(CorridorLocalizer loc, FreeRunMission mise,
                                       ARBot.Common.Devices.CameraFrame f)
    {
        loc?.Process(f);
        return mise?.Process(f);
    }

    /// <summary>Oboustranne snimky skutecne sirky <paramref name="width"/> do obou stupnu.</summary>
    private static void Oboustranne(CorridorLocalizer loc, FreeRunMission mise, double width, int cykly = 12,
                                    double dirRad = 0)
    {
        for (int i = 0; i < cykly; i++)
        {
            var t = T0.AddMilliseconds(i * 100);
            Oboum(loc, mise, Frame(left: true, width: width, lateral: 0, dirRad: dirRad, t));
            Oboum(loc, mise, Frame(left: false, width: width, lateral: 0, dirRad: dirRad, t.AddMilliseconds(20)));
        }
    }

    /// <summary>
    /// Cesta je skutecne 5 m, mapa rika 3 m (Modrany bez tagu width). Lokalizace se z oboustrannych
    /// cyklu nauci 5 m, pak vypadne prava kamera: mrkev podle leve hrany musi pocitat s TOUZ
    /// naucenou sirkou, ne s mapovymi 3 m.
    /// </summary>
    [Test]
    public void JednaHrana_BereSirkuNaucenouLokalizaci()
    {
        var (loc, mise) = Sestava(mapWidth: 3.0);
        Oboustranne(loc, mise, width: 5.0);
        Assert.That(mise.CarrotsFromCorridor, Is.GreaterThan(0), "oboustranny koridor mel vzniknout");
        Assert.That(loc.Widths.TryGetWidth(loc.LastFix.Axis.WayId, out double naucena), Is.True,
                    "lokalizace se mela naucit");

        // Samotna leva kamera az za parovacim oknem -> jedna hrana.
        var r = Oboum(loc, mise, Frame(left: true, width: 5.0, lateral: 0, dirRad: 0, T0.AddSeconds(3)));

        Assert.That(r, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(r.SingleSide, Is.EqualTo(CorridorSide.Left));
            Assert.That(r.WidthFromMap, Is.True);
            Assert.That(r.WidthLearned, Is.True);
            Assert.That(r.Width, Is.EqualTo(naucena).Within(1e-12), "presne sirka lokalizace");
            Assert.That(r.Width, Is.EqualTo(5.0).Within(0.03), "naucena sirka, ne mapova 3 m");
            Assert.That(r.Lateral, Is.EqualTo(0.0).Within(0.03), "robot na ose 5m cesty");
            Assert.That(FreeRunMission.PhaseTextFor(r), Is.EqualTo("jede podle leve hrany, sirka naucena"));
        });
    }

    /// <summary>
    /// Mise do odhadu <b>NEPISE</b> — pise jen lokalizace. Kdyby psala i mise (vidi tytez snimky),
    /// bylo by kazde merenie v okne dvakrat a rozptyl by vychazel mensi, nez je.
    /// </summary>
    [Test]
    public void Mise_DoOdhaduNepise()
    {
        var (loc, mise) = Sestava(mapWidth: 3.0);
        Oboustranne(null, mise, width: 5.0);   // jen mise, lokalizace nic nedostala

        Assert.That(mise.CarrotsFromCorridor, Is.GreaterThan(0), "koridor vznikl - jinak by test prosel naprazdno");
        Assert.That(loc.Widths.Count, Is.Zero);
    }

    /// <summary>
    /// Lokalizace a mise nad tymiz snimky: v okne je stejne merení jako u lokalizace, ktera bezi
    /// sama — mise nepridala nic.
    /// </summary>
    [Test]
    public void SdilenyOdhad_StejneMereniJakoBezMise()
    {
        var (loc, mise) = Sestava(mapWidth: 3.0);
        Oboustranne(loc, mise, width: 5.0, cykly: 5);
        var (samotna, _) = Sestava(mapWidth: 3.0);
        Oboustranne(samotna, null, width: 5.0, cykly: 5);

        long way = loc.LastFix.Axis.WayId;
        Assert.That(samotna.Widths.Samples(way), Is.GreaterThan(0));
        Assert.That(loc.Widths.Samples(way), Is.EqualTo(samotna.Widths.Samples(way)));
    }

    /// <summary>
    /// Bez lokalizace (<c>corridor=false</c>) naucena sirka neni nikde — ani v mape — a mise bere
    /// mapovou (chovani do 8. 10. 2026), i kdyz sama oboustranny koridor videla.
    /// </summary>
    [Test]
    public void BezLokalizace_SirkaZMapy()
    {
        var (_, mise) = Sestava(mapWidth: 3.0, sdilet: false);
        Oboustranne(null, mise, width: 5.0);

        var r = mise.Process(Frame(left: true, width: 5.0, lateral: 0, dirRad: 0, T0.AddSeconds(3)));

        Assert.That(r, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(r.SingleSide, Is.EqualTo(CorridorSide.Left));
            Assert.That(r.WidthFromMap, Is.True);
            Assert.That(r.WidthLearned, Is.False);
            Assert.That(r.Width, Is.EqualTo(3.0).Within(1e-6));
        });
    }

    /// <summary>
    /// Malo oboustrannych merení (pod <c>MinSamples</c>) = odhad bez kvality, takze se mu neveri
    /// a jede se dal podle mapy. „Nevim" je spravna odpoved, ne prvni zmerene cislo.
    /// </summary>
    [Test]
    public void JednaHrana_NedostatekMereni_SirkaZMapy()
    {
        var (loc, mise) = Sestava(mapWidth: 3.0);
        Oboustranne(loc, mise, width: 5.0, cykly: 3);

        var r = Oboum(loc, mise, Frame(left: true, width: 5.0, lateral: 0, dirRad: 0, T0.AddSeconds(3)));

        Assert.That(r.WidthLearned, Is.False);
        Assert.That(r.Width, Is.EqualTo(3.0).Within(1e-6));
    }

    /// <summary>
    /// Naucena sirka patri MAPOVE ceste, takze hrana kolma na ni (pricna ulice u krizovatky) ji
    /// nedostane — mapova cesta neprojde branou smeru a mise drzi odstup.
    /// </summary>
    [Test]
    public void KolmaHrana_NaucenouSirkuNedostane()
    {
        var (loc, mise) = Sestava(mapWidth: 3.0);
        Oboustranne(loc, mise, width: 5.0);
        Assert.That(loc.Widths.Count, Is.GreaterThan(0));

        var r = Oboum(loc, mise, Frame(left: true, width: 5.0, lateral: 0, dirRad: Math.PI / 2 * 0.9,
                                        T0.AddSeconds(3)));

        Assert.That(r.SingleSide, Is.EqualTo(CorridorSide.Left));
        Assert.That(r.WidthFromMap, Is.False);
        Assert.That(r.WidthLearned, Is.False);
    }

    // ---------------- stav a zprava ----------------

    [Test]
    public void TextStavu_RozlisujeJednuHranu()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = true, SingleSide = CorridorSide.Right, WidthFromMap = true }),
                        Is.EqualTo("jede podle prave hrany, sirka z mapy"));
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = true, SingleSide = CorridorSide.Right, WidthFromMap = true, WidthLearned = true }),
                        Is.EqualTo("jede podle prave hrany, sirka naucena"));
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = true, SingleSide = CorridorSide.Left }),
                        Is.EqualTo("jede podle leve hrany, drzi odstup"));
        });
    }

    /// <summary>Zprava verze 2 nese jednu hranu tam i zpet.</summary>
    [Test]
    public void Zprava_NeseJednuHranu()
    {
        var r = new FreeRunResult
        {
            TimeStamp = T0, HasPose = true, SingleSide = CorridorSide.Right, EdgeOffset = -1.25,
            WidthFromMap = true, Width = 3.5, Lateral = -0.5,
        };
        var loaded = RoundTrip(r.ToLogMessage());
        Assert.Multiple(() =>
        {
            Assert.That(loaded.SingleSide, Is.EqualTo((byte)CorridorSide.Right));
            Assert.That(loaded.EdgeOffset, Is.EqualTo(-1.25).Within(1e-12));
            Assert.That(loaded.WidthFromMap, Is.True);
            Assert.That(loaded.Width, Is.EqualTo(3.5).Within(1e-12));
        });
    }

    /// <summary>Zprava verze 3 nese naucenou sirku tam i zpet.</summary>
    [Test]
    public void Zprava_NeseNaucenouSirku()
    {
        var r = new FreeRunResult
        {
            TimeStamp = T0, HasPose = true, SingleSide = CorridorSide.Left, WidthFromMap = true, WidthLearned = true, Width = 5,
        };
        Assert.That(RoundTrip(r.ToLogMessage()).WidthLearned, Is.True);
    }

    /// <summary>Zaznamy verze 2 (naucenou sirku nezna) se ctou dal a nectou za konec zpravy.</summary>
    [Test]
    public void Zprava_Verze2_SeCteBezNauceneSirky()
    {
        var v2 = new ARBot.Common.Logs.FreeRunMsg { TimeStamp = T0, SingleSide = 1, WidthFromMap = true, WidthLearned = true };
        v2.Verze = 2;
        var buffer = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v2.ToData(bw);
        buffer.Position = 0;
        var loaded = new ARBot.Common.Logs.FreeRunMsg { Verze = 2 };
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.WidthFromMap, Is.True);
            Assert.That(loaded.WidthLearned, Is.False, "verze 2 ji nenese");
            Assert.That(buffer.Position, Is.EqualTo(buffer.Length));
        });
    }

    /// <summary>Zaznamy z doby pred jednou hranou (verze 1) se musi dat precist dal.</summary>
    [Test]
    public void Zprava_Verze1_SeCteBezJedneHrany()
    {
        var v1 = new ARBot.Common.Logs.FreeRunMsg { TimeStamp = T0, GoalX = 1, GoalY = 2, FromCorridor = true, HasPose = true };
        v1.Verze = 1;
        var buffer = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            v1.ToData(bw);
        buffer.Position = 0;
        var loaded = new ARBot.Common.Logs.FreeRunMsg { Verze = 1 };
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.GoalY, Is.EqualTo(2).Within(1e-12));
            Assert.That(loaded.FromCorridor, Is.True);
            Assert.That(loaded.SingleSide, Is.EqualTo((byte)0));
            Assert.That(buffer.Position, Is.EqualTo(buffer.Length), "verze 1 nesmi cist za konec zpravy");
        });
    }

    private static ARBot.Common.Logs.FreeRunMsg RoundTrip(ARBot.Common.Logs.FreeRunMsg m)
    {
        var buffer = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            m.ToData(bw);
        buffer.Position = 0;
        var loaded = new ARBot.Common.Logs.FreeRunMsg();
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);
        return loaded;
    }
}
