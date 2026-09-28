using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;
using ARBot.HAL.Devices.NeoPixel;

namespace ARBot.HAL.Tests;

/// <summary>
/// Kodovani WS2812 do SPI sub-bitu (<see cref="SpiNeoPixelDriver"/>) s casovanim, se kterym ho
/// zaklada <c>ARBotHW</c> na Orange Pi: 6,4 MHz, nula 2+6, jednicka 5+3 sub-bitu.
///
/// <para><b>Proc to ma test:</b> na pasku se chyba projevi jen jako „jine barvy" nebo tma, a to
/// nejde odlisit od spatneho zapojeni. Poradi GRB a MSB-first je presne to, co se da splest.</para>
/// </summary>
public class SpiNeoPixelDriverTests
{
    private sealed class Zachyt : SpiNeoPixelDriver
    {
        public List<byte> Data = new();
        public Zachyt() : base(new PulseConfig { T0H = 2, T0L = 6, T1H = 5, T1L = 3 }) { }
        protected override void WriteData(List<byte> values) => Data = values.ToList();
    }

    private const byte Nula = 0b1100_0000;      // 2 sub-bity vysoko, 6 nizko
    private const byte Jednicka = 0b1111_1000;  // 5 vysoko, 3 nizko

    [Test]
    public void JednaLed_PoradiGRB_MsbFirst()
    {
        var d = new Zachyt();
        d.Send(new[] { new Color(0x01, 0x80, 0x00) });   // R=0x01, G=0x80, B=0

        Assert.That(d.Data, Has.Count.EqualTo(24), "8 sub-bitu na bit = 1 bajt na bit WS2812");
        // G prvni a MSB prvni: 0x80 -> 1 a sedm nul.
        Assert.That(d.Data[0], Is.EqualTo(Jednicka));
        Assert.That(d.Data.Skip(1).Take(7), Is.All.EqualTo(Nula));
        // R = 0x01 -> sedm nul a jednicka na konci.
        Assert.That(d.Data.Skip(8).Take(7), Is.All.EqualTo(Nula));
        Assert.That(d.Data[15], Is.EqualTo(Jednicka));
        Assert.That(d.Data.Skip(16), Is.All.EqualTo(Nula));
    }

    [Test]
    public void CelyPasek_36Led_VejdeSeDoJednohoZapisuSpidev()
    {
        var d = new Zachyt();
        d.Send(Enumerable.Range(0, 36).Select(_ => new Color(0xff, 0xff, 0xff)).ToArray());

        Assert.That(d.Data, Has.Count.EqualTo(36 * 24));
        Assert.That(d.Data, Is.All.EqualTo(Jednicka));
        // spidev ma vychozi bufsiz 4096 B - vetsi zapis by se rozdelil a pasek by v pulce
        // dostal pauzu, kterou WS2812 bere jako konec snimku.
        Assert.That(d.Data.Count, Is.LessThanOrEqualTo(4096));
    }
}
