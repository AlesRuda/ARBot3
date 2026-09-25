using System;
using System.Threading;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests;

/// <summary>
/// <see cref="NativeCallWatch"/> — hlídač nativních volání RealSense.
///
/// <para><b>Proč to vzniklo:</b> v <c>20260923-143515.rec</c> levá D435 po restartu pipeline
/// (zamrzlá barva) zatuhla a už nikdy nic nenahlásila; supervizor zotavení nezasáhl, protože
/// kamera o nic nežádala. Hlídač má příště říct, ve kterém volání vlákno visí.</para>
///
/// <para>Stav hlídače je statický (nastavuje ho runtime), proto ho testy nastavují a vracejí
/// a běží v jednom vlákně (<c>NonParallelizable</c>).</para>
/// </summary>
[NonParallelizable]
public class NativeCallWatchTests
{
    private double puvodniLimit;
    private Action<string> puvodniHacek;

    [SetUp]
    public void Uloz()
    {
        puvodniLimit = NativeCallWatch.LimitSec;
        puvodniHacek = NativeCallWatch.OnHang;
    }

    [TearDown]
    public void Vrat()
    {
        NativeCallWatch.LimitSec = puvodniLimit;
        NativeCallWatch.OnHang = puvodniHacek;
    }

    [Test]
    public void VolaniCoNedobehne_vystreliJednou()
    {
        NativeCallWatch.LimitSec = 0.05;
        string co = null;
        int kolikrat = 0;
        using var hotovo = new ManualResetEventSlim(false);
        NativeCallWatch.OnHang = x => { co = x; Interlocked.Increment(ref kolikrat); hotovo.Set(); };

        using (NativeCallWatch.Guard("Left: pipeline.Stop"))
        {
            Assert.That(hotovo.Wait(TimeSpan.FromSeconds(5)), Is.True, "hlidac musi vystrelit");
            Thread.Sleep(150);   // jednorazovy: dalsi limit uz nic nepridá
        }

        Assert.That(co, Is.EqualTo("Left: pipeline.Stop"));
        Assert.That(kolikrat, Is.EqualTo(1));
    }

    [Test]
    public void VolaniCoDobehne_nevystreli()
    {
        NativeCallWatch.LimitSec = 0.2;
        int kolikrat = 0;
        NativeCallWatch.OnHang = _ => Interlocked.Increment(ref kolikrat);

        using (NativeCallWatch.Guard("Left: pipeline.Start")) { }
        Thread.Sleep(400);

        Assert.That(kolikrat, Is.Zero);
    }

    [Test]
    public void NulovyLimit_hlidacVypne()
    {
        NativeCallWatch.LimitSec = 0;
        int kolikrat = 0;
        NativeCallWatch.OnHang = _ => Interlocked.Increment(ref kolikrat);

        using (NativeCallWatch.Guard("Left: QueryDevices")) Thread.Sleep(100);

        Assert.That(kolikrat, Is.Zero);
    }

    [Test]
    public void VyjimkaVHacku_neshodiProces()
    {
        NativeCallWatch.LimitSec = 0.05;
        using var hotovo = new ManualResetEventSlim(false);
        NativeCallWatch.OnHang = _ => { hotovo.Set(); throw new InvalidOperationException("dump selhal"); };

        using (NativeCallWatch.Guard("Right: pipeline.Dispose"))
            Assert.That(hotovo.Wait(TimeSpan.FromSeconds(5)), Is.True);
        // Proces zije a test dobehl - vyjimka z casovace by jinak shodila testhost.
    }
}
