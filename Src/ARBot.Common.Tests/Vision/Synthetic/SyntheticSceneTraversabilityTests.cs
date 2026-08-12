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
