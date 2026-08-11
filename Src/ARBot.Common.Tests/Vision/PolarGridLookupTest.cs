using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Vision;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Testy zpetneho dohledani bunky polarniho gridu pro bod na zemi - mechanismus, ktery pouziva
    /// zapis do kartezskeho occupancy gridu (viz doc/occupancy-and-local-planning.md).
    ///
    /// <para><b>Klicove zjisteni, ktere tvar tohoto API urcilo:</b> u sklonene kamery NENI sloupec
    /// obrazu konstantnim azimutem. Azimut pozemniho bodu na jednom sloupci se meni s radkem - u nasi
    /// geometrie (sklon 20°, HFOV ~77°) az o 0,15 rad, tedy skoro o celou sirku azimutove bunky.
    /// Nelze proto zavest "azimutove hranice" jako pole uhlu; jediny presny zpusob je promitnout bod
    /// zeme do obrazu (<see cref="ICameraProjection.Transform"/>) a vzit jeho SLOUPEC - to presne
    /// invertuje mapovani, ktere pouzil <see cref="CameraFrameProcessor.BuildGrid"/>.</para>
    ///
    /// <para>Scena: kamera miri DOPREDU sklonena o <see cref="PitchDeg"/> ve vysce <see cref="Hc"/>.
    /// Robot-rel. ramec: X vpred, Y vlevo, Z nahoru. Kamerovy: X vpravo, Y dolu, Z od kamery.</para>
    /// </summary>
    public class PolarGridLookupTest
    {
        private const int W = 64, H = 64;
        private const float Hc = 0.52f;
        private const float F = 40f;
        private const double PitchDeg = 20;

        private static readonly double Pitch = PitchDeg * Math.PI / 180.0;

        /// <summary>Transformace kamera -&gt; robot. System.Numerics pouziva radkovou konvenci
        /// (<c>v * M</c>), takze radky jsou obrazy bazovych vektoru kamery.</summary>
        private static Matrix4x4 ForwardCamera()
        {
            float s = (float)Math.Sin(Pitch), c = (float)Math.Cos(Pitch);
            return new Matrix4x4(
                0, -1, 0, 0,      // X_cam (vpravo) -> -Y_robot
                -s, 0, -c, 0,     // Y_cam (dolu)
                c, 0, -s, 0,      // Z_cam (vpred)
                0, 0, Hc, 1);     // pozice kamery
        }

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
            return new FakeProjection(table, ForwardCamera());
        }

        /// <summary>Hloubkovy obraz rovne zeme (z = 0); pixely nad horizontem = 0 (neplatne).</summary>
        private static Image<Gray16> FlatGroundDepth()
        {
            double s = Math.Sin(Pitch), c = Math.Cos(Pitch);
            var img = new Image<Gray16>(W, H);
            var d = img.Data;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double ry = (y - H / 2.0) / F;
                    double denom = ry * c + s;
                    ushort v = 0;
                    if (denom > 1e-3)
                    {
                        double dist = Hc / denom;
                        if (dist > 0 && dist < 60) v = (ushort)Math.Round(dist * 1000);
                    }
                    int idx = (y * W + x) * 2;
                    d[idx] = (byte)(v & 0xFF);
                    d[idx + 1] = (byte)(v >> 8);
                }
            return img;
        }

        private static PolarGridConfig Cfg() => new PolarGridConfig
        {
            ColumnsPerCell = 8,           // -> 8 azimutovych bunek
            TargetPointsPerCell = 12,
            MinPointsPerCell = 8,
            MinRangeM = 0.3f,
            MaxRangeM = 5.0f,
            MinRadialStepM = 0.05f,
            AssumedValidFraction = 1.0f,
        };

        /// <summary>Referencni projekce bodu zeme (robot-rel.) do sloupce/radku obrazu - stejny model,
        /// jaky pouziva <c>CameraProjection.Transform</c>, jen pro nasi syntetickou kameru.</summary>
        private static bool GroundToPixel(double x, double y, out double col, out double row)
        {
            double s = Math.Sin(Pitch), c = Math.Cos(Pitch);
            // Bod zeme v ramci kamery: inverzni transformace (rotace je ortonormalni -> transpozice).
            double px = x, py = y, pz = -Hc;               // vuci pozici kamery
            double camX = -py;                             // radek 1 transpozice
            double camY = -s * px - c * pz;
            double camZ = c * px - s * pz;
            col = double.NaN; row = double.NaN;
            if (camZ <= 1e-6) return false;
            col = camX / camZ * F + W / 2.0;
            row = camY / camZ * F + H / 2.0;
            return true;
        }

        [Test]
        public void SloupecObrazuNeniKonstantniAzimut()
        {
            // Doklad zjisteni, ktere urcilo tvar API: na JEDNOM sloupci se azimut pozemniho bodu meni
            // s radkem srovnatelne s celou sirkou azimutove bunky. Kdyby to tak nebylo, dala by se
            // pouzit tabulka azimutovych hranic.
            var proj = MakeProjection();
            var table = proj.Camera2DToCamera3D;
            var m = proj.Transformation;
            var origin = new Point4D { A = 1 }.Transform(m);

            double min = double.MaxValue, max = double.MinValue;
            const int col = 4;   // levy okraj obrazu
            for (int y = H / 2 + 2; y < H; y++)
            {
                var ray = table[y, col];
                var p1 = new Point4D { X = ray.X, Y = ray.Y, Z = 1, A = 1 }.Transform(m);
                float dirZ = p1.Z - origin.Z;
                if (dirZ >= 0) continue;
                float t = -origin.Z / dirZ;
                float gx = origin.X + t * (p1.X - origin.X);
                float gy = origin.Y + t * (p1.Y - origin.Y);
                double theta = Math.Atan2(gy, gx);
                min = Math.Min(min, theta);
                max = Math.Max(max, theta);
            }

            double spanNaSloupci = max - min;
            double sirkaBunky = 2 * Math.Atan(W / 2.0 / F) / (W / 8);   // HFOV / pocet bunek

            Assert.That(spanNaSloupci, Is.GreaterThan(0.5 * sirkaBunky),
                        $"azimut na sloupci se meni o {spanNaSloupci:F3} rad, sirka bunky je {sirkaBunky:F3} rad " +
                        "- tabulka azimutovych hranic by tedy byla nepresna");
        }

        [Test]
        public void BodZeme_PresSloupecObrazu_NajdeSpravnouBunku()
        {
            // Klicova vlastnost pro zapis do occupancy: pro teziste bunky musim pres projekci do obrazu
            // najit TU SAMOU bunku, do ktere BuildGrid body zaradil.
            var cfg = Cfg();
            var proc = new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = MakeProjection() }, cfg);
            var grid = proc.BuildGrid(FlatGroundDepth(), MakeProjection());

            Assert.That(grid, Is.Not.Null);
            Assert.That(grid.AzimuthCount, Is.EqualTo(8));

            int overeno = 0, azimutOk = 0, radialOk = 0;
            for (int a = 0; a < grid.AzimuthCount; a++)
                for (int r = 0; r < grid.RadialCount; r++)
                {
                    var cell = grid[a, r];
                    if (cell.Count < cfg.MinPointsPerCell) continue;

                    Assert.That(GroundToPixel(cell.MeanX, cell.MeanY, out double col, out _), Is.True,
                                $"teziste bunky [{a},{r}] se nepromitlo do obrazu");

                    int bin = grid.AzimuthBinFromColumn((int)Math.Round(col), cfg.EdgeColumnTrim);
                    float range = MathF.Sqrt(cell.MeanX * cell.MeanX + cell.MeanY * cell.MeanY);
                    int rbin = grid.RadialBin(range);

                    overeno++;
                    if (bin == a) azimutOk++;
                    if (rbin == r) radialOk++;
                }

            Assert.That(overeno, Is.GreaterThan(20), $"test overil jen {overeno} bunek - scena je prilis prazdna");
            // Teziste lezi uvnitr pudorysu bunky, takze zarazeni pres sloupec obrazu vychazi PRESNE
            // - to je prave duvod, proc se gather dela projekci do obrazu a ne pres uhel.
            Assert.That(azimutOk, Is.EqualTo(overeno),
                        $"azimut sedel jen u {azimutOk}/{overeno} bunek");
            Assert.That(radialOk, Is.EqualTo(overeno), "radialni prstenec musi sedet vzdy (bin je dan vzdalenosti)");
        }

        [Test]
        public void AzimuthBinFromColumn_MimoPouzitelnouSirku_VraciMinusJedna()
        {
            var grid = new PolarTraversabilityGrid
            {
                AzimuthCount = 8,
                ColumnsPerCell = 8,
                RadialEdges = new[] { new RadialEdge(0.5f, 10), new RadialEdge(1.0f, 5) },
                Cells = new PolarCell[8],
            };

            Assert.That(grid.AzimuthBinFromColumn(0), Is.EqualTo(0));
            Assert.That(grid.AzimuthBinFromColumn(7), Is.EqualTo(0));
            Assert.That(grid.AzimuthBinFromColumn(8), Is.EqualTo(1));
            Assert.That(grid.AzimuthBinFromColumn(63), Is.EqualTo(7));
            Assert.That(grid.AzimuthBinFromColumn(64), Is.EqualTo(-1), "za pouzitelnou sirkou");
            Assert.That(grid.AzimuthBinFromColumn(-1), Is.EqualTo(-1), "pred pouzitelnou sirkou");

            // S oriznutim krajnich sloupcu se index posouva.
            Assert.That(grid.AzimuthBinFromColumn(4, edgeColumnTrim: 4), Is.EqualTo(0));
            Assert.That(grid.AzimuthBinFromColumn(3, edgeColumnTrim: 4), Is.EqualTo(-1));
        }

        [Test]
        public void RadialBin_MimoRozsah_VraciMinusJedna()
        {
            var proc = new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = MakeProjection() }, Cfg());
            var grid = proc.BuildGrid(FlatGroundDepth(), MakeProjection());

            var e = grid.RadialEdges;
            Assert.That(grid.RadialBin(e[0].Range - 0.1f), Is.EqualTo(-1), "bliz nez prvni hrana");
            Assert.That(grid.RadialBin(e[e.Length - 1].Range + 0.1f), Is.EqualTo(-1), "dal nez posledni hrana");
            Assert.That(grid.RadialBin(e[0].Range), Is.EqualTo(0), "presne na prvni hrane");
        }
    }
}
