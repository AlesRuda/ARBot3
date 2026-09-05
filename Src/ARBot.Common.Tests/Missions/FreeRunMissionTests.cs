using System;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Missions;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// Testy mise FreeRun (viz doc/mission-freerun.md). <b>Jadro je geometrie mrkve</b> — pravá polovina
/// koridoru, znamenka a prevod do sveta pozou POŘÍZENÍ. Prave tady se to splete nejsnaz, takze
/// kontroly znamenka jsou tu jako samostatne testy.
/// </summary>
public class FreeRunMissionTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Koridor v ramci robotu; <paramref name="lateral"/> kladne = robot VLEVO od osy.</summary>
    private static RoadCorridor Corridor(double width, double lateral, double directionRad = 0)
        => new RoadCorridor
        {
            Width = width,
            Lateral = lateral,
            DirectionRad = directionRad,
            HasLeftLine = true,
            HasRightLine = true,
            Reason = CorridorReason.Ok,
        };

    private static RobotState Pose(double x, double y, double theta)
        => new RobotState { X = x, Y = y, Theta = theta, TimeStamp = T0 };

    // ---------------- Hlaseni stavu (IMissionStatus) ----------------

    private sealed class FakeGoal : ARBot.Common.Runtime.ILocalGoalSink
    {
        public void SetGoal(double worldX, double worldY, double corridorWidthM = 0) { }
        public void ClearGoal() { }
    }

    private static FreeRunMission NovaMise()
    {
        var engine = new AsyncFusionEngine(new EKFModel());
        return new FreeRunMission(engine, new FakeGoal(), new CorridorSource(engine));
    }

    /// <summary>
    /// FreeRun <b>neceka na nic zvenci</b> — nema stanoviste, kod ani operatora. Kdyby se sem
    /// vloudil umely duvod cekani, prestal by radek „ceka se na" na strance znamenat „bez zasahu
    /// cloveka se nic nestane" (a u Robotour prave to znamena).
    /// </summary>
    [Test]
    public void FreeRun_NecekaNaNic()
    {
        Assert.That(NovaMise().WaitingFor, Is.EqualTo(MissionWait.None));
    }

    [Test]
    public void NovaMise_HlasiJmenoCekaniNaSnimekANulovyCas()
    {
        var mise = NovaMise();

        Assert.Multiple(() =>
        {
            Assert.That(mise.MissionName, Is.EqualTo("freerun"));
            Assert.That(mise.PhaseText, Is.EqualTo("ceka na prvni snimek"));
            Assert.That(mise.Elapsed, Is.EqualTo(TimeSpan.Zero));
        });
    }

    /// <summary>
    /// Rozdil „v koridoru" x „drzi kurz" je jediny stav FreeRunu, ktery se zvenci pozna jako jina
    /// jizda — a je to prvni otazka pri diagnostice („vidi vubec cestu?").
    /// </summary>
    [Test]
    public void TextStavu_RozlisujeKoridorOdDrzeniKurzu()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FreeRunMission.PhaseTextFor(null), Is.EqualTo("ceka na prvni snimek"));
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = false }),
                        Is.EqualTo("ceka na pozu z fuze"));
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = true, FromCorridor = true }),
                        Is.EqualTo("jede v koridoru"));
            Assert.That(FreeRunMission.PhaseTextFor(new FreeRunResult { HasPose = true, FromCorridor = false }),
                        Is.EqualTo("bez koridoru, drzi kurz"));
        });
    }

    // ---------------- Geometrie mrkve: kontroly znamenka ----------------

    /// <summary>
    /// Robot na ose 2m cesty ma mrkev <b>0,5 m vpravo</b> (= Width/4). Znamenko je jadro cele mise:
    /// „pravá polovina" je v FLU konvenci ZAPORNE Y.
    /// </summary>
    [Test]
    public void MrkevNaOse_MiriDoPraveHalfky()
    {
        var cfg = new FreeRunConfig { LookaheadM = 3.0 };

        var (bodyX, bodyY) = FreeRunMission.CarrotBody(Corridor(width: 2.0, lateral: 0.0), cfg);

        Assert.Multiple(() =>
        {
            Assert.That(bodyX, Is.EqualTo(3.0).Within(1e-9), "lookahead vpred");
            Assert.That(bodyY, Is.EqualTo(-0.5).Within(1e-9), "+Y je VLEVO, takze vpravo je zaporne");
        });
    }

    /// <summary>Robot uz na pozadovane care -> mrkev primo po smeru cesty, zadne uhybani.</summary>
    [Test]
    public void MrkevNaPozadovaneCare_MiriPrimoVpred()
    {
        var cfg = new FreeRunConfig { LookaheadM = 3.0 };

        var (bodyX, bodyY) = FreeRunMission.CarrotBody(Corridor(width: 2.0, lateral: -0.5), cfg);

        Assert.Multiple(() =>
        {
            Assert.That(bodyX, Is.EqualTo(3.0).Within(1e-9));
            Assert.That(bodyY, Is.EqualTo(0.0).Within(1e-9), "uz je vpravo spravne - nema kam uhybat");
        });
    }

    /// <summary>Robot PRILIS vpravo -> mrkev ho tahne zpatky VLEVO.</summary>
    [Test]
    public void MrkevPriliVpravo_TahneZpatkyVlevo()
    {
        var cfg = new FreeRunConfig { LookaheadM = 3.0 };

        var (_, bodyY) = FreeRunMission.CarrotBody(Corridor(width: 2.0, lateral: -1.5), cfg);

        Assert.That(bodyY, Is.EqualTo(1.0).Within(1e-9), "kladne Y = vlevo");
    }

    /// <summary>
    /// Odsazeni je <b>proporcionalni sirce</b> (Width/4), ne pevny odstup od hrany. Na 4m ceste tedy
    /// 1,0 m od osy, na 1m ceste 0,25 m — degraduje rozumne na obou koncich. Pevnych „0,5 m od prave
    /// hrany" by na 1m ceste poslalo robota VLEVO od osy.
    /// </summary>
    [Test]
    public void Odsazeni_JeProporcionalniSirce()
    {
        var cfg = new FreeRunConfig { LookaheadM = 1.0 };

        Assert.Multiple(() =>
        {
            Assert.That(FreeRunMission.CarrotBody(Corridor(4.0, 0.0), cfg).bodyY,
                        Is.EqualTo(-1.0).Within(1e-9), "4 m cesta");
            Assert.That(FreeRunMission.CarrotBody(Corridor(1.0, 0.0), cfg).bodyY,
                        Is.EqualTo(-0.25).Within(1e-9), "1 m cesta");
        });
    }

    /// <summary>Kdyz cesta zatáci, mrkev jde po jejim SMERU, ne po ose robotu.</summary>
    [Test]
    public void ZatacejiciCesta_MrkevJdePoSmeruCesty()
    {
        var cfg = new FreeRunConfig { LookaheadM = 2.0 };
        double phi = Math.PI / 6;   // cesta 30 stupnu vlevo

        var (bodyX, bodyY) = FreeRunMission.CarrotBody(Corridor(2.0, 0.0, phi), cfg);

        // L*d + (-Lateral - W/4)*n, kde d = (cos, sin), n = (-sin, cos)
        double expX = 2.0 * Math.Cos(phi) + (-0.5) * (-Math.Sin(phi));
        double expY = 2.0 * Math.Sin(phi) + (-0.5) * Math.Cos(phi);

        Assert.Multiple(() =>
        {
            Assert.That(bodyX, Is.EqualTo(expX).Within(1e-9));
            Assert.That(bodyY, Is.EqualTo(expY).Within(1e-9));
        });
    }

    // ---------------- Prevod do sveta ----------------

    /// <summary>
    /// Mrkev se prevadi pozou <b>POŘÍZENÍ</b> snimku, ne „posledni znamou". Kdyz se robot mezitim
    /// pohnul, dava to jiny svetovy bod — a ten spravny je ten z pozy porizeni. Tataz konvence jako
    /// u RoadCorridorMsg.PoseX; parovat podle razitka neprezije seek (viz doc/record-replay.md).
    /// </summary>
    [Test]
    public void PrevodDoSveta_PouzijePozuPorizeni()
    {
        var cfg = new FreeRunConfig { LookaheadM = 3.0 };
        var corridor = Corridor(width: 2.0, lateral: 0.0);

        // Robot v (10, 5) otoceny na sever: telove +X je svetove +Y, telove +Y je svetove -X.
        var pose = Pose(10, 5, Math.PI / 2);
        var (wx, wy) = FreeRunMission.CarrotWorld(corridor, pose, cfg);

        Assert.Multiple(() =>
        {
            // body = (3, -0,5) -> svet: X = 10 - (-0,5) = 10,5 ; Y = 5 + 3 = 8
            Assert.That(wx, Is.EqualTo(10.5).Within(1e-9));
            Assert.That(wy, Is.EqualTo(8.0).Within(1e-9));
        });
    }

    /// <summary>
    /// <b>Bez koridoru robot drzi AKTUALNI kurz</b> — mrkev je lookahead primo vpred. Rozhodnuti
    /// autora: jednodussi a predvidatelnejsi nez podrzeni posledniho koridoru. Test hlida, ze se
    /// robot nikam neuhne.
    /// </summary>
    [Test]
    public void BezKoridoru_MrkevPrimoVpredOdAktualniPozy()
    {
        var cfg = new FreeRunConfig { LookaheadM = 4.0 };
        var pose = Pose(1, 2, 0.0);   // miri na vychod

        var (wx, wy) = FreeRunMission.CarrotStraightAhead(pose, cfg);

        Assert.Multiple(() =>
        {
            Assert.That(wx, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(wy, Is.EqualTo(2.0).Within(1e-9), "zadne pricne uhnuti");
        });
    }

    // ---------------- Zprava do zaznamu ----------------

    /// <summary>
    /// Bez zpravy je mise v zaznamu neviditelna a neda se zmerit, jak jela. Test drzi obousmernost
    /// i to, ze <c>FromCorridor</c> a poza prezijou — to jsou udaje, na kterych stoji cely rozbor
    /// (<c>ARBot.Analyze freerun</c>).
    /// </summary>
    [Test]
    public void Zprava_JeObousmerna()
    {
        var result = new FreeRunResult
        {
            TimeStamp = T0,
            GoalX = 12.5, GoalY = -3.25,
            FromCorridor = true,
            Width = 2.016, Lateral = -0.503, DirectionRad = 0.021,
            Reason = CorridorFixReason.Ok,
            PoseX = 1.5, PoseY = 2.5, PoseTheta = 0.75, HasPose = true,
        };

        var original = result.ToLogMessage();
        var buffer = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new ARBot.Common.Logs.FreeRunMsg();
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.TimeStamp, Is.EqualTo(T0));
            Assert.That(loaded.GoalX, Is.EqualTo(12.5).Within(1e-9));
            Assert.That(loaded.GoalY, Is.EqualTo(-3.25).Within(1e-9));
            Assert.That(loaded.FromCorridor, Is.True, "bez tohohle nejde poznat, jestli mise sledovala koridor");
            Assert.That(loaded.Width, Is.EqualTo(2.016).Within(1e-9));
            Assert.That(loaded.Lateral, Is.EqualTo(-0.503).Within(1e-9));
            Assert.That(loaded.DirectionRad, Is.EqualTo(0.021).Within(1e-9));
            Assert.That(loaded.Reason, Is.EqualTo((byte)CorridorFixReason.Ok));
            Assert.That(loaded.HasPose, Is.True);
            Assert.That(loaded.PoseX, Is.EqualTo(1.5).Within(1e-9));
            Assert.That(loaded.PoseTheta, Is.EqualTo(0.75).Within(1e-9));
        });
    }

    [Test]
    public void Zprava_JeVKataloguZprav()
    {
        // Bez registrace v katalogu index zpravu ukaze, ale Read vrati null - a tvari se to jako
        // chybejici stupen. Presne to se 25. 8. 2026 stalo u GPSState v ARBot.Analyze.
        Assert.That(ARBot.Common.Communication.MessageCatalog.CommonDefaults().Contains("FreeRunMsg"),
                    Is.True);
    }

    [Test]
    public void Vychozi_LookaheadJeKladny()
    {
        // Nulovy lookahead by polozil mrkev NA robota - planovac by nemel kam jet.
        Assert.That(new FreeRunConfig().LookaheadM, Is.GreaterThan(0.5));
        Assert.That(() => new FreeRunConfig { LookaheadM = 0 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Vychozi_OdsazeniJeCtvrtinaSirky()
    {
        // Ctvrtina sirky = stred prave poloviny. Polovina by robota polozila NA pravou hranici.
        Assert.That(new FreeRunConfig().RightOffsetFraction, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(() => new FreeRunConfig { RightOffsetFraction = 0.5 }.Validate(),
                    Throws.TypeOf<ArgumentException>(),
                    "polovina sirky uz je na hranici koridoru - to neni 'prava polovina'");
    }
}
