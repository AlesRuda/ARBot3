using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Occupancy;
using ARBot.Common.Vision;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Testy zapisu snimku do kartezskeho occupancy gridu (<see cref="OccupancyIntegrator"/>).
    /// Pouziva SKUTECNOU <see cref="CameraProjection"/> (ne fake), aby prosla realna cesta
    /// bod zeme -&gt; pixel, na ktere gather stoji. Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para>Scena: kamera miri dopredu sklonena o 20° ve vysce 0,52 m; robot-rel. ramec X vpred,
    /// Y vlevo, Z nahoru.</para>
    /// </summary>
    public class OccupancyIntegratorTest
    {
        private const int W = 64, H = 64;
        private const float Hc = 0.52f;
        private const float F = 40f;
        private const double PitchDeg = 20;
        private static readonly double Pitch = PitchDeg * Math.PI / 180.0;

        // --- kamera ---

        private static Intrinsics MakeIntrinsics() => new Intrinsics
        {
            Width = W,
            Height = H,
            PPx = W / 2f,
            PPy = H / 2f,
            Fx = F,
            Fy = F,
            Model = Intrinsics.Distortion.None,
            Coeffs = new float[5],
        };

        /// <summary>Transformace kamera -&gt; robot (radkova konvence: radky = obrazy bazi kamery).</summary>
        private static Matrix4x4 ForwardCamera()
        {
            float s = (float)Math.Sin(Pitch), c = (float)Math.Cos(Pitch);
            return new Matrix4x4(
                0, -1, 0, 0,      // X_cam (vpravo) -> -Y_robot
                -s, 0, -c, 0,     // Y_cam (dolu)
                c, 0, -s, 0,      // Z_cam (vpred)
                0, 0, Hc, 1);
        }

        private static CameraProjection MakeProjection()
        {
            var p = new CameraProjection(MakeIntrinsics(), MakeIntrinsics(),
                                         Matrix4x4.Identity, Matrix4x4.Identity);
            p.SetOrientation(ForwardCamera());
            return p;
        }

        // --- polarni grid ---

        private static PolarGridConfig PolarCfg() => new PolarGridConfig
        {
            ColumnsPerCell = 8,
            TargetPointsPerCell = 12,
            MinPointsPerCell = 8,
            MinRangeM = 0.3f,
            MaxRangeM = 5.0f,
            MinRadialStepM = 0.05f,
            AssumedValidFraction = 1.0f,
        };

        /// <summary>Hloubkovy obraz rovne zeme; <paramref name="obstacleHeight"/> aplikuje prekazku
        /// dane vysky ve sloupcich <paramref name="colFrom"/>..<paramref name="colTo"/>.</summary>
        private static Image<Gray16> Depth(double obstacleHeight = 0, int colFrom = -1, int colTo = -1)
        {
            double s = Math.Sin(Pitch), c = Math.Cos(Pitch);
            var img = new Image<Gray16>(W, H);
            var d = img.Data;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double ry = (y - H / 2.0) / F;
                    // Vyska plochy, na kterou se paprsek promita.
                    double z = (x >= colFrom && x <= colTo) ? obstacleHeight : 0.0;
                    double denom = ry * c + s;
                    ushort v = 0;
                    if (denom > 1e-3)
                    {
                        double dist = (Hc - z) / denom;
                        if (dist > 0 && dist < 60) v = (ushort)Math.Round(dist * 1000);
                    }
                    int idx = (y * W + x) * 2;
                    d[idx] = (byte)(v & 0xFF);
                    d[idx + 1] = (byte)(v >> 8);
                }
            return img;
        }

        private static PolarTraversabilityGrid BuildPolar(Image<Gray16> depth)
        {
            var proj = MakeProjection();
            var proc = new CameraFrameProcessor(
                new Dictionary<string, IDepthCameraProjection> { ["Cam"] = proj }, PolarCfg());
            return proc.BuildGrid(depth, proj);
        }

        /// <summary>Probability obraz s konstantni hodnotou; volitelne pas nizkych hodnot (mimo cestu).</summary>
        private static Image<Gray> Probability(byte value, int colFrom = -1, int colTo = -1, byte bandValue = 0)
        {
            var img = new Image<Gray>(W, H);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    img[x, y].Value = (x >= colFrom && x <= colTo) ? bandValue : value;
            return img;
        }

        private static OccupancyGrid MakeGrid() => new OccupancyGrid(new OccupancyGridConfig
        {
            Size = 256,
            Resolution = 0.05,
        });

        // ---------------- geometricky kanal ----------------

        [Test]
        public void RovnaZem_ZapiseVolnoPredRobotem()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();
            var frame = new CameraFrame { Name = "Cam", Grid = BuildPolar(Depth()) };

            int touched = integrator.Integrate(frame, proj, null, 0, 0, 0);

            Assert.That(touched, Is.GreaterThan(100), "rovna zem ma zaplnit vyrazny kus gridu");

            // Bod 1,5 m pred robotem (uvnitr zorneho pole) musi po dost pozorovanich zvolnet.
            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, null, 0, 0, 0);
            Assert.That(grid.LogOddsOcc(grid.CellX(1.5), grid.CellY(0.0)), Is.LessThan(-0.5),
                        "zem pred robotem ma byt pozorovana jako volna");
        }

        [Test]
        public void ZaRobotem_ZustavaNeznamo()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();
            var frame = new CameraFrame { Name = "Cam", Grid = BuildPolar(Depth()) };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, null, 0, 0, 0);

            Assert.That(grid.State(grid.CellX(-1.5), grid.CellY(0.0)), Is.EqualTo(CellState.Unknown),
                        "kamera dozadu nevidi - musi zustat Unknown");
            Assert.That(grid.LogOddsOcc(grid.CellX(-1.5), grid.CellY(0.0)), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Prekazka_ZapiseSeNaSpravneMisto()
        {
            // Prekazka vysky 0,4 m ve sloupcich 8..15. Nizky index sloupce = leva cast obrazu
            // = v robot-rel. ramci VLEVO, tedy KLADNE Y (Y roste vlevo; sloupec roste doprava,
            // tedy k zapornemu Y).
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();
            var frame = new CameraFrame { Name = "Cam", Grid = BuildPolar(Depth(0.4, 8, 15)) };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, null, 0, 0, 0);

            // Najdi vsechny Blocked bunky - musi lezet vlevo (Y > 0) a pred robotem (X > 0).
            int blocked = 0;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < grid.Size; i++)
                for (int j = 0; j < grid.Size; j++)
                {
                    if (grid.StateAt(grid.LocalIndex(i, j)) != CellState.Blocked) continue;
                    blocked++;
                    double y = grid.CenterY(grid.OriginY + j);
                    double x = grid.CenterX(grid.OriginX + i);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    Assert.That(x, Is.GreaterThan(0), "prekazka je pred robotem");
                }

            Assert.That(blocked, Is.GreaterThan(5), "prekazka se ma zapsat do gridu");
            Assert.That(minY, Is.GreaterThan(0), "prekazka ve sloupcich 8..15 lezi vlevo (Y > 0)");
        }

        [Test]
        public void KurzRobotu_OtociZapisDoSveta()
        {
            // Stejny snimek pri kurzu 0 a pri kurzu 90°: obsah musi byt otoceny o 90°.
            var frame = new CameraFrame { Name = "Cam", Grid = BuildPolar(Depth()) };
            var proj = MakeProjection();

            var g0 = MakeGrid();
            var i0 = new OccupancyIntegrator(g0);
            for (int k = 0; k < 10; k++) i0.Integrate(frame, proj, null, 0, 0, 0);

            var g90 = MakeGrid();
            var i90 = new OccupancyIntegrator(g90);
            for (int k = 0; k < 10; k++) i90.Integrate(frame, proj, null, 0, 0, Math.PI / 2);

            // Pri kurzu 0 je zem videt na +X, pri 90° na +Y.
            Assert.That(g0.LogOddsOcc(g0.CellX(1.5), g0.CellY(0)), Is.LessThan(-0.5));
            Assert.That(g0.State(g0.CellX(0), g0.CellY(1.5)), Is.EqualTo(CellState.Unknown));

            Assert.That(g90.LogOddsOcc(g90.CellX(0), g90.CellY(1.5)), Is.LessThan(-0.5));
            Assert.That(g90.State(g90.CellX(1.5), g90.CellY(0)), Is.EqualTo(CellState.Unknown));
        }

        [Test]
        public void PolohaRobotu_PosuneZapisDoSveta()
        {
            var frame = new CameraFrame { Name = "Cam", Grid = BuildPolar(Depth()) };
            var proj = MakeProjection();

            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, null, 10.0, -5.0, 0);

            Assert.That(grid.LogOddsOcc(grid.CellX(11.5), grid.CellY(-5.0)), Is.LessThan(-0.5),
                        "zem 1,5 m pred robotem ve SVETOVYCH souradnicich");
        }

        // ---------------- semanticky kanal ----------------

        [Test]
        public void Probability_ZapiseCestuAMimoCestu()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();

            // Pas sloupcu 8..15 je "mimo cestu" (hodnota 0), zbytek je cesta (255).
            // Nizky index sloupce = leva cast obrazu = kladne Y v robot-rel. ramci.
            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = BuildPolar(Depth()),
                ImageProbability = Probability(255, 8, 15, bandValue: 0),
            };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);

            // Uprostred zorneho pole je cesta (kanal zaporny), nekde vlevo pas mimo cestu (kladny).
            Assert.That(grid.LogOddsRoad(grid.CellX(1.5), grid.CellY(0.0)), Is.LessThan(-0.5),
                        "stred zorneho pole je cesta");

            bool nasel = false;
            for (double y = 0.2; y <= 1.5; y += 0.05)
                if (grid.LogOddsRoad(grid.CellX(1.5), grid.CellY(y)) > 0.5) nasel = true;
            Assert.That(nasel, Is.True, "pas mimo cestu se ma zapsat vlevo");
        }

        [Test]
        public void MimoCestu_UdelaBunkuNeprujezdnou()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();

            // Vse je geometricky volne, ale barva rika "nikde neni cesta".
            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = BuildPolar(Depth()),
                ImageProbability = Probability(0),
            };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);

            Assert.That(grid.State(grid.CellX(1.5), grid.CellY(0.0)), Is.EqualTo(CellState.Blocked),
                        "semanticky kanal blokuje stejne jako geometricky");
        }

        [Test]
        public void NeutralniProbability_NeudelaZNeznamaCestu()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();

            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = BuildPolar(Depth()),
                ImageProbability = Probability(128),   // presne neutralni
            };

            for (int k = 0; k < 20; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);

            Assert.That(grid.LogOddsRoad(grid.CellX(1.5), grid.CellY(0.0)), Is.EqualTo(0f).Within(1e-6f),
                        "\"o ceste nic nevim\" nesmi kanal posunout");
            Assert.That(grid.State(grid.CellX(1.5), grid.CellY(0.0)), Is.EqualTo(CellState.Unknown),
                        "bez informace o ceste nesmi byt bunka Free, i kdyz geometrie je volna");
        }

        [Test]
        public void Okluze_ZaPrekazkouSeBarvaNevzorkuje()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();

            // Prekazka pres CELOU sirku obrazu ve vysce 0,4 m -> vse za ni je ve stinu.
            var polar = BuildPolar(Depth(0.4, 0, W - 1));
            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = polar,
                ImageProbability = Probability(255),   // barva by rikala "cesta" vsude
            };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);

            // Najdi nabeznou hranu prvni prekazky ve strednim azimutu a zkontroluj bunku za ni.
            int a = polar.AzimuthCount / 2;
            int first = -1;
            for (int r = 0; r < polar.RadialCount; r++)
                if (polar[a, r].Class == TraversabilityClass.Obstacle) { first = r; break; }
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "scena ma mit prekazku");

            double behind = polar.RadialEdges[first].Range + 0.5;
            Assert.That(grid.LogOddsRoad(grid.CellX(behind), grid.CellY(0.0)), Is.EqualTo(0f).Within(1e-6f),
                        "za prekazkou se barva nesmi vzorkovat (patrila by prekazce, ne zemi)");
        }

        [Test]
        public void BarvaSmiPsatIZaDosahHloubky()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();

            var polar = BuildPolar(Depth());
            double depthMax = polar.RadialEdges[polar.RadialEdges.Length - 1].Range;

            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = polar,
                ImageProbability = Probability(255),
            };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);

            double beyond = depthMax + 0.5;
            Assume.That(beyond, Is.LessThan(integrator.Config.RoadMaxRangeM), "test predpoklada dosah barvy dal");

            Assert.That(grid.LogOddsRoad(grid.CellX(beyond), grid.CellY(0.0)), Is.LessThan(-0.1),
                        "barva dohledne dal nez hloubka a ma se zapsat");
            Assert.That(grid.LogOddsOcc(grid.CellX(beyond), grid.CellY(0.0)), Is.EqualTo(0f).Within(1e-6f),
                        "geometrie tam nic nevi");
        }

        [Test]
        public void BarvaZaDosahemLzeVypnout()
        {
            var cfg = new OccupancyIntegratorConfig { RoadBeyondDepthRange = false };
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid, cfg);
            var proj = MakeProjection();

            var polar = BuildPolar(Depth());
            double depthMax = polar.RadialEdges[polar.RadialEdges.Length - 1].Range;
            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = polar,
                ImageProbability = Probability(255),
            };

            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);

            Assert.That(grid.LogOddsRoad(grid.CellX(depthMax + 0.5), grid.CellY(0.0)),
                        Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void DuveraBarvy_KlesaSVzdalenosti()
        {
            var cfg = new OccupancyIntegratorConfig { RoadFullRangeM = 2.0, RoadMaxRangeM = 4.0 };
            Assert.That(cfg.RoadConfidence(1.0), Is.EqualTo(1f));
            Assert.That(cfg.RoadConfidence(2.0), Is.EqualTo(1f));
            Assert.That(cfg.RoadConfidence(3.0), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(cfg.RoadConfidence(4.0), Is.EqualTo(0f));
            Assert.That(cfg.RoadConfidence(10.0), Is.EqualTo(0f));
        }

        [Test]
        public void ProbabilityNaPravdepodobnost_JeSymetricka()
        {
            var cfg = new OccupancyIntegratorConfig();
            Assert.That(cfg.ProbabilityToTraversable(255), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(cfg.ProbabilityToTraversable(128), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(cfg.ProbabilityToTraversable(0), Is.EqualTo(0f).Within(1e-6f));
        }

        // ---------------- rezie ----------------

        [Test]
        public void Integrate_NealokujeOpakovane()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var proj = MakeProjection();
            var frame = new CameraFrame
            {
                Name = "Cam",
                Grid = BuildPolar(Depth()),
                ImageProbability = Probability(200),
            };
            integrator.Integrate(frame, proj, proj, 0, 0, 0);   // prohrati

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 10; k++) integrator.Integrate(frame, proj, proj, 0, 0, 0);
            long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 10;

            Assert.That(perCall, Is.LessThan(4096), $"Integrate alokuje {perCall} B na volani");
        }

        [Test]
        public void BezGriduABezProbability_NicNedela()
        {
            var grid = MakeGrid();
            var integrator = new OccupancyIntegrator(grid);
            var frame = new CameraFrame { Name = "Cam" };

            Assert.That(integrator.Integrate(frame, null, null, 0, 0, 0), Is.EqualTo(0));
            Assert.That(integrator.Integrate(null, null, null, 0, 0, 0), Is.EqualTo(0));
        }
    }
}
