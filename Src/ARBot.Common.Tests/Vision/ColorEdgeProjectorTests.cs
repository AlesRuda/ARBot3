using System;
using System.Numerics;
using System.Collections.Generic;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Vision;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision;

/// <summary>
/// Managed prepocet pixelu barevneho obrazu na metricky bod v ramci robotu — nahrada nativniho
/// <c>ColorPixel23D</c>.
///
/// <para><b>Proc to vznikalo</b> (21. 8. 2026): nativni <c>ColorPixel23D</c> <b>v NativeLib dnes
/// vubec neni</b>, takze cesta pres <c>D435CameraProjection.TransformBack(points, depth)</c> je
/// mrtva na vsech platformach (na ARM navic vyhazuje <c>NotSupportedException</c> explicitne).
/// Hranova lokalizace na tom stoji, viz doc/map-correlation-localization.md.</para>
///
/// <para>Matematika je shodna s originalem: barevny pixel se prepocita na hloubkovy, z tabulky
/// smeru (<c>Camera2DToCamera3D</c>) se vezme paprsek a bod je <c>(ray.x·d, ray.y·d, d)</c>,
/// nakonec se aplikuje montazni transformace kamery.</para>
/// </summary>
public class ColorEdgeProjectorTests
{
    private const int ColorW = 640, ColorH = 480;
    private const int DepthW = 480, DepthH = 270;

    private static Intrinsics Color() => SyntheticIntrinsics.Pinhole(ColorW, ColorH, 69.4);
    private static Intrinsics Depth() => SyntheticIntrinsics.Pinhole(DepthW, DepthH, 87.0);

    /// <summary>Hloubkova projekce s danou montazi (pinhole, bez zkresleni).</summary>
    private static CameraProjection DepthProjection(Matrix4x4 mount)
    {
        var i = Depth();
        var p = new CameraProjection(i, i, Matrix4x4.Identity, Matrix4x4.Identity);
        p.SetOrientation(mount);
        return p;
    }

    /// <summary>Hloubkovy obraz s jedinou platnou hodnotou na zadanem pixelu [mm].</summary>
    private static Image<Gray16> DepthImage(int x, int y, int mm)
    {
        var img = new Image<Gray16>(DepthW, DepthH);
        if (x >= 0 && y >= 0 && x < DepthW && y < DepthH)
            img[x, y].Value = mm;   // indexer vraci sdilenou instanci pixelu se setterem
        return img;
    }

    private static ColorEdgeProjector Projector(Matrix4x4 mount)
        => new ColorEdgeProjector(Color(), Depth(), DepthProjection(mount));

    [Test]
    public void StredObrazu_daBodPredKameroui()
    {
        // Montaz = identita: ramec robotu == prostor kamery (X vpravo, Y dolu, Z od kamery).
        var color = Color();
        var depth = Depth();
        var img = DepthImage((int)Math.Round(depth.PPx), (int)Math.Round(depth.PPy), 2000);

        var p = Projector(Matrix4x4.Identity).ToRobot((int)Math.Round(color.PPx), (int)Math.Round(color.PPy), img);

        Assert.That(p.A, Is.EqualTo(1), "stred obrazu s platnou hloubkou musi projit");
        Assert.That(p.X, Is.EqualTo(0).Within(0.01));
        Assert.That(p.Y, Is.EqualTo(0).Within(0.01));
        Assert.That(p.Z, Is.EqualTo(2.0).Within(0.01));
    }

    [Test]
    public void MontazSePrictePosunem()
    {
        var color = Color();
        var depth = Depth();
        var img = DepthImage((int)Math.Round(depth.PPx), (int)Math.Round(depth.PPy), 2000);
        var mount = Matrix4x4.CreateTranslation(0, 0, 0.5f);

        var p = Projector(mount).ToRobot((int)Math.Round(color.PPx), (int)Math.Round(color.PPy), img);

        Assert.That(p.A, Is.EqualTo(1));
        Assert.That(p.Z, Is.EqualTo(2.5).Within(0.01), "montazni posun se musi pricist");
    }

    [Test]
    public void PrepocetBarevnehoPixeluNaHloubkovy_respektujeOboIntrinsics()
    {
        // Klicovy test: streamy maji RUZNE rozliseni i FOV (640x480 @69,4 vs 480x270 @87).
        // Barevny pixel s normalizovanou souradnici 0,1 musi trefit hloubkovy pixel s tou samou
        // normalizovanou souradnici - jinak by se hrany promitaly na spatne misto.
        var color = Color();
        var depth = Depth();
        const double xn = 0.1;

        int cx = (int)Math.Round(color.PPx + xn * color.Fx);
        int cy = (int)Math.Round(color.PPy);
        int dx = (int)Math.Round(depth.PPx + xn * depth.Fx);
        int dy = (int)Math.Round(depth.PPy);

        // Hloubka JEN na ocekavanem hloubkovem pixelu: kdyz prepocet trefi jinam, vyjde A = 0.
        var img = DepthImage(dx, dy, 3000);

        var p = Projector(Matrix4x4.Identity).ToRobot(cx, cy, img);

        Assert.That(p.A, Is.EqualTo(1), "prepocet musi trefit spravny hloubkovy pixel");
        Assert.That(p.Z, Is.EqualTo(3.0).Within(0.01));
        Assert.That(p.X / p.Z, Is.EqualTo(xn).Within(0.01), "smer paprsku musi odpovidat normalizovane souradnici");
    }

    [Test]
    public void BezHloubky_vratiNeplatnyBod()
    {
        var color = Color();
        var img = new Image<Gray16>(DepthW, DepthH);   // vsude 0 = zadna hloubka

        var p = Projector(Matrix4x4.Identity).ToRobot((int)color.PPx, (int)color.PPy, img);

        Assert.That(p.A, Is.EqualTo(0));
    }

    [Test]
    public void NasycenaHloubka_jeNeplatna()
    {
        // 0xffff je "neplatne" uz v originalnim ColorPixel23D.
        var color = Color();
        var depth = Depth();
        var img = DepthImage((int)Math.Round(depth.PPx), (int)Math.Round(depth.PPy), ushort.MaxValue);

        var p = Projector(Matrix4x4.Identity).ToRobot((int)Math.Round(color.PPx), (int)Math.Round(color.PPy), img);

        Assert.That(p.A, Is.EqualTo(0));
    }

    [Test]
    public void MimoRozsahHloubky_jeNeplatny()
    {
        var color = Color();
        var depth = Depth();
        int cx = (int)Math.Round(color.PPx), cy = (int)Math.Round(color.PPy);
        int dx = (int)Math.Round(depth.PPx), dy = (int)Math.Round(depth.PPy);

        var tooClose = Projector(Matrix4x4.Identity).ToRobot(cx, cy, DepthImage(dx, dy, 300));    // 0,3 m
        var tooFar = Projector(Matrix4x4.Identity).ToRobot(cx, cy, DepthImage(dx, dy, 20000));    // 20 m

        Assert.That(tooClose.A, Is.EqualTo(0), "pod minimalnim dosahem senzoru");
        Assert.That(tooFar.A, Is.EqualTo(0), "nad maximalnim dosahem");
    }

    /// <summary>Hloubkovy obraz s konstantni hloubkou vsude (rovina ve vzdalenosti Z).</summary>
    private static Image<Gray16> FlatDepth(int mm)
    {
        var img = new Image<Gray16>(DepthW, DepthH);
        for (int y = 0; y < DepthH; y++)
            for (int x = 0; x < DepthW; x++)
                img[x, y].Value = mm;
        return img;
    }

    [Test]
    public void ExtrinsikaIdentita_daTenTyzVysledekJakoZarovnaneStreamy()
    {
        // Kdyz jsou extrinsiky identita, hledani podel epipolary MUSI dat totez co prepocet
        // intrinsik - jinak by se dve cesty tehoz kodu rozesly.
        var color = Color();
        var img = FlatDepth(3000);
        int cx = (int)Math.Round(color.PPx + 0.15 * color.Fx);
        int cy = (int)Math.Round(color.PPy + 0.05 * color.Fy);

        var aligned = new ColorEdgeProjector(Color(), Depth(), DepthProjection(Matrix4x4.Identity));
        var viaSearch = new ColorEdgeProjector(Color(), Depth(), DepthProjection(Matrix4x4.Identity),
                                               colorToDepth: Matrix4x4.CreateTranslation(0, 0, 0),
                                               depthToColor: Matrix4x4.CreateTranslation(0, 0, 0));

        var a = aligned.ToRobot(cx, cy, img);
        var b = viaSearch.ToRobot(cx, cy, img);

        Assert.That(a.A, Is.EqualTo(1));
        Assert.That(b.A, Is.EqualTo(1));
        Assert.That(b.X, Is.EqualTo(a.X).Within(0.02), "obe cesty musi dat tentyz bod");
        Assert.That(b.Y, Is.EqualTo(a.Y).Within(0.02));
        Assert.That(b.Z, Is.EqualTo(a.Z).Within(0.02));
    }

    [Test]
    public void ZakladnaMeziSenzory_posunePrirazenyPixel()
    {
        // Extrinsika s realnou zakladnou D435 (~15 mm do strany) musi vest na JINY hloubkovy pixel
        // nez naivni prepocet intrinsik - presne to je duvod, proc original volal
        // rs2_project_color_pixel_to_depth_pixel a proc se extrinsika nesmi vynechat.
        var color = Color();
        var img = FlatDepth(1000);                       // rovina 1 m: posun je nejvetsi
        int cx = (int)Math.Round(color.PPx + 0.30 * color.Fx);
        int cy = (int)Math.Round(color.PPy);

        var baseline = Matrix4x4.CreateTranslation(-0.015f, 0, 0);
        var withExtr = new ColorEdgeProjector(Color(), Depth(), DepthProjection(Matrix4x4.Identity),
                                              colorToDepth: baseline,
                                              depthToColor: Matrix4x4.CreateTranslation(0.015f, 0, 0));
        var naive = new ColorEdgeProjector(Color(), Depth(), DepthProjection(Matrix4x4.Identity));

        var withE = withExtr.ToRobot(cx, cy, img);
        var withoutE = naive.ToRobot(cx, cy, img);

        Assert.That(withE.A, Is.EqualTo(1), "hledani musi bod najit");
        Assert.That(withoutE.A, Is.EqualTo(1));
        // Rozdil je maly, ale nenulovy - a roste s blizkosti. Kdyby extrinsika nemela zadny vliv,
        // znamenalo by to, ze se ignoruje.
        double dxDiff = Math.Abs(withE.X - withoutE.X);
        Assert.That(dxDiff, Is.GreaterThan(0.001), "extrinsika musi mit vliv");
        Assert.That(dxDiff, Is.LessThan(0.10), "ale ne rad metru - to by byla chyba geometrie");
    }

    [Test]
    public void BazovaTransformBack_pouzivaHloubku_neRovinuZeme()
    {
        // Do 21. 8. 2026 CameraProjection.TransformBack(points, depth) parametr depth IGNOROVALA
        // a promitala paprsek na rovinu zeme - u pixelu u horizontu to davalo stovky metru.
        // Skutecny vypocet delaly az prepisy v D435CameraProjection, ktere volaly nativni
        // ColorPixel23D (to v NativeLib neni). Ted to umi baze, takze ty prepisy zmizely.
        var depthIntr = Depth();
        // Montaz: kamera 0,5 m nad zemi, hledi vodorovne (identita rotace) - kdyby se pouzila
        // rovina zeme, paprsek stredem obrazu ji NIKDY netrefi a bod by byl neplatny.
        var proj = DepthProjection(Matrix4x4.CreateTranslation(0, 0, 0.5f));
        proj.SetColorAlignment(Color());

        var img = DepthImage((int)Math.Round(depthIntr.PPx), (int)Math.Round(depthIntr.PPy), 2500);
        var color = Color();
        var pts = new List<Point> { new Point((int)Math.Round(color.PPx), (int)Math.Round(color.PPy)) };

        var res = proj.TransformBack(pts, img);

        Assert.That(res, Has.Count.EqualTo(1));
        Assert.That(res[0].A, Is.EqualTo(1), "s hloubkou bod vznikne");
        Assert.That(res[0].Z, Is.EqualTo(3.0).Within(0.01), "2,5 m hloubky + 0,5 m montaz");
    }

    [Test]
    public void BazovaTransformBack_bezHloubkyVracíNeplatnyBod()
    {
        var proj = DepthProjection(Matrix4x4.Identity);
        proj.SetColorAlignment(Color());
        var color = Color();
        var pts = new List<Point> { new Point((int)color.PPx, (int)color.PPy) };

        var res = proj.TransformBack(pts, new Image<Gray16>(DepthW, DepthH));

        Assert.That(res[0].A, Is.EqualTo(0));
    }

    [Test]
    public void PixelMimoHloubkovyObraz_jeNeplatny()
    {
        var img = DepthImage(0, 0, 2000);

        // Barevny pixel u kraje: po prepoctu vyjde mimo hloubkovy obraz (uzsi FOV v ose Y).
        var p = Projector(Matrix4x4.Identity).ToRobot(0, ColorH - 1, img);

        Assert.That(p.A, Is.EqualTo(0));
    }
}
