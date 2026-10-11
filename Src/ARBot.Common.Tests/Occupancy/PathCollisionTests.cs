using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Kontrola kolize rozjete drahy s aktualni mapou (<see cref="PathCollision"/>, drive soukrome
    /// <c>LocalNavigator.PathCollides</c>). Hlavne: unikova draha smi odjet z bunky pod robotem,
    /// i kdyz ji blokuje geometrie - stejne pravidlo jako <c>LocalPathPlanner.PlanEscape</c>
    /// (lp-unik-kontrola-kolize-startu; do 9. 10. 2026 falesne „NOUZOVE ZASTAVENI - kolize 0,00 m“).
    /// </summary>
    [TestFixture]
    public class PathCollisionTests
    {
        private const int N = 128;
        private const double Res = 0.05;

        private static LocalPlannerConfig Cfg() => new LocalPlannerConfig
        {
            SafeDist = 0.4,
            PrefDist = 0.8,
            MaxSpeed = 0.8,
            MaxAcceleration = 0.3,
        };

        private sealed class Scene
        {
            public readonly OccupancyGrid Grid;
            public readonly ClearanceField Field;

            public Scene()
            {
                Grid = new OccupancyGrid(new OccupancyGridConfig { Size = N, Resolution = Res });
                Grid.Recenter(0, 0);
                Field = new ClearanceField(Grid);
                for (int cx = Grid.CellX(-2); cx <= Grid.CellX(2); cx++)
                    for (int cy = Grid.CellY(-2); cy <= Grid.CellY(2); cy++)
                        for (int k = 0; k < 10; k++)
                        {
                            Grid.ObserveFree(cx, cy, 1f);
                            Grid.ObserveRoad(cx, cy, 1f, 1f);
                        }
            }

            /// <summary>Bunka pod bodem jako GEOMETRICKA prekazka (hloubka).</summary>
            public void Geometry(double x, double y)
            {
                for (int k = 0; k < 10; k++) Grid.ObserveOccupied(Grid.CellX(x), Grid.CellY(y), 1f);
            }

            /// <summary>Bunka pod bodem blokovana jen SEMANTIKOU (mimo cestu).</summary>
            public void OffRoad(double x, double y)
            {
                for (int k = 0; k < 10; k++) Grid.ObserveRoad(Grid.CellX(x), Grid.CellY(y), 0f, 1f);
            }

            public bool Collides(bool escape, double v, out double hit)
            {
                Field.Build(Grid);
                var path = new[]
                {
                    new RegulatorWayPoint { X = 0, Y = 0, Speed = v },
                    new RegulatorWayPoint { X = 1.0, Y = 0, Speed = 0 },
                };
                return PathCollision.Collides(Grid, Field, Cfg(), path, 0, 0, v, escape, out hit);
            }
        }

        [Test]
        public void Unik_ZGeometrickyBlokovaneBunkyPodRobotem_NeniKolize()
        {
            var s = new Scene();
            s.Geometry(0, 0);                       // robot stoji na geometricky blokovane bunce

            bool kolize = s.Collides(escape: true, v: 0.3, out double hit);

            Assert.That(kolize, Is.False, $"z bunky pod robotem se smi odjet (hit {hit:F2} m)");
        }

        [Test]
        public void Unik_GeometrickaPrekazkaPredRobotem_JeKolize()
        {
            var s = new Scene();
            s.Geometry(0, 0);
            s.Geometry(0.15, 0);                    // skutecna prekazka v dosahu brzdne drahy

            bool kolize = s.Collides(escape: true, v: 0.3, out double hit);

            Assert.That(kolize, Is.True, "vyjimka plati jen pro bunku pod robotem");
            Assert.That(hit, Is.GreaterThan(0.1).And.LessThan(0.2));
        }

        [Test]
        public void Unik_PresSemantickyBlokovane_NeniKolize()
        {
            var s = new Scene();
            s.OffRoad(0.10, 0);
            s.OffRoad(0.15, 0);

            Assert.That(s.Collides(escape: true, v: 0.3, out _), Is.False,
                        "unik vede zamerne pres semanticky blokovane bunky");
        }

        [Test]
        public void BeznaDraha_BlokovanaBunkaPodRobotem_JeKoliziJakoDriv()
        {
            var s = new Scene();
            s.Geometry(0, 0);

            bool kolize = s.Collides(escape: false, v: 0.3, out double hit);

            Assert.That(kolize, Is.True, "bezna draha se nemeni");
            Assert.That(hit, Is.EqualTo(0).Within(1e-9));
        }

        /// <summary>
        /// I stojici robot kontroluje rezervu jedne bunky (check = Resolution) - tedy i bunku pod
        /// sebou. Proto se falesna kolize 0,00 m hlasila i ve stani; vyjimka ji musi odstranit i tam.
        /// </summary>
        [Test]
        public void Unik_StojiciRobotNaGeometrickeBunce_NeniKolize()
        {
            var s = new Scene();
            s.Geometry(0, 0);

            Assert.That(s.Collides(escape: true, v: 0, out _), Is.False);
        }

        // ---------------- bezna draha: prah = prah uniku (lp-kolize-pri-stani, 11. 10. 2026) ----------------

        /// <summary>Bunka (cx, cy) vzhledem k bunce robotu (0, 0) -> stred bunky ve svete.</summary>
        private static (double X, double Y) Stred(int cx, int cy) => ((cx + 0.5) * Res, (cy + 0.5) * Res);

        [Test]
        public void TesnyOdstup_JeSafeDistMinusPulBunky()
            => Assert.That(Cfg().TightClearance(Res), Is.EqualTo(0.375).Within(1e-12));

        /// <summary>
        /// Kolo 4 (19. 9.): robot stoji v bunce s odstupem 0,391 m - v pasmu mezi prahem uniku (0,375)
        /// a SafeDist (0,4). Planovac z ni planuje bezne, kontrola stare drahy dela totez - do
        /// 11. 10. 2026 tu hlasila „kolize 0,00 m".
        /// </summary>
        [Test]
        public void BeznaDraha_RobotVPasmuPodSafeDist_NeniKolize()
        {
            var s = new Scene();
            var o = Stred(-6, -5);                  // sqrt(36 + 25) * 0,05 = 0,3905 m od bunky robotu
            s.Geometry(o.X, o.Y);
            s.Field.Build(s.Grid);
            Assume.That(s.Field.Distance(0, 0), Is.InRange(0.375, 0.4), "robot v pasmu");

            Assert.That(s.Collides(escape: false, v: 0, out double hit), Is.False, $"hit {hit:F3}");
        }

        /// <summary>
        /// Kolo 4 a 18. 9.: robot stoji v bezpecne bunce (0,424 m), ale stara draha zacina v sousedni
        /// bunce v pasmu (0,391 m) a prumet robotu na ni padne na jeji zacatek. Do 11. 10. 2026
        /// „kolize 0,00 m" na startu drahy.
        /// </summary>
        [Test]
        public void BeznaDraha_StartStareDrahyVPasmu_NeniKolize()
        {
            var s = new Scene();
            var o = Stred(6, 6);                    // bunka (0,1): (6,5) -> 0,3905; bunka robotu (0,0): (6,6) -> 0,424
            s.Geometry(o.X, o.Y);
            s.Field.Build(s.Grid);
            Assume.That(s.Field.Distance(0, 1), Is.InRange(0.375, 0.4), "start drahy v pasmu");
            Assume.That(s.Field.Distance(0, 0), Is.GreaterThanOrEqualTo(0.4), "robot v bezpecne bunce");
            var path = new[]
            {
                new RegulatorWayPoint { X = 0.025, Y = 0.05, Speed = 0 },   // hranice bunek (0,0) / (0,1)
                new RegulatorWayPoint { X = -1.0, Y = 0.05, Speed = 0 },
            };

            bool kolize = PathCollision.Collides(s.Grid, s.Field, Cfg(), path, 0.025, 0.0499, 0, false, out double hit);

            Assert.That(kolize, Is.False, $"hit {hit:F3}");
        }

        [Test]
        public void BeznaDraha_PrekazkaPodTesnymOdstupem_JeKolize()
        {
            var s = new Scene();
            var o = Stred(-5, -5);                  // sqrt(50) * 0,05 = 0,354 m < 0,375
            s.Geometry(o.X, o.Y);
            s.Field.Build(s.Grid);
            Assume.That(s.Field.Distance(0, 0), Is.LessThan(0.375));

            Assert.That(s.Collides(escape: false, v: 0, out double hit), Is.True, "skutecne blizka prekazka zustava kolizi");
            Assert.That(hit, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void BeznaDraha_ZaJizdy_PrekazkaUDrahyPodTesnymOdstupem_JeKolize()
        {
            var s = new Scene();
            // Bunky drahy (x, 0): (2,0) 0,403, (3,0) 0,381 (pasmo - jede se dal), (4,0) sqrt(53)*0,05 = 0,364
            // < 0,375 -> kolize na prvnim vzorku v bunce (4,0), tj. 0,200-0,225 m (krok vzorku 0,025 m,
            // podle zaokrouhleni souctu), uvnitr dosahu kontroly 0,15 + 0,03 + 0,05 = 0,23 m. Se starym
            // prahem SafeDist by kolize byla uz v bunce (3,0), tj. v 0,15 m - mez 0,175 je mezi nimi.
            var o = Stred(6, -7);
            s.Geometry(o.X, o.Y);

            bool kolize = s.Collides(escape: false, v: 0.3, out double hit);

            Assert.That(kolize, Is.True);
            Assert.That(hit, Is.GreaterThan(0.175).And.LessThanOrEqualTo(0.23));
        }

        /// <summary>
        /// Planovac a kontrola kolize maji JEDNO pravidlo: robot v pasmu dostane od planovace bezny
        /// plan (ne unik) a kontrola teze drahy pak NEhlasi kolizi. Pred 11. 10. 2026 se tu rozchazely.
        /// </summary>
        [Test]
        public void Planovac_RobotVPasmu_PlanujeBezne_AKontrolaToSamePrijme()
        {
            var s = new Scene();
            var o = Stred(-6, -5);
            s.Geometry(o.X, o.Y);
            s.Field.Build(s.Grid);
            Assume.That(s.Field.Distance(0, 0), Is.InRange(0.375, 0.4));

            var plan = new LocalPathPlanner(N, Cfg()).Plan(s.Grid, s.Field, 0.025, 0.025, 0, 1.0, 0.025);

            Assert.That(plan.Status, Is.Not.EqualTo(LocalPlanStatus.EscapingBlocked), "v pasmu se neunika");
            Assert.That(plan.HasPath, Is.True, $"stav {plan.Status}");
            Assert.That(PathCollision.Collides(s.Grid, s.Field, Cfg(), plan.WayPoints, 0.025, 0.025, 0, false, out double hit),
                        Is.False, $"kontrola musi prijmout drahu, kterou planovac z teze bunky naplanoval (hit {hit:F3})");
        }

        [Test]
        public void Unik_StojiciRobot_PrekazkaZaRezervou_NeniKolize()
        {
            var s = new Scene();
            s.Geometry(0.10, 0);

            Assert.That(s.Collides(escape: true, v: 0, out _), Is.False, "ve stani se kontroluje jen rezerva 5 cm");
        }
    }
}
