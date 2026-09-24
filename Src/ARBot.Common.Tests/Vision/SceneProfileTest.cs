using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Vision;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Testy <see cref="SceneProfile"/> - profilu sceny v jednom azimutu gridu. Hlavni zaruka:
    /// profil ukazuje TYTEZ body, ze kterych vznikl grid (pocty v prstencich sedi na bod), a jeho
    /// vysvetleni klasifikace dava tutez tridu jako <see cref="CameraFrameProcessor"/>.
    /// Geometrie je tataz jako v <see cref="CameraFrameProcessorTest"/> (kamera mirici dolu).
    /// </summary>
    public class SceneProfileTest
    {
        private const int W = 64, H = 64;
        private const float Hc = 1.0f;
        private const float F = 10f;

        private sealed class FakeProjection : IDepthCameraProjection
        {
            public Matrix4x4 Transformation { get; private set; }
            public Point2D[,] Camera2DToCamera3D { get; }
            public FakeProjection(Point2D[,] table, Matrix4x4 t) { Camera2DToCamera3D = table; Transformation = t; }
            public void SetOrientation(Matrix4x4 transform) => Transformation = transform;
            public List<Point4D> GetPointCloud(Image<Gray16> depth) => throw new NotImplementedException();
            public List<Point2D> TargetPoly => throw new NotImplementedException();
            public List<Point4D> TransformBack(List<Point> points, Image<Gray16> depth) => throw new NotImplementedException();
        }

        private static FakeProjection MakeProjection()
        {
            var table = new Point2D[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    table[y, x] = new Point2D((x - W / 2f) / F, (y - H / 2f) / F);
            return new FakeProjection(table, new Matrix4x4(
                1, 0, 0, 0,
                0, -1, 0, 0,
                0, 0, -1, 0,
                0, 0, Hc, 1));
        }

        private static Image<Gray16> Depth(Func<int, int, ushort> distMm)
        {
            var img = new Image<Gray16>(W, H);
            var d = img.Data;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    ushort v = distMm(x, y);
                    int idx = (y * W + x) * 2;
                    d[idx] = (byte)(v & 0xFF);
                    d[idx + 1] = (byte)(v >> 8);
                }
            return img;
        }

        private static PolarGridConfig TestConfig() => new PolarGridConfig
        {
            ColumnsPerCell = 16,
            TargetPointsPerCell = 12,
            MinPointsPerCell = 8,
            MinRangeM = 0.3f,
            MaxRangeM = 5.0f,
            MinRadialStepM = 0.05f,
            AssumedValidFraction = 1.0f,
        };

        private static (SceneProfile profile, PolarTraversabilityGrid grid) Build(
            Image<Gray16> depth, int azimuth, PolarGridConfig cfg = null)
        {
            cfg ??= TestConfig();
            var proj = MakeProjection();
            var grid = new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = proj }, cfg).BuildGrid(depth, proj);
            Assert.That(grid, Is.Not.Null);
            return (SceneProfile.Extract(depth, proj, grid, azimuth, cfg), grid);
        }

        [Test]
        public void FlatGround_PointsAtZeroAndVerdictMatchesGrid()
        {
            var (p, grid) = Build(Depth((x, y) => (ushort)(Hc * 1000)), azimuth: 1);

            Assert.That(p.ColumnFrom, Is.EqualTo(16));
            Assert.That(p.ColumnTo, Is.EqualTo(32));
            Assert.That(p.Points.Length, Is.EqualTo(16 * H), "vsechny pixely sloupcu bunky jsou platne");
            Assert.That(p.Points.All(q => q.Column >= 16 && q.Column < 32));
            Assert.That(p.Points.Max(q => Math.Abs(q.Z)), Is.LessThan(1e-3f), "rovna zem z = 0");

            Assert.That(p.Cells.Length, Is.EqualTo(grid.RadialCount));
            Assert.That(p.Mismatches, Is.EqualTo(0), "vysvetleni dava tutez tridu jako grid");
            Assert.That(p.Verdicts.Any(v => v.Evaluated), Is.True);
            foreach (var v in p.Verdicts.Where(v => v.Evaluated))
                Assert.That(Math.Abs(v.PlaneZ), Is.LessThan(1e-3f), "rovina rovne zeme lezi v z = 0");
        }

        [Test]
        public void PointsPerRing_EqualGridCellCounts()
        {
            // Nerovny teren (vlny v hloubce), aby se body rozlozily nerovnomerne.
            var depth = Depth((x, y) => (ushort)(Hc * 1000 - 40 * Math.Sin(x * 0.7) - 30 * Math.Cos(y * 0.5)));
            for (int a = 0; a < 4; a++)
            {
                var (p, grid) = Build(depth, a);
                for (int r = 0; r < grid.RadialCount; r++)
                    Assert.That(p.Points.Count(q => q.Radial == r), Is.EqualTo(grid[a, r].Count),
                        $"azimut {a}, prstenec {r}: profil ukazuje jine body, nez z jakych vznikla bunka");
                Assert.That(p.Mismatches, Is.EqualTo(0), $"azimut {a}");
            }
        }

        [Test]
        public void RaisedSector_ObstacleExplainedByHeight()
        {
            const float ho = 0.3f;
            var depth = Depth((x, y) => (ushort)((x < 16 ? (Hc - ho) : Hc) * 1000));
            var (p, _) = Build(depth, azimuth: 0);

            Assert.That(p.Points.Where(q => q.Radial >= 0).Average(q => q.Z), Is.EqualTo(ho).Within(0.01));
            Assert.That(p.Mismatches, Is.EqualTo(0));
            var obstacles = Enumerable.Range(0, p.Cells.Length)
                .Where(r => p.Cells[r].Class == TraversabilityClass.Obstacle).ToList();
            Assert.That(obstacles, Is.Not.Empty);
            foreach (int r in obstacles)
                Assert.That(p.Verdicts[r].TooHigh || p.Verdicts[r].TooSteep, Is.True,
                    $"prstenec {r}: prekazka ma byt vysvetlena vyskou nebo stoupanim");
        }

        [Test]
        public void WithoutGrid_OnlyRawPoints()
        {
            var depth = Depth((x, y) => (ushort)(Hc * 1000));
            var p = SceneProfile.Extract(depth, MakeProjection(), grid: null, azimuth: 2, columnsPerCell: 16);

            Assert.That(p.ColumnFrom, Is.EqualTo(32));
            Assert.That(p.Points.Length, Is.EqualTo(16 * H));
            Assert.That(p.Points.All(q => q.Radial == -1));
            Assert.That(p.Cells, Is.Null);
        }

        [Test]
        public void InvalidPixels_AreSkipped()
        {
            var depth = Depth((x, y) => (ushort)(y % 2 == 0 ? 0 : Hc * 1000));
            var (p, _) = Build(depth, azimuth: 0);
            Assert.That(p.Points.Length, Is.EqualTo(16 * H / 2));
        }
    }
}
