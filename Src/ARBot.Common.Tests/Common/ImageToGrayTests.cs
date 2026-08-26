using System;
using ARBot.Common.Common;

namespace ARBot.Common.Tests.Common;

/// <summary>
/// Testy <see cref="Image{T}.ToGray"/> — prevod libovolneho pixel typu na sedou (Y800) s volitelnym
/// podvzorkovanim.
///
/// <para>Vzniklo to jako <c>QrImage.ToGray</c> pro cteni QR kodu, ale je to obecna operace nad
/// obrazem, takze zije na <see cref="Image{T}"/>. Testy proto hlidaji i <b>obecnost</b>: musi to
/// projit pro barevne i uz sede pixel typy, ne jen pro <see cref="BGR32"/>, se kterym to zacalo.</para>
/// </summary>
public class ImageToGrayTests
{
    /// <summary>Nastavi pixel obrazu <see cref="BGR32"/> na danou barvu.</summary>
    private static void SetBgr(Image<BGR32> img, int x, int y, byte r, byte g, byte b)
    {
        var p = new BGR32 { Data = img.Data, Index = img.Index(x, y) };
        p.R = r; p.G = g; p.B = b;
    }

    private static int GrayAt(Image<Gray> img, int x, int y)
        => new Gray { Data = img.Data, Index = img.Index(x, y) }.Value;

    // ---------------- Jas a rozmery ----------------

    [Test]
    public void ZachovaRozmeryAVyrobiJedenBajtNaPixel()
    {
        var rgb = new Image<BGR32>(4, 3);

        var gray = rgb.ToGray();

        Assert.Multiple(() =>
        {
            Assert.That(gray.Width, Is.EqualTo(4));
            Assert.That(gray.Height, Is.EqualTo(3));
            Assert.That(gray.Data.Length, Is.EqualTo(4 * 3), "Y800 = 1 bajt na pixel");
        });
    }

    [Test]
    public void BilyPixel_Je255AZbytekZustaneCerny()
    {
        var rgb = new Image<BGR32>(4, 3);
        SetBgr(rgb, 0, 0, 255, 255, 255);

        var gray = rgb.ToGray();

        Assert.Multiple(() =>
        {
            Assert.That(GrayAt(gray, 0, 0), Is.EqualTo(255), "bila je 255");
            Assert.That(GrayAt(gray, 1, 0), Is.Zero, "zbytek obrazu je cerny");
        });
    }

    /// <summary>
    /// Jas je <b>vazeny</b> (BT.601), ne prumer slozek — zelena vazi nejvic. Kdyby to byl prumer,
    /// cista zelena by dala 85 misto 149.
    /// </summary>
    [Test]
    public void JasJeVazeny_NeProstyPrumerSlozek()
    {
        var rgb = new Image<BGR32>(3, 1);
        SetBgr(rgb, 0, 0, 255, 0, 0);     // cervena
        SetBgr(rgb, 1, 0, 0, 255, 0);     // zelena
        SetBgr(rgb, 2, 0, 0, 0, 255);     // modra

        var gray = rgb.ToGray();

        Assert.Multiple(() =>
        {
            Assert.That(GrayAt(gray, 0, 0), Is.EqualTo(76), "0,299 * 255");
            Assert.That(GrayAt(gray, 1, 0), Is.EqualTo(149), "0,587 * 255 — zelena vazi nejvic");
            Assert.That(GrayAt(gray, 2, 0), Is.EqualTo(29), "0,114 * 255");
        });
    }

    // ---------------- Obecnost pres pixel typy ----------------

    /// <summary>
    /// Uz sedy obraz projde <b>bez zmeny hodnot</b>. Kdyby se na jednoslozkovy pixel pouzil vzorec
    /// pro barvu, sedá 200 by vyšla jako 60 (jen 0,299) — proto je to samostatny test.
    /// </summary>
    [Test]
    public void SedyZdroj_ZachovaHodnoty()
    {
        var src = new Image<Gray>(2, 1);
        var p = new Gray { Data = src.Data, Index = src.Index(0, 0) };
        p.Value = 200;

        var gray = src.ToGray();

        Assert.Multiple(() =>
        {
            Assert.That(GrayAt(gray, 0, 0), Is.EqualTo(200));
            Assert.That(GrayAt(gray, 1, 0), Is.Zero);
        });
    }

    /// <summary>Jiny barevny typ (<see cref="RGB"/>) dava tentyz jas jako <see cref="BGR32"/>.</summary>
    [Test]
    public void JinyBarevnyTyp_DavaTentyzJas()
    {
        var src = new Image<RGB>(1, 1);
        var p = new RGB { Data = src.Data, Index = 0 };
        p.R = 0; p.G = 255; p.B = 0;

        Assert.That(GrayAt(src.ToGray(), 0, 0), Is.EqualTo(149),
                    "poradi slozek nesmi zmenit vysledek");
    }

    /// <summary>
    /// 16bitovy zdroj se <b>skaluje</b> (nejvyssi bajt), ne saturuje. Hloubkovy obraz ma hodnoty
    /// v milimetrech, tedy klidne tisice — saturace by z celeho obrazu udelala bilou placku,
    /// zatimco skalovani zachova prubeh. Konvenci urcuje <see cref="IPixel.R"/>, ktere ji ma
    /// shodnou s <see cref="IPixel.Color"/>.
    /// </summary>
    [Test]
    public void SestnactibitovyZdroj_SeSkalujeNaBajt()
    {
        var src = new Image<Gray16>(1, 1);
        var p = new Gray16 { Data = src.Data, Index = 0 };
        p.Value = 3000;

        Assert.That(GrayAt(src.ToGray(), 0, 0), Is.EqualTo(11), "3000 / 256");
    }

    // ---------------- Podvzorkovani ----------------

    [Test]
    public void Podvzorkovani_ZmensiRozmery()
    {
        var rgb = new Image<BGR32>(64, 48);

        var gray = rgb.ToGray(downscale: 2);

        Assert.Multiple(() =>
        {
            Assert.That(gray.Width, Is.EqualTo(32));
            Assert.That(gray.Height, Is.EqualTo(24));
        });
    }

    /// <summary>
    /// Podvzorkovani <b>vybira pixely, neprumeruje</b>. Na sachovnici 1 px to znamena, ze vysledek
    /// je jednolity (vybrane pixely jsou vsechny stejne) — prumerovani by dalo sedou kasi. U QR kodu
    /// je to podstatne: je to binarni vzor s ostrymi hranami a rozmazani je presne to, co dekoderu vadi.
    /// </summary>
    [Test]
    public void Podvzorkovani_VybiraPixelyNeprumeruje()
    {
        var rgb = new Image<BGR32>(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                byte v = (x + y) % 2 == 0 ? (byte)255 : (byte)0;
                SetBgr(rgb, x, y, v, v, v);
            }

        var gray = rgb.ToGray(downscale: 2);

        Assert.Multiple(() =>
        {
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    Assert.That(GrayAt(gray, x, y), Is.EqualTo(255),
                                $"[{x},{y}]: vybrane pixely (sude souradnice) jsou bile");
        });
    }

    /// <summary>Podvzorkovani vetsi nez obraz nesmi dat prazdny obraz — zbyde aspon jeden pixel.</summary>
    [Test]
    public void PodvzorkovaniVetsiNezObraz_ZbydeJedenPixel()
    {
        var gray = new Image<BGR32>(4, 4).ToGray(downscale: 100);

        Assert.Multiple(() =>
        {
            Assert.That(gray.Width, Is.EqualTo(1));
            Assert.That(gray.Height, Is.EqualTo(1));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NeplatnePodvzorkovani_Vyhodi(int downscale)
    {
        Assert.That(() => new Image<BGR32>(4, 4).ToGray(downscale),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
