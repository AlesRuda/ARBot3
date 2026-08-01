using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Vision;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Testy <see cref="CameraFrameProcessor"/> (vypocet polarniho gridu + probability primo do
    /// <see cref="CameraFrame"/>). Jadro <see cref="CameraFrameProcessor.BuildGrid"/> je prenesene
    /// z drivejsiho <c>DepthTraversabilityProcessor</c> - proto tu zustavaji i puvodni scenare
    /// (rovna zem, zvednuty sektor, ekvivalence nativni vs. managed transform).
    ///
    /// Geometrie je syntetizovana kamerou MIRICI PRIMO DOLU z vysky <c>Hc</c>: hloubka (Z osa kamery)
    /// odpovida vzdalenosti k rovine kolme na osu, takze rovna zem = konstantni hloubka. Transformace
    /// mapuje prostor kamery (X vpravo, Y dolu, Z od kamery) do robot-rel. ramce
    /// (X vychod, Y sever, Z nahoru): world = (camX, -camY, Hc - camZ). Rovna zem (z=0) je tedy
    /// hloubka = Hc; prekazka vysky <c>ho</c> = hloubka Hc - ho.
    /// </summary>
    public class CameraFrameProcessorTest
    {
        private const int W = 64, H = 64;
        private const float Hc = 1.0f;   // vyska kamery [m]
        private const float F = 10f;     // ohniskova vzdalenost [px]
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Kamera miric primo dolu ve vysce Hc: world = (camX, -camY, Hc - camZ).
        private static Matrix4x4 DownCamera() => new Matrix4x4(
            1, 0, 0, 0,
            0, -1, 0, 0,
            0, 0, -1, 0,
            0, 0, Hc, 1);

        private sealed class FakeProjection : IDepthCameraProjection
        {
            public Matrix4x4 Transformation { get; private set; }
            public Point2DF[,] Camera2DToCamera3D { get; }
            public FakeProjection(Point2DF[,] table, Matrix4x4 t) { Camera2DToCamera3D = table; Transformation = t; }
            public void SetOrientation(Matrix4x4 transform) => Transformation = transform;
            public List<Point4D> GetPointCloud(Image<Gray16> depth) => throw new NotImplementedException();
            public List<Point2D> TargetPoly => throw new NotImplementedException();
            public List<Point4D> TransformBack(List<Point> points, Image<Gray16> depth) => throw new NotImplementedException();
        }

        // Pinhole tabulka smeru paprsku (stred obrazu = 0), index [y, x].
        private static FakeProjection MakeProjection()
        {
            var table = new Point2DF[H, W];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    table[y, x] = new Point2DF((x - W / 2f) / F, (y - H / 2f) / F);
            return new FakeProjection(table, DownCamera());
        }

        // Hloubkovy obraz Gray16 (mm, little-endian).
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
            ColumnsPerCell = 16,          // -> 4 azimutove bunky
            TargetPointsPerCell = 12,
            MinPointsPerCell = 8,
            MinRangeM = 0.3f,
            MaxRangeM = 5.0f,
            MinRadialStepM = 0.05f,
            AssumedValidFraction = 1.0f,  // test ma 100% platnych pixelu
        };

        private static CameraFrameProcessor Proc(PolarGridConfig cfg = null)
            => new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = MakeProjection() }, cfg ?? TestConfig());

        private static (int free, int obstacle, int unknown, int populated) Counts(PolarTraversabilityGrid g)
        {
            int free = 0, obs = 0, unk = 0, pop = 0;
            foreach (var c in g.Cells)
            {
                if (c.Count > 0) pop++;
                switch (c.Class)
                {
                    case TraversabilityClass.Free: free++; break;
                    case TraversabilityClass.Obstacle: obs++; break;
                    default: unk++; break;
                }
            }
            return (free, obs, unk, pop);
        }

        [Test]
        public void BuildGrid_FlatGround_AllFreeNoObstacle()
        {
            var depth = Depth((x, y) => (ushort)(Hc * 1000));   // rovna zem
            var grid = Proc().BuildGrid(depth, MakeProjection());

            Assert.That(grid, Is.Not.Null);
            Assert.That(grid.AzimuthCount, Is.EqualTo(4));
            Assert.That(grid.RadialCount, Is.GreaterThan(0));

            var (free, obstacle, _, _) = Counts(grid);
            Assert.That(free, Is.GreaterThan(0), "aspon nejake Free bunky");
            Assert.That(obstacle, Is.EqualTo(0), "rovna zem nema mit prekazky");

            foreach (var c in grid.Cells)
                if (c.Class != TraversabilityClass.Unknown)
                {
                    Assert.That(c.Count, Is.GreaterThanOrEqualTo(8), "≥8 bodu na klasifikovanou bunku");
                    Assert.That(Math.Abs(c.MeanZ), Is.LessThan(0.05f), "rovna zem ~ z=0");
                    Assert.That(c.Confidence, Is.GreaterThan(0f));
                }
        }

        [Test]
        public void BuildGrid_RaisedSector_ProducesObstacleCells()
        {
            // Sloupce 0..15 (azimutova bunka 0) zvednute o 0.3 m (hloubka Hc-0.3), zbytek rovna zem.
            const float ho = 0.3f;
            var depth = Depth((x, y) => (ushort)((x < 16 ? (Hc - ho) : Hc) * 1000));

            var grid = Proc().BuildGrid(depth, MakeProjection());
            Assert.That(grid, Is.Not.Null);

            int R = grid.RadialCount;
            int obsBin0 = 0, freeBin3 = 0, obsBin3 = 0;
            for (int r = 0; r < R; r++)
            {
                if (grid[0, r].Class == TraversabilityClass.Obstacle) obsBin0++;
                if (grid[3, r].Class == TraversabilityClass.Free) freeBin3++;
                if (grid[3, r].Class == TraversabilityClass.Obstacle) obsBin3++;
            }

            Assert.That(obsBin0, Is.GreaterThan(0), "zvednuty sektor 0 ma mit prekazky");
            Assert.That(obsBin3, Is.EqualTo(0), "sektor 3 (rovny) nema mit prekazky");
            Assert.That(freeBin3, Is.GreaterThan(0), "sektor 3 ma mit sjizdne bunky");

            for (int r = 0; r < R; r++)
                if (grid[0, r].Class == TraversabilityClass.Obstacle)
                {
                    Assert.That(grid[0, r].MaxZ, Is.GreaterThan(0.15f));
                    break;
                }
        }

        [Test]
        public void BuildGrid_NativeTransform_MatchesManaged()
        {
            // Stejny vstup (rovna zem + zvednuty sektor) pres managed i nativni transform -> stejny grid.
            const float ho = 0.3f;
            Image<Gray16> Scene() => Depth((x, y) => (ushort)((x < 16 ? (Hc - ho) : Hc) * 1000));

            var cfgM = TestConfig();
            var cfgN = TestConfig();
            cfgN.UseNativeTransform = true;

            var gM = Proc(cfgM).BuildGrid(Scene(), MakeProjection());
            var gN = Proc(cfgN).BuildGrid(Scene(), MakeProjection());

            Assert.That(gN, Is.Not.Null, "native grid");
            Assert.That(gN.RadialEdges.Length, Is.EqualTo(gM.RadialEdges.Length), "edges len");
            Assert.That(gN.Cells.Length, Is.EqualTo(gM.Cells.Length), "cells len");

            int classMismatch = 0;
            for (int i = 0; i < gM.Cells.Length; i++)
            {
                if (gM.Cells[i].Class != gN.Cells[i].Class) classMismatch++;
                if (gM.Cells[i].Count >= 8 && gN.Cells[i].Count >= 8)
                    Assert.That(gN.Cells[i].MeanZ, Is.EqualTo(gM.Cells[i].MeanZ).Within(0.02f), $"MeanZ[{i}]");
            }
            Assert.That(classMismatch, Is.LessThanOrEqualTo(2), $"class mismatches = {classMismatch}");

            var (fM, oM, _, _) = Counts(gM);
            var (fN, oN, _, _) = Counts(gN);
            Assert.That(fN, Is.GreaterThan(0), "native free>0");
            Assert.That(oN, Is.GreaterThan(0), "native obstacle>0");
            Assert.That(Math.Abs(fN - fM), Is.LessThanOrEqualTo(2), "free count");
            Assert.That(Math.Abs(oN - oM), Is.LessThanOrEqualTo(2), "obstacle count");
        }

        [Test]
        public void Process_ComputesGridInFrame()
        {
            var frame = new CameraFrame
            {
                Name = "Cam",
                TimeStamp = T0,
                ImageDepth = Depth((x, y) => (ushort)(Hc * 1000)),
            };

            Proc().Process(frame);

            Assert.That(frame.Grid, Is.Not.Null, "grid dopocten do ramce");
            Assert.That(frame.Grid.AzimuthCount, Is.EqualTo(4));
            Assert.That(frame.Grid.RadialCount, Is.GreaterThan(0));
            Assert.That(frame.Grid.ComputeMs, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void Process_UnknownCamera_LeavesGridNull()
        {
            // Resolver nezna kameru "Other" -> grid se nespocita (frame.Grid zustane null).
            var frame = new CameraFrame
            {
                Name = "Other",
                TimeStamp = T0,
                ImageDepth = Depth((x, y) => (ushort)(Hc * 1000)),
            };

            Proc().Process(frame);

            Assert.That(frame.Grid, Is.Null);
        }

        // Trivialni BackProject: pravdepodobnost = jas (prumer B,G,R), stejny rozmer.
        private sealed class IdentityBackProject : IBackProject
        {
            public System.Drawing.Size Size(int width, int height) => new System.Drawing.Size(width, height);
            public void Process(Image<BGR32> src, Image<Gray> dest)
            {
                for (int y = 0; y < src.Height; y++)
                    for (int x = 0; x < src.Width; x++)
                    {
                        var p = src[x, y];
                        dest[x, y].Value = (byte)((p.R + p.G + p.B) / 3);
                    }
            }
        }

        [Test]
        public void Process_WithBackProject_ComputesProbability()
        {
            var rgb = new Image<BGR32>(8, 8);
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var p = rgb[x, y];
                    p.R = 90; p.G = 90; p.B = 90;
                }

            var frame = new CameraFrame { Name = "Cam", TimeStamp = T0, ImageRGB = rgb };
            var proc = new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = MakeProjection() },
                TestConfig(), backProject: new IdentityBackProject());

            proc.Process(frame);

            Assert.That(frame.ImageProbability, Is.Not.Null);
            Assert.That((frame.ImageProbability.Width, frame.ImageProbability.Height), Is.EqualTo((8, 8)));
            Assert.That(frame.ImageProbability[0, 0].Value, Is.EqualTo(90));
        }
    }
}
