using System;
using ARBot.HAL.Devices.GPSs.uBlox;

namespace ARBot.HAL.Tests;

/// <summary>
/// <see cref="uBloxGps.FixTimeFrom"/>: UTC cas dne fixu z UTC poli UBX-NAV-PVT.
///
/// <para><b>Proc to ma test:</b> do 27. 9. 2026 se <c>FixTime</c> skladal z ITOW a skladal se
/// spatne (i „9 dni"); nic to nekrylo. Viz <c>GpsFixTimeTests</c> v ARBot.Common.Tests pro cteni
/// starsich zaznamu.</para>
/// </summary>
public class UBloxFixTimeTests
{
    private const byte Platny = uBloxGps.ValidTime | 0x01 | 0x04;   // validDate | validTime | fullyResolved

    [Test]
    public void PlatnyCas_JeUtcCasDne()
    {
        var t = uBloxGps.FixTimeFrom(12, 24, 29, 100_000_000, Platny);
        Assert.That(t, Is.EqualTo(new TimeSpan(0, 12, 24, 29, 100)));
    }

    [Test]
    public void BezValidTime_JeNeznamy()
    {
        // Pred prvnim fixem posila prijimac v UTC polich nesmysl.
        Assert.That(uBloxGps.FixTimeFrom(12, 24, 29, 0, 0x01), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ZapornaNanosekunda_OpravujeDolu()
    {
        // nano je znamenkova oprava: 12:24:30 − 0,2 s = 12:24:29,8.
        var t = uBloxGps.FixTimeFrom(12, 24, 30, -200_000_000, Platny);
        Assert.That(t, Is.EqualTo(new TimeSpan(0, 12, 24, 29, 800)));
    }

    [Test]
    public void ZapornaNanosekundaOPulnoci_SeZabaliDoDne()
    {
        var t = uBloxGps.FixTimeFrom(0, 0, 0, -1_000_000, Platny);
        Assert.That(t, Is.EqualTo(TimeSpan.FromDays(1) - TimeSpan.FromMilliseconds(1)));
    }

    [Test]
    public void PresnePulnoc_NeniNeznamy()
    {
        // Zero znamena „cas neznamy" - pulnoc se od nej musi lisit.
        Assert.That(uBloxGps.FixTimeFrom(0, 0, 0, 0, Platny), Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public void PvtZprava_CteUtcPoleZeSpravnychOffsetu()
    {
        // Payload UBX-NAV-PVT (92 B): iTOW 0, year 4, month 6, day 7, hour 8, min 9, sec 10,
        // valid 11, tAcc 12, nano 16 (ICD). Year se do 27. 9. 2026 cetl z offsetu 2.
        var p = new byte[92];
        BitConverter.GetBytes(518_687_000u).CopyTo(p, 0);
        BitConverter.GetBytes((ushort)2026).CopyTo(p, 4);
        p[6] = 9; p[7] = 25; p[8] = 12; p[9] = 24; p[10] = 29; p[11] = Platny;
        BitConverter.GetBytes(-5_000_000).CopyTo(p, 16);
        var pvt = new PVTMessage(p);

        Assert.Multiple(() =>
        {
            Assert.That(pvt.Year, Is.EqualTo(2026));
            Assert.That(uBloxGps.FixTimeFrom(pvt.Hour, pvt.Min, pvt.Sec, pvt.NanoSec, pvt.Valid),
                Is.EqualTo(new TimeSpan(0, 12, 24, 28, 995)));
        });
    }
}
