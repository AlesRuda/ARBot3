using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Localization;
using ARBot.Common.Vision;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision;

/// <summary>
/// <b>Ohýbá se hranice cesty s rostoucí vzdáleností?</b>
///
/// <para><b>Proč to vzniklo</b> (22. 8. 2026). Za jízdy zahazoval koridor skoro všechna měření
/// s <c>NotParallel</c>: směr levé hranice vycházel +5,1°, pravé −6,2°, tedy obě se symetricky
/// <b>sbíhaly</b>, zatímco mapa hlásila cestu rovně vpřed. Nerovnoběžnost seděla ustáleně na
/// 10,8–11,4°, těsně nad prahem 10°, a rostla s délkou hranice (4,4° při 60–119 inlierech proti
/// 11,2° při 240–299). Vedoucí hypotéza byla systematická chyba zpětné projekce hloubky na zem,
/// která roste s dosahem. Viz doc/map-correlation-localization.md.</para>
///
/// <para><b>Co test dělá.</b> Vezme <b>dokonale rovnou</b> hranici cesty na <b>rovné zemi</b>
/// (bez šumu, bez trávy, bez detektoru hran), promítne ji do pixelů skutečnou montáží kamer
/// (<see cref="Profile.LeftCameraTransform"/>) a nechá ji zpětně přepočítat přes
/// <see cref="ColorPixelTo3D"/> — tedy přesně tou cestou, kterou jde hranová lokalizace. Pak
/// změří, jestli body pořád leží na přímce rovnoběžné s cestou.</para>
///
/// <para>Test <b>neověřuje detekci hran</b> (ta je v nativní ComputeUnit a managed náhradu nemá) —
/// izoluje pouze geometrii. Když projde, ohyb v projekci není a příčinu je nutné hledat
/// v pixelech, které detektor vrací.</para>
/// </summary>
public class BoundaryStraightnessTests
{
    private const int ColorW = 640, ColorH = 480;
    private const int DepthW = 480, DepthH = 270;

    private static Intrinsics Color() => SyntheticIntrinsics.Pinhole(ColorW, ColorH, 69.4);
    private static Intrinsics Depth() => SyntheticIntrinsics.Pinhole(DepthW, DepthH, 87.0);

    private static CameraProjection DepthProjection(Matrix4x4 mount)
    {
        var i = Depth();
        var p = new CameraProjection(i, i, Matrix4x4.Identity, Matrix4x4.Identity);
        p.SetOrientation(mount);
        return p;
    }

    /// <summary>
    /// Promítne bod v rámci robotu do pixelů obou streamů a zapíše hloubku do obrazu.
    /// Vrací false, když bod padne mimo obraz nebo za kameru.
    /// </summary>
    private static bool Project(Matrix4x4 mount, Vector3 robotPoint, Image<Gray16> depthImage,
                                out int colorX, out int colorY)
    {
        colorX = colorY = 0;

        // Montáž mapuje kameru → robot, potřebujeme opačný směr.
        if (!Matrix4x4.Invert(mount, out var toCamera)) return false;
        var c = Vector3.Transform(robotPoint, toCamera);

        // Prostor kamery: X vpravo, Y dolů, Z od kamery (viz ColorPixelTo3DTests).
        if (c.Z <= 0.1f) return false;

        var col = Color();
        var dep = Depth();
        double xn = c.X / c.Z, yn = c.Y / c.Z;

        colorX = (int)Math.Round(col.PPx + xn * col.Fx);
        colorY = (int)Math.Round(col.PPy + yn * col.Fy);
        int dx = (int)Math.Round(dep.PPx + xn * dep.Fx);
        int dy = (int)Math.Round(dep.PPy + yn * dep.Fy);

        if (colorX < 0 || colorY < 0 || colorX >= ColorW || colorY >= ColorH) return false;
        if (dx < 0 || dy < 0 || dx >= DepthW || dy >= DepthH) return false;

        // Hloubka je souřadnice Z (paprsek je normalizovaný na z = 1), v milimetrech.
        depthImage[dx, dy].Value = (ushort)Math.Round(c.Z * 1000.0);
        return true;
    }

    /// <summary>Směr přímky proloženej body [rad]; 0 = rovnoběžně s osou X robotu (rovně vpřed).</summary>
    private static double FitDirection(IReadOnlyList<Point2D> pts)
    {
        double mx = 0, my = 0;
        foreach (var p in pts) { mx += p.X; my += p.Y; }
        mx /= pts.Count; my /= pts.Count;

        double sxx = 0, sxy = 0, syy = 0;
        foreach (var p in pts)
        {
            double dx = p.X - mx, dy = p.Y - my;
            sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
        }
        // Hlavní osa rozptylu (total least squares) - u téměř svislé přímky by MNČ selhaly.
        return 0.5 * Math.Atan2(2 * sxy, sxx - syy);
    }

    /// <summary>
    /// Zpětně přepočítá rovnou hranici ve vzdálenostech <paramref name="fromM"/>..<paramref name="toM"/>
    /// a vrátí body v rámci robotu.
    /// </summary>
    private static List<Point2D> BackProjectStraightEdge(Matrix4x4 mount, double lateralM,
                                                         double fromM, double toM, int samples)
    {
        var depthImage = new Image<Gray16>(DepthW, DepthH);
        var pixels = new List<(int X, int Y)>();

        for (int i = 0; i < samples; i++)
        {
            double t = fromM + (toM - fromM) * i / (samples - 1.0);
            var robotPoint = new Vector3((float)t, (float)lateralM, 0f);   // FLU, země z = 0
            if (Project(mount, robotPoint, depthImage, out int cx, out int cy))
                pixels.Add((cx, cy));
        }

        var projector = new ColorPixelTo3D(Color(), Depth(), DepthProjection(mount));
        var result = new List<Point2D>();
        foreach (var (cx, cy) in pixels)
        {
            var p = projector.ToRobot(cx, cy, depthImage);
            if (p.A != 0) result.Add(new Point2D(p.X, p.Y));
        }
        return result;
    }

    /// <summary>
    /// Rovná hranice na rovné zemi musí zůstat rovná a rovnoběžná s cestou — pro obě kamery
    /// a v celém dosahu. Tohle je ta vlastnost, kterou <c>NotParallel</c> zpochybnil.
    /// </summary>
    [TestCase(1.0, TestName = "BackProject_StraightEdge_StaysStraight(leva kamera)")]
    [TestCase(-1.0, TestName = "BackProject_StraightEdge_StaysStraight(prava kamera)")]
    public void BackProject_StraightEdge_StaysStraight(double lateralM)
    {
        var mount = lateralM > 0 ? Profile.LeftCameraTransform : Profile.RightCameraTransform;

        var pts = BackProjectStraightEdge(mount, lateralM, fromM: 1.0, toM: 6.0, samples: 60);

        Assert.That(pts.Count, Is.GreaterThan(20), "předpoklad testu: hranice je v zorném poli");

        double dirDeg = Conversions.Rad2Deg(FitDirection(pts));
        Assert.That(dirDeg, Is.EqualTo(0).Within(1.0),
                    "rovná hranice se nesmí ohnout - směr musí vyjít rovnoběžně s cestou");

        // Přímost: největší odchylka bodu od proložené přímky.
        double dir = FitDirection(pts);
        double nx = -Math.Sin(dir), ny = Math.Cos(dir);
        double mx = 0, my = 0;
        foreach (var p in pts) { mx += p.X; my += p.Y; }
        mx /= pts.Count; my /= pts.Count;

        double maxOff = 0;
        foreach (var p in pts)
            maxOff = Math.Max(maxOff, Math.Abs((p.X - mx) * nx + (p.Y - my) * ny));

        Assert.That(maxOff, Is.LessThan(0.05),
                    "body musí ležet na přímce (do 5 cm - zbytek je kvantizace pixelu)");
    }

    /// <summary>
    /// <b>Jádro věci:</b> směr hranice nesmí záviset na tom, jak daleko hranice dosáhne. Právě
    /// tahle závislost byla nad záznamem naměřena (delší hranice = větší sbíhavost), takže kdyby
    /// vznikala v projekci, musí ji tenhle test ukázat.
    /// </summary>
    [Test]
    public void BackProject_Direction_DoesNotDependOnRange()
    {
        var mount = Profile.LeftCameraTransform;

        double near = Conversions.Rad2Deg(FitDirection(
            BackProjectStraightEdge(mount, 1.0, fromM: 1.0, toM: 2.5, samples: 40)));
        double far = Conversions.Rad2Deg(FitDirection(
            BackProjectStraightEdge(mount, 1.0, fromM: 1.0, toM: 6.0, samples: 40)));

        Assert.That(far - near, Is.EqualTo(0).Within(1.0),
                    $"směr se s dosahem nesmí měnit (blízko {near:F2}°, daleko {far:F2}°)");
    }

    /// <summary>
    /// Kontrola, že samotný <see cref="CorridorFinder"/> z rovných rovnoběžných hranic udělá
    /// koridor a nezahodí ho jako <see cref="CorridorReason.NotParallel"/>. Uzavírá řetěz
    /// projekce → proložení → rozhodnutí.
    /// </summary>
    [Test]
    public void CorridorFinder_FromBackProjectedStraightEdges_IsNotRejected()
    {
        var left = BackProjectStraightEdge(Profile.LeftCameraTransform, 1.0, 1.0, 6.0, 60);
        var right = BackProjectStraightEdge(Profile.RightCameraTransform, -1.0, 1.0, 6.0, 60);

        Assert.That(left.Count, Is.GreaterThan(20), "předpoklad testu: levá hranice je vidět");
        Assert.That(right.Count, Is.GreaterThan(20), "předpoklad testu: pravá hranice je vidět");

        var corridor = new CorridorFinder(new CorridorConfig()).Find(left, right);

        Assert.Multiple(() =>
        {
            Assert.That(corridor.Reason, Is.EqualTo(CorridorReason.Ok));
            Assert.That(Conversions.Rad2Deg(corridor.ParallelErrorRad), Is.LessThan(2.0),
                        "hranice jsou rovnoběžné, nerovnoběžnost musí být téměř nulová");
            Assert.That(corridor.Width, Is.EqualTo(2.0).Within(0.05));
            Assert.That(corridor.Lateral, Is.EqualTo(0.0).Within(0.05),
                        "robot je uprostřed mezi hranicemi");
        });
    }
}
