using ARBot.Common.Maps.OsmNav.Navigation;

namespace ARBot.Common.Tests.OsmNav.Navigation;

/// <summary>
/// Testy klouzaveho okna postupu (detektor B - "bloudim").
/// Okno bezi proti UJETE DRAZE, ne proti casu: kdyz robot stoji, okno se neposouva
/// a nic se nevyhodnocuje. Viz doc/global-navigation-runtime.md.
/// </summary>
public class ProgressWindowTests
{
    /// <summary>Okno 20 m, pozadovany pokles potencialu 6 s na oknu.</summary>
    private static ProgressWindow New(double windowM = 20.0) => new ProgressWindow(windowM);

    [Test]
    public void BeforeWindowIsFull_ProgressIsNotEvaluated()
    {
        var w = New();
        w.Add(travelledM: 0, phi: 100);
        w.Add(travelledM: 5, phi: 95);

        Assert.That(w.TryGetDrop(out _), Is.False, "s kratkym oknem se jeste nesmi soudit");
    }

    [Test]
    public void SteadyApproach_ReportsPhiDrop()
    {
        var w = New();
        w.Add(travelledM: 0, phi: 100);
        w.Add(travelledM: 25, phi: 70);      // za 25 m klesl potencial o 30 s

        Assert.That(w.TryGetDrop(out double drop), Is.True);
        Assert.That(drop, Is.GreaterThan(0), "priblizeni k cili = kladny pokles");
    }

    [Test]
    public void DrivingInCircles_ReportsNoDrop()
    {
        var w = New();
        w.Add(travelledM: 0, phi: 100);
        w.Add(travelledM: 10, phi: 104);
        w.Add(travelledM: 25, phi: 100);     // ujel 25 m a je stejne daleko

        Assert.That(w.TryGetDrop(out double drop), Is.True);
        Assert.That(drop, Is.LessThanOrEqualTo(0.001), "jizda dokola nesmi vypadat jako postup");
    }

    [Test]
    public void StandingStill_DoesNotShiftTheWindow()
    {
        var w = New();
        w.Add(travelledM: 0, phi: 100);
        w.Add(travelledM: 0, phi: 100);
        w.Add(travelledM: 0, phi: 100);

        Assert.That(w.TryGetDrop(out _), Is.False, "bez ujete drahy neni co vyhodnocovat");
    }

    /// <summary>
    /// Okno musi drzet jen posledni usek jizdy, ne celou historii. Vzorky chodi hustě
    /// (kazdych ~200 ms), takze se testuje realisticky po metru.
    /// </summary>
    [Test]
    public void OldSamplesFallOutOfTheWindow()
    {
        var w = New(windowM: 10.0);

        // 30 m jizdy, potencial klesa o 1 s na metr.
        for (int m = 0; m <= 30; m++)
            w.Add(travelledM: m, phi: 100 - m);

        Assert.That(w.TryGetDrop(out double drop), Is.True);
        Assert.That(drop, Is.EqualTo(10).Within(1.5),
                    "pokles se ma merit pres okno (10 m), ne pres celou historii (30 m)");
    }
}
