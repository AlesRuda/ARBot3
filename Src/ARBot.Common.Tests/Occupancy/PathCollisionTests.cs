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

        [Test]
        public void Unik_StojiciRobot_PrekazkaZaRezervou_NeniKolize()
        {
            var s = new Scene();
            s.Geometry(0.10, 0);

            Assert.That(s.Collides(escape: true, v: 0, out _), Is.False, "ve stani se kontroluje jen rezerva 5 cm");
        }
    }
}
