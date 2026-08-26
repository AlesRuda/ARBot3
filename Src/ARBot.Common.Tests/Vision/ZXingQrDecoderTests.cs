using System;
using ARBot.Common.Common;
using ARBot.Common.Vision.Qr;

namespace ARBot.Common.Tests.Vision;

/// <summary>
/// Testy skutecneho dekoderu QR (ZXing.Net za <see cref="IQrDecoder"/>).
///
/// <para><b>Testovaci obraz se generuje, ne cte ze souboru.</b> Kod se zakoduje ZXingem a hned
/// dekoduje zpatky — je to deterministicke, nezavisle na build stroji a bez binarky v repozitari.
/// Cena teto volby: dekoduje se to, co zakodovala tataz knihovna, takze test overuje <b>cestu</b>
/// (BGR32 → Y800 → dekoder), nikoli uspesnost cteni na skutecnem stanovisti. Ta se musi zmerit na
/// zarizeni — je to vedeny krok „overeni na HW" v doc/robotour-mission.md.</para>
///
/// <para>Puvodni navrh pocital se ZBarem z predchozi generace robotu; jeho binding nebyl k dispozici,
/// takze se vzal ZXing.Net (ciste managed, zadna nativni knihovna ani na ARM64). Vymena je za
/// <see cref="IQrDecoder"/> lokalni zmena.</para>
/// </summary>
public class ZXingQrDecoderTests
{
    /// <summary>Vyrobi obraz s QR kodem daneho textu (bily podklad, cerny vzor).</summary>
    private static Image<BGR32> Encode(string text, int size = 240)
    {
        var writer = new ZXing.QrCode.QRCodeWriter();
        var matrix = writer.encode(text, ZXing.BarcodeFormat.QR_CODE, size, size);

        var img = new Image<BGR32>(matrix.Width, matrix.Height);
        var p = new BGR32 { Data = img.Data };
        for (int y = 0; y < matrix.Height; y++)
            for (int x = 0; x < matrix.Width; x++)
            {
                byte v = matrix[x, y] ? (byte)0 : (byte)255;
                p.Index = (y * matrix.Width + x) * 4;
                p.R = v; p.G = v; p.B = v;
            }
        return img;
    }

    [Test]
    public void ZakodovanyKod_SePrecteZpatky()
    {
        const string text = "geo:49.2103,16.5991";
        var gray = Encode(text).ToGray();

        var found = new ZXingQrDecoder().Decode(gray);

        Assert.That(found, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(found[0].Text, Is.EqualTo(text));
            Assert.That(found[0].Corners, Is.Not.Empty, "polohove znacky jsou vstup pro vizualni dojezd");
        });
    }

    /// <summary>
    /// Podvzorkovani na polovinu kod porad precte — presne to je duvod, proc
    /// <see cref="QrScannerConfig.Downscale"/> je ve vychozim stavu 2.
    /// </summary>
    [Test]
    public void PoPodvzorkovani_SeKodPorodPrecte()
    {
        const string text = "geo:49.2103,16.5991";
        var gray = Encode(text, size: 480).ToGray(downscale: 2);

        var found = new ZXingQrDecoder().Decode(gray);

        Assert.That(found, Has.Length.EqualTo(1));
        Assert.That(found[0].Text, Is.EqualTo(text));
    }

    /// <summary>
    /// Obraz bez kodu vraci prazdne pole, <b>ne vyjimku</b>. Snimek bez kodu je normalni, ocekavany
    /// stav — scanner ho vidi vetsinu casu.
    /// </summary>
    [Test]
    public void ObrazBezKodu_VraciPrazdnePole()
    {
        var gray = new Image<Gray>(64, 48);

        var found = new ZXingQrDecoder().Decode(gray);

        Assert.That(found, Is.Empty);
    }

    /// <summary>Jiná symbologie (carovy kod) se nehlasi — povoleny je jen QR.</summary>
    [Test]
    public void CarovyKod_SeNehlasi()
    {
        var writer = new ZXing.OneD.Code128Writer();
        var matrix = writer.encode("123456", ZXing.BarcodeFormat.CODE_128, 300, 100);

        var img = new Image<BGR32>(matrix.Width, matrix.Height);
        var p = new BGR32 { Data = img.Data };
        for (int y = 0; y < matrix.Height; y++)
            for (int x = 0; x < matrix.Width; x++)
            {
                byte v = matrix[x, y] ? (byte)0 : (byte)255;
                p.Index = (y * matrix.Width + x) * 4;
                p.R = v; p.G = v; p.B = v;
            }

        var found = new ZXingQrDecoder().Decode(img.ToGray());

        Assert.That(found, Is.Empty, "ostatni symbologie jen zdrzuji a mohou plodit falesne nalezy");
    }
}
