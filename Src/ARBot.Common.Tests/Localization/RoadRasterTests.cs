using ARBot.Common.Coordinates;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy rastru mapy pro korelaci (viz doc/map-correlation-localization.md).
/// Rastr ma stejne rozliseni i zarovnani jako occupancy grid, jen je o marzi vetsi.
/// </summary>
public class RoadRasterTests
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Sit s jedinou hranou z pocatku 20 m na vychod, sirka 4 m (polosirka 2 m).</summary>
    private static RoadNetwork StraightEastRoad(GeoReference origin)
    {
        var a = new Node(1, origin.ToLLA(0, 0), 4.0);
        var b = new Node(2, origin.ToLLA(20, 0), 4.0);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 20.0, wayId: 1, traversalCost: 20.0);
        return builder.Build();
    }

    private static RoadRaster Build(double marginM = 3.0)
    {
        var origin = Origin();
        var scene = new RoadScene(StraightEastRoad(origin), origin);
        // Grid 256 bunek po 5 cm se strede na (6,4 ; 0) => origin (0, -128).
        return RoadRaster.Build(scene, gridOriginX: 0, gridOriginY: -128,
                                gridSize: 256, resolution: 0.05, marginM: marginM);
    }

    [Test]
    public void Build_RozsiriRastrOMarziNaObeStrany()
    {
        var raster = Build(marginM: 3.0);

        // 3 m pri 5 cm = 60 bunek na kazdou stranu.
        Assert.That(raster.Size, Is.EqualTo(256 + 120));
        Assert.That(raster.OriginX, Is.EqualTo(-60));
        Assert.That(raster.OriginY, Is.EqualTo(-128 - 60));
    }

    [Test]
    public void TryIsRoad_NaOseVozovky_JeCesta()
    {
        var raster = Build();

        Assert.That(raster.TryIsRoad(10.0, 0.0, out bool isRoad), Is.True);
        Assert.That(isRoad, Is.True);
    }

    [Test]
    public void TryIsRoad_ZaPolosirkou_NeniCesta()
    {
        var raster = Build();

        Assert.That(raster.TryIsRoad(10.0, 3.0, out bool isRoad), Is.True);
        Assert.That(isRoad, Is.False);
    }

    [Test]
    public void TryIsRoad_MimoRastr_VraciFalse()
    {
        var raster = Build();

        // Daleko na zapad, mimo grid i marzi.
        Assert.That(raster.TryIsRoad(-50.0, 0.0, out _), Is.False);
    }

    [Test]
    public void TryIsRoad_SouhlasiSeScenouNaCelemRastru()
    {
        var origin = Origin();
        var scene = new RoadScene(StraightEastRoad(origin), origin);
        var raster = RoadRaster.Build(scene, 0, -128, 256, 0.05, 3.0);

        // Rastr nesmi byt jen "priblizne" scena - na strednich bodech bunek musi souhlasit presne.
        int checked_ = 0;
        for (int j = 0; j < raster.Size; j += 7)
        {
            for (int i = 0; i < raster.Size; i += 7)
            {
                double x = (raster.OriginX + i + 0.5) * 0.05;
                double y = (raster.OriginY + j + 0.5) * 0.05;
                Assert.That(raster.TryIsRoad(x, y, out bool isRoad), Is.True);
                Assert.That(isRoad, Is.EqualTo(scene.IsRoad(x, y)),
                            $"Rozpor v bunce ({i},{j}) = svet ({x:F3},{y:F3}).");
                checked_++;
            }
        }
        Assert.That(checked_, Is.GreaterThan(2000), "Test musi projit dost bunek, aby mel vypovidaci hodnotu.");
    }
}
