using System;
using System.Threading;
using ARBot.Common.Devices;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Devices;

/// <summary>
/// Zivotni cyklus senzoru: <see cref="SensorBase{TState}.Start"/> / <c>Stop</c> a to, ze
/// <see cref="SensorBase{TState}.GetLastMeasurement"/> senzor <b>nespousti</b>.
///
/// <para><b>Proc to vzniklo</b> (21. 8. 2026): <c>GetLastMeasurement()</c> si senzor sam spustil,
/// takze zastaveny senzor se do jednoho tiku sam rozjel — kdokoli si vyzvedl mereni (pull kamer
/// v runtime, detailni okno v UI), tim ho zapnul. Zastavit senzor tedy neslo vubec, a lifecycle
/// side effect v getteru byl navic skryty. Viz Src/ARBot/ViewModels/SensorStatusTool.cs.</para>
/// </summary>
public class SensorLifecycleTests
{
    /// <summary>Zkusebni senzor: kazde "mereni" je jen pocitadlo, smycka nic neblokuje.</summary>
    private sealed class CountingSensor : SensorBase<MotorStateBase>
    {
        public override string Name => "Counting";
        public int Measurements;

        protected override MotorStateBase GetMeasurement()
        {
            Interlocked.Increment(ref Measurements);
            Thread.Sleep(5);
            return new MotorStateBase(false, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    private static bool WaitUntil(Func<bool> cond, int ms = 2000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            if (cond()) return true;
            Thread.Sleep(10);
        }
        return cond();
    }

    [Test]
    public void NovySenzorNebezi()
    {
        using var s = new CountingSensor();

        Assert.That(s.IsRunning, Is.False);
        Assert.That(s.Measurements, Is.Zero);
    }

    [Test]
    public void GetLastMeasurement_senzorNESPOUSTI()
    {
        using var s = new CountingSensor();

        var m = s.GetLastMeasurement();

        Assert.That(m, Is.Null, "nic nebezi -> neni co vyzvednout");
        Assert.That(s.IsRunning, Is.False, "vyzvednuti mereni nesmi senzor rozjet");
        Thread.Sleep(50);
        Assert.That(s.Measurements, Is.Zero, "a nesmi se ani nic zmerit");
    }

    [Test]
    public void PoStartSeMeri_aGetLastMeasurementUzDavaData()
    {
        using var s = new CountingSensor();

        s.Start();

        Assert.That(s.IsRunning, Is.True);
        Assert.That(WaitUntil(() => s.GetLastMeasurement() != null), Is.True, "po Start ma merit");
    }

    [Test]
    public void PoStopSeMereniZastavi_aNerozjedeHoVyzvednuti()
    {
        using var s = new CountingSensor();
        s.Start();
        Assert.That(WaitUntil(() => s.Measurements > 0), Is.True);

        s.Stop();
        int afterStop = s.Measurements;

        Assert.That(s.IsRunning, Is.False);
        s.GetLastMeasurement();                  // driv prave tohle senzor znovu rozjelo
        Thread.Sleep(80);
        Assert.That(s.IsRunning, Is.False, "zastaveny senzor musi zustat zastaveny");
        Assert.That(s.Measurements, Is.EqualTo(afterStop), "a nesmi po Stop merit dal");
    }

    [Test]
    public void ZastavenySenzorLzeZnovuSpustit()
    {
        using var s = new CountingSensor();
        s.Start();
        Assert.That(WaitUntil(() => s.Measurements > 0), Is.True);
        s.Stop();
        int afterStop = s.Measurements;

        s.Start();

        Assert.That(s.IsRunning, Is.True);
        Assert.That(WaitUntil(() => s.Measurements > afterStop), Is.True, "po znovuspusteni ma merit dal");
    }

    [Test]
    public void SenzorJeOvladatelnyPresRozhrani()
    {
        // Panel senzoru drzi ISensor; ovladani je zamerne v samostatnem rozhrani, protoze
        // MD23 ani DummyMotors zadnou smycku nemaji a no-op Start/Stop by u nich lhaly.
        using var s = new CountingSensor();

        Assert.That(s, Is.InstanceOf<IControllableSensor>());
        Assert.That(new DummyMotors(), Is.Not.InstanceOf<IControllableSensor>(),
                    "fiktivni motory nemaji co spustit");

        var ctl = (IControllableSensor)s;
        ctl.Start();
        Assert.That(ctl.IsRunning, Is.True);
        ctl.Stop();
        Assert.That(ctl.IsRunning, Is.False);
    }
}
