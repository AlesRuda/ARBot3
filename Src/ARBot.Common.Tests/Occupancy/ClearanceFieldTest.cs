using ARBot.Common.Occupancy;
using NUnit.Framework;
using System;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Testy euklidovskeho distance transformu (<see cref="ClearanceField"/>) a rychlostnich stropu
    /// (<see cref="LocalPlannerConfig"/>). Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    [TestFixture]
    public class ClearanceFieldTest
    {
        private const int N = 32;
        private const double Res = 0.1;

        private static OccupancyGrid Grid() => new OccupancyGrid(new OccupancyGridConfig
        {
            Size = N,
            Resolution = Res,
        });

        /// <summary>Udela z bunky spolehlive <see cref="CellState.Blocked"/>.</summary>
        private static void Block(OccupancyGrid g, int cx, int cy)
        {
            for (int k = 0; k < 5; k++) g.ObserveOccupied(cx, cy, 1f);
            Assert.That(g.State(cx, cy), Is.EqualTo(CellState.Blocked));
        }

        // ---------------- EDT ----------------

        [Test]
        public void JednaPrekazka_VzdalenostJeEuklidovska()
        {
            var g = Grid();
            g.Recenter(0, 0);
            int bx = g.OriginX + N / 2, by = g.OriginY + N / 2;
            Block(g, bx, by);

            var f = new ClearanceField(g);
            f.Build(g);

            Assert.That(f.Distance(bx, by), Is.EqualTo(0.0).Within(1e-6));

            // Porovnani s referencni hrubou silou pro kazdou bunku.
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    int cx = g.OriginX + i, cy = g.OriginY + j;
                    double dx = cx - bx, dy = cy - by;
                    double expected = Math.Sqrt(dx * dx + dy * dy) * Res;
                    Assert.That(f.Distance(cx, cy), Is.EqualTo(expected).Within(1e-5),
                                $"bunka ({i},{j})");
                }
        }

        [Test]
        public void VicePrekazek_OdpovidaHrubeSile()
        {
            var g = Grid();
            g.Recenter(0, 0);

            // Nepravidelny vzorek prekazek (vc. dvou u sebe a jedne u kraje).
            var blocked = new (int i, int j)[] { (3, 4), (3, 5), (20, 8), (31, 31), (12, 25), (0, 17) };
            foreach (var (i, j) in blocked) Block(g, g.OriginX + i, g.OriginY + j);

            var f = new ClearanceField(g);
            f.Build(g);

            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    double best = double.MaxValue;
                    foreach (var (bi, bj) in blocked)
                    {
                        double dx = i - bi, dy = j - bj;
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < best) best = d;
                    }
                    Assert.That(f.DistanceLocal(i, j), Is.EqualTo(best * Res).Within(1e-5),
                                $"bunka ({i},{j})");
                }
        }

        [Test]
        public void PrazdnyGrid_VzdalenostJeZastropovana()
        {
            var g = Grid();
            g.Recenter(0, 0);

            var f = new ClearanceField(g);
            f.Build(g);

            // Bez jedine prekazky nesmi vyjit Inf/NaN - jen zastropovana (velka) hodnota.
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    float d = f.DistanceLocal(i, j);
                    Assert.That(float.IsFinite(d), Is.True, $"bunka ({i},{j}) neni finitni");
                    Assert.That(d, Is.GreaterThan(N * Res), $"bunka ({i},{j}) ma prekvapive maly odstup");
                }
        }

        [Test]
        public void UnknownNeniPrekazka()
        {
            var g = Grid();
            g.Recenter(0, 0);
            // Grid je cely Unknown (nic nepozorovano).

            var f = new ClearanceField(g);
            f.Build(g);

            Assert.That(f.DistanceLocal(N / 2, N / 2), Is.GreaterThan(N * Res),
                        "Unknown se nesmi chovat jako prekazka");
        }

        [Test]
        public void MimoCestuBlokujeStejneJakoPrekazka()
        {
            var g = Grid();
            g.Recenter(0, 0);
            int bx = g.OriginX + 10, by = g.OriginY + 10;

            // Jen semanticky kanal: jiste mimo cestu.
            for (int k = 0; k < 10; k++) g.ObserveRoad(bx, by, 0f, 1f);
            Assert.That(g.State(bx, by), Is.EqualTo(CellState.Blocked));

            var f = new ClearanceField(g);
            f.Build(g);

            Assert.That(f.Distance(bx, by), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(f.Distance(bx + 1, by), Is.EqualTo(Res).Within(1e-6));
        }

        [Test]
        public void PoPosunuGridu_PoleCteSpravnouBunku()
        {
            var g = Grid();
            g.Recenter(0, 0);
            int bx = g.OriginX + N / 2, by = g.OriginY + N / 2;
            Block(g, bx, by);

            var f = new ClearanceField(g);
            f.Build(g);
            Assert.That(f.Distance(bx, by), Is.EqualTo(0.0).Within(1e-6));

            // Posun gridu na vychod; prekazka zustava (je v prekryvu). Po prepoctu musi pole
            // vracet nulu porad na te same ABSOLUTNI bunce.
            g.MoveOrigin(g.OriginX + 4, g.OriginY);
            f.Build(g);

            Assert.That(f.OriginX, Is.EqualTo(g.OriginX));
            Assert.That(f.Distance(bx, by), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(f.Distance(bx, by + 2), Is.EqualTo(2 * Res).Within(1e-6));
        }

        [Test]
        public void Build_NealokujeOpakovane()
        {
            var g = Grid();
            g.Recenter(0, 0);
            Block(g, g.OriginX + 5, g.OriginY + 5);
            var f = new ClearanceField(g);
            f.Build(g);   // prvni beh (JIT, prohrati)

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++) f.Build(g);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(delta, Is.LessThan(4096), $"Build alokuje ({delta} B za 20 behu)");
        }

        // ---------------- rychlostni stropy ----------------

        [Test]
        public void VClear_LinearniRampaMeziSafeAPref()
        {
            var cfg = new LocalPlannerConfig { SafeDist = 0.4, PrefDist = 0.8, MaxSpeed = 0.8 };
            cfg.Validate();

            Assert.That(cfg.VClear(0.0), Is.EqualTo(0.0));
            Assert.That(cfg.VClear(0.4), Is.EqualTo(0.0), "na tvrdem odstupu je rychlost nulova");
            Assert.That(cfg.VClear(0.5), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(cfg.VClear(0.6), Is.EqualTo(0.4).Within(1e-9));
            Assert.That(cfg.VClear(0.8), Is.EqualTo(0.8).Within(1e-9));
            Assert.That(cfg.VClear(5.0), Is.EqualTo(0.8), "za PrefDist se rychlost uz neomezuje");
        }

        [Test]
        public void VBrake_ZastaviNaHraniciPotvrzeneho()
        {
            var cfg = new LocalPlannerConfig { MaxSpeed = 0.8, MaxDeceleration = 0.3 };

            Assert.That(cfg.VBrake(0.0), Is.EqualTo(0.0), "na hranici potvrzeneho musi byt nula");
            Assert.That(cfg.VBrake(0.5), Is.EqualTo(Math.Sqrt(2 * 0.3 * 0.5)).Within(1e-9));

            // Od brzdne drahy vys uz strop nesnizuje: 0,8^2/(2*0,3) = 1,067 m
            Assert.That(cfg.VBrake(1.07), Is.EqualTo(0.8).Within(1e-3));
            Assert.That(cfg.VBrake(10.0), Is.EqualTo(0.8));
        }

        [Test]
        public void VCost_JeVzdyKladna_AbyBylaCenaKonecna()
        {
            var cfg = new LocalPlannerConfig { SafeDist = 0.4, PrefDist = 0.8, MaxSpeed = 0.8 };

            Assert.That(cfg.VCost(0.4), Is.EqualTo(cfg.MinCostSpeed),
                        "bunka presne na tvrdem odstupu musi mit konecnou (velkou) cenu");
            Assert.That(cfg.VCost(0.8), Is.EqualTo(0.8).Within(1e-9));
        }

        [Test]
        public void Config_PrefDistNeniVetsiNezSafeDist_Vyhodi()
        {
            var cfg = new LocalPlannerConfig { SafeDist = 0.8, PrefDist = 0.4 };
            Assert.Throws<ArgumentException>(() => cfg.Validate());
        }
    }
}
