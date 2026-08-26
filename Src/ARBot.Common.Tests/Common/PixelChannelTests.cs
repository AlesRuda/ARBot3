using System;
using ARBot.Common.Common;

namespace ARBot.Common.Tests.Common;

/// <summary>
/// Testy kanalu <see cref="IPixel.R"/> / <see cref="IPixel.G"/> / <see cref="IPixel.B"/> —
/// jednotneho pristupu k barve pixelu bez ohledu na jeho typ a rozlozeni v pameti.
///
/// <para><b>Proc to na <see cref="IPixel"/> je:</b> algoritmy, ktere potrebuji barvu (napr.
/// <see cref="Image{T}.ToGray"/>), jinak musi hadat z <see cref="IPixel.Values"/> — a to je pole,
/// u ktereho rozhrani neslibuje ani delku, ani poradi slozek. Pro dnesni typy to nahodou vychazi
/// (<c>Values</c> se plni z pojmenovanych vlastnosti, takze je vzdy <c>[R,G,B]</c>), ale pixel
/// typ s jinou barevnou reprezentaci — YUV, HSV — by tichouncko dal nesmysl: vzorec pro jas by
/// spocital neco z <c>[Y,U,V]</c> a nikde by to nespadlo.</para>
/// </summary>
public class PixelChannelTests
{
    /// <summary>
    /// <b>Klicovy test.</b> <see cref="BGR"/> a <see cref="RGB"/> maji obracene rozlozeni v pameti,
    /// ale <c>R</c>/<c>G</c>/<c>B</c> musi dat <b>totez</b> — presne to z toho dela pouzitelnou
    /// abstrakci. Kdyby kanaly cetly bajty podle indexu misto podle vyznamu, tenhle test padne.
    /// </summary>
    [Test]
    public void ObracenePoradiVPameti_NezmeniKanaly()
    {
        var bgr = new BGR { Data = new byte[3], Index = 0 };
        var rgb = new RGB { Data = new byte[3], Index = 0 };

        // Tataz barva zapsana pres pojmenovane vlastnosti.
        bgr.R = 10; bgr.G = 20; bgr.B = 30;
        rgb.R = 10; rgb.G = 20; rgb.B = 30;

        Assert.Multiple(() =>
        {
            Assert.That((bgr.R, bgr.G, bgr.B), Is.EqualTo(((byte)10, (byte)20, (byte)30)));
            Assert.That((rgb.R, rgb.G, rgb.B), Is.EqualTo(((byte)10, (byte)20, (byte)30)));

            // A pritom v pameti lezi obracene - to je ten rozdil, ktery kanaly schovavaji.
            Assert.That(bgr.Data[0], Is.EqualTo(30), "BGR ma na prvnim bajtu modrou");
            Assert.That(rgb.Data[0], Is.EqualTo(10), "RGB ma na prvnim bajtu cervenou");
        });
    }

    [Test]
    public void Bgr32_MaKanalySpravne()
    {
        var p = new BGR32 { Data = new byte[4], Index = 0 };
        p.R = 1; p.G = 2; p.B = 3;

        Assert.That((p.R, p.G, p.B), Is.EqualTo(((byte)1, (byte)2, (byte)3)));
    }

    /// <summary>Sedy pixel vraci svou hodnotu ve <b>vsech treh</b> kanalech.</summary>
    [Test]
    public void SedyPixel_MaVsechnyKanalyStejne()
    {
        var p = new Gray { Data = new byte[1], Index = 0 };
        p.Value = 200;

        Assert.That((p.R, p.G, p.B), Is.EqualTo(((byte)200, (byte)200, (byte)200)));
    }

    /// <summary>
    /// Typy sirsi nez bajt vraci <b>nejvyssi bajt</b>, tedy hodnotu skalovanou do rozsahu bajtu —
    /// <b>ne saturaci na 255</b>. Je to tataz konvence, jakou uz ma <see cref="IPixel.Color"/>:
    /// kdyby se kanaly rozhodly jinak, tentyz pixel by hlasil jinou barvu pres <c>R</c> a jinou
    /// pres <c>Color.R</c>. U hloubkoveho obrazu v milimetrech navic saturace udela bilou placku,
    /// zatimco skalovani zachova prubeh.
    /// </summary>
    [Test]
    public void Gray16_SkalujeNaNejvyssiBajt()
    {
        var p = new Gray16 { Data = new byte[2], Index = 0 };
        p.Value = 3000;

        Assert.Multiple(() =>
        {
            Assert.That((p.R, p.G, p.B), Is.EqualTo(((byte)11, (byte)11, (byte)11)), "3000 / 256");
            Assert.That(p.R, Is.EqualTo(p.Color.R), "kanaly se nesmi rozejit s Color");
        });
    }

    [Test]
    public void Gray32_SkalujeNaNejvyssiBajt()
    {
        var p = new Gray32 { Data = new byte[4], Index = 0 };
        p.Value = 0x12345678;

        Assert.Multiple(() =>
        {
            Assert.That(p.R, Is.EqualTo(0x12));
            Assert.That(p.R, Is.EqualTo(p.Color.R), "kanaly se nesmi rozejit s Color");
        });
    }

    /// <summary>Hodnota pod 256 tim padem vyjde jako 0 — je to skalovani, ne uriznuti.</summary>
    [Test]
    public void Gray16PodRozsahemBajtu_VyjdeNula()
    {
        var p = new Gray16 { Data = new byte[2], Index = 0 };
        p.Value = 137;

        Assert.That(p.R, Is.Zero, "137 / 256 = 0; skalovani, ne uriznuti");
    }

    /// <summary>
    /// Kanaly jsou dostupne i pres rozhrani, ne jen na konkretnim typu — bez toho by je
    /// genericky kod (<c>where T : IPixel</c>) nemohl pouzit, coz je cely dusledek toho pridani.
    /// </summary>
    [Test]
    public void KanalyJsouDostupnePresRozhrani()
    {
        IPixel p = new BGR32 { Data = new byte[4], Index = 0 };
        ((BGR32)p).R = 42;

        Assert.That(p.R, Is.EqualTo(42));
    }
}
