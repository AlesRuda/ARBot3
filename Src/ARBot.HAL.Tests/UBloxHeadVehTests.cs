using System;
using ARBot.HAL.Devices.GPSs.uBlox;

namespace ARBot.HAL.Tests;

/// <summary>
/// Kurz VOZIDLA z UBX-NAV-PVT (<see cref="PVTMessage.HeadVeh"/>, <see cref="uBloxGps.VehicleHeadingFrom"/>).
///
/// <para><b>Proc to ma test:</b> do 10. 10. 2026 se <c>HeadVeh</c> cetl z offsetu 64, tedy z kurzu
/// POHYBU (<c>headMot</c>), ne z <c>headVeh</c> na offsetu 84. Spici vada (prijimac ten kurz zatim
/// nehlasi), ale <c>GPSState.Orientation</c> ma ve fuzi prednost a obchazi vylouceni jizdy vzad.</para>
/// </summary>
public class UBloxHeadVehTests
{
    private const byte HeadVehValid = 32;   // flags bit 5

    private static byte[] Pvt(int delka, byte flags, double headMotDeg, double headVehDeg)
    {
        var p = new byte[delka];
        p[21] = flags;
        BitConverter.GetBytes((int)Math.Round(headMotDeg * 1e5)).CopyTo(p, 64);
        if (delka >= PVTMessage.HeadVehOffset + 4)
            BitConverter.GetBytes((int)Math.Round(headVehDeg * 1e5)).CopyTo(p, PVTMessage.HeadVehOffset);
        return p;
    }

    [Test]
    public void KurzVozidla_SeCteZOffsetu84_NeZKurzuPohybu()
    {
        var pvt = new PVTMessage(Pvt(92, HeadVehValid, headMotDeg: 45, headVehDeg: 120));

        Assert.Multiple(() =>
        {
            Assert.That(pvt.HeadMot, Is.EqualTo(45).Within(1e-6));
            Assert.That(pvt.HeadVeh, Is.EqualTo(120).Within(1e-6), "kurz vozidla, ne kurz pohybu");
            Assert.That(pvt.HeadVehValid, Is.True);
        });
    }

    [Test]
    public void KurzVozidla_JdeDoGpsStateJakoMatematickaOrientace()
    {
        // Azimut 120° (od severu po smeru hodin) = matematicky 90 − 120 = −30°.
        var pvt = new PVTMessage(Pvt(92, HeadVehValid, headMotDeg: 45, headVehDeg: 120));
        Assert.That(uBloxGps.VehicleHeadingFrom(pvt), Is.EqualTo(-Math.PI / 6).Within(1e-9));
    }

    [Test]
    public void BezPriznaku_KurzVozidlaNeni()
    {
        var pvt = new PVTMessage(Pvt(92, 0, headMotDeg: 45, headVehDeg: 120));
        Assert.Multiple(() =>
        {
            Assert.That(pvt.HeadVehValid, Is.False);
            Assert.That(uBloxGps.VehicleHeadingFrom(pvt), Is.Null);
        });
    }

    [Test]
    public void KratkaZprava_BezPoleHeadVeh_NeniPlatna()
    {
        // Starsi protokol posilal NAV-PVT bez headVeh (84 B) - priznak sam nestaci a cist se nesmi.
        var pvt = new PVTMessage(Pvt(84, HeadVehValid, headMotDeg: 45, headVehDeg: 0));
        Assert.Multiple(() =>
        {
            Assert.That(pvt.HeadVehValid, Is.False);
            Assert.That(double.IsNaN(pvt.HeadVeh), Is.True);
            Assert.That(uBloxGps.VehicleHeadingFrom(pvt), Is.Null);
        });
    }
}
