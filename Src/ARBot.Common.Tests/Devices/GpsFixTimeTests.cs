using System;
using ARBot.Common.Devices;

namespace ARBot.Common.Tests.Devices;

/// <summary>
/// Testy vyznamu <see cref="GPSState.FixTime"/> napric verzemi zaznamu (<see cref="GPSState.UtcTimeOfDay"/>).
///
/// <para><b>Proc to ma test:</b> ovladac u-bloxu do 27. 9. 2026 skladal do <c>FixTime</c> ITOW
/// (cas v GPS tydnu) a skladal ho spatne — sekundy odecital s hodinami *60 misto *3600, takze
/// vychazelo i „9 dni". Panel GPS ukazoval nesmysl a export GPX z toho spocital posun proti UTC
/// −07:45 misto +02:00 (i vyexportovany <c>Kolo3b.gpx</c> mel cas 21:40Z u odpoledniho kola).
/// Od verze 3 je <c>FixTime</c> UTC cas dne; starsi zaznamy se prepsat nedaji, takze se musi dat
/// spravne precist.</para>
/// </summary>
public class GpsFixTimeTests
{
    /// <summary>Puvodni (rozbity) rozklad ITOW z <c>uBloxGps</c>, doslova.</summary>
    private static TimeSpan RozbityRozklad(uint itow)
    {
        int d = (int)itow / (1000 * 60 * 60 * 24);
        int h = (int)itow / (1000 * 60 * 60) - d * 24;
        int m = (int)itow / (1000 * 60) - (d * 24 + h) * 60;
        int s = (int)itow / 1000 - ((d * 24 + h) * 60 + m * 60);
        int ms = (int)itow - (((d * 24 + h) * 60 + m * 60) + s) * 1000;
        return new TimeSpan(d, h, m, s, ms);
    }

    [Test]
    public void RozbityRozklad_SeInvertujeProKazdyDenAHodinu()
    {
        for (int den = 0; den < 7; den++)
            for (int hod = 0; hod < 24; hod++)
                foreach (int zbytekMs in new[] { 0, 1, 59_999, 1_234_567, 3_599_999 })
                {
                    uint itow = (uint)(den * 86_400_000L + hod * 3_600_000L + zbytekMs);
                    var fix = RozbityRozklad(itow);
                    if (fix < TimeSpan.FromDays(1)) continue;   // tam se nevola, viz UtcTimeOfDay
                    Assert.That(GPSState.ItowFromBrokenUblox(fix), Is.EqualTo(itow),
                        $"den {den}, hodina {hod}, zbytek {zbytekMs} ms");
                }
    }

    [Test]
    public void Verze2_RozbityUblox_DaUtcCasDne()
    {
        // Patek 25. 9. 2026 12:24:29 UTC = GPS 12:24:47 (den v tydnu 5).
        uint itow = (uint)(5 * 86_400_000L + (12 * 3600 + 24 * 60 + 47) * 1000L);
        var g = new GPSState { Verze = 2, FixTime = RozbityRozklad(itow) };

        Assert.That(g.FixTime.Days, Is.GreaterThan(6), "predpoklad testu: ulozena hodnota je nesmysl");
        Assert.That(g.UtcTimeOfDay(), Is.EqualTo(new TimeSpan(12, 24, 29)));
    }

    [Test]
    public void Verze2_CasDnePodJednimDnem_SeBereJakNeni()
    {
        // NMEA (GGA) dava UTC cas dne uz odjakziva.
        var g = new GPSState { Verze = 2, FixTime = new TimeSpan(0, 14, 5, 7, 250) };
        Assert.That(g.UtcTimeOfDay(), Is.EqualTo(new TimeSpan(0, 14, 5, 7, 250)));
    }

    [Test]
    public void Verze3_FixTimeJeUtcCasDne()
    {
        var g = new GPSState { FixTime = new TimeSpan(0, 12, 24, 29, 100) };
        Assert.That(g.Verze, Is.EqualTo(GPSState.FormatVersion));
        Assert.That(GPSState.FormatVersion, Is.GreaterThanOrEqualTo(3));
        Assert.That(g.UtcTimeOfDay(), Is.EqualTo(new TimeSpan(0, 12, 24, 29, 100)));
    }

    [Test]
    public void BezCasu_JeNull()
    {
        Assert.That(new GPSState { FixTime = TimeSpan.Zero }.UtcTimeOfDay(), Is.Null);
        Assert.That(new GPSState { Verze = 2, FixTime = TimeSpan.Zero }.UtcTimeOfDay(), Is.Null);
    }

    [Test]
    public void Verze2_GpsCasTesneZaPulnoci_SeZabaliDoPredchozihoDneUtc()
    {
        // Pondeli GPS 00:00:10 = nedele 23:59:52 UTC.
        uint itow = (uint)(1 * 86_400_000L + 10_000);
        var g = new GPSState { Verze = 2, FixTime = RozbityRozklad(itow) };
        Assert.That(g.UtcTimeOfDay(), Is.EqualTo(new TimeSpan(23, 59, 52)));
    }
}
