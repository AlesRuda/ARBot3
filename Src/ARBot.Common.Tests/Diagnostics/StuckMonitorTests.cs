using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Tests.Diagnostics;

/// <summary>
/// <b>Hlidac uvaznuti</b> (<see cref="StuckMonitor"/>, tema <c>nav-uvaznuti-neohlasene</c>).
///
/// <para>Do 8. 10. 2026 uvaznuti neohlasil nikdo: globalni navigace i mise hlasily „jede" a robot
/// stal do zasahu obsluhy (1. 10. 132 + 42 s v <c>RobotBlocked</c>, 18. 9. ~6 min v
/// <c>AlreadyAtGoal</c>, Kolo4 268 s v <c>GoalUnsafe</c>). Rozhodnuti autora: stani pri aktivni
/// jizde bez ohledu na stav planu, „stoji" od 20 s, „UVAZL" od 60 s, jen hlasit.</para>
///
/// <para>Simulace taktu ridici smycky 10 Hz: <c>DriveCommandMsg</c> + <c>RobotStateMsg</c> kazdych
/// 100 ms, navigace 5 Hz, plan 10 Hz. Cas je cas DAT.</para>
/// </summary>
public class StuckMonitorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Scenar: co se v kazdem taktu posila. Vychozi = robot stoji, ma cil, plan RobotBlocked v hloubce.</summary>
    private sealed class Scena
    {
        public Func<double, double> Poloha = _ => 0.0;           // ujeta poloha X(t) [m]
        public Func<double, double> Rychlost = _ => 0.0;         // V(t) [m/s] z fuze
        public Func<double, bool> Stop = _ => false;
        public Func<double, bool> Hold = _ => false;
        public Func<double, double> Povel = _ => 0.0;
        public Func<double, GlobalNavStatus?> Nav = _ => GlobalNavStatus.Driving;   // null = navigace mlci
        public Func<double, LocalPlanMsg> Plan = t => PlanMsg(LocalPlanStatus.RobotBlocked, start: CellBlockReason.Geometry);
        public Func<double, bool> FreeRun = _ => false;
    }

    private static LocalPlanMsg PlanMsg(LocalPlanStatus st, CellBlockReason start = CellBlockReason.None,
                                        double goalDist = 6.0, double clearance = 0.1,
                                        double reqX = 6, double reachedX = 0, int verze = LocalPlanMsg.FormatVersion)
    {
        var m = new LocalPlanMsg
        {
            Status = (int)st, StartBlock = (byte)start, GoalDistanceM = goalDist, StartClearanceM = clearance,
            RequestedGoalX = reqX, ReachedGoalX = reachedX,
        };
        m.Verze = verze;
        return m;
    }

    private sealed class Vysledek
    {
        public readonly List<(double T, string Line)> Lines = new();
        public readonly List<(double T, StuckMsg Msg)> Msgs = new();
        public StuckMonitor Monitor;
        public IEnumerable<string> Texty => Lines.Select(l => l.Line);
    }

    private static Vysledek Prehraj(Scena s, double doSec, StuckConfig cfg = null)
    {
        var v = new Vysledek();
        double tNow = 0;
        v.Monitor = new StuckMonitor(cfg, line => v.Lines.Add((tNow, line)));
        for (int k = 0; k <= (int)Math.Round(doSec * 10); k++)
        {
            tNow = k / 10.0;
            var t = T0.AddSeconds(tNow);
            var lp = s.Plan(tNow);
            if (lp != null) { lp.TimeStamp = t.AddMilliseconds(-30); Posli(v, lp, tNow); }
            if (s.FreeRun(tNow)) Posli(v, new FreeRunMsg { TimeStamp = t.AddMilliseconds(-30) }, tNow);
            if (k % 2 == 0 && s.Nav(tNow) is GlobalNavStatus ns)
                Posli(v, new GlobalNavMsg { Status = (int)ns, HasGoal = ns != GlobalNavStatus.NoGoal, TimeStamp = t }, tNow);
            Posli(v, new DriveCommandMsg { Speed = s.Povel(tNow), EmergencyStop = s.Stop(tNow), Held = s.Hold(tNow), TimeStamp = t }, tNow);
            double x = s.Poloha(tNow);
            Posli(v, new RobotStateMsg { X = x, OdomX = x, V = s.Rychlost(tNow), TimeStamp = t }, tNow);
        }
        return v;
    }

    private static void Posli(Vysledek v, ARBot.Common.Logs.Message m, double t)
    {
        var o = v.Monitor.Process(m);
        if (o != null) v.Msgs.Add((t, o));
    }

    private static DateTime At(double sec) => T0.AddSeconds(sec);

    // ---------------- prahy ----------------

    /// <summary>1. 10. 2026: robot stoji v bunce blokovane hloubkou, navigace hlasi Driving.</summary>
    [Test]
    public void StojiciRobotPriJizde_Od20sStoji_Od60sUvazl()
    {
        var v = Prehraj(new Scena(), 95);

        Assert.Multiple(() =>
        {
            var lines = v.Lines;
            Assert.That(lines[0].Line, Is.EqualTo("STANI: robot stoji 20 s pri jizde - v bunce blokovane hloubkou, unik nenalezen."));
            Assert.That(lines[0].T, Is.EqualTo(20.0).Within(0.15));
            Assert.That(lines[1].Line, Does.StartWith("UVAZL: robot stoji 60 s pri jizde - v bunce blokovane hloubkou"));
            Assert.That(lines[1].T, Is.EqualTo(60.0).Within(0.15));
            Assert.That(lines[2].Line, Does.StartWith("UVAZL trva 90 s"));
            Assert.That(lines.Count, Is.EqualTo(3), "pripominka jen jednou za 30 s, ne kazdy takt");

            Assert.That(v.Msgs.First().T, Is.EqualTo(20.0).Within(0.15), "zprava az od prahu");
            Assert.That(v.Msgs.Count, Is.InRange(74, 77), "1 Hz od 20 do 95 s");
            Assert.That(v.Msgs.Last().Msg.Level, Is.EqualTo((byte)StuckLevel.Stuck));
            Assert.That(v.Msgs.Last().Msg.Cause, Is.EqualTo((byte)StuckCause.InBlockedCell));
            Assert.That(v.Msgs.Last().Msg.StartBlock, Is.EqualTo((byte)CellBlockReason.Geometry));

            var r = v.Monitor.Read(At(95));
            Assert.That(r.Level, Is.EqualTo(StuckLevel.Stuck));
            Assert.That(r.Text, Is.EqualTo("UVÁZL 95 s — v buňce blokované hloubkou, únik nenalezen — zásah obsluhy"));
        });
    }

    [Test]
    public void KratkeStani_Nehlasi()
    {
        var v = Prehraj(new Scena(), 19.5);
        Assert.That(v.Lines, Is.Empty);
        Assert.That(v.Msgs, Is.Empty);
        Assert.That(v.Monitor.Read(At(19.5)).Level, Is.EqualTo(StuckLevel.None));
    }

    [Test]
    public void Rozjezd_UkonciStani()
    {
        var s = new Scena { Poloha = t => t < 30 ? 0 : (t - 30) * 0.5, Rychlost = t => t < 30 ? 0 : 0.5 };
        var v = Prehraj(s, 35);

        Assert.That(v.Texty.Last(), Does.StartWith("STANI skoncilo po 3").And.EndWith(" s: rozjel se."));
        Assert.That(v.Msgs.Last().Msg.Level, Is.EqualTo((byte)StuckLevel.None), "konec jde do zaznamu");
        Assert.That(v.Msgs.Last().Msg.Text, Is.EqualTo("stání skončilo: rozjel se"));
        Assert.That(v.Monitor.Read(At(35)).Level, Is.EqualTo(StuckLevel.None));
    }

    /// <summary>
    /// Pomaly rozjezd z uvaznuti: okno se zkracuje postupne a „UVÁZL 16 s" by lhalo - pod polovinou
    /// prahu (30 s) se uroven vrati na „stoji", konec az pod 10 s.
    /// </summary>
    [Test]
    public void PomalyRozjezdZUvaznuti_SestoupiNaStoji()
    {
        // stoji 80 s, pak se plizi 2 cm/s (0,5 m za 25 s), od 120 s jede 0,3 m/s
        var s = new Scena
        {
            Poloha = t => t < 80 ? 0 : t < 120 ? 0.02 * (t - 80) : 0.8 + 0.3 * (t - 120),
            Rychlost = t => t < 80 ? 0 : t < 120 ? 0.02 : 0.3,
        };
        var v = Prehraj(s, 125);

        var uvazl = v.Msgs.Where(m => m.Msg.Level == (byte)StuckLevel.Stuck).Select(m => m.Msg.StandingSec);
        Assert.That(uvazl, Has.All.GreaterThanOrEqualTo(30.0), "UVAZL s malym cislem by lhalo");
        Assert.That(v.Msgs.Any(m => m.T > 100 && m.T < 120 && m.Msg.Level == (byte)StuckLevel.Standing), Is.True,
                    "pri plizeni sestoupi na „stoji“");
        Assert.That(v.Texty.Last(), Does.StartWith("STANI skoncilo po 12").And.EndWith("rozjel se."));
        var konec = v.Msgs.Last().Msg;
        Assert.That(konec.Level, Is.EqualTo((byte)StuckLevel.None));
        Assert.That(konec.EpisodeSec, Is.GreaterThan(120.0), "koncova zprava nese delku cele epizody");
        Assert.That(konec.StandingSec, Is.LessThan(10.0), "okno je pri konci kratke");
    }

    // ---------------- legitimni stani ----------------

    /// <summary>Nouzove zastaveni je legitimni stani (stanoviste, zasah obsluhy) a pocita se az od uvolneni.</summary>
    [Test]
    public void NouzoveZastaveni_NeniStani_PocitaSeOdUvolneni()
    {
        var s = new Scena { Stop = t => t < 100 };
        var v = Prehraj(s, 125);

        Assert.That(v.Lines.Count, Is.EqualTo(1));
        Assert.That(v.Lines[0].T, Is.EqualTo(120.0).Within(0.15), "20 s od uvolneni, ne od zacatku");
    }

    [Test]
    public void DrzeneZastaveni_UkonciUvaznuti()
    {
        var s = new Scena { Hold = t => t >= 70 };
        var v = Prehraj(s, 80);

        Assert.That(v.Texty.Last(), Is.EqualTo("STANI skoncilo po 70 s: drzene zastaveni."));
        Assert.That(v.Monitor.Read(At(80)).Level, Is.EqualTo(StuckLevel.None));
    }

    /// <summary>Unik se plizi 0,05 m/s - 0,5 m za 10 s. To je postup, ne uvaznuti.</summary>
    [Test]
    public void PlizeniUnikem_NeniStani()
    {
        var s = new Scena
        {
            Poloha = t => 0.05 * t, Rychlost = _ => 0.05,
            Plan = _ => PlanMsg(LocalPlanStatus.EscapingBlocked, CellBlockReason.Geometry),
        };
        var v = Prehraj(s, 120);
        Assert.That(v.Lines, Is.Empty);
    }

    /// <summary>
    /// Plizeni 2,5 cm/s (0,5 m za 20 s - prave na prahu): JEDNO hlaseni „stoji", ne pila
    /// „STANI → skoncilo po 21 s → STANI" kazdy pul metr. Nad zaznamy z 2. 9. 2026 to s kotvou
    /// misto klouzaveho okna presne takhle blikalo.
    /// </summary>
    [Test]
    public void PomalePlizeni_JednoHlaseniBezBlikani()
    {
        var s = new Scena { Poloha = t => 0.024 * t, Rychlost = _ => 0.024 };
        var v = Prehraj(s, 120);

        Assert.That(v.Texty.Count(l => l.StartsWith("STANI:")), Is.EqualTo(1));
        Assert.That(v.Texty, Has.None.StartsWith("STANI skoncilo"));
        Assert.That(v.Texty, Has.None.StartsWith("UVAZL"), "postupuje, i kdyz pomalu - neuvazl");
    }

    /// <summary>Skok pozy po korekci (bez rychlosti) neni pohyb - stani pokracuje.</summary>
    [Test]
    public void SkokPozy_NeniPohyb()
    {
        var s = new Scena { Poloha = t => t < 15 ? 0 : 3.0 };
        var v = Prehraj(s, 25);
        Assert.That(v.Lines.Count, Is.EqualTo(1));
        Assert.That(v.Lines[0].T, Is.EqualTo(20.0).Within(0.15));
    }

    [TestCase(GlobalNavStatus.NoGoal)]
    [TestCase(GlobalNavStatus.Arrived)]
    [TestCase(GlobalNavStatus.NoRoute)]
    public void NavigaceNeJede_NeniStani(GlobalNavStatus st)
    {
        var v = Prehraj(new Scena { Nav = _ => st }, 90);
        Assert.That(v.Lines, Is.Empty);
    }

    [Test]
    public void CilSkoncil_UkonciStani()
    {
        var s = new Scena { Nav = t => t < 40 ? GlobalNavStatus.Driving : GlobalNavStatus.Arrived };
        var v = Prehraj(s, 45);
        Assert.That(v.Texty.Last(), Is.EqualTo("STANI skoncilo po 40 s: dojel do cile."));
    }

    // ---------------- bez globalni navigace ----------------

    /// <summary>FreeRun globalni navigaci nema (NoGoal) - stani se hlasi i tak.</summary>
    [Test]
    public void FreeRun_BezNavigace_Hlasi()
    {
        var s = new Scena
        {
            Nav = _ => GlobalNavStatus.NoGoal,
            FreeRun = _ => true,
            Plan = _ => PlanMsg(LocalPlanStatus.LocalMinimum, goalDist: 1.5, reqX: 1.5, reachedX: 0.02),
        };
        var v = Prehraj(s, 25);

        Assert.That(v.Lines.Single().Line, Is.EqualTo("STANI: robot stoji 20 s pri jizde - mrkev 1.5 m nedosazitelna (lokalni minimum)."));
    }

    /// <summary>
    /// FreeRun pri vypadku kamer: FreeRunMsg i plany zmlknou (vznikaji ze snimku), navigace s mapou
    /// hlasi NoGoal. To neni konec jizdy - stani se hlasi s pricinou „bez planu" a probihajici
    /// UVAZL se neukonci duvodem „jizda skoncila" (nalezla nezavisla kontrola 9. 10. 2026).
    /// </summary>
    [Test]
    public void FreeRun_VypadekKamer_HlasiBezPlanu()
    {
        var s = new Scena
        {
            Nav = _ => GlobalNavStatus.NoGoal,
            FreeRun = t => t < 70,
            Plan = t => t < 70 ? PlanMsg(LocalPlanStatus.RobotBlocked, CellBlockReason.Geometry) : null,
        };
        var v = Prehraj(s, 100);

        Assert.Multiple(() =>
        {
            Assert.That(v.Texty, Has.None.Contains("jizda skoncila"));
            Assert.That(v.Texty, Has.None.StartsWith("STANI skoncilo"));
            Assert.That(v.Texty.Last(), Does.StartWith("UVAZL trva 90 s - bez lokalniho planu"));
            Assert.That(v.Monitor.Read(At(100)).Level, Is.EqualTo(StuckLevel.Stuck));
        });
    }

    /// <summary>Jizda goal= bez mapy: skutecny dojezd neni stani, slepy konec (LocalMinimum) ano.</summary>
    [Test]
    public void BezMapy_SkutecnyDojezdNeniStani_LokalniMinimumAno()
    {
        var dojel = Prehraj(new Scena
        {
            Nav = _ => null,
            Plan = _ => PlanMsg(LocalPlanStatus.AlreadyAtGoal, goalDist: 0.1, reqX: 3, reachedX: 3),
        }, 70);
        var minimum = Prehraj(new Scena
        {
            Nav = _ => null,
            Plan = _ => PlanMsg(LocalPlanStatus.LocalMinimum, goalDist: 7.2, reqX: 7.2, reachedX: 0.01),
        }, 70);

        Assert.That(dojel.Lines, Is.Empty);
        Assert.That(minimum.Lines.Select(l => l.Line), Has.Some.StartsWith("UVAZL: robot stoji 60 s pri jizde - mrkev 7.2 m nedosazitelna"));
    }

    /// <summary>
    /// Vyznam AlreadyAtGoal se 9. 10. 2026 zmenil (slepy konec ma vlastni stav LocalMinimum).
    /// Ve STARSIM zaznamu (LocalPlanMsg verze &lt; 4) je AlreadyAtGoal s mrkvi daleko slepy konec
    /// a s dosazenym cilem dojezd; od verze 4 je AlreadyAtGoal vzdy dojezd, i kdyz je mrkev daleko
    /// (cilova zona), a nehada se.
    /// </summary>
    [Test]
    public void AlreadyAtGoal_VyznamPodleVerzeZpravy()
    {
        var stary = Prehraj(new Scena
        {
            Nav = _ => null,
            Plan = _ => PlanMsg(LocalPlanStatus.AlreadyAtGoal, goalDist: double.NaN, reqX: 7.2, reachedX: 0.01, verze: 3),
        }, 21);
        var staryDojezd = Prehraj(new Scena
        {
            Nav = _ => null,
            Plan = _ => PlanMsg(LocalPlanStatus.AlreadyAtGoal, goalDist: double.NaN, reqX: 3, reachedX: 3, verze: 3),
        }, 70);
        var novyDojezd = Prehraj(new Scena
        {
            Nav = _ => null,
            Plan = _ => PlanMsg(LocalPlanStatus.AlreadyAtGoal, goalDist: 2.2, reqX: 2.2, reachedX: 0.01),
        }, 70);

        Assert.Multiple(() =>
        {
            Assert.That(stary.Lines.Single().Line, Does.EndWith("mrkev 7.2 m nedosazitelna (lokalni minimum)."));
            Assert.That(staryDojezd.Lines, Is.Empty);
            Assert.That(novyDojezd.Lines, Is.Empty, "od verze 4 je AlreadyAtGoal dojezd, ne slepy konec");
        });
    }

    /// <summary>
    /// Navigace chce jet dal, ale lokalni plan hlasi skutecny dojezd k mrkvi - mrkev se neposouva.
    /// Neni to slepy konec a hlaseni to nesmi tvrdit.
    /// </summary>
    [Test]
    public void DojezdKMrkviPriJedouciNavigaci_JeVlastniPricina()
    {
        var v = Prehraj(new Scena { Plan = _ => PlanMsg(LocalPlanStatus.AlreadyAtGoal, goalDist: 0.1, reqX: 3, reachedX: 3) }, 21);
        Assert.That(v.Lines.Single().Line, Does.EndWith("lokalni plan hlasi dojezd k mrkvi (0.1 m), navigace jede dal."));
        Assert.That(v.Msgs.Last().Msg.Cause, Is.EqualTo((byte)StuckCause.PlanAtGoal));
    }

    // ---------------- priciny ----------------

    /// <summary>
    /// Slepy konec ma od 9. 10. 2026 vlastni stav <c>LocalMinimum</c> (driv <c>AlreadyAtGoal</c>
    /// s mrkvi daleko) - hlidac ho pojmenuje stejne a vzdalenost bere z planu.
    /// </summary>
    [Test]
    public void LocalMinimum_JeNedosazitelnaMrkev()
    {
        var v = Prehraj(new Scena { Plan = _ => PlanMsg(LocalPlanStatus.LocalMinimum, goalDist: 6.6) }, 21);
        Assert.That(v.Lines.Single().Line, Is.EqualTo("STANI: robot stoji 20 s pri jizde - mrkev 6.6 m nedosazitelna (lokalni minimum)."));
        Assert.That(v.Msgs.Last().Msg.Cause, Is.EqualTo((byte)StuckCause.CarrotUnreachable));
    }

    /// <summary>Kolo4 (19. 9.): 268 s v GoalUnsafe - „platny" plan, a presto uvaznuti.</summary>
    [Test]
    public void GoalUnsafe_JePricina()
    {
        var v = Prehraj(new Scena { Plan = _ => PlanMsg(LocalPlanStatus.GoalUnsafe, goalDist: 4.04) }, 21);
        Assert.That(v.Lines.Single().Line, Is.EqualTo("STANI: robot stoji 20 s pri jizde - mrkev 4.0 m tesne u prekazky."));
    }

    [Test]
    public void BezPlanu_JePricina()
    {
        var v = Prehraj(new Scena { Plan = _ => null }, 21);
        Assert.That(v.Lines.Single().Line, Is.EqualTo("STANI: robot stoji 20 s pri jizde - lokalni plan neprisel (kamery nebo fuze)."));

        var w = Prehraj(new Scena { Plan = t => t < 5 ? PlanMsg(LocalPlanStatus.Ok) : null }, 21);
        Assert.That(w.Lines.Single().Line, Does.EndWith("bez lokalniho planu 15 s (kamery nebo fuze)."));
    }

    [Test]
    public void PlanVedeDal_PovelBezPohybu()
    {
        var v = Prehraj(new Scena { Plan = _ => PlanMsg(LocalPlanStatus.Ok), Povel = _ => 0.4 }, 21);
        Assert.That(v.Lines.Single().Line, Does.EndWith("plan vede dal, povel 0.40 m/s, ale robot nejede (prekazka u kol? motory?)."));
    }

    [Test]
    public void TesneUPrekazky_BezBlokovaneBunky()
    {
        var v = Prehraj(new Scena { Plan = _ => PlanMsg(LocalPlanStatus.RobotBlocked, clearance: 0.32) }, 21);
        Assert.That(v.Lines.Single().Line, Does.EndWith("tesne u prekazky (odstup 0.32 m), unik nenalezen."));
    }

    /// <summary>AbortedCollision prepise stav planu - pricina zustava z planu pred nim.</summary>
    [Test]
    public void AbortedCollision_NeprepisePricinu()
    {
        var s = new Scena
        {
            // Od 10 s uz chodi jen AbortedCollision (stav planovace je v nich prepsany).
            Plan = t => t >= 10
                ? new LocalPlanMsg { Status = (int)LocalPlanStatus.AbortedCollision }
                : PlanMsg(LocalPlanStatus.RobotBlocked, CellBlockReason.Semantics),
        };
        var v = Prehraj(s, 21);
        Assert.That(v.Lines.Single().Line, Does.Contain("v bunce blokovane barvou"));
    }

    /// <summary>
    /// Stavy planu se stridaji ~19× za sekundu. Pricina je PREVAZUJICI za poslednich 10 s, ne
    /// posledni plan: 18. 9. vyslo podle posledniho „povel je nulovy" uprostred stani, ktere bylo
    /// cele o nedosazitelne mrkvi.
    /// </summary>
    [Test]
    public void Pricina_JePrevazujici_NeNahodnaPosledni()
    {
        var s = new Scena
        {
            // kazdy paty takt Ok (Partial faze v lokalnim minimu), jinak LocalMinimum (slepy konec, mrkev daleko)
            Plan = t => (int)Math.Round(t * 10) % 5 == 0
                ? PlanMsg(LocalPlanStatus.Ok)
                : PlanMsg(LocalPlanStatus.LocalMinimum, goalDist: 7.2, reqX: 7.2, reachedX: 0.01),
        };
        var v = Prehraj(s, 65);
        Assert.That(v.Lines.Select(l => l.Line), Has.All.Contains("mrkev 7.2 m nedosazitelna"));
        Assert.That(v.Msgs.Select(m => m.Msg.Cause).Distinct(), Is.EqualTo(new[] { (byte)StuckCause.CarrotUnreachable }));
    }

    // ---------------- stranka ----------------

    /// <summary>Bez taktu (fuze/smycka stoji) stary stav nelze: stranka ukaze nic, ne zamrzle cislo.</summary>
    [Test]
    public void ZastaralyStav_NaStranceNeni()
    {
        var v = Prehraj(new Scena(), 30);
        Assert.That(v.Monitor.Read(At(30)).Level, Is.EqualTo(StuckLevel.Standing));
        Assert.That(v.Monitor.Read(At(30 + StuckMonitor.StaleSec + 0.5)).Level, Is.EqualTo(StuckLevel.None));
    }

    [Test]
    public void Ascii_BezDiakritiky()
    {
        Assert.That(StuckDetector.Ascii("UVÁZL — mrkev nedosažitelná, únik"), Is.EqualTo("UVAZL - mrkev nedosazitelna, unik"));
    }

    [Test]
    public void Zprava_TamAZpet()
    {
        var m = new StuckMsg
        {
            Level = 2, StandingSec = 75.5, EpisodeSec = 80.25, Cause = (byte)StuckCause.CarrotUnreachable, PlanStatus = 2,
            PlanAgeSec = 0.1, GoalDistanceM = 7.2, StartBlock = 1, StartClearanceM = 0.3, CommandSpeed = 0,
            Text = "UVÁZL 75 s — mrkev 7,2 m", TimeStamp = T0,
        };
        var buf = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buf, System.Text.Encoding.UTF8, leaveOpen: true)) m.ToData(bw);
        buf.Position = 0;
        var r = new StuckMsg();
        using (var br = new System.IO.BinaryReader(buf, System.Text.Encoding.UTF8, leaveOpen: true)) r.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(r.Level, Is.EqualTo(2));
            Assert.That(r.StandingSec, Is.EqualTo(75.5));
            Assert.That(r.EpisodeSec, Is.EqualTo(80.25));
            Assert.That(r.Cause, Is.EqualTo((byte)StuckCause.CarrotUnreachable));
            Assert.That(r.GoalDistanceM, Is.EqualTo(7.2));
            Assert.That(r.Text, Is.EqualTo("UVÁZL 75 s — mrkev 7,2 m"));
            Assert.That(r.TimeStamp, Is.EqualTo(T0));
            Assert.That(buf.Position, Is.EqualTo(buf.Length));
        });
    }

    [Test]
    public void Katalog_ZnaZpravu()
    {
        Assert.That(ARBot.Common.Communication.MessageCatalog.RecordDefaults().Contains(new StuckMsg().MsgName), Is.True);
    }
}
