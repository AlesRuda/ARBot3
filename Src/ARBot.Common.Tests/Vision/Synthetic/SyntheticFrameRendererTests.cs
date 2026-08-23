using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision.Synthetic;

/// <summary>
/// Testy rasterizace virtualni kamery (viz doc/virtual-hw.md).
/// Klicovy je round-trip: co renderer vykresli, musi vize rozbalit zpet na spravnou geometrii.
/// </summary>
public class SyntheticFrameRendererTests
{
    private const int W = 64;
    private const int H = 48;

    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Siroka rovna vozovka podel osy vychod-zapad (cely vyhled kamery je na vozovce).</summary>
    private static RoadScene WideEastRoad(GeoReference origin, double widthMeters = 50.0)
    {
        var a = new Node(1, origin.ToLLA(-100, 0), widthMeters);
        var b = new Node(2, origin.ToLLA(200, 0), widthMeters);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 300.0, wayId: 1, traversalCost: 300.0);
        return new RoadScene(builder.Build(), origin);
    }

    /// <summary>Bezzkreslena (pinhole) intrinsika ze zadaneho horizontalniho FOV.</summary>
    private static Intrinsics Pinhole(int w, int h, double hfovDeg)
    {
        float fx = (float)(w / 2.0 / Math.Tan(Conversions.Deg2Rad(hfovDeg) / 2.0));
        return new Intrinsics
        {
            Width = w,
            Height = h,
            Fx = fx,
            Fy = fx,
            PPx = w / 2f,
            PPy = h / 2f,
            Model = Intrinsics.Distortion.None,
            Coeffs = new float[5],
        };
    }

    /// <summary>Projekce kamery ve vysce 0,5 m sklonene o 20 stupnu dolu.</summary>
    private static CameraProjection ForwardDownCamera()
    {
        var intr = Pinhole(W, H, 87.0);
        var proj = new CameraProjection(intr, intr, Matrix4x4.Identity, Matrix4x4.Identity);
        proj.SetOrientation(Conversions.CameraToWorldTransform(
            0, Conversions.Deg2Rad(-20), 0, new Vector3(0, 0, 0.5f)));
        return proj;
    }

    /// <summary>Robot v pocatku lokalni roviny otoceny na vychod.</summary>
    private static RobotState AtOriginFacingEast() => new RobotState { X = 0, Y = 0, Theta = 0 };

    /// <summary>
    /// Rozbali hloubku presne tak, jak to dela <c>CameraFrameProcessor.BuildGrid</c>
    /// (managed vetev): bod = Transform(ray * d, proj.Transformation).
    /// </summary>
    private static List<Vector3> Unproject(Image<Gray16> depth, CameraProjection proj)
    {
        var table = ((IDepthCameraProjection)proj).Camera2DToCamera3D;
        var m = proj.Transformation;
        var data = depth.Data;
        var points = new List<Vector3>();

        for (int y = 0; y < depth.Height; y++)
            for (int x = 0; x < depth.Width; x++)
            {
                int o = (y * depth.Width + x) * 2;
                int d = data[o] | (data[o + 1] << 8);
                if (d <= 0 || d >= 65535) continue;

                float dm = d * 0.001f;
                var ray = table[y, x];
                points.Add(Vector3.Transform(new Vector3(ray.X * dm, ray.Y * dm, dm), m));
            }

        return points;
    }

    [Test]
    public void RenderDepth_FlatRoadWithoutNoise_UnprojectsOntoGroundPlane()
    {
        var origin = Origin();
        var options = new SyntheticSceneOptions
        {
            DepthNoiseM = 0,
            GrassRoughnessM = 0,
            MaxRangeM = 20,
        };
        var renderer = new SyntheticFrameRenderer(WideEastRoad(origin), options);

        var proj = ForwardDownCamera();
        var depth = new Image<Gray16>(W, H);

        renderer.RenderDepth(proj, AtOriginFacingEast(), frameIndex: 0, depth);

        var points = Unproject(depth, proj);

        Assert.That(points, Is.Not.Empty, "kamera sklonena k zemi musi videt vozovku");
        Assert.That(points.TrueForAll(p => Math.Abs(p.Z) < 0.02f), Is.True,
                    "vsechny platne pixely maji lezet v rovine vozovky z = 0");
    }

    /// <summary>
    /// Uzka vozovka: pixely mimo ni maji dopadnout na vyvysenou rovinu travy, ne na vozovku.
    /// </summary>
    [Test]
    public void RenderDepth_OffRoad_UnprojectsOntoGrassPlane()
    {
        var origin = Origin();
        var options = new SyntheticSceneOptions
        {
            DepthNoiseM = 0,
            GrassRoughnessM = 0,
            GrassHeightM = 0.20,
            MaxRangeM = 20,
        };
        var renderer = new SyntheticFrameRenderer(WideEastRoad(origin, widthMeters: 4.0), options);

        var proj = ForwardDownCamera();
        var depth = new Image<Gray16>(W, H);

        renderer.RenderDepth(proj, AtOriginFacingEast(), frameIndex: 0, depth);

        var points = Unproject(depth, proj);
        int onRoad = points.Count(p => Math.Abs(p.Z) < 0.02f);
        int onGrass = points.Count(p => Math.Abs(p.Z - 0.20f) < 0.02f);

        // Od 23. 8. 2026 je na okraji cesty jeste SVISLA STENA (viz doc/virtual-hw.md), takze
        // tenka cara pixelu lezi MEZI obema rovinami. Drive tam nelezelo nic - byla to dira.
        int onWall = points.Count(p => p.Z >= 0.02f && p.Z <= 0.18f);

        Assert.Multiple(() =>
        {
            Assert.That(onRoad, Is.GreaterThan(0), "cast vyhledu ma padnout na vozovku");
            Assert.That(onGrass, Is.GreaterThan(0), "cast vyhledu ma padnout na travu");
            Assert.That(onRoad + onGrass + onWall, Is.EqualTo(points.Count),
                        "kazdy platny pixel lezi na vozovce, na trave, nebo na stene mezi nimi");
            Assert.That(onWall, Is.LessThan(points.Count / 10),
                        "stena je tenka cara podel hranice, ne plocha");
        });
    }

    /// <summary>Vyrenderuje hloubku dane sceny/nastaveni pro zadany snimek.</summary>
    private static Image<Gray16> RenderDepthFrame(SyntheticSceneOptions options, int frameIndex,
                                                  CameraProjection proj, double roadWidth = 50.0)
    {
        var renderer = new SyntheticFrameRenderer(WideEastRoad(Origin(), roadWidth), options);
        var depth = new Image<Gray16>(W, H);
        renderer.RenderDepth(proj, AtOriginFacingEast(), frameIndex, depth);
        return depth;
    }

    [Test]
    public void RenderDepth_WithNoise_ScattersAroundExactPlane()
    {
        var options = new SyntheticSceneOptions
        {
            DepthNoiseM = 0.02,
            GrassRoughnessM = 0,
            MaxRangeM = 20,
        };
        var proj = ForwardDownCamera();

        var points = Unproject(RenderDepthFrame(options, 0, proj), proj);

        Assert.That(points, Is.Not.Empty);
        double mean = points.Average(p => (double)p.Z);
        double sd = Math.Sqrt(points.Average(p => (p.Z - mean) * (p.Z - mean)));

        Assert.Multiple(() =>
        {
            Assert.That(sd, Is.GreaterThan(0.002), "sum se ma v datech projevit");
            Assert.That(Math.Abs(mean), Is.LessThan(0.02), "sum ma byt vycentrovany na rovine");
        });
    }

    /// <summary>
    /// Drsnost travy rozhazi vysku POUZE mimo vozovku - vozovka zustava presna rovina.
    /// </summary>
    [Test]
    public void RenderDepth_GrassRoughness_AffectsOnlyGrass()
    {
        var options = new SyntheticSceneOptions
        {
            DepthNoiseM = 0,
            GrassRoughnessM = 0.05,
            GrassHeightM = 0.20,
            MaxRangeM = 20,
        };
        var proj = ForwardDownCamera();

        var points = Unproject(RenderDepthFrame(options, 0, proj, roadWidth: 4.0), proj);

        // Delime podle GEOMETRIE, ne podle vysky: robot stoji v ose 4 m siroke cesty otoceny na
        // vychod, takze v jeho ramci je vozovka pas |Y| <= 2 m. Pas kolem hrany vynechavame -
        // tam do sebe okluze a drsnost zasahuji.
        var road = points.Where(p => Math.Abs(p.Y) < 1.8f).Select(p => (double)p.Z).ToList();
        var grass = points.Where(p => Math.Abs(p.Y) > 2.2f).Select(p => (double)p.Z).ToList();

        Assert.That(road, Is.Not.Empty);
        Assert.That(grass, Is.Not.Empty);

        double grassMean = grass.Average();
        double grassSd = Math.Sqrt(grass.Average(z => (z - grassMean) * (z - grassMean)));

        Assert.Multiple(() =>
        {
            Assert.That(road.TrueForAll(z => Math.Abs(z) < 0.02), Is.True,
                        "vozovka ma zustat presna rovina");
            Assert.That(grassSd, Is.GreaterThan(0.01), "trava ma byt zdrsnena");
        });
    }

    [Test]
    public void RenderDepth_SameSeedAndFrame_IsBitwiseReproducible()
    {
        var options = new SyntheticSceneOptions { DepthNoiseM = 0.02, MaxRangeM = 20 };
        var proj = ForwardDownCamera();

        var first = RenderDepthFrame(options, 7, proj);
        var second = RenderDepthFrame(options, 7, proj);

        Assert.That(second.Data, Is.EqualTo(first.Data));
    }

    [Test]
    public void RenderDepth_DifferentFrameIndex_ChangesNoise()
    {
        var options = new SyntheticSceneOptions { DepthNoiseM = 0.02, MaxRangeM = 20 };
        var proj = ForwardDownCamera();

        var first = RenderDepthFrame(options, 0, proj);
        var second = RenderDepthFrame(options, 1, proj);

        Assert.That(second.Data, Is.Not.EqualTo(first.Data));
    }

    /// <summary>Barva pixelu jako trojice (B, G, R) z <see cref="Image{T}"/> dat.</summary>
    private static (byte b, byte g, byte r) ColorAt(Image<BGR32> rgb, int x, int y)
    {
        int o = (y * rgb.Width + x) * 4;
        return (rgb.Data[o], rgb.Data[o + 1], rgb.Data[o + 2]);
    }

    private static List<(byte b, byte g, byte r)> AllColors(Image<BGR32> rgb)
    {
        var list = new List<(byte, byte, byte)>(rgb.Width * rgb.Height);
        for (int y = 0; y < rgb.Height; y++)
            for (int x = 0; x < rgb.Width; x++)
                list.Add(ColorAt(rgb, x, y));
        return list;
    }

    private static Image<BGR32> RenderColorFrame(SyntheticSceneOptions options, CameraProjection proj,
                                                 double roadWidth = 4.0, int frameIndex = 0)
    {
        var renderer = new SyntheticFrameRenderer(WideEastRoad(Origin(), roadWidth), options);
        var rgb = new Image<BGR32>(W, H);
        renderer.RenderColor(proj, AtOriginFacingEast(), frameIndex, rgb);
        return rgb;
    }

    [Test]
    public void RenderColor_WithoutNoise_UsesOnlyRoadAndGrassColors()
    {
        var options = new SyntheticSceneOptions { ColorNoise = 0 };
        var colors = AllColors(RenderColorFrame(options, ForwardDownCamera()));

        var road = (options.RoadB, options.RoadG, options.RoadR);
        var grass = (options.GrassB, options.GrassG, options.GrassR);

        Assert.Multiple(() =>
        {
            Assert.That(colors.Count(c => c == road), Is.GreaterThan(0), "vozovka ma byt videt");
            Assert.That(colors.Count(c => c == grass), Is.GreaterThan(0), "trava ma byt videt");
            Assert.That(colors.TrueForAll(c => c == road || c == grass), Is.True,
                        "bez sumu smi vzniknout jen tyto dve barvy");
        });
    }

    /// <summary>Nad horizontem neni zadny povrch - podle zadani je i tam zelena jako trava.</summary>
    [Test]
    public void RenderColor_AboveHorizon_IsGrassColor()
    {
        var options = new SyntheticSceneOptions { ColorNoise = 0 };

        var intr = Pinhole(W, H, 87.0);
        var up = new CameraProjection(intr, intr, Matrix4x4.Identity, Matrix4x4.Identity);
        up.SetOrientation(Conversions.CameraToWorldTransform(
            0, Conversions.Deg2Rad(60), 0, new Vector3(0, 0, 0.5f)));   // kamera k obloze

        var colors = AllColors(RenderColorFrame(options, up));
        var grass = (options.GrassB, options.GrassG, options.GrassR);

        Assert.That(colors.TrueForAll(c => c == grass), Is.True);
    }

    [Test]
    public void RenderDepth_DifferentSeed_ChangesNoise()
    {
        var proj = ForwardDownCamera();

        var first = RenderDepthFrame(new SyntheticSceneOptions { DepthNoiseM = 0.02, MaxRangeM = 20, Seed = 1 }, 0, proj);
        var second = RenderDepthFrame(new SyntheticSceneOptions { DepthNoiseM = 0.02, MaxRangeM = 20, Seed = 2 }, 0, proj);

        Assert.That(second.Data, Is.Not.EqualTo(first.Data));
    }

    // ==================== Svisla stena na rozhrani cesty a travy (23. 8. 2026) ====================

    /// <summary>
    /// REGRESE: drsnost travy nesmi delat diry v hloubce podel hranice cesty.
    ///
    /// <para><b>Co se delo.</b> <c>Trace</c> prijimal zasah roviny vozovky jen kdyz bod lezi
    /// NA ceste a zasah roviny travy jen kdyz na ceste NENI. Drsnost obe roviny rozdvoji, takze
    /// u paprsku miricich na hranici padl zasah vozovky tesne VEN a zasah travy tesne DOVNITR -
    /// neprosel ani jeden a pixel zustal nulovy. Vysledkem byla tenka cara chybejici hloubky podel
    /// cele hranice; detektor hranic hleda presne tam, takze <b>22,9 % jeho sloupcu nemelo metricky
    /// bod</b> (nameřeno nad zaznamem 20260822-230538).</para>
    ///
    /// <para>Fyzikalne je to spatne: trava ma vysku, takze na okraji cesty stoji <b>svisla stena</b>
    /// a paprsek do ni narazi. Viz doc/virtual-hw.md.</para>
    /// </summary>
    [Test]
    public void RenderDepth_GrassRoughness_LeavesNoHolesAlongRoadEdge()
    {
        var proj = ForwardDownCamera();

        // Uzka cesta, aby hranice byla ve vyhledu; bez sumu hloubky, at meri jen tu vadu.
        var smooth = RenderDepthFrame(
            new SyntheticSceneOptions { GrassRoughnessM = 0, DepthNoiseM = 0, MaxRangeM = 20 }, 0, proj, roadWidth: 4.0);
        var rough = RenderDepthFrame(
            new SyntheticSceneOptions { GrassRoughnessM = 0.03, DepthNoiseM = 0, MaxRangeM = 20 }, 0, proj, roadWidth: 4.0);

        int holesSmooth = CountZeros(smooth), holesRough = CountZeros(rough);

        Assert.That(holesRough, Is.EqualTo(holesSmooth),
                    $"drsnost travy nesmi ubrat pixely s hloubkou (hladka {holesSmooth}, drsna {holesRough})");
    }

    /// <summary>
    /// Stena musi lezet <b>mezi</b> obema rovinami — nikdy ne pred rovinou travy. Jinak by se
    /// oprava projevila prave tim, ceho se autor obaval: ze se trava rendruje bliz, nez ve
    /// skutecnosti je.
    ///
    /// <para><b>Analyticka mez.</b> Kamera je ve vysce 0,5 m, obe roviny jsou vodorovne, takze pro
    /// tentyz paprsek plati <c>s(vyska h) / s(vyska 0) = (0,5 − h) / 0,5</c>. Pri
    /// <c>GrassHeightM = 0,20</c> tedy zadny pixel nesmi hlasit min nez <b>0,6×</b> hloubky nad
    /// rovnou zemi — a zaroven ne vic nez tu rovnou zem (rovina vozovky je nejdal, co muze byt).</para>
    /// </summary>
    [Test]
    public void RenderDepth_Wall_LiesBetweenBothPlanes()
    {
        var proj = ForwardDownCamera();
        var flat = RenderDepthFrame(
            new SyntheticSceneOptions { GrassHeightM = 0, GrassRoughnessM = 0, DepthNoiseM = 0, MaxRangeM = 20 },
            0, proj, roadWidth: 4.0);
        var raised = RenderDepthFrame(
            new SyntheticSceneOptions { GrassHeightM = 0.20, GrassRoughnessM = 0, DepthNoiseM = 0, MaxRangeM = 20 },
            0, proj, roadWidth: 4.0);

        const double ratio = (0.5 - 0.20) / 0.5;   // rovina travy je nejbliz, co paprsek muze trefit
        const double epsM = 0.002;                 // hloubka je v milimetrech, plus zaokrouhleni

        int compared = 0;
        double worstNear = 0, worstFar = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double f = flat[x, y].Value / 1000.0, r = raised[x, y].Value / 1000.0;
                if (f <= 0 || r <= 0) continue;
                compared++;
                worstNear = Math.Max(worstNear, ratio * f - r);   // > 0 = bliz nez rovina travy
                worstFar = Math.Max(worstFar, r - f);             // > 0 = dal nez rovina vozovky
            }

        Assert.That(compared, Is.GreaterThan(100), "predpoklad testu: je co porovnavat");
        Assert.Multiple(() =>
        {
            Assert.That(worstNear, Is.LessThan(epsM),
                        $"zadny pixel nesmi byt bliz nez rovina travy (nejhorsi {worstNear:F3} m)");
            Assert.That(worstFar, Is.LessThan(epsM),
                        $"zadny pixel nesmi byt dal nez rovina vozovky (nejhorsi {worstFar:F3} m)");
        });
    }

    private static int CountZeros(Image<Gray16> depth)
    {
        int zeros = 0;
        for (int y = 0; y < depth.Height; y++)
            for (int x = 0; x < depth.Width; x++)
                if (depth[x, y].Value == 0) zeros++;
        return zeros;
    }
}
