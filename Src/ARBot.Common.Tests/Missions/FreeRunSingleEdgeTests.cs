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

    // ---------------- stav a zprava ----------------

    [Test]
    public void TextStavu_RozlisujeJednuHranu()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = true, SingleSide = CorridorSide.Right, WidthFromMap = true }),
                        Is.EqualTo("jede podle prave hrany, sirka z mapy"));
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
