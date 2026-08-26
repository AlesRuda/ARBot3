using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Missions;
using ARBot.Common.Regulators;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// Testy stavoveho automatu mise Robotour (viz doc/robotour-mission.md).
///
/// <para>Automat je cista logika, takze je testovatelny <b>cely</b> — bez HW, bez kamer a bez fuze.
/// Jadro, ktere se hlida nejvic, je <b>servisni okno</b>: bez zmacknuteho nouzoveho zastaveni se
/// nesmi zapnout scanner, bez potvrzeni obsluhou se nesmi prijmout cil a bez uvolneni stopu se
/// nesmi rozjet. Kazda z tech tri podminek je fyzicka pojistka pro cloveka, ktery stoji u robotu
/// s krabici v ruce.</para>
/// </summary>
public class RobotourMissionTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc);

    // Depo a dve stanoviste par set metru od sebe (Brno).
    private const double DepotLatDeg = 49.2103;
    private const double DepotLonDeg = 16.5991;
    private const string PickupCode = "geo:49.2110,16.5991";
    private const string DropCode = "geo:49.2103,16.6000";

    // ---------------- Falesne okoli ----------------

    private sealed class FakeGoals : IGlobalGoalSink
    {
        public List<LLA> Goals { get; } = new List<LLA>();
        public int Cancels { get; private set; }

        public void SetGoal(LLA target) => Goals.Add(target);
        public void Cancel() => Cancels++;
    }

    private sealed class FakeFusion : IPositionInitializer
    {
        public int Calls { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public double Std { get; private set; }
        public DateTime At { get; private set; }

        public void InitializePosition(double x, double y, double std, DateTime t)
        {
            Calls++; X = x; Y = y; Std = std; At = t;
        }
    }

    /// <summary>Regulator, ktery nic neridi — testum staci, ze je (nebo neni) nastaveny.</summary>
    private sealed class StubRegulator : IRegulator
    {
        public RegulatorResult Control(Models.IModelState state) => new RegulatorResult();
        public bool IsFinished => false;
    }

    private sealed class FakeRegulatorHolder : IRegulatorHolder
    {
        public IRegulator Regulator { get; set; } = new StubRegulator();
    }

    private sealed class FakeScanner : IQrScannerControl
    {
        public bool Enabled { get; set; }
        public bool WasEverEnabled { get; private set; }

        public void Note() { if (Enabled) WasEverEnabled = true; }
    }

    private sealed class FakeRoutes : IRouteProbe
    {
        public bool Reachable = true;
        public double LengthM = 123.0;
        public int Probes { get; private set; }

        public RouteProbeResult Probe(LLA target)
        {
            Probes++;
            return new RouteProbeResult(Reachable, LengthM);
        }
    }

    /// <summary>Mise se vsim falesnym okolim pohromade, aby testy nemusely drzet pet promennych.</summary>
    private sealed class Harness
    {
        public readonly FakeGoals Goals = new FakeGoals();
        public readonly FakeFusion Fusion = new FakeFusion();
        public readonly FakeRegulatorHolder Control = new FakeRegulatorHolder();
        public readonly FakeScanner Scanner = new FakeScanner();
        public readonly FakeRoutes Routes = new FakeRoutes();
        public readonly RobotourMission Mission;

        public Harness(RobotourConfig config = null)
        {
            var origin = GeoReference.FromDegrees(DepotLatDeg, DepotLonDeg);
            Mission = new RobotourMission(Goals, Fusion, origin, Control, Scanner,
                                          new GeoUriTargetParser(), Routes,
                                          config ?? new RobotourConfig());
        }

        /// <summary>Kvalitni fix drzeny <paramref name="seconds"/> sekund (vzorek kazdou 1 s).</summary>
        public void FeedGoodFixes(double seconds, DateTime from, double latDeg = DepotLatDeg,
                                  double lonDeg = DepotLonDeg)
        {
            for (double t = 0; t <= seconds; t += 1.0)
                Mission.OnGps(Gps(latDeg, lonDeg, from.AddSeconds(t)));
        }

        /// <summary>Stav motoru: stoji / jede, se stopem nebo bez.</summary>
        public void FeedMotors(bool emergencyStop, bool standing, DateTime now)
            => Mission.OnMotors(new MotorStateBase(emergencyStop, 0, 0, 24, 0, 0,
                                                   standing ? 0 : 0.5, standing ? 0 : 0.5), now);

        public void Arrive(DateTime now)
            => Mission.OnGlobalNav(new GlobalNavMsg { Status = (int)GlobalNavStatus.Arrived, TimeStamp = now });

        public void ReadCode(string text, DateTime now)
            => Mission.OnQrCode(new QrCodeMsg { CameraName = "Right", Text = text, TimeStamp = now });
    }

    /// <summary>
    /// Fix z GPS. <see cref="GPSState.Latitude"/> je od 26. 8. 2026 v <b>RADIANECH</b> (tatáž
    /// jednotka jako <c>LLA</c>), takze se tu prevadi ze stupnu, ve kterych je citelnejsi zadani.
    ///
    /// <para>Pomocnik testu musi drzet kontrakt <b>SENZORU</b>, ne domnenku testovaneho kodu —
    /// dokud tady prevod nesouhlasil s produkcnim kodem, testy vadu jednotek <i>potvrzovaly</i>.</para>
    /// </summary>
    private static GPSState Gps(double latDeg, double lonDeg, DateTime at,
                                int satellites = 9, double hdop = 0.8)
        => new GPSState
        {
            Latitude = Conversions.Deg2Rad(latDeg),
            Longitude = Conversions.Deg2Rad(lonDeg),
            Quality = GPSState.FixQuality.GpsFix,
            NumberOfSatellites = satellites,
            Hdop = hdop,
            TimeStamp = at,
        };

    /// <summary>Projede jedno servisni okno: stop -> (kod) -> potvrzeni -> uvolneni stopu.</summary>
    private static DateTime PassServiceWindow(Harness h, DateTime now, string code)
    {
        h.FeedMotors(emergencyStop: false, standing: true, now);          // dobrzdil
        h.FeedMotors(emergencyStop: true, standing: true, now = now.AddSeconds(1));   // obsluha zmackla stop
        if (code != null) h.ReadCode(code, now = now.AddSeconds(1));
        h.Mission.Confirm();
        h.FeedMotors(emergencyStop: false, standing: true, now = now.AddSeconds(1));  // stop uvolnen

        // Za jizdy nasadi regulator VRSTVA POD misi (LocalNavigator → ControlLoop). Mise ho jen
        // zahazuje, takze bez tohoto kroku by nebylo na cem dvoufazove zastaveni merit.
        h.Control.Regulator = new StubRegulator();
        return now;
    }

    /// <summary>Mise az do prvniho servisniho okna v depu (po inicializaci fuze).</summary>
    private static (Harness h, DateTime now) StartedAtDepot()
    {
        var h = new Harness();
        h.Mission.StartMission();
        h.FeedGoodFixes(6.0, T0);
        return (h, T0.AddSeconds(7));
    }

    // ---------------- Prubeh celou misi ----------------

    /// <summary>
    /// <b>Cela mise:</b> depo -> nakladka -> vykladka -> depo. Hlida se posloupnost fazi i to, ze
    /// posledni zadany cil je <b>zapamatovane depo</b> — to je jediny cil, ktery robot nedostane
    /// z QR kodu a ke kteremu se musi vratit.
    /// </summary>
    [Test]
    public void PruchodCelouMisi_ZadaCileVeSpravnemPoradiAVratiSeDoDepa()
    {
        var (h, now) = StartedAtDepot();

        Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.AwaitingEStop), "po armovani se ceka na stop");

        now = PassServiceWindow(h, now, PickupCode);
        Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.DrivingToPickup));

        h.Arrive(now = now.AddSeconds(30));
        now = PassServiceWindow(h, now, DropCode);
        Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.DrivingToDrop));

        h.Arrive(now = now.AddSeconds(30));
        now = PassServiceWindow(h, now, code: null);   // u vykladky se kod necte
        Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.DrivingToDepot));

        h.Arrive(now.AddSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Finished));
            Assert.That(h.Goals.Goals, Has.Count.EqualTo(3), "nakladka, vykladka, depo");
            Assert.That(Conversions.Rad2Deg(h.Goals.Goals[0].Latitude), Is.EqualTo(49.2110).Within(1e-9));
            Assert.That(Conversions.Rad2Deg(h.Goals.Goals[1].Longitude), Is.EqualTo(16.6000).Within(1e-9));
            Assert.That(Conversions.Rad2Deg(h.Goals.Goals[2].Latitude), Is.EqualTo(DepotLatDeg).Within(1e-6),
                        "posledni cil musi byt ZAPAMATOVANE depo");
        });
    }

    // ---------------- Servisni okno: tri pojistky ----------------

    /// <summary>
    /// <b>Bez zmacknuteho nouzoveho zastaveni se scanner nezapne</b> ani se automat neposune. Robot
    /// nikdy neskenuje, kdyz muze jet — obsluha ma fyzickou garanci, ne jen softwarovou.
    /// </summary>
    [Test]
    public void BezZmacknutehoStopu_SeScannerNezapneAniSeNepokroci()
    {
        var (h, now) = StartedAtDepot();

        h.FeedMotors(emergencyStop: false, standing: true, now);
        h.Scanner.Note();

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.AwaitingEStop));
            Assert.That(h.Scanner.Enabled, Is.False);
            Assert.That(h.Scanner.WasEverEnabled, Is.False);
        });
    }

    [Test]
    public void PodDrzenymStopem_SeScannerZapne()
    {
        var (h, now) = StartedAtDepot();

        h.FeedMotors(emergencyStop: true, standing: true, now);

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing));
            Assert.That(h.Scanner.Enabled, Is.True, "v servisnim okne, kde se ceka kod, se skenuje");
        });
    }

    /// <summary>
    /// <b>Bez potvrzeni obsluhou se cil neprijme.</b> Jedno chybne dekodovani muze poslat robota
    /// o stovky metru jinam — proto je clovek druha, nezavisla pojistka vedle strojovych kontrol.
    /// </summary>
    [Test]
    public void BezPotvrzeniObsluhou_SeCilNeprijme()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode(PickupCode, now.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing), "porad se ceka na potvrzeni");
            Assert.That(h.Mission.PendingTarget, Is.Not.Null, "cil je nabidnuty ke kontrole");
            Assert.That(h.Goals.Goals, Is.Empty, "ale zadany neni");
        });
    }

    /// <summary>Potvrzeni bez precteneho kodu misi neposune — nema co potvrzovat.</summary>
    [Test]
    public void PotvrzeniBezKodu_MisiNeposune()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.Mission.Confirm();

        Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing));
    }

    /// <summary><b>Bez uvolneni nouzoveho zastaveni se nerozjede</b> — motory jsou mrtve zamerne.</summary>
    [Test]
    public void BezUvolneniStopu_SeNerozjede()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);
        h.ReadCode(PickupCode, now = now.AddSeconds(1));
        h.Mission.Confirm();

        h.FeedMotors(emergencyStop: true, standing: true, now.AddSeconds(5));   // stop porad drzi

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.AwaitingEStopRelease));
            Assert.That(h.Goals.Goals, Is.Empty, "cil se zada teprve po uvolneni stopu");
            Assert.That(h.Scanner.Enabled, Is.False, "po potvrzeni uz se neskenuje");
        });
    }

    /// <summary>
    /// <b>Nouzove zastaveni za jizdy automat neposune.</b> O zastaveni se stara ControlLoop a po
    /// uvolneni se jede dal k TEMUZ cili — mise o stopu za jizdy vubec nemusi vedet. Kdyby ho
    /// automat bral jako signal, zmacknuti stopu za jizdy by mu podstrcilo servisni okno.
    /// </summary>
    [Test]
    public void NouzoveZastaveniZaJizdy_AutomatNeposuneAniNezrusiCil()
    {
        var (h, now) = StartedAtDepot();
        now = PassServiceWindow(h, now, PickupCode);
        int cancelsBefore = h.Goals.Cancels;

        h.FeedMotors(emergencyStop: true, standing: false, now.AddSeconds(5));
        h.FeedMotors(emergencyStop: true, standing: true, now.AddSeconds(6));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.DrivingToPickup));
            Assert.That(h.Goals.Cancels, Is.EqualTo(cancelsBefore), "cil se neruší");
            Assert.That(h.Goals.Goals, Has.Count.EqualTo(1), "ani se nezadava znovu");
        });
    }

    // ---------------- ArmingAtDepot: kvalitni fix a inicializace fuze ----------------

    [TestCase(3, 0.8, Description = "malo satelitu")]
    [TestCase(9, 5.0, Description = "vysoky Hdop")]
    public void NekvalitniFix_MisiNeposuneAFuziNeinicializuje(int satellites, double hdop)
    {
        var h = new Harness();
        h.Mission.StartMission();

        for (double t = 0; t <= 20; t += 1.0)
            h.Mission.OnGps(Gps(DepotLatDeg, DepotLonDeg, T0.AddSeconds(t), satellites, hdop));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.ArmingAtDepot));
            Assert.That(h.Fusion.Calls, Is.Zero, "podle spatneho fixu se pocatek nestavi");
        });
    }

    /// <summary>
    /// Velky rozptyl fixu okno <b>zrusi</b>, i kdyz kazdy jednotlivy fix vypada kvalitne. Robot
    /// stoji, takze rozptyl je zdarma dostupna kontrola kvality — a soucasne realisticka
    /// <c>std</c> pro filtr.
    /// </summary>
    [Test]
    public void VelkyRozptylFixu_MisiNeposune()
    {
        var h = new Harness();
        h.Mission.StartMission();

        // Fixy skacou o ~30 m sem a tam (0,0003 stupne sirky je asi 33 m).
        for (int i = 0; i <= 20; i++)
            h.Mission.OnGps(Gps(DepotLatDeg + (i % 2 == 0 ? 0.0003 : -0.0003), DepotLonDeg,
                                T0.AddSeconds(i)));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.ArmingAtDepot));
            Assert.That(h.Fusion.Calls, Is.Zero);
        });
    }

    /// <summary>
    /// Pri vyhovujicim okne se <see cref="IPositionInitializer.InitializePosition"/> zavola
    /// <b>prave jednou</b> a s polohou rovnou <b>prumeru okna</b>. Prumer je poctivejsi nez jediny
    /// vzorek prave proto, ze robot stoji.
    /// </summary>
    [Test]
    public void VyhovujiciOkno_InicializujeFuziPraveJednouNaPrumerOkna()
    {
        var h = new Harness();
        h.Mission.StartMission();

        // Symetricky kolem depa -> prumer je presne depo, tedy pocatek ENU (0, 0).
        for (int i = 0; i <= 10; i++)
            h.Mission.OnGps(Gps(DepotLatDeg + (i % 2 == 0 ? 2e-6 : -2e-6), DepotLonDeg,
                                T0.AddSeconds(i)));
        h.FeedGoodFixes(3.0, T0.AddSeconds(20));   // dalsi fixy uz nic znovu neinicializuji

        Assert.Multiple(() =>
        {
            Assert.That(h.Fusion.Calls, Is.EqualTo(1), "inicializace je JEDNORAZOVA");
            Assert.That(h.Fusion.X, Is.EqualTo(0).Within(0.5));
            Assert.That(h.Fusion.Y, Is.EqualTo(0).Within(0.5));
            Assert.That(h.Fusion.Std, Is.GreaterThan(0), "std musi byt kladna, jinak filtr vyhodi");
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.AwaitingEStop));
            Assert.That(h.Mission.Depot, Is.Not.Null, "depo se zapamatuje");
        });
    }

    /// <summary>
    /// Okno musi projit i pri <b>realistickem sumu GPS</b>. Virtualni GPS ma ve vychozim stavu
    /// sigma 1,5 m (a skutecna spotrebni GPS ve stoje driftuje podobne), takze kdyby kriterium
    /// merilo MAXIMALNI odchylku s prahem 1 m, mise se nezarmuje NIKDY — ani v simulaci, ani na
    /// zarizeni.
    ///
    /// <para>Zaroven to hlida spravnou statistiku: kriterium musi byt <b>efektivni</b> odchylka
    /// (RMS), ktera s rostoucim n konverguje k sigma. Maximum s rostoucim n <b>roste</b>, takze by
    /// delsi cekani kriterium PRITUZOVALO — presne naopak, nez ma.</para>
    /// </summary>
    [Test]
    public void RealistickySumGps_OknoProjde()
    {
        var h = new Harness();
        h.Mission.StartMission();

        // Sum se u virtualni GPS (i u skutecne) prida do OBOU osi se sigma 1,5 m, takze RADIALNI
        // odchylka je ~1,5*sqrt(2) = 2,1 m. Merit prah proti sumu jedne osy je podceneni.
        double[] northM = { 0.4, -1.9, 1.2, -0.7, 2.4, -1.1, 0.9, -2.0, 1.6, -0.8, 1.3 };
        double[] eastM = { -1.6, 1.1, -2.2, 0.8, -1.0, 2.1, -0.5, 1.7, -1.4, 2.3, -0.9 };
        for (int i = 0; i < northM.Length; i++)
            h.Mission.OnGps(Gps(DepotLatDeg + MetersToDegLat(northM[i]),
                                DepotLonDeg + MetersToDegLon(eastM[i], DepotLatDeg),
                                T0.AddSeconds(i)));

        Assert.Multiple(() =>
        {
            Assert.That(h.Fusion.Calls, Is.EqualTo(1), "pri normalnim sumu GPS se MUSI zarmovat");
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.AwaitingEStop));
            Assert.That(h.Fusion.Std, Is.EqualTo(2.1).Within(0.5),
                        "filtru se hlasi skutecne namereny RADIALNI sum, ne podlaha");
        });
    }

    /// <summary>
    /// <b>Zamitnuty kod musi rict PROC.</b> Tri duvody se z pohledu obsluhy chovaji stejne („nic se
    /// nestalo"), ale znamenaji uplne jine reseni: nesrozumitelny kod = jiny kod, prilis daleko =
    /// spatne zadany cil, bez trasy = cil mimo sit.
    ///
    /// <para>Nalezeno pri praci s panelem 26. 8. 2026: autor zkusil cil 71 km daleko, mise ho
    /// spravne zamitla — a protoze to nikde nebylo videt, vypadalo to, ze se kod <i>neprecetl</i>.</para>
    /// </summary>
    [TestCase("http://example.com/neco", "nesrozumitel", Description = "neparsovatelny")]
    [TestCase("geo:50.0,17.0", "daleko", Description = "prilis daleko od depa")]
    public void ZamitnutyKod_ZpravaRekneProc(string code, string expectedFragment)
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode(code, now.AddSeconds(1));

        var msg = h.Mission.LastMessage;
        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg!.HasPending, Is.False);
            Assert.That(msg.RejectReason, Does.Contain(expectedFragment).IgnoreCase,
                        "duvod zamitnuti musi byt ve zprave, jinak to vypada jako nepreceteny kod");
            Assert.That(msg.RejectedCodeText, Is.EqualTo(code), "a s nim text, ktery se zamitl");
        });
    }

    /// <summary>Cil bez trasy v grafu se taky zamitne s duvodem — ne jen tise.</summary>
    [Test]
    public void CilBezTrasy_ZpravaRekneProc()
    {
        var (h, now) = StartedAtDepot();
        h.Routes.Reachable = false;
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode(PickupCode, now.AddSeconds(1));

        Assert.That(h.Mission.LastMessage!.RejectReason, Does.Contain("trasa").IgnoreCase);
    }

    /// <summary>Prijaty kod duvod zamitnuti smaze — jinak by v panelu strasil stary.</summary>
    [Test]
    public void PrijatyKod_SmazeDuvodZamitnuti()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode("geo:50.0,17.0", now.AddSeconds(1));       // zamitnuty
        Assert.That(h.Mission.LastMessage!.RejectReason, Is.Not.Empty);

        h.ReadCode(PickupCode, now.AddSeconds(2));            // prijaty

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.LastMessage!.HasPending, Is.True);
            Assert.That(h.Mission.LastMessage!.RejectReason, Is.Empty);
        });
    }

    /// <summary>Prevod metru na stupne sirky (1° ~ 111,32 km) — jen pro citelnost testu.</summary>
    private static double MetersToDegLat(double meters) => meters / 111320.0;

    /// <summary>Prevod metru na stupne delky (poledniky se k polum stahuji, odtud cos).</summary>
    private static double MetersToDegLon(double meters, double atLatDeg)
        => meters / (111320.0 * Math.Cos(Conversions.Deg2Rad(atLatDeg)));

    /// <summary>
    /// <b>Fix z GPS se cte jako STUPNE.</b> Zapamatovane depo tedy musi vyjit na zadanych
    /// souradnicich a inicializovana poloha na pocatku ENU roviny (fixy jsou v depu, ktere je
    /// pocatkem).
    ///
    /// <para><b>Proc samostatny test:</b> zamena stupnu za radiany <b>nic nenahlasi</b> — body
    /// v okne jsou pak desitky radianu od sebe, rozptyl vyjde astronomicky a okno se zamita VZDY,
    /// takze mise uvizne v <c>ArmingAtDepot</c> a vypada to jako „necekam se fixu". Presne to se
    /// stalo 26. 8. 2026 a odhalil to az beh v aplikaci: testy vadu <b>potvrzovaly</b>, protoze si
    /// jejich pomocnik prevadel na radiany taky. Tenhle test kontroluje jednotky proti
    /// <b>skutecne hodnote</b>, ne proti domnence testovaneho kodu.</para>
    /// </summary>
    [Test]
    public void FixSeCteJakoStupne_DepoVyjdeNaZadanychSouradnicich()
    {
        var h = new Harness();
        h.Mission.StartMission();

        h.FeedGoodFixes(6.0, T0);

        Assert.That(h.Mission.Depot, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(Conversions.Rad2Deg(h.Mission.Depot.Latitude),
                        Is.EqualTo(DepotLatDeg).Within(1e-6), "sirka depa ve stupnich");
            Assert.That(Conversions.Rad2Deg(h.Mission.Depot.Longitude),
                        Is.EqualTo(DepotLonDeg).Within(1e-6), "delka depa ve stupnich");

            // Pocatek ENU roviny JE depo, takze inicializovana poloha musi byt v jeho okoli.
            Assert.That(Math.Sqrt(h.Fusion.X * h.Fusion.X + h.Fusion.Y * h.Fusion.Y),
                        Is.LessThan(1.0), "poloha se inicializuje na pocatek, ne stovky km jinde");
        });
    }

    /// <summary>
    /// <b>Kdyz se mise v depu necekava fixu, musi byt videt PROC.</b> Zprava proto nese kvalitu
    /// posledniho fixu (druzice, HDOP, rozptyl okna a jeho limit) — jinak je „ceka se na kvalitni
    /// fix" nediagnostikovatelne a jediny zpusob, jak zjistit duvod, je zeptat se nekoho, kdo zna kod.
    /// Presne to se stalo 26. 8. 2026.
    /// </summary>
    [Test]
    public void NekvalitniFix_ZpravaRekneProc()
    {
        var h = new Harness();
        h.Mission.StartMission();

        // Fix s malo druzicemi a vysokym HDOP.
        h.Mission.OnGps(Gps(DepotLatDeg, DepotLonDeg, T0, satellites: 3, hdop: 5.0));

        var msg = h.Mission.LastMessage;
        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg!.HasFixInfo, Is.True, "kvalita fixu musi byt ve zprave");
            Assert.That(msg.FixSatellites, Is.EqualTo(3));
            Assert.That(msg.FixHdop, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(msg.FixQualityOk, Is.False, "tenhle fix kriteria nesplnuje");
            Assert.That(msg.FixSpreadLimitM, Is.GreaterThan(0), "limit ma byt v zaznamu taky");
        });
    }

    /// <summary>Rozptyl okna se hlasi průběžně, ne teprve až je okno plné.</summary>
    [Test]
    public void RozptylOkna_SeHlasiPrubezne()
    {
        var h = new Harness();
        h.Mission.StartMission();

        // Tri fixy rozhozene o ~30 m: okno jeste neni plne (DepotFixSec = 5 s), ale rozptyl uz
        // je znamy - a je to prave ten udaj, ktery vysvetluje, proc se nikam nepokracuje.
        for (int i = 0; i < 3; i++)
            h.Mission.OnGps(Gps(DepotLatDeg + MetersToDegLat(i % 2 == 0 ? 30 : -30), DepotLonDeg,
                                T0.AddSeconds(i)));

        var msg = h.Mission.LastMessage;
        Assert.Multiple(() =>
        {
            Assert.That(msg!.FixSamples, Is.EqualTo(3));
            Assert.That(msg.FixQualityOk, Is.True, "jednotlive fixy jsou kvalitni");
            Assert.That(msg.FixSpreadM, Is.GreaterThan(10), "ale rozptyl okna je obrovsky");
        });
    }

    // ---------------- Zastaveni na stanovisti: dve faze ----------------

    /// <summary>
    /// <b>Zastaveni na stanovisti je dvoufazove:</b> na <c>Arrived</c> se nejdriv jen zrusi cil
    /// (robot se rizene dobrzdi cestou, ktera uz existuje), a <c>Regulator = null</c> se nastavi
    /// <b>teprve az robot stoji</b>. Tvrda varianta je vyhrazena pro <c>Aborted</c>.
    /// </summary>
    [Test]
    public void ZastaveniNaStanovisti_JeDvoufazove()
    {
        var (h, now) = StartedAtDepot();
        now = PassServiceWindow(h, now, PickupCode);

        h.Arrive(now = now.AddSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(h.Goals.Cancels, Is.GreaterThan(0), "cil se rusi hned - rizene dobrzdeni");
            Assert.That(h.Control.Regulator, Is.Not.Null, "ale regulator se jeste NEZAHAZUJE");
        });

        h.FeedMotors(emergencyStop: false, standing: false, now.AddSeconds(1));
        Assert.That(h.Control.Regulator, Is.Not.Null, "kola se jeste toci -> porad rizene");

        h.FeedMotors(emergencyStop: false, standing: true, now.AddSeconds(2));
        Assert.That(h.Control.Regulator, Is.Null, "teprve kdyz stoji, aby se nemohlo nic rozjet");
    }

    /// <summary><c>Abort</c> zastavi <b>hned</b> — tam je zastaveni dulezitejsi nez plynulost.</summary>
    [Test]
    public void Abort_ZastaviHnedAZKazdehoStavu()
    {
        var (h, now) = StartedAtDepot();
        now = PassServiceWindow(h, now, PickupCode);

        h.Mission.Abort("test");

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Aborted));
            Assert.That(h.Goals.Cancels, Is.GreaterThan(0));
            Assert.That(h.Control.Regulator, Is.Null, "hned, bez cekani na stani");
            Assert.That(h.Mission.AbortReason, Is.EqualTo("test"), "duvod musi byt v zaznamu");
        });
    }

    // ---------------- Strojove kontroly cile ----------------

    /// <summary>Cil dal nez <see cref="RobotourConfig.MaxTargetDistanceM"/> od depa se zamitne.</summary>
    [Test]
    public void CilMimoMaxVzdalenost_SeZamitne()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode("geo:50.0,17.0", now.AddSeconds(1));   // stovky km daleko
        h.Mission.Confirm();

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.PendingTarget, Is.Null, "takovy cil se ani nenabidne");
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing));
            Assert.That(h.Mission.RejectedCodes, Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// Cil, na ktery nevede trasa, se zamitne <b>uz tady</b> — jinak by se <c>NoRoute</c> zjistilo
    /// az za jizdy.
    /// </summary>
    [Test]
    public void CilBezTrasyVGrafu_SeZamitne()
    {
        var (h, now) = StartedAtDepot();
        h.Routes.Reachable = false;
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode(PickupCode, now.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(h.Routes.Probes, Is.GreaterThan(0), "dosazitelnost se opravdu zkousi");
            Assert.That(h.Mission.PendingTarget, Is.Null);
        });
    }

    /// <summary>
    /// Nesrozumitelny kod misi <b>nikdy</b> neposune — ale jeho text se presto uchova, aby bylo
    /// v zaznamu videt, co robot precetl a proc to zamitl.
    /// </summary>
    [Test]
    public void NedekodovatelnyText_CilNeprijmeAlePresteSeUchova()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode("http://example.com/neco", now.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.PendingTarget, Is.Null);
            Assert.That(h.Mission.LastCodeText, Is.EqualTo("http://example.com/neco"),
                        "text jde do zaznamu DOSLOVA i kdyz se zamitne");
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing));
        });
    }

    // ---------------- Timeouty ----------------

    /// <summary>
    /// Stav bez cloveka v cyklu ma timeout a jeho vyprseni se <b>ohlasi</b> — nikdy tiche
    /// zaseknuti. Mise nema zotavovaci manevr, takze jedina bezpecna odpoved je zastavit.
    /// </summary>
    [Test]
    public void TimeoutJizdy_MisiPreruseSDuvodem()
    {
        var cfg = new RobotourConfig { DrivingTimeoutSec = 10 };
        var h = new Harness(cfg);
        h.Mission.StartMission();
        h.FeedGoodFixes(6.0, T0);
        var now = PassServiceWindow(h, T0.AddSeconds(7), PickupCode);

        h.Mission.Tick(now.AddSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Aborted));
            Assert.That(h.Mission.AbortReason, Does.Contain("timeout").IgnoreCase);
            Assert.That(h.Mission.Timeouts, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// <b>Stavy pod nouzovym zastavenim timeout nemaji</b> — ceka se na obsluhu, jak dlouho je
    /// potreba. Meri se a loguje jen uplynuly cas.
    /// </summary>
    [Test]
    public void StavPodStopem_TimeoutNema()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.Mission.Tick(now.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing));
            Assert.That(h.Mission.Timeouts, Is.Zero);
        });
    }

    /// <summary>
    /// Kdyz se kod dlouho nedekoduje, mise to <b>hlasi</b>, ale dal skenuje — resenim je obsluha
    /// (posunout kod, prisunout robota), ne otocka robota, ktery ma pod rukama cloveka.
    /// </summary>
    [Test]
    public void KodyNeviditelnyDlouho_SeHlasiAleSkenujeSeDal()
    {
        var cfg = new RobotourConfig { QrSearchSec = 5 };
        var h = new Harness(cfg);
        h.Mission.StartMission();
        h.FeedGoodFixes(6.0, T0);
        var now = T0.AddSeconds(7);
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.Mission.Tick(now.AddSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.Phase, Is.EqualTo(RobotourPhase.Servicing));
            Assert.That(h.Mission.CodeNotSeen, Is.True, "UI to musi ukazat");
            Assert.That(h.Scanner.Enabled, Is.True, "a skenuje se DAL");
        });
    }

    // ---------------- Zprava a zaznam ----------------

    /// <summary>
    /// Servisni okno <b>nezpusobi falesny zasek</b>: cil je zruseny, takze detektory globalni
    /// vrstvy (vazane na aktivni cil) jsou vypnute a stani u nakladky nikdo nevyhodnoti jako zasek.
    /// </summary>
    [Test]
    public void ServisniOkno_ZrusiCilTakzeDetektoryZasekuVypadnou()
    {
        var (h, now) = StartedAtDepot();
        now = PassServiceWindow(h, now, PickupCode);
        int before = h.Goals.Cancels;

        h.Arrive(now.AddSeconds(30));

        Assert.That(h.Goals.Cancels, Is.GreaterThan(before), "aktivni cil zmizi hned na Arrived");
    }

    [Test]
    public void Zprava_JeObousmerna()
    {
        var state = new MissionState
        {
            Phase = RobotourPhase.DrivingToDrop,
            PhaseEnteredAt = T0,
            ElapsedSec = 421.5,
            HasDepot = true, DepotLatDeg = DepotLatDeg, DepotLonDeg = DepotLonDeg,
            HasPickup = true, PickupLatDeg = 49.2110, PickupLonDeg = 16.5991, PickupCodeText = PickupCode,
            HasDrop = true, DropLatDeg = 49.2103, DropLonDeg = 16.6000, DropCodeText = DropCode,
            AbortReason = "",
            CodesRead = 2, CodesRejected = 1, Timeouts = 0,
            EmergencyStop = false, CodeNotSeen = false,
            TimeStamp = T0.AddSeconds(421.5),
        };

        var original = state.ToLogMessage();
        var buffer = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new MissionMsg();
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Phase, Is.EqualTo((int)RobotourPhase.DrivingToDrop));
            Assert.That(loaded.ElapsedSec, Is.EqualTo(421.5).Within(1e-9));
            Assert.That(loaded.DepotLatDeg, Is.EqualTo(DepotLatDeg).Within(1e-9));
            Assert.That(loaded.PickupCodeText, Is.EqualTo(PickupCode), "zdrojovy text kodu prezije");
            Assert.That(loaded.DropCodeText, Is.EqualTo(DropCode));
            Assert.That(loaded.CodesRead, Is.EqualTo(2));
            Assert.That(loaded.CodesRejected, Is.EqualTo(1));
            Assert.That(loaded.TimeStamp, Is.EqualTo(T0.AddSeconds(421.5)));
        });
    }

    /// <summary>
    /// <b>Nabidnuty cil jde do zpravy</b> (verze 2) — vcetne delky trasy, kterou obsluha videla.
    /// Ta se pocita jen ve zkousce dosazitelnosti a <b>nikde jinde v zaznamu neni</b>, takze bez
    /// toho by po soutezi neslo dohledat, na zaklade CEHO se cil potvrdil.
    /// </summary>
    [Test]
    public void NabidnutyCil_JdeDoZpravy()
    {
        var (h, now) = StartedAtDepot();
        h.Routes.LengthM = 412.5;
        h.FeedMotors(emergencyStop: true, standing: true, now);

        h.ReadCode(PickupCode, now.AddSeconds(1));

        var msg = h.Mission.LastMessage;
        Assert.That(msg, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(msg!.HasPending, Is.True);
            Assert.That(msg.PendingLatDeg, Is.EqualTo(49.2110).Within(1e-9));
            Assert.That(msg.PendingCodeText, Is.EqualTo(PickupCode));
            Assert.That(msg.PendingRouteLengthM, Is.EqualTo(412.5).Within(1e-9));
            Assert.That(msg.PendingDistanceFromDepotM, Is.GreaterThan(50),
                        "stanoviste je ~78 m od depa");
        });
    }

    /// <summary>Po potvrzeni uz nabidnuty cil ve zprave neni — je z nej prijaty cil.</summary>
    [Test]
    public void PoPotvrzeni_NabidnutyCilZeZpravyZmizi()
    {
        var (h, now) = StartedAtDepot();
        h.FeedMotors(emergencyStop: true, standing: true, now);
        h.ReadCode(PickupCode, now.AddSeconds(1));

        h.Mission.Confirm();

        Assert.Multiple(() =>
        {
            Assert.That(h.Mission.LastMessage!.HasPending, Is.False);
            Assert.That(h.Mission.LastMessage!.PickupCodeText, Is.EqualTo(PickupCode),
                        "text se presunul do prijateho cile");
        });
    }

    /// <summary>
    /// Zprava verze 1 nabidnuty cil nenese — <c>HasPending</c> zustane <c>false</c> a rozbor musi
    /// priznat, ze co obsluha pred potvrzenim videla, uz nezjisti (viz doc/record-replay.md).
    /// </summary>
    [Test]
    public void StaraZpravaVerze1_NabidnutyCilNema()
    {
        var v1 = new MissionMsg { Verze = 1, Phase = (int)RobotourPhase.Servicing, TimeStamp = T0 };
        var buffer = new System.IO.MemoryStream();

        // Verze 1 zapsala vse az po CodeNotSeen a TimeStamp; nabidnuty cil za tim jeste nebyl.
        using (var bw = new System.IO.BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(v1.Phase); bw.Write(0);
            bw.Write(T0.ToBinary()); bw.Write(0.0);
            bw.Write(false); bw.Write(0.0); bw.Write(0.0);
            bw.Write(false); bw.Write(0.0); bw.Write(0.0); bw.Write("");
            bw.Write(false); bw.Write(0.0); bw.Write(0.0); bw.Write("");
            bw.Write(""); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(false); bw.Write(false);
            bw.Write(T0.ToBinary());
        }

        buffer.Position = 0;
        var loaded = new MissionMsg { Verze = 1 };
        using (var br = new System.IO.BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.HasPending, Is.False);
            Assert.That(loaded.Phase, Is.EqualTo((int)RobotourPhase.Servicing), "zbytek se precte dal");
        });
    }

    /// <summary>
    /// <b>Nespustena mise hlasi uplynuly cas 0</b>, ne rozdil proti <c>default(DateTime)</c>.
    ///
    /// <para>Bez toho vyjde <c>now − 0001-01-01</c>, tedy ~64 miliard sekund — a nejde jen
    /// o kosmetiku v UI: ta hodnota tece i do <see cref="MissionMsg"/>, takze by ji mel v sobe
    /// <b>zaznam</b> (nalezeno v bezicí aplikaci 26. 8. 2026).</para>
    /// </summary>
    [Test]
    public void NespustenaMise_HlasiNulovyCas()
    {
        var h = new Harness();

        // Zpravy tecou i v Idle (stupen zije a periodicky hlasi), takze se to projevi hned.
        h.Mission.Tick(T0);

        Assert.That(h.Mission.LastMessage, Is.Not.Null);
        Assert.That(h.Mission.LastMessage!.ElapsedSec, Is.Zero,
                    "mise jeste nezacala - uplynuly cas nema odkud merit");
    }

    /// <summary>Po startu uz uplynuly cas bezi v hodinach DAT, ne stroje.</summary>
    [Test]
    public void PoStartu_UplynulyCasBeziVHodinachDat()
    {
        var h = new Harness();
        h.Mission.StartMission(T0);

        h.Mission.Tick(T0.AddSeconds(42));

        Assert.That(h.Mission.LastMessage!.ElapsedSec, Is.EqualTo(42).Within(1e-6));
    }

    [Test]
    public void Zprava_JeVKataloguZprav()
    {
        Assert.That(ARBot.Common.Communication.MessageCatalog.CommonDefaults().Contains("MissionMsg"),
                    Is.True);
    }

    /// <summary>Zprava se emituje pri KAZDE zmene faze — jinak by se mise ve View nedala prehrat.</summary>
    [Test]
    public void ZmenaFaze_VyrobiZpravu()
    {
        var h = new Harness();

        h.Mission.StartMission();

        Assert.That(h.Mission.LastMessage, Is.Not.Null);
        Assert.That(h.Mission.LastMessage!.Phase, Is.EqualTo((int)RobotourPhase.ArmingAtDepot));
    }
}
