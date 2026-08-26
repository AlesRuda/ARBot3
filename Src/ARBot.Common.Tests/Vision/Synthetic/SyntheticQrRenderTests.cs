using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision.Qr;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision.Synthetic;

/// <summary>
/// <b>QR kod ve virtualni kamere</b> (viz doc/virtual-hw.md).
///
/// <para>Tohle je ten test, na kterem zalezi: kod se postavi do sceny jako svisla deska, virtualni
/// kamera vyrenderuje barevny obraz a <see cref="ZXingQrDecoder"/> ho z toho obrazu <b>precte
/// zpatky</b>. Tim je uzavrena cela cesta, kterou do 26. 8. 2026 v simulaci nesla projit — servisni
/// okno mise Robotour se necha dokoncit, misto aby se cekalo na zelezo.</para>
///
/// <para>Proti testum dekoderu samotneho je tu navic <b>perspektiva a rasterizace</b>: kod neni
/// dodany jako cisty obraz, ale prochazi projekci kamery.</para>
/// </summary>
public class SyntheticQrRenderTests
{
    private const int W = 640;
    private const int H = 480;

    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    private static RoadScene WideEastRoad()
    {
        var a = new Node(1, Origin().ToLLA(-100, 0), 50.0);
        var b = new Node(2, Origin().ToLLA(200, 0), 50.0);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 300.0, wayId: 1, traversalCost: 300.0);
        return new RoadScene(builder.Build(), Origin());
    }

    /// <summary>Kamera koukajici <b>vodorovne vpred</b> ve vysce 0,5 m — na desku pred robotem.</summary>
    private static CameraProjection ForwardCamera()
    {
        float fx = (float)(W / 2.0 / System.Math.Tan(Conversions.Deg2Rad(87.0) / 2.0));
        var intr = new Intrinsics
        {
            Width = W, Height = H, Fx = fx, Fy = fx, PPx = W / 2f, PPy = H / 2f,
            Model = Intrinsics.Distortion.None, Coeffs = new float[5],
        };
        var proj = new CameraProjection(intr, intr, Matrix4x4.Identity, Matrix4x4.Identity);
        proj.SetOrientation(Conversions.CameraToWorldTransform(0, 0, 0, new Vector3(0, 0, 0.5f)));
        return proj;
    }

    /// <summary>
    /// <b>Kod postaveny „pred kameru" musi byt v jejim vyhledu a KOLMO na ni</b> — i pro skutecnou
    /// montaz prave kamery, ktera je stocena o 29° vpravo a sklonena o 18,6° dolu.
    ///
    /// <para>Presne tohle prvni verze nedodrzela (nalezeno v aplikaci 26. 8. 2026): deska se stavela
    /// „1,5 m vpravo" a normalou mirila na STRED ROBOTA, takze byla za hranou vyhledu, a kdyz uz se
    /// dostala do obrazu, byla o tech 29° zkosena. Smer si proto bere z <b>montazni matice kamery</b>,
    /// ne z domnenky „vpravo je vpravo".</para>
    /// </summary>
    [Test]
    public void KodPredSkutecnouPravouKamerou_SePrecte()
    {
        const string text = "geo:50.0281,14.5212";
        var mount = ARBot.Common.Configuration.Profile.RightCameraTransform;
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };

        var options = new SyntheticSceneOptions { ColorNoise = 0, GrassRoughnessM = 0 };
        options.Billboards.Add(QrBillboard.InFrontOfCamera(text, mount, pose.X, pose.Y, pose.Theta,
                                                           distanceM: 1.2, heightM: 0.35, sizeM: 0.4));

        var proj = MountedCamera(mount);
        var rgb = new Image<BGR32>(W, H);
        new SyntheticFrameRenderer(WideEastRoad(), options).RenderColor(proj, pose, 0, rgb);

        var found = new ZXingQrDecoder().Decode(rgb.ToGray());

        Assert.That(found, Has.Length.EqualTo(1), "kod ma byt ve vyhledu prave kamery a citelny");
        Assert.That(found[0].Text, Is.EqualTo(text));
    }

    /// <summary>
    /// Deska je <b>kolma na vodorovny smer pohledu kamery</b>. Merí se to na sirce kodu v obraze:
    /// zkosena deska je uzsi nez kolma (pri 29° o 13 %), takze kolma musi dat <b>nejsirsi</b> obraz.
    /// </summary>
    [Test]
    public void KodPredKamerou_JeKolmyNaPohled()
    {
        var mount = ARBot.Common.Configuration.Profile.RightCameraTransform;
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };
        var proj = MountedCamera(mount);

        int perpendicular = CodeWidthPx(mount, pose, proj, extraYawRad: 0);
        int skewed = CodeWidthPx(mount, pose, proj, extraYawRad: System.Math.PI / 6);   // 30° zkoseni

        Assert.Multiple(() =>
        {
            Assert.That(perpendicular, Is.GreaterThan(0), "kod ma byt videt");
            Assert.That(perpendicular, Is.GreaterThan(skewed),
                        "kolma deska musi v obraze vyjit sirsi nez zkosena");
        });
    }

    /// <summary>Sirka bileho/cerneho vzoru kodu v obraze [px] — kolik sloupcu neni trava ani vozovka.</summary>
    private static int CodeWidthPx(Matrix4x4 mount, RobotState pose, CameraProjection proj,
                                   double extraYawRad)
    {
        var options = new SyntheticSceneOptions { ColorNoise = 0, GrassRoughnessM = 0 };
        var board = QrBillboard.InFrontOfCamera("geo:50.0281,14.5212", mount,
                                                pose.X, pose.Y, pose.Theta, 1.2, 0.35, 0.4);
        board.YawRad += extraYawRad;
        options.Billboards.Add(board);

        var rgb = new Image<BGR32>(W, H);
        new SyntheticFrameRenderer(WideEastRoad(), options).RenderColor(proj, pose, 0, rgb);

        // Kod je cistě cernobily; vozovka je seda 128 a trava zelena, takze bily pixel (255,255,255)
        // muze byt jen z kodu.
        var p = new BGR32 { Data = rgb.Data };
        int minX = int.MaxValue, maxX = int.MinValue;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                p.Index = (y * W + x) * 4;
                if (p.R != 255 || p.G != 255 || p.B != 255) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
            }
        return maxX < minX ? 0 : maxX - minX + 1;
    }

    /// <summary>Projekce kamery se <b>skutecnou</b> montazni matici z profilu.</summary>
    private static CameraProjection MountedCamera(Matrix4x4 mount)
    {
        float fx = (float)(W / 2.0 / System.Math.Tan(Conversions.Deg2Rad(87.0) / 2.0));
        var intr = new Intrinsics
        {
            Width = W, Height = H, Fx = fx, Fy = fx, PPx = W / 2f, PPy = H / 2f,
            Model = Intrinsics.Distortion.None, Coeffs = new float[5],
        };
        var proj = new CameraProjection(intr, intr, Matrix4x4.Identity, Matrix4x4.Identity);
        proj.SetOrientation(mount);
        return proj;
    }

    [Test]
    public void QrKodVeScene_SePrecteZVyrenderovanehoObrazu()
    {
        const string text = "geo:50.0281,14.5212";

        var options = new SyntheticSceneOptions { ColorNoise = 0, GrassRoughnessM = 0 };
        // Deska 0,4 x 0,4 m dva metry pred robotem, ve vysce kamery, celem k nemu.
        options.Billboards.Add(QrBillboard.Create(text, centerX: 2.0, centerY: 0.0, centerZ: 0.5,
                                                  yawRad: System.Math.PI, sizeM: 0.4));

        var rgb = new Image<BGR32>(W, H);
        new SyntheticFrameRenderer(WideEastRoad(), options)
            .RenderColor(ForwardCamera(), new RobotState { X = 0, Y = 0, Theta = 0 }, 0, rgb);

        var found = new ZXingQrDecoder().Decode(rgb.ToGray());

        Assert.That(found, Has.Length.EqualTo(1), "kod z virtualni kamery se ma precist");
        Assert.That(found[0].Text, Is.EqualTo(text));
    }

    /// <summary>
    /// <b>Bez desky se nic nepřečte.</b> Kontrola, ze predchozi test nemeri nahodu — a zaroven ze
    /// prazdna scena dekoder nerozbije (snimek bez kodu je normalni, ocekavany stav).
    /// </summary>
    [Test]
    public void PrazdnaScena_ZadnyKod()
    {
        var options = new SyntheticSceneOptions { ColorNoise = 0, GrassRoughnessM = 0 };

        var rgb = new Image<BGR32>(W, H);
        new SyntheticFrameRenderer(WideEastRoad(), options)
            .RenderColor(ForwardCamera(), new RobotState { X = 0, Y = 0, Theta = 0 }, 0, rgb);

        Assert.That(new ZXingQrDecoder().Decode(rgb.ToGray()), Is.Empty);
    }

    /// <summary>
    /// Deska <b>za</b> robotem se nevykresli — jinak by kod „prosvital" skrz robota a cetl se
    /// odkudkoli, cimz by simulace prestala testovat to, na cem v miси zalezi (kod musi byt
    /// ve vyhledu prave kamery).
    /// </summary>
    [Test]
    public void DeskaZaRobotem_NeniVidet()
    {
        var options = new SyntheticSceneOptions { ColorNoise = 0, GrassRoughnessM = 0 };
        options.Billboards.Add(QrBillboard.Create("geo:50.0281,14.5212",
                                                  centerX: -2.0, centerY: 0, centerZ: 0.5,
                                                  yawRad: 0, sizeM: 0.4));

        var rgb = new Image<BGR32>(W, H);
        new SyntheticFrameRenderer(WideEastRoad(), options)
            .RenderColor(ForwardCamera(), new RobotState { X = 0, Y = 0, Theta = 0 }, 0, rgb);

        Assert.That(new ZXingQrDecoder().Decode(rgb.ToGray()), Is.Empty);
    }

    /// <summary>Deska se kresli jen do BARVY, ne do hloubky — je to vizualni znacka, ne prekazka.</summary>
    [Test]
    public void Deska_NezasahujeDoHloubky()
    {
        var options = new SyntheticSceneOptions { ColorNoise = 0, GrassRoughnessM = 0, DepthNoiseM = 0 };
        var proj = ForwardCamera();
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };
        var renderer = new SyntheticFrameRenderer(WideEastRoad(), options);

        var without = new Image<Gray16>(W, H);
        renderer.RenderDepth(proj, pose, 0, without);

        options.Billboards.Add(QrBillboard.Create("geo:50.0281,14.5212", 2.0, 0, 0.5,
                                                  System.Math.PI, 0.4));
        var with = new Image<Gray16>(W, H);
        renderer.RenderDepth(proj, pose, 0, with);

        Assert.That(with.Data, Is.EqualTo(without.Data),
                    "hloubka se deskou nesmi zmenit - jinak by se stala prekazkou v occupancy gridu");
    }
}
