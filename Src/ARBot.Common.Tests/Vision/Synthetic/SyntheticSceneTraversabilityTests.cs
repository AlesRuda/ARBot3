using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision;
using ARBot.Common.Vision.Synthetic;

namespace ARBot.Common.Tests.Vision.Synthetic;

/// <summary>
/// Konec retezu: hloubka z virtualni kamery prohnana polarnim gridem sjizdnosti.
/// Vozovka ma vyjit jako sjizdna, vyvysena trava jako prekazka - to je duvod, proc
/// simulace vznikla (viz doc/virtual-hw.md).
/// </summary>
public class SyntheticSceneTraversabilityTests
{
    // Rozliseni hloubky jako u realne D435 v teto aplikaci.
    private const int W = 480;
    private const int H = 270;

    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Rovna vozovka sirky 4 m podel osy vychod-zapad, robot stoji v jeji ose.</summary>
    private static RoadScene NarrowEastRoad(GeoReference origin)
    {
        var a = new Node(1, origin.ToLLA(-50, 0), 4.0);
        var b = new Node(2, origin.ToLLA(100, 0), 4.0);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 150.0, wayId: 1, traversalCost: 150.0);
        return new RoadScene(builder.Build(), origin);
    }

    private static CameraProjection Camera()
    {
        float fx = (float)(W / 2.0 / Math.Tan(Conversions.Deg2Rad(87.0) / 2.0));
        var intr = new Intrinsics
        {
            Width = W,
            Height = H,
            Fx = fx,
            Fy = fx,
            PPx = W / 2f,
            PPy = H / 2f,
            Model = Intrinsics.Distortion.None,
            Coeffs = new float[5],
        };

        var proj = new CameraProjection(intr, intr, Matrix4x4.Identity, Matrix4x4.Identity);
        proj.SetOrientation(Conversions.CameraToWorldTransform(
            0, Conversions.Deg2Rad(-20), 0, new Vector3(0, 0, 0.5f)));
        return proj;
    }

    /// <summary>
    /// <b>Trava VYSSI NEZ KAMERA z hloubky zmizi</b> misto aby byla prekazkou.
    ///
    /// <para>Renderer strili paprsek proti rovine travy <c>z = GrassHeightM</c>:
    /// <c>s = (height - eye.Z) / dir.Z</c>. Kamera je u tohoto robota ve vysce <b>0,522 m</b>
    /// a sklopena 20,2° dolu, takze vetsina paprsku ma <c>dir.Z &lt; 0</c>. Kdyz je trava VYS nez
    /// kamera, je citatel kladny a <c>s</c> vyjde <b>negativni</b> - zasah se zahodi. Paprsek pak
    /// dopadne na rovinu vozovky mimo cestu, kde neplati <c>IsRoad</c>, takze pixel propadne jako
    /// <c>Surface.None</c> a hloubka tam <b>vubec neni</b>.</para>
    ///
    /// <para>Dusledek pro sjizdnost: bunky nad travou nemaji body, takze nejsou
    /// <c>Obstacle</c>, ale <c>Unknown</c> — a "neznamo" se chova jinak nez "prekazka".
    /// Reportovano z UI (24. 8. 2026): "nastavil jsem vysku travy 1 m, ale traverzabilita to
    /// nevidi". Test to <b>dokumentuje</b>, nema to za spravne chovani.</para>
    /// </summary>
    /// <summary>Rovna vozovka zadane sirky podel osy vychod-zapad, robot stoji v jeji ose.</summary>
    private static RoadScene EastRoad(GeoReference origin, double width)
    {
        var a = new Node(1, origin.ToLLA(-50, 0), width);
        var b = new Node(2, origin.ToLLA(100, 0), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 150.0, wayId: 1, traversalCost: 150.0);
        return new RoadScene(builder.Build(), origin);
    }

    /// <summary>Kamera s realnou montazi z <see cref="Configuration.Profile"/> (yaw 29°, pitch -20,2°).</summary>
    private static CameraProjection RealMountCamera()
    {
        float fx = (float)(W / 2.0 / Math.Tan(Conversions.Deg2Rad(87.0) / 2.0));
        var intr = new Intrinsics
        {
            Width = W, Height = H, Fx = fx, Fy = fx, PPx = W / 2f, PPy = H / 2f,
            Model = Intrinsics.Distortion.None, Coeffs = new float[5],
        };
        var proj = new CameraProjection(intr, intr, Matrix4x4.Identity, Matrix4x4.Identity);
        proj.SetOrientation(ARBot.Common.Configuration.Profile.LeftCameraTransform);
        return proj;
    }

    /// <summary>
    /// <b>Nizka trava na UZKE ceste se ztraci</b> — na 2m ceste je pri vysce 0,15 m vetsina bunek
    /// v trave <c>Free</c>, na 4m ceste tataz vyska funguje. Efekt zavisi na sirce cesty.
    ///
    /// <para><b>Mechanismus NEZNAME.</b> Dva vyklady uz padly a nemaji se zkouset znovu:</para>
    /// <list type="number">
    ///   <item>„Referencni rovina se prolozi travou, protoze blizke pole je na uzke ceste prevazne
    ///   trava" — <b>vyvraceno</b>: do fitu jde 591 bunek z CESTY (prum. z 0,002 m) proti 162
    ///   z travy (0,141 m), cesta tedy dominuje 78 : 22. Test to i tiskne.</item>
    ///   <item>Nativni SIMD transform (<c>UseNativeTransform</c>, ktery runtime zapina a testy ne)
    ///   — <b>vyvraceno</b>: managed i nativni cesta daji bit za bit tataz cisla.</item>
    /// </list>
    ///
    /// <para><b>A hlavne: v BEZICI APLIKACI se to nepodarilo zreprodukovat vubec.</b> Autor hlasi
    /// (24. 8. 2026), ze sjizdnost travu nevidi ani pri 0,15 m, ani pri 0,25 m — kde tenhle test
    /// dava 440 bunek prekazky proti 1 volne. Rozdil mezi touto knihovnou a behem aplikace je
    /// <b>neobjasneny</b>; tenhle test tedy hlida chovani KNIHOVNY, netvrdi nic o UI.</para>
    /// </summary>
    [Test]
    public void NizkaTravaNaUzkeCeste_seZtraci_zavisiNaSirceCesty()
    {
        var proj = RealMountCamera();
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };

        (int free, int obstacle) Grass(double roadWidth, double grassHeight, bool useNative = false)
        {
            var opts = new SyntheticSceneOptions { MaxRangeM = 10, GrassHeightM = grassHeight,
                                                   GrassRoughnessM = 0.030, DepthNoiseM = 0.003 };
            var depth = new Image<Gray16>(W, H);
            new SyntheticFrameRenderer(EastRoad(Origin(), roadWidth), opts)
                .RenderDepth(proj, pose, 0, depth);

            // POZOR: runtime pouziva UseNativeTransform = true (ARBotRuntime), testy vychozi false.
            var cfg = new PolarGridConfig { UseNativeTransform = useNative };
            var grid = new CameraFrameProcessor(_ => proj, cfg).BuildGrid(depth, proj);
            int f = 0, o = 0;
            foreach (var cell in grid.Cells)
            {
                if (cell.Class == TraversabilityClass.Unknown) continue;
                if (Math.Abs(cell.MeanY) <= roadWidth / 2 + 0.5) continue;   // jen bezpecne v trave
                if (cell.Class == TraversabilityClass.Free) f++; else o++;
            }
            return (f, o);
        }

        // Z CEHO se sklada sada pro fit referencni roviny (tytez podminky jako
        // CameraFrameProcessor.FitReferencePlane: dost bodu, do PlaneFitMaxRangeM, |MeanZ| pod
        // PlaneFitMaxAbsHeightM). Bez tohoto rozpadu je jakykoli vyklad "rovina se prolozila travou"
        // jen domnenka.
        void PlaneFitSet(double roadWidth, double grassHeight)
        {
            var cfg = new PolarGridConfig();
            var opts = new SyntheticSceneOptions { MaxRangeM = 10, GrassHeightM = grassHeight,
                                                   GrassRoughnessM = 0.030, DepthNoiseM = 0.003 };
            var depth = new Image<Gray16>(W, H);
            new SyntheticFrameRenderer(EastRoad(Origin(), roadWidth), opts)
                .RenderDepth(proj, pose, 0, depth);
            var grid = new CameraFrameProcessor(_ => proj, cfg).BuildGrid(depth, proj);

            int roadN = 0, grassN = 0; double roadZ = 0, grassZ = 0;
            float maxR2 = cfg.PlaneFitMaxRangeM * cfg.PlaneFitMaxRangeM;
            foreach (var c in grid.Cells)
            {
                if (c.Count < cfg.MinPointsPerCell) continue;
                if (c.MeanX * c.MeanX + c.MeanY * c.MeanY > maxR2) continue;
                if (Math.Abs(c.MeanZ) > cfg.PlaneFitMaxAbsHeightM) continue;
                if (Math.Abs(c.MeanY) <= roadWidth / 2) { roadN++; roadZ += c.MeanZ; }
                else { grassN++; grassZ += c.MeanZ; }
            }
            TestContext.Out.WriteLine(
                $"  cesta {roadWidth:F1} m, trava {grassHeight:F2} m -> do fitu roviny jde "
                + $"{roadN} bunek z CESTY (prum. z {(roadN > 0 ? roadZ / roadN : 0):F3} m) "
                + $"a {grassN} z TRAVY (prum. z {(grassN > 0 ? grassZ / grassN : 0):F3} m)");
        }

        TestContext.Out.WriteLine("SLOZENI sady pro fit referencni roviny:");
        PlaneFitSet(2.0, 0.15);
        PlaneFitSet(2.0, 0.25);
        PlaneFitSet(4.0, 0.15);
        TestContext.Out.WriteLine();

        TestContext.Out.WriteLine("sirka cesty / vyska travy -> bunky v trave (free / obstacle):");
        TestContext.Out.WriteLine("                            managed              NATIVNI (jako runtime)");
        foreach (double roadWidth in new[] { 2.0, 4.0 })
            foreach (double gh in new[] { 0.15, 0.20, 0.25, 0.35 })
            {
                var m = Grass(roadWidth, gh);
                var n = Grass(roadWidth, gh, useNative: true);
                TestContext.Out.WriteLine($"  cesta {roadWidth:F1} m, trava {gh:F2} m  ->  "
                                          + $"free={m.free,4} obst={m.obstacle,4}"
                                          + $"      free={n.free,4} obst={n.obstacle,4}");
            }

        var narrowAtLimit = Grass(2.0, 0.15);
        var narrowAbove = Grass(2.0, 0.35);

        Assert.That(narrowAbove.obstacle, Is.GreaterThan(narrowAtLimit.obstacle),
                    "vyssi trava nad limitem fitu roviny musi byt videt lip nez trava PRESNE na limitu");
    }

    [Test]
    public void TravaVyssiNezKamera_zHloubkyZmizi_neniPrekazkou()
    {
        var proj = Camera();
        var scene = NarrowEastRoad(Origin());
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };

        // 0,25 m = pod kamerou (0,522 m) -> tráva se renderuje.
        // 1,00 m = nad kamerou            -> paprsky dolu ji minou.
        int ValidPixels(double grassHeight)
        {
            var opts = new SyntheticSceneOptions { MaxRangeM = 10, GrassHeightM = grassHeight };
            var depth = new Image<Gray16>(W, H);
            new SyntheticFrameRenderer(scene, opts).RenderDepth(proj, pose, 0, depth);

            int valid = 0;
            for (int i = 0; i + 1 < depth.Data.Length; i += 2)
                if ((depth.Data[i] | (depth.Data[i + 1] << 8)) > 0) valid++;
            return valid;
        }

        // Klasifikace bunek NAD TRAVOU - to je to, na co si UI stezovalo.
        (int free, int obstacle, int unknown) Grass(double grassHeight,
                                                   double rough = 0, double depthNoise = 0)
        {
            var opts = new SyntheticSceneOptions { MaxRangeM = 10, GrassHeightM = grassHeight,
                                                   GrassRoughnessM = rough, DepthNoiseM = depthNoise };
            var depth = new Image<Gray16>(W, H);
            new SyntheticFrameRenderer(scene, opts).RenderDepth(proj, pose, 0, depth);

            var grid = new CameraFrameProcessor(_ => proj, new PolarGridConfig()).BuildGrid(depth, proj);
            int f = 0, o = 0, u = 0;
            foreach (var cell in grid.Cells)
            {
                if (Math.Abs(cell.MeanY) <= 3.0) continue;       // bezpecne v trave (vozovka 4 m)
                if (cell.Class == TraversabilityClass.Free) f++;
                else if (cell.Class == TraversabilityClass.Unknown) u++;
                else o++;
            }
            return (f, o, u);
        }

        // Sweep pres vysky: ukazuje, ze signal od travy s vyskou NEROSTE, ale nad vyskou kamery
        // KLESA. To je ta protiintuitivni cast - "zvysil jsem travu a je ji videt MIN".
        TestContext.Out.WriteLine("vyska travy -> bunky v trave (free / obstacle / unknown):");
        TestContext.Out.WriteLine("                 ideálni rovina        s vychozim sumem (0,003/0,030)");
        foreach (double gh in new[] { 0.05, 0.10, 0.15, 0.20, 0.25, 0.30, 0.40, 0.52, 0.80, 1.00, 1.50 })
        {
            var a = Grass(gh);
            var b = Grass(gh, rough: 0.030, depthNoise: 0.003);
            TestContext.Out.WriteLine(
                $"  {gh,4:F2} m  ->  f={a.free,3} o={a.obstacle,3} u={a.unknown,3}"
                + $"        f={b.free,3} o={b.obstacle,3} u={b.unknown,3}"
                + (gh > 0.522 ? "   (nad kamerou)" : ""));
        }

        TestContext.Out.WriteLine($"platnych pixelu hloubky: 0,25 m -> {ValidPixels(0.25)}, "
                                  + $"1,00 m -> {ValidPixels(1.00)}");

        Assert.Multiple(() =>
        {
            // Nizka trava funguje - to je stav, ktery hlida test nize.
            Assert.That(Grass(0.25).obstacle, Is.GreaterThan(0), "nizka trava ma byt prekazka");

            // A tady jsou ty degenerovane pripady, ktere se maji po oprave OBRATIT:
            Assert.That(Grass(0.52).obstacle, Is.Zero,
                        "DNESNI VADA: v urovni kamery trava zmizi uplne (s = 0, zasah se zahodi)");
            Assert.That(Grass(1.50).obstacle + Grass(1.50).unknown, Is.Zero,
                        "DNESNI VADA: velmi vysoka trava nevyrobi v jejim miste ANI JEDNU bunku");
        });
    }

    [Test]
    public void PolarGrid_SeesRoadAsFreeAndGrassAsObstacle()
    {
        var proj = Camera();
        // Vyska travy 0,25 m: klasifikator povoluje odchylku vysky MaxHeightDev(r) = 0,03 + 0,02*r,
        // takze vychozich 0,10 m je prekazkou jen do ~3,5 m. Viz poznamka v doc/virtual-hw.md.
        var options = new SyntheticSceneOptions { MaxRangeM = 10, GrassHeightM = 0.25 };
        var renderer = new SyntheticFrameRenderer(NarrowEastRoad(Origin()), options);

        var depth = new Image<Gray16>(W, H);
        renderer.RenderDepth(proj, new RobotState { X = 0, Y = 0, Theta = 0 }, frameIndex: 0, depth);

        var processor = new CameraFrameProcessor(_ => proj, new PolarGridConfig());
        var grid = processor.BuildGrid(depth, proj);

        Assert.That(grid, Is.Not.Null, "z hloubky ma jit sestavit polarni grid");

        int freeOnRoad = 0, obstacleOnRoad = 0;
        int freeOnGrass = 0, obstacleOnGrass = 0;

        foreach (var cell in grid.Cells)
        {
            if (cell.Class == TraversabilityClass.Unknown) continue;

            double lateral = Math.Abs(cell.MeanY);
            if (lateral < 1.5)          // bezpecne uvnitr 4 m siroke vozovky
            {
                if (cell.Class == TraversabilityClass.Free) freeOnRoad++;
                else obstacleOnRoad++;
            }
            else if (lateral > 3.0)     // bezpecne v trave
            {
                if (cell.Class == TraversabilityClass.Free) freeOnGrass++;
                else obstacleOnGrass++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(freeOnRoad, Is.GreaterThan(0), "na vozovce maji byt sjizdne bunky");
            Assert.That(freeOnRoad, Is.GreaterThan(obstacleOnRoad),
                        "vozovka ma byt prevazne sjizdna");
            Assert.That(obstacleOnGrass, Is.GreaterThan(0), "trava ma byt videt jako prekazka");
            Assert.That(obstacleOnGrass, Is.GreaterThan(freeOnGrass),
                        "trava ma byt prevazne nesjizdna");
        });
    }
}
