using System;
using ARBot.Common.Common;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision;

/// <summary>
/// Testy <see cref="SyntheticBillboard"/> — svisle desky s texturou, kterou umi virtualni kamera
/// vykreslit do barevneho obrazu (viz doc/virtual-hw.md).
///
/// <para><b>Nacpak to je:</b> aby se dal v simulaci projit krok mise Robotour, ve kterem robot cte
/// QR kod. Bez toho simulace zadny kod nerenderovala, takze servisni okno se nedalo dokoncit ani
/// rucne — to je vedeny otevreny ukol z puvodniho navrhu („dekoder nad realnym obrazem potrebuje
/// bud zelezo, nebo QR ve virtualni kamere").</para>
///
/// <para>Jadro je <b>geometrie</b>: kam v obraze deska padne a jak se z nej vzorkuje textura.
/// Presne tam se to splete, takze se testuje samostatne, bez kamery.</para>
/// </summary>
public class SyntheticBillboardTests
{
    /// <summary>Deska 1x1 m, stred 2 m na vychod od pocatku, celem na zapad (k pozorovateli).</summary>
    private static SyntheticBillboard Board()
        => new SyntheticBillboard
        {
            CenterX = 2.0, CenterY = 0.0, CenterZ = 0.5,
            YawRad = Math.PI,          // normala miri na zapad, tedy proti pozorovateli v pocatku
            WidthM = 1.0, HeightM = 1.0,
            Texture = Checker(4),
        };

    /// <summary>Sachovnice n x n (bila/cerna) — na ni se pozna orientace vzorkovani.</summary>
    private static Image<BGR32> Checker(int n)
    {
        var img = new Image<BGR32>(n, n);
        var p = new BGR32 { Data = img.Data };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                byte v = (x + y) % 2 == 0 ? (byte)255 : (byte)0;
                p.Index = (y * n + x) * 4;
                p.R = v; p.G = v; p.B = v;
            }
        return img;
    }

    [Test]
    public void PaprsekDoStredu_Zasahne()
    {
        var b = Board();

        // Z pocatku ve vysce stredu desky primo na vychod.
        bool hit = b.TryIntersect(0, 0, 0.5, 1, 0, 0, out double t, out _, out _);

        Assert.Multiple(() =>
        {
            Assert.That(hit, Is.True);
            Assert.That(t, Is.EqualTo(2.0).Within(1e-9), "parametr je vzdalenost k desce");
        });
    }

    /// <summary>Mimo obdelnik se nezasahne — deska ma konecne rozmery, neni to nekonecna rovina.</summary>
    [TestCase(0.0, 3.0, Description = "vysoko nad deskou")]
    [TestCase(0.0, -1.0, Description = "pod deskou")]
    public void PaprsekMimoObdelnik_Nezasahne(double fromY, double fromZ)
    {
        var b = Board();

        bool hit = b.TryIntersect(0, fromY, fromZ, 1, 0, 0, out _, out _, out _);

        Assert.That(hit, Is.False);
    }

    /// <summary>Paprsek OD desky (za pozorovatelem) se nezasahne, i kdyz rovina lezi na primce.</summary>
    [Test]
    public void PaprsekDozadu_Nezasahne()
    {
        var b = Board();

        bool hit = b.TryIntersect(0, 0, 0.5, -1, 0, 0, out _, out _, out _);

        Assert.That(hit, Is.False);
    }

    /// <summary>
    /// Paprsek rovnobezny s deskou se nezasahne — bez teto kontroly by delenim skoro nulou vysel
    /// nesmyslny (nebo nekonecny) parametr.
    /// </summary>
    [Test]
    public void PaprsekRovnobeznySDeskou_Nezasahne()
    {
        var b = Board();

        bool hit = b.TryIntersect(0, 0, 0.5, 0, 1, 0, out _, out _, out _);

        Assert.That(hit, Is.False);
    }

    /// <summary>
    /// <b>Orientace vzorkovani.</b> Stred desky je stred textury; horni pulka desky vzorkuje HORNI
    /// pulku obrazu (radek 0 je nahore). Kdyz se to obrati, QR kod se zrcadli nebo prevrati a
    /// dekoder ho precte spatne — a pozna se to jen na obrazku.
    /// </summary>
    [Test]
    public void Vzorkovani_MaSpravnouOrientaci()
    {
        var b = Board();

        b.TryIntersect(0, 0, 0.5, 1, 0, 0, out _, out double us, out double vs);
        Assert.Multiple(() =>
        {
            Assert.That(us, Is.EqualTo(0.5).Within(1e-9), "stred vodorovne");
            Assert.That(vs, Is.EqualTo(0.5).Within(1e-9), "stred svisle");
        });

        // Bod 0,4 m NAD stredem desky -> horni cast textury, tedy v blizko nuly.
        b.TryIntersect(0, 0, 0.9, 1, 0, 0, out _, out _, out double vTop);
        Assert.That(vTop, Is.LessThan(0.2), "vys na desce = mensi v (radek 0 je nahore)");

        // Bod 0,4 m VLEVO od stredu z pohledu pozorovatele (tedy +Y ve svete).
        b.TryIntersect(0, 0.4, 0.5, 1, 0, 0, out _, out double uLeft, out _);
        Assert.That(uLeft, Is.Not.EqualTo(0.5).Within(0.1), "posun do strany meni u");
    }

    /// <summary>Vzorek textury podle (u, v) — sachovnice rozliseni 4 musi dat cernou i bilou.</summary>
    [Test]
    public void VzorekTextury_CteSpravnyPixel()
    {
        var b = Board();

        var topLeft = b.Sample(0.01, 0.01);
        var next = b.Sample(0.26, 0.01);

        Assert.That(topLeft, Is.Not.EqualTo(next), "sousedni polia sachovnice se musi lisit");
    }

    /// <summary>Deska bez textury nic nekresli — nesmi to spadnout.</summary>
    [Test]
    public void DeskaBezTextury_Nezasahne()
    {
        var b = new SyntheticBillboard
        {
            CenterX = 2, CenterZ = 0.5, YawRad = Math.PI, WidthM = 1, HeightM = 1,
        };

        Assert.That(b.TryIntersect(0, 0, 0.5, 1, 0, 0, out _, out _, out _), Is.False);
    }
}
