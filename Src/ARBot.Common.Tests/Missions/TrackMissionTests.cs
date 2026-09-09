using System;
using System.Collections.Generic;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Missions;
using ARBot.Common.Regulators;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// Testy stavoveho automatu mise Track (viz doc/track-mission.md).
///
/// <para>Automat je cista logika, takze je testovatelny <b>cely</b> — bez HW, bez mapy a bez fuze.
/// Nejvic se hlidaji dve veci, obe bezpecnostni: <b>volba mise robota nerozjede</b> (jede se
/// teprve po stisku a uvolneni nouzoveho zastaveni) a <b>bod prilis daleko od site misi
/// PRERUSI</b>, misto aby se tise preskocil.</para>
/// </summary>
public class TrackMissionTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);

    private static readonly string[] TriBodyDokola =
    {
        "50.0337431,14.5257403",
        "50.0336719,14.5253072",
        "50.0338847,14.5261453",
        "repeat",
    };

    private static readonly string[] DvaBodyJednou =
    {
        "50.0337431,14.5257403",
        "50.0336719,14.5253072",
    };

    // ---------------- Falesne okoli ----------------

    private sealed class FakeGoals : IGlobalGoalSink
    {
        public List<LLA> Goals { get; } = new List<LLA>();
        public int Cancels { get; private set; }

        public void SetGoal(LLA target) => Goals.Add(target);
        public void Cancel() => Cancels++;
    }

    private sealed class StubRegulator : IRegulator
    {
        public RegulatorResult Control(Models.IModelState state) => new RegulatorResult();
        public bool IsFinished => false;
    }

    private sealed class FakeRegulatorHolder : IRegulatorHolder
    {
        public IRegulator Regulator { get; set; } = new StubRegulator();
    }

    private sealed class FakeRoutes : IRouteProbe
    {
        public bool Reachable = true;
        public double LengthM = 42.0;
        public double OffRoadM;

        /// <summary>Prichyceny cil; <c>null</c> = vrat cil beze zmeny (lezel uz na ceste).</summary>
        public LLA Snapped;

        public int Probes { get; private set; }

        public RouteProbeResult Probe(LLA target)
        {
            Probes++;
            return new RouteProbeResult(Reachable, LengthM, Snapped ?? target, OffRoadM);
        }
    }

    private sealed class Harness
    {
        public readonly FakeGoals Goals = new FakeGoals();
        public readonly FakeRegulatorHolder Control = new FakeRegulatorHolder();
        public readonly FakeRoutes Routes = new FakeRoutes();
        public readonly TrackMission Mission;

        public Harness(string[] soubor = null, TrackConfig config = null)
        {
            Mission = new TrackMission(Goals, TrackPlan.Parse(soubor ?? TriBodyDokola),
                                       Control, Routes, config ?? new TrackConfig());
        }

        /// <summary>Stav motoru: stoji / jede, se stopem nebo bez.</summary>
        public void FeedMotors(bool emergencyStop, bool standing, DateTime now)
            => Mission.OnMotors(new MotorStateBase(emergencyStop, 0, 0, 24, 0, 0,
                                                   standing ? 0 : 0.5, standing ? 0 : 0.5), now);

        public void Arrive(DateTime now)
            => Mission.OnGlobalNav(new GlobalNavMsg
               { Status = (int)GlobalNavStatus.Arrived, TimeStamp = now });

        public void NoRoute(DateTime now)
            => Mission.OnGlobalNav(new GlobalNavMsg
               { Status = (int)GlobalNavStatus.NoRoute, TimeStamp = now });

        /// <summary>Rozjede misi: start → stisk stopu → uvolneni stopu.</summary>
        public DateTime Rozjed(DateTime now)
        {
            Mission.StartMission(now);
            FeedMotors(emergencyStop: true, standing: true, now = now.AddSeconds(1));
            FeedMotors(emergencyStop: false, standing: true, now = now.AddSeconds(1));

            // Za jizdy nasadi regulator VRSTVA POD misi (LocalNavigator → ControlLoop); mise ho
            // jen zahazuje. ⚠️ Nasadit ho smi tedy jen kdyz se OPRAVDU jede — kdyby ho pomocnik
            // nastavoval vzdycky, „prerusena mise zastavila" by se testem nedalo overit: atrapa
            // by regulator vratila hned po tom, co ho Abort zahodil.
            if (Mission.Phase == TrackPhase.Driving) Control.Regulator = new StubRegulator();
            return now;
        }
    }

    // ---------------- Bezpecnost: mise se sama nerozjede ----------------

    [Test]
    public void PoStartu_CekaNaStiskStopu_ANezadaZadnyCil()
    {
        var h = new Harness();

        h.Mission.StartMission(T0);

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.AwaitingEStop));
        Assert.That(h.Mission.WaitingFor, Is.EqualTo(MissionWait.EmergencyStopPressed));
        Assert.That(h.Goals.Goals, Is.Empty, "volba mise sama robota rozjet NESMI");
    }

    [Test]
    public void PriCekaniNaStop_JeRegulatorZahozeny()
    {
        var h = new Harness();
        h.Mission.StartMission(T0);

        h.FeedMotors(emergencyStop: false, standing: true, T0.AddSeconds(1));

        Assert.That(h.Control.Regulator, Is.Null, "dokud se nejede, robot musi stat");
    }

    [Test]
    public void TeprveUvolneniStopu_ZadaPrvniCil()
    {
        var h = new Harness();
        var now = T0;

        h.Mission.StartMission(now);
        h.FeedMotors(emergencyStop: true, standing: true, now = now.AddSeconds(1));
        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.AwaitingEStopRelease));
        Assert.That(h.Goals.Goals, Is.Empty, "drzeny stop jeste neni pokyn k jizde");

        h.FeedMotors(emergencyStop: false, standing: true, now.AddSeconds(1));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Driving));
        Assert.That(h.Goals.Goals, Has.Count.EqualTo(1));
        Assert.That(h.Mission.PointIndex, Is.EqualTo(0));
    }

    // ---------------- Objezd mist ----------------

    [Test]
    public void PoDosazeni_SeJedeNaDalsiMisto()
    {
        var h = new Harness();
        var now = h.Rozjed(T0);

        h.Arrive(now = now.AddSeconds(10));

        Assert.That(h.Mission.PointIndex, Is.EqualTo(1));
        Assert.That(h.Mission.Reached, Is.EqualTo(1));
        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Driving));
        Assert.That(h.Goals.Goals, Has.Count.EqualTo(2));
        Assert.That(h.Goals.Goals[1].Latitude,
                    Is.EqualTo(h.Mission.Plan.Points[1].Latitude).Within(1e-12));
    }

    [Test]
    public void MeziBody_SeNEZASTAVUJE()
    {
        // Zadani mise: "az ho dosahne, tak pojede na dalsi misto". Cancel() by robota zbytecne
        // dobrzdil a zase rozjel - a hlavne by zahodil regulator, takze by se cukalo.
        var h = new Harness();
        var now = h.Rozjed(T0);

        h.Arrive(now.AddSeconds(10));

        Assert.That(h.Goals.Cancels, Is.Zero, "mezi body se cil jen PREPINA, neni co rusit");
        Assert.That(h.Control.Regulator, Is.Not.Null, "robot nesmi mezi body zastavit");
    }

    [Test]
    public void Repeat_ZacneZnovuOdPrvniho_APocitaKola()
    {
        var h = new Harness(TriBodyDokola);
        var now = h.Rozjed(T0);

        for (int i = 0; i < 3; i++) h.Arrive(now = now.AddSeconds(10));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Driving), "s repeat mise nekonci");
        Assert.That(h.Mission.PointIndex, Is.EqualTo(0), "zacina se znovu od prvniho bodu");
        Assert.That(h.Mission.Lap, Is.EqualTo(2));
        Assert.That(h.Mission.Reached, Is.EqualTo(3));
        Assert.That(h.Goals.Goals, Has.Count.EqualTo(4));
        Assert.That(h.Goals.Goals[3].Latitude,
                    Is.EqualTo(h.Mission.Plan.Points[0].Latitude).Within(1e-12),
                    "ctvrty cil je zase prvni bod");
    }

    [Test]
    public void BezRepeat_MisePoPoslednimBodeSkonci()
    {
        var h = new Harness(DvaBodyJednou);
        var now = h.Rozjed(T0);

        h.Arrive(now = now.AddSeconds(10));
        h.Arrive(now = now.AddSeconds(10));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Finished));
        Assert.That(h.Mission.Reached, Is.EqualTo(2));
        Assert.That(h.Mission.WaitingFor, Is.EqualTo(MissionWait.None));
        Assert.That(h.Goals.Cancels, Is.EqualTo(1), "na konci se cil ZRUSI, aby robot dobrzdil");
    }

    [Test]
    public void PoDojezdu_SeRegulatorZahodiTEPRVEAzRobotStoji()
    {
        // Dvoufazove zastaveni: nejdriv Cancel (robot dobrzdi rizene po existujici draze),
        // Regulator = null az kdyz skutecne stoji. Vzor z Robotouru.
        var h = new Harness(DvaBodyJednou);
        var now = h.Rozjed(T0);
        h.Arrive(now = now.AddSeconds(10));
        h.Arrive(now = now.AddSeconds(10));

        h.FeedMotors(emergencyStop: false, standing: false, now = now.AddSeconds(1));
        Assert.That(h.Control.Regulator, Is.Not.Null, "dokud jede, dobrzduje se rizene");

        h.FeedMotors(emergencyStop: false, standing: true, now.AddSeconds(1));
        Assert.That(h.Control.Regulator, Is.Null);
    }

    // ---------------- Prichyceni na sit ----------------

    [Test]
    public void CilSeJedeNaPRICHYCENYBod_NeNaSurovy()
    {
        // Kdyby se jelo na surovy bod, Navigator by dojezd meril proti nemu a Arrived by pri
        // vetsim odsazeni nenastalo NIKDY (past nalezena u Robotouru 27. 8. 2026).
        var h = new Harness();
        h.Routes.Snapped = new LLA(0.873, 0.254);
        h.Routes.OffRoadM = 4.0;

        h.Rozjed(T0);

        Assert.That(h.Goals.Goals, Has.Count.EqualTo(1));
        Assert.That(h.Goals.Goals[0].Latitude, Is.EqualTo(0.873).Within(1e-12));
        Assert.That(h.Mission.ActiveOffRoadM, Is.EqualTo(4.0).Within(1e-9));
    }

    [Test]
    public void BodPrilisDalekoOdSite_MisiPRERUSI_NEPRESKOCI()
    {
        // TOHLE JE TEN DULEZITY TEST. Tiche preskoceni by znamenalo, ze robot objel jinou trasu,
        // nez clovek zadal - a poznalo by se to jen tim, co v ni NENI.
        var h = new Harness(config: new TrackConfig { MaxPointOffRoadM = 10.0 });
        h.Routes.OffRoadM = 300.0;

        h.Rozjed(T0);

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Aborted));
        Assert.That(h.Mission.AbortReason, Does.Contain("300"));
        Assert.That(h.Mission.AbortReason, Does.Contain("od site cest"));
        Assert.That(h.Goals.Goals, Is.Empty, "nedosazitelny bod se nesmi zadat jako cil");
        Assert.That(h.Control.Regulator, Is.Null, "preruseni zastavuje tvrde");
    }

    [Test]
    public void DuvodPreruseni_RikaKTERYBod()
    {
        // Bez cisla bodu by clovek hledal vadny radek v celem souboru.
        var h = new Harness(config: new TrackConfig { MaxPointOffRoadM = 10.0 });
        var now = h.Rozjed(T0);
        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Driving), "prvni bod je v poradku");

        h.Routes.OffRoadM = 500.0;
        h.Arrive(now.AddSeconds(10));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Aborted));
        Assert.That(h.Mission.AbortReason, Does.Contain("misto 2/3"));
        Assert.That(h.Mission.AbortReason, Does.Contain("50.033671"), "i souradnice, ne jen cislo");
    }

    [Test]
    public void KazdyBod_SePrichycujeZNOVU()
    {
        // Trasa se pocita z AKTUALNI polohy robota, ktera uz je pri druhem bodu jina.
        var h = new Harness();
        var now = h.Rozjed(T0);
        int poPrvnim = h.Routes.Probes;

        h.Arrive(now.AddSeconds(10));

        Assert.That(h.Routes.Probes, Is.EqualTo(poPrvnim + 1));
    }

    [Test]
    public void BezZkouskySite_SeJedeNaSuroveSouradnice()
    {
        // V testech (a kdyby sit nebyla) je poctivejsi jet na surovy bod nez nejezdit vubec —
        // a je to VIDET, protoze odstup vyjde nula.
        var goals = new FakeGoals();
        var mise = new TrackMission(goals, TrackPlan.Parse(DvaBodyJednou), routes: null);
        var now = T0;

        mise.StartMission(now);
        mise.OnMotors(new MotorStateBase(true, 0, 0, 24, 0, 0, 0, 0), now = now.AddSeconds(1));
        mise.OnMotors(new MotorStateBase(false, 0, 0, 24, 0, 0, 0, 0), now.AddSeconds(1));

        Assert.That(goals.Goals, Has.Count.EqualTo(1));
        Assert.That(goals.Goals[0].Latitude,
                    Is.EqualTo(mise.Plan.Points[0].Latitude).Within(1e-12));
        Assert.That(mise.ActiveOffRoadM, Is.Zero);
    }

    // ---------------- Poruchy ----------------

    [Test]
    public void NoRoute_MisiPrerusi()
    {
        // Zotavovaci manevr neexistuje, takze zastaveni je jedina bezpecna odpoved.
        var h = new Harness();
        var now = h.Rozjed(T0);

        h.NoRoute(now.AddSeconds(5));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Aborted));
        Assert.That(h.Mission.AbortReason, Does.Contain("NoRoute"));
        Assert.That(h.Control.Regulator, Is.Null);
    }

    [Test]
    public void TimeoutJizdy_MisiPrerusi_NeTicheZaseknuti()
    {
        // Jizda k cili sama timeout nema, takze bez tohohle stropu by zaklineny robot stal
        // navzdy a mise by dal hlasila "jede".
        var h = new Harness(config: new TrackConfig { DrivingTimeoutSec = 30.0 });
        var now = h.Rozjed(T0);

        h.Mission.Tick(now.AddSeconds(31));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Aborted));
        Assert.That(h.Mission.Timeouts, Is.EqualTo(1));
        Assert.That(h.Mission.AbortReason, Does.Contain("timeout"));
    }

    [Test]
    public void CekaniNaCloveka_TimeoutNEMA()
    {
        // Ceka se, jak dlouho je potreba - clovek muze robota nosit.
        var h = new Harness(config: new TrackConfig { DrivingTimeoutSec = 5.0 });
        h.Mission.StartMission(T0);

        h.Mission.Tick(T0.AddSeconds(3600));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.AwaitingEStop));
    }

    [Test]
    public void Abort_JdeZKazdeFaze_APoNemSeJizNepokracuje()
    {
        var h = new Harness();
        var now = h.Rozjed(T0);

        h.Mission.Abort("rucni zastaveni");
        h.Arrive(now.AddSeconds(10));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Aborted));
        Assert.That(h.Mission.Reached, Is.Zero, "dojezd po preruseni uz mise nesmi vzit");
    }

    // ---------------- Hlaseni stavu a zprava ----------------

    [Test]
    public void PhaseText_ZaJizdy_ObsahujeCisloMistaIKolo()
    {
        var h = new Harness();
        var now = h.Rozjed(T0);
        h.Arrive(now.AddSeconds(10));

        Assert.That(h.Mission.PhaseText, Does.Contain("2/3"));
        Assert.That(h.Mission.PhaseText, Does.Contain("kolo 1"));
        Assert.That(h.Mission.MissionName, Is.EqualTo("track"));
    }

    [Test]
    public void Zprava_NeseSurovyIPrichycenyCil()
    {
        var h = new Harness();
        h.Routes.Snapped = new LLA(0.873, 0.254);
        h.Routes.OffRoadM = 7.5;
        h.Routes.LengthM = 120.0;

        h.Rozjed(T0);

        var m = h.Mission.LastMessage;
        Assert.That(m, Is.Not.Null);
        Assert.That(m.Phase, Is.EqualTo((int)TrackPhase.Driving));
        Assert.That(m.PointCount, Is.EqualTo(3));
        Assert.That(m.Repeat, Is.True);
        Assert.That(m.RawLatitude, Is.EqualTo(h.Mission.Plan.Points[0].Latitude).Within(1e-12));
        Assert.That(m.TargetLatitude, Is.EqualTo(0.873).Within(1e-12));
        Assert.That(m.OffRoadM, Is.EqualTo(7.5).Within(1e-9));
        Assert.That(m.RouteLengthM, Is.EqualTo(120.0).Within(1e-9));
    }

    [Test]
    public void Elapsed_JeNulaDokudMiseNezacala()
    {
        // Rozdil proti default(DateTime) by dal ~64 miliard sekund (past z Robotouru).
        var h = new Harness();

        Assert.That(h.Mission.Elapsed, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void DruhyStartMission_NicNedela()
    {
        var h = new Harness();
        var now = h.Rozjed(T0);

        h.Mission.StartMission(now.AddSeconds(5));

        Assert.That(h.Mission.Phase, Is.EqualTo(TrackPhase.Driving), "bezici mise se nerestartuje");
    }
}
