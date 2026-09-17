using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Missions;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// <b>Mise nesmi publikovat zpravu s drzenym vlastnim zamkem.</b>
///
/// <para><b>Proc to hlida test.</b> 17. 9. 2026 runtime na robotu zatuhl pri volbe mise `track`
/// ze stranky. Z minidumpu (<c>HangWatchdog</c>) vyslo uzavrene poradi zamku:
/// vlakno mise drzelo zamek mise (<c>StartMission</c> → <c>EnterPhase</c> → <c>EmitState</c> →
/// <c>EmitDerived</c> → fan-out → <c>WebStatus.Post</c>) a cekalo na zamek stranky, zatimco
/// vlakno stranky drzelo zamek stranky (<c>ToJson</c> → <c>AppendHead</c>) a cekalo na zamek
/// mise (<c>PhaseText</c>). Spoustecem byl uplne bezny provoz — stranka se sama obnovuje
/// a mise se z ni vybira, takze oboji prislo naraz.</para>
///
/// <para><b>Jak se to meri.</b> Odberatel pripojeny na <c>Output</c> dostane zpravu na vlakne
/// producenta; v tu chvili si z JINEHO vlakna sahne na vlastnost mise, ktera bere zamek mise.
/// Kdyby mise zamek jeste drzela, to cteni se do limitu nestihne — a presne to je ta vada.
/// Test se pritom <b>nezasekne</b>: cekani ma limit a druhe vlakno se po uvolneni zamku
/// dokonci samo.</para>
///
/// <para>Pravidlo je obecne a psane uz v <see cref="RelaySource"/>: fan-out bezi na vlakne
/// producenta, takze se do nej nesmi vstupovat s drzenym zamkem. Viz doc/headless.md.</para>
/// </summary>
    /// <summary>
/// Odberatel, ktery pri kazde zprave zkusi z ciziho vlakna precist vlastnost mise.
/// <see cref="ZamekDrzen"/> = cteni se nestihlo, tedy producent zamek jeste drzel.
/// </summary>
internal sealed class CteNaVlakneNavic : IMessageSink
{
    /// <summary>Limit pro cteni vlastnosti z ciziho vlakna. Pri vade se ho nedosahne.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private readonly Func<string> cti;
    public int Zprav { get; private set; }
    public bool ZamekDrzen { get; private set; }

    public CteNaVlakneNavic(Func<string> cti) => this.cti = cti;

    public void Post(Message msg)
    {
        Zprav++;

        // ⚠️ VLASTNI VLAKNO, ne Task.Run: pri behu cele sady je thread pool vytizeny a uloha by
        // se nemusela rozbehnout vcas — test by pak hlasil "zamek drzen" tam, kde jen cekala
        // na vlakno. Presne tak se to jednou stalo (jeden beh spadl, druhy prosel), a flaky
        // test je horsi nez zadny. Vlastni vlakno na pool nesaha.
        var vlakno = new Thread(() => { try { cti(); } catch { /* cteni nesmi shodit test */ } })
        {
            IsBackground = true,
            Name = "CteNaVlakneNavic",
        };
        vlakno.Start();
        if (!vlakno.Join(Limit)) ZamekDrzen = true;
    }
}

public class MisePublikujeMimoZamekTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 17, 16, 21, 0, DateTimeKind.Utc);

    // ---------------- Falesne okoli (minimum, at je videt jen to mereni) ----------------

    private sealed class FakeGoals : IGlobalGoalSink
    {
        public List<LLA> Goals { get; } = new List<LLA>();
        public void SetGoal(LLA target) => Goals.Add(target);
        public void Cancel() { }
    }

    private static MotorStateBase Motory(bool stop)
        => new MotorStateBase(stop, 0, 0, 24, 0, 0, 0, 0);

    // ---------------- Testy ----------------

    [Test]
    public void TrackMission_PriPublikaciNedrziSvujZamek()
    {
        var mise = new TrackMission(new FakeGoals(),
                                    TrackPlan.Parse(new[] { "50.0337431,14.5257403" }),
                                    routes: null);
        var odber = new CteNaVlakneNavic(() => mise.PhaseText);
        mise.Output.Connect(odber);

        // Prechody faz = prave ta cesta, na ktere to na robotu zatuhlo.
        mise.StartMission(T0);
        mise.OnMotors(Motory(stop: true), T0.AddSeconds(1));
        mise.OnMotors(Motory(stop: false), T0.AddSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(odber.Zprav, Is.GreaterThan(0),
                        "test nic nezmeril - mise neposlala zadnou zpravu");
            Assert.That(odber.ZamekDrzen, Is.False,
                        "mise publikovala zpravu s DRZENYM vlastnim zamkem - to je to poradi "
                        + "zamku, kterym 17. 9. 2026 zatuhl runtime pri volbe mise");
        });
    }

    [Test]
    public void TrackMission_PriPreruseniZevnitrNedrziSvujZamek()
    {
        // Abort() je verejny, ale vola se i ZEVNITR OnGlobalNav, ktery uz zamek drzi. Prave
        // takove vnorene volani nesmi publikovat samo - musi to nechat vnejsimu rozsahu.
        var mise = new TrackMission(new FakeGoals(),
                                    TrackPlan.Parse(new[] { "50.0337431,14.5257403" }),
                                    routes: null);
        var odber = new CteNaVlakneNavic(() => mise.PhaseText);
        mise.Output.Connect(odber);

        mise.StartMission(T0);
        mise.OnMotors(Motory(stop: true), T0.AddSeconds(1));
        mise.OnMotors(Motory(stop: false), T0.AddSeconds(2));
        mise.OnGlobalNav(new GlobalNavMsg
        {
            Status = (int)GlobalNavStatus.NoRoute,
            TimeStamp = T0.AddSeconds(3),
        });

        Assert.That(odber.ZamekDrzen, Is.False,
                    "preruseni mise zevnitr OnGlobalNav publikovalo s drzenym zamkem");
    }

}
