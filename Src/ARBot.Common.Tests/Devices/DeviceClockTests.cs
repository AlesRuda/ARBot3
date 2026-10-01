using System;
using ARBot.Common.Devices;

namespace ARBot.Common.Tests.Devices;

/// <summary>
/// Prevod casu zarizeni na cas aplikace (<see cref="DeviceClock"/>, <c>lok-fuze-poza-pred-koly</c>).
/// Zarizeni vzorkuje pravidelne, ale vzorky prichazeji s jitterem linky (USB CDC v davkach) -
/// razitko i interval maji vyjit z hodin zarizeni, ne z prichodu.
/// </summary>
public class DeviceClockTests
{
    private static readonly DateTime T0 = new DateTime(2026, 10, 1, 12, 0, 0);
    private const long Mod = 1_000_000_000;

    /// <summary>
    /// Latence linky jako na robotu: radky chodi v davkach, takze razitka prichodu mela vzor
    /// 12 / 12 / 9 ms pri vzorkovani po 11 ms. Tady: zakladni latence 2 ms + 0..8 ms podle davky.
    /// </summary>
    private static double LatencyMs(int k) => 2.0 + (k % 3) * 4.0 + (k % 7 == 0 ? 0 : 1.0);

    /// <summary>
    /// REGRESE (lok-fuze-poza-pred-koly): interval mezi razitky je interval ZARIZENI, ne prichodu.
    /// Z casu prichodu by rychlost Δenkoder/Δrazitko kolisala o desitky procent a fuze, ktera
    /// merenie drzi dopredu, z toho nadsadila drahu o ~1,9 %.
    /// </summary>
    [Test]
    public void JitterLinky_IntervalJeIntervalZarizeni()
    {
        var c = new DeviceClock(Mod);
        DateTime? prev = null;
        for (int k = 0; k < 2000; k++)
        {
            long dev = 11L * k;
            var arrival = T0.AddMilliseconds(dev + LatencyMs(k));
            var r = c.Map(dev, arrival)!.Value;

            Assert.That(r.Time, Is.LessThanOrEqualTo(arrival), "razitko nesmi byt pozdeji nez prichod");
            if (k > 0)
            {
                Assert.That(r.DeltaMs, Is.EqualTo(11), $"k={k}");
                double dt = (r.Time - prev!.Value).TotalMilliseconds;
                Assert.That(dt, Is.EqualTo(11.0).Within(0.2), $"interval razitek k={k} (ne 9 / 12 ms jako z prichodu)");
            }
            else Assert.That(r.DeltaMs, Is.Null, "prvni vzorek interval nema");
            prev = r.Time;
        }
        Assert.That(c.Syncs, Is.EqualTo(1));
    }

    /// <summary>Po usazeni nese razitko jen MINIMALNI latenci linky (konstantu), ne jeji jitter.</summary>
    [Test]
    public void PoUsazeni_RazitkoNeseMinimalniLatenci()
    {
        var c = new DeviceClock(Mod);
        double maxErr = 0;
        for (int k = 0; k < 2000; k++)
        {
            long dev = 11L * k;
            var r = c.Map(dev, T0.AddMilliseconds(dev + LatencyMs(k)))!.Value;
            if (k > 100) maxErr = Math.Max(maxErr, Math.Abs((r.Time - T0.AddMilliseconds(dev)).TotalMilliseconds - 2.0));
        }
        Assert.That(maxErr, Is.LessThan(0.2), "odchylka od minimalni latence 2 ms (stoupani mezi minimy)");
    }

    /// <summary>
    /// Krystal zarizeni ujizdi (+-100 ppm). Ciste minimum by na pomalejsi hodiny nedosahlo,
    /// proto smi posun stoupat (MaxDriftPpm); chyba razitka ma zustat omezena.
    /// </summary>
    [TestCase(100.0)]
    [TestCase(-100.0)]
    public void DriftKrystalu_ChybaZustaneOmezena(double ppm)
    {
        var c = new DeviceClock(Mod);
        double maxErr = 0;
        for (int k = 0; k < 60_000; k++)            // ~11 minut
        {
            long dev = 11L * k;
            double hostMs = dev * (1 + ppm * 1e-6);  // skutecny cas vzorku v hodinach aplikace
            var arrival = T0.AddMilliseconds(hostMs + LatencyMs(k));
            var r = c.Map(dev, arrival)!.Value;
            Assert.That(r.Time, Is.LessThanOrEqualTo(arrival));
            if (k > 100) maxErr = Math.Max(maxErr, Math.Abs((r.Time - T0.AddMilliseconds(hostMs)).TotalMilliseconds - 2.0));
        }
        Assert.That(maxErr, Is.LessThan(1.0), $"drift {ppm} ppm: chyba razitka [ms]");
        Assert.That(c.Syncs, Is.EqualTo(1));
    }

    /// <summary>Pretoceni citace pres modulo neni restart: interval pokracuje.</summary>
    [Test]
    public void PretoceniPresModulo_NeniResync()
    {
        var c = new DeviceClock(Mod);
        c.Map(Mod - 5, T0);
        var r = c.Map(6, T0.AddMilliseconds(11))!.Value;

        Assert.That(r.DeltaMs, Is.EqualTo(11));
        Assert.That(c.Syncs, Is.EqualTo(1));
    }

    /// <summary>Restart jednotky vynuluje citac: cas zarizeni poskoci "dopredu" o dny -> resync.</summary>
    [Test]
    public void RestartZarizeni_Resynchronizuje()
    {
        var c = new DeviceClock(Mod);
        for (int k = 0; k < 100; k++) c.Map(300_000 + 11L * k, T0.AddMilliseconds(11 * k + 2));

        var arrival = T0.AddMilliseconds(5000);
        var r = c.Map(11, arrival)!.Value;

        Assert.That(c.Syncs, Is.EqualTo(2));
        Assert.That(r.DeltaMs, Is.Null, "po restartu interval neni znamy");
        Assert.That(r.Time, Is.EqualTo(arrival));
    }

    /// <summary>
    /// Aplikace chvili necte (GC, pretizeni) a pak dohani davku: latence je vysoka jen po dobu
    /// cteni davky. To restart neni - intervaly zarizeni plati dal.
    /// </summary>
    [Test]
    public void ZahlceniLinky_NeniResync()
    {
        var c = new DeviceClock(Mod);
        int k = 0;
        for (; k < 100; k++) c.Map(11L * k, T0.AddMilliseconds(11 * k + 2));

        // 1,5 s nic, pak 137 vzorku prijde behem 20 ms.
        double burstStart = 11 * k + 1500;
        for (int j = 0; j < 137; j++, k++)
        {
            var r = c.Map(11L * k, T0.AddMilliseconds(burstStart + j * 0.15))!.Value;
            Assert.That(r.DeltaMs, Is.EqualTo(11));
        }
        // Po davce: backlog vycisten, radky chodi s beznou latenci (tady o neco vyssi nez minimum).
        for (int j = 0; j < 100; j++, k++)
            Assert.That(c.Map(11L * k, T0.AddMilliseconds(11 * k + 30))!.Value.DeltaMs, Is.EqualTo(11));

        Assert.That(c.Syncs, Is.EqualTo(1));
    }

    /// <summary>
    /// Restart kratce po startu (citac po restartu je VYS nez pred nim, takze to nevypada jako
    /// skok dopredu): latence zustane vysoka -> resync po <see cref="DeviceClock.ResyncPersistMs"/>.
    /// </summary>
    [Test]
    public void TrvaleVysokaLatence_ResynchronizujePoLimitu()
    {
        var c = new DeviceClock(Mod);
        for (int k = 0; k < 10; k++) c.Map(11L * k, T0.AddMilliseconds(11 * k + 2));

        // Jednotka se restartovala v 0,1 s a po 5 s (host) hlasi citac zase od ~200 ms.
        int syncsAt = -1;
        for (int k = 0; k < 400 && syncsAt < 0; k++)
        {
            c.Map(200 + 11L * k, T0.AddMilliseconds(5000 + 11 * k + 2));
            if (c.Syncs == 2) syncsAt = k;
        }
        Assert.That(syncsAt, Is.GreaterThan(0), "resync musi nastat");
        Assert.That(11.0 * syncsAt, Is.GreaterThan(c.ResyncPersistMs - 20).And.LessThan(c.ResyncPersistMs + 50));
    }

    /// <summary>Citac mimo rozsah je nesmysl z linky: vysledek null a stav se nemeni.</summary>
    [Test]
    public void CitacMimoRozsah_Null()
    {
        var c = new DeviceClock(Mod);
        c.Map(100, T0);

        Assert.That(c.Map(-1, T0.AddMilliseconds(11)), Is.Null);
        Assert.That(c.Map(Mod, T0.AddMilliseconds(11)), Is.Null);
        Assert.That(c.Map(111, T0.AddMilliseconds(11))!.Value.DeltaMs, Is.EqualTo(11), "stav zustal");
    }
}
