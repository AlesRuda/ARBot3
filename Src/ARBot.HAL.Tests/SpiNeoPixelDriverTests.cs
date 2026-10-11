using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;
using ARBot.HAL.Devices.NeoPixel;

namespace ARBot.HAL.Tests;

/// <summary>
/// Kodovani WS2812 do SPI sub-bitu (<see cref="SpiNeoPixelDriver"/>) s casovanim, se kterym ho
/// zaklada <c>ARBotHW</c> na Orange Pi (<see cref="SpiNeoPixelDriver.PulseConfig.OrangePi"/>:
/// 3,2 MHz, nula 1+3, jednicka 2+2 sub-bity, dva bity WS2812 v jednom bajtu).
///
/// <para><b>Proc to ma test:</b> na pasku se chyba projevi jen jako „jine barvy" nebo tma, a to
/// nejde odlisit od spatneho zapojeni. Poradi GRB a MSB-first je presne to, co se da splest.</para>
/// </summary>
public class SpiNeoPixelDriverTests
{
    private sealed class Zachyt : SpiNeoPixelDriver
    {
        public List<byte> Data = new();
        public Zachyt() : base(PulseConfig.OrangePi) { }
        protected override void WriteData(List<byte> values) => Data = values.ToList();
    }

    // Pulbajt = jeden bit WS2812: nula 1 sub-bit vysoko a 3 nizko, jednicka 2 a 2.
    private const byte NulaNula = 0b1000_1000;
    private const byte JednickaNula = 0b1100_1000;
    private const byte NulaJednicka = 0b1000_1100;
    private const byte JednickaJednicka = 0b1100_1100;

    private static readonly int Uvod = SpiNeoPixelDriver.PulseConfig.OrangePi.LeadingZeroBytes;

    [Test]
    public void JednaLed_PoradiGRB_MsbFirst()
    {
        var d = new Zachyt();
        d.Send(new[] { new Color(0x01, 0x80, 0x00) });   // R=0x01, G=0x80, B=0
        var led = d.Data.Skip(Uvod).ToList();

        Assert.That(led, Has.Count.EqualTo(12), "4 sub-bity na bit = 2 bity WS2812 v bajtu");
        // G prvni a MSB prvni: 0x80 -> 1 a sedm nul, tedy horni pulbajt prvniho bajtu.
        Assert.That(led[0], Is.EqualTo(JednickaNula));
        Assert.That(led.Skip(1).Take(3), Is.All.EqualTo(NulaNula));
        // R = 0x01 -> sedm nul a jednicka na konci, tedy dolni pulbajt posledniho bajtu R.
        Assert.That(led.Skip(4).Take(3), Is.All.EqualTo(NulaNula));
        Assert.That(led[7], Is.EqualTo(NulaJednicka));
        Assert.That(led.Skip(8), Is.All.EqualTo(NulaNula));
    }

    /// <summary>
    /// Data nesmi zacinat prvnim bajtem prenosu: bez uvodnich nul prvni LED na robotu obcas
    /// „zustala cervena" (ztraceny prvni bit, 11. 10. 2026). Uvod musi byt i resetem WS2812
    /// (&gt; 280 us), i kdyby linka mezi prenosy nestala v nule.
    /// </summary>
    [Test]
    public void PredDaty_NulovyUvod_DelsiNezResetWs2812()
    {
        var d = new Zachyt();
        d.Send(new[] { new Color(0xff, 0xff, 0xff) });

        Assert.That(Uvod, Is.GreaterThan(0));
        Assert.That(d.Data.Take(Uvod), Is.All.EqualTo((byte)0));
        Assert.That(d.Data[Uvod], Is.EqualTo(JednickaJednicka), "hned za uvodem zacinaji data");
        double uvodUs = Uvod * 8 * 1e6 / SpiNeoPixelDriver.OrangePiClockHz;
        Assert.That(uvodUs, Is.GreaterThan(280), "reset WS2812B je > 280 us");
    }

    [Test]
    public void CelyPasek_36Led_VejdeSeDoJednohoZapisuSpidev()
    {
        var d = new Zachyt();
        d.Send(Enumerable.Range(0, 36).Select(_ => new Color(0xff, 0xff, 0xff)).ToArray());

        Assert.That(d.Data, Has.Count.EqualTo(Uvod + 36 * 24 / 2));
        Assert.That(d.Data.Skip(Uvod), Is.All.EqualTo(JednickaJednicka));
        // spidev ma vychozi bufsiz 4096 B - vetsi zapis by se rozdelil a pasek by v pulce
        // dostal pauzu, kterou WS2812 bere jako konec snimku.
        Assert.That(d.Data.Count, Is.LessThanOrEqualTo(4096));
    }
}
