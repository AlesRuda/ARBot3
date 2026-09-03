using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Testy lokalniho planovace: prujezd koridorem, brany o rozdilne sirce (tvrdy odstup se nikdy
    /// neporusi), planovani skrz neznamo se zastavenim na jeho hranici, cena otoceni a determinismus.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    [TestFixture]
    public class LocalPathPlannerTest
    {
        private const int N = 128;          // 128 * 5 cm = 6,4 m
        private const double Res = 0.05;

        private static OccupancyGridConfig GridCfg() => new OccupancyGridConfig
        {
            Size = N,
            Resolution = Res,
        };

        private static LocalPlannerConfig PlannerCfg() => new LocalPlannerConfig
        {
            SafeDist = 0.4,
            PrefDist = 0.8,
            MaxSpeed = 0.8,
            MaxDeceleration = 0.3,
            MaxRotationSpeed = Math.PI / 6,
            HorizonM = 6.0,
        };

        /// <summary>Scena: grid vycentrovany na (0,0), robot v pocatku, kurz na vychod.</summary>
        private sealed class Scene
        {
            public OccupancyGrid Grid;
            public ClearanceField Field;
            public LocalPathPlanner Planner;

            public static Scene Create(LocalPlannerConfig cfg = null)
            {
                var g = new OccupancyGrid(GridCfg());
                g.Recenter(0, 0);
                return new Scene
                {
                    Grid = g,
                    Field = new ClearanceField(g),
                    Planner = new LocalPathPlanner(N, cfg ?? PlannerCfg()),
                };
            }

            /// <summary>Oznaci obdelnik [x0,x1] x [y0,y1] (metry) jako jiste sjizdnou plochu.</summary>
            public void MarkFree(double x0, double y0, double x1, double y1)
                => ForEachCell(x0, y0, x1, y1, (cx, cy) =>
                {
                    for (int k = 0; k < 10; k++)
                    {
                        Grid.ObserveFree(cx, cy, 1f);
                        Grid.ObserveRoad(cx, cy, 1f, 1f);
                    }
                });

            /// <summary>Oznaci obdelnik jako prekazku (geometrie).</summary>
            public void MarkObstacle(double x0, double y0, double x1, double y1)
                => ForEachCell(x0, y0, x1, y1, (cx, cy) =>
                {
                    for (int k = 0; k < 10; k++) Grid.ObserveOccupied(cx, cy, 1f);
                });

            /// <summary>Oznaci obdelnik jako "jiste mimo cestu" (semantika).</summary>
            public void MarkOffRoad(double x0, double y0, double x1, double y1)
                => ForEachCell(x0, y0, x1, y1, (cx, cy) =>
                {
                    for (int k = 0; k < 10; k++) Grid.ObserveRoad(cx, cy, 0f, 1f);
                });

            private void ForEachCell(double x0, double y0, double x1, double y1, Action<int, int> a)
            {
                for (int cx = Grid.CellX(x0); cx <= Grid.CellX(x1); cx++)
                    for (int cy = Grid.CellY(y0); cy <= Grid.CellY(y1); cy++)
                        a(cx, cy);
            }

            public void Rebuild() => Field.Build(Grid);

            public LocalPlanResult Plan(double gx, double gy, double heading = 0)
                => Planner.Plan(Grid, Field, 0, 0, heading, gx, gy);

            /// <summary>Plan z jine polohy robotu nez z pocatku (grid se NErecentruje - staci, ze robot zustane uvnitr).</summary>
            public LocalPlanResult PlanFrom(double rx, double ry, double gx, double gy, double heading = 0)
                => Planner.Plan(Grid, Field, rx, ry, heading, gx, gy);

            /// <summary>Odstup [m] bunky pod danym bodem od nejblizsi neprujezdne bunky.</summary>
            public double ClearanceAt(double x, double y) => Field.Distance(Grid.CellX(x), Grid.CellY(y));
        }

        /// <summary>Nejmensi odstup od neprujezdneho podel cele drahy (hustsi vzorkovani nez planovac).</summary>
        private static double MinClearanceAlongPath(Scene s, RegulatorWayPoint[] wps)
        {
            double min = double.MaxValue;
            for (int k = 0; k < wps.Length - 1; k++)
            {
                double dx = wps[k + 1].X - wps[k].X, dy = wps[k + 1].Y - wps[k].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)Math.Ceiling(len / (Res * 0.25)));
                for (int i = 0; i <= steps; i++)
                {
                    double t = (double)i / steps;
                    double x = wps[k].X + dx * t, y = wps[k].Y + dy * t;
                    min = Math.Min(min, s.Field.Distance(s.Grid.CellX(x), s.Grid.CellY(y)));
                }
            }
            return min;
        }

        // ---------------- zakladni pruchod ----------------

        [Test]
        public void VolnaPlocha_JedeRovnouKCili()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.Rebuild();

            var r = s.Plan(2.0, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Ok));
            Assert.That(r.HasPath, Is.True);
            // Bez prekazek musi string-pulling slozit drahu do jedine usecky.
            Assert.That(r.WayPoints.Length, Is.EqualTo(2));
            Assert.That(r.WayPoints[0].X, Is.EqualTo(0).Within(1e-9));
            Assert.That(r.WayPoints[1].X, Is.EqualTo(2.0).Within(2 * Res));
            Assert.That(r.WayPoints[1].Y, Is.EqualTo(0.0).Within(2 * Res));
            // Skutecny cil = zastaveni.
            Assert.That(r.WayPoints[1].Speed, Is.EqualTo(0.0));
            Assert.That(r.LengthM, Is.EqualTo(2.0).Within(0.2));
        }

        [Test]
        public void VysledekJdeRovnouDoPathPlanneru()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(0.8, -0.2, 1.0, 0.2);
            s.Rebuild();

            var r = s.Plan(2.0, 0.0);
            Assert.That(r.HasPath, Is.True);

            // Kontrakt PathPlanneru: >= 2 body, zadny nulovy usek.
            var pp = new PathPlanner(new TrapezoidMotionProfile(
                maxSpeed: 0.8, maxOrientationSpeed: Math.PI / 6, acceleration: 0.3, rozchod: 0.41));
            Assert.DoesNotThrow(() => pp.Plan(r.WayPoints));
        }

        [Test]
        public void MezilehleUzly_NikdyNemajiSpeedNula()
        {
            // Speed == 0 chape PathPlanner jako "bez stropu" - nula u mezilehleho uzlu by strop zrusila.
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(0.8, -0.2, 1.0, 0.6);
            s.MarkObstacle(0.8, -1.5, 1.0, -0.9);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);
            Assert.That(r.HasPath, Is.True);

            for (int k = 0; k < r.WayPoints.Length - 1; k++)
                Assert.That(r.WayPoints[k].Speed, Is.GreaterThan(0.0), $"waypoint {k}");
        }

        // ---------------- odstupy od prekazek ----------------

        [Test]
        public void ObjezdPrekazky_DrziTvrdyOdstup()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(1.0, -0.15, 1.2, 0.15);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);

            Assert.That(r.HasPath, Is.True);
            double min = MinClearanceAlongPath(s, r.WayPoints);
            Assert.That(min, Is.GreaterThanOrEqualTo(s.Planner.Config.SafeDist - 1e-6),
                        "draha se dostala bliz nez tvrdy odstup");
            Assert.That(r.MinClearanceM, Is.GreaterThanOrEqualTo(s.Planner.Config.SafeDist - 1e-6));
        }

        [Test]
        public void SirokaBrana_ProjedePlnouRychlosti()
        {
            // Brana 2,4 m: stred je 1,2 m od kazde strany -> nad PrefDist -> zadne omezeni.
            // Zdi jdou pres CELY grid, takze brana je jedina cesta (test neni vakuozni).
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(1.0, 1.2, 1.2, 3.2);
            s.MarkObstacle(1.0, -3.2, 1.2, -1.2);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);

            Assert.That(r.HasPath, Is.True);
            Assert.That(MinClearanceAlongPath(s, r.WayPoints),
                        Is.GreaterThanOrEqualTo(s.Planner.Config.SafeDist - 1e-6));
            // Nekde na draze musi byt povolena plna rychlost.
            double maxSpeed = 0;
            foreach (var w in r.WayPoints) maxSpeed = Math.Max(maxSpeed, w.Speed);
            Assert.That(maxSpeed, Is.EqualTo(s.Planner.Config.MaxSpeed).Within(1e-6));
        }

        [Test]
        public void UzkaBrana_ProjedeAleSeSnizenouRychlosti()
        {
            // Brana 1,0 m: stred je 0,5 m od kazde strany -> nad SafeDist (0,4), pod PrefDist (0,8)
            // -> projet se musi, ale jen omezenou rychlosti. Zdi pres cely grid = brana je jedina cesta.
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(1.0, 0.5, 1.2, 3.2);
            s.MarkObstacle(1.0, -3.2, 1.2, -0.5);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);

            Assert.That(r.HasPath, Is.True, "brana 1,0 m je pro odstup 0,4 m prujezdna");
            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Ok), "cil za branou musi byt dosazen");
            double min = MinClearanceAlongPath(s, r.WayPoints);
            Assert.That(min, Is.GreaterThanOrEqualTo(s.Planner.Config.SafeDist - 1e-6));
            Assert.That(min, Is.LessThan(s.Planner.Config.PrefDist),
                        "v brane 1,0 m nelze mit pohodlny odstup");

            // Strop rychlosti u nejtesnejsiho uzlu musi byt vyrazne pod maximem.
            double minSpeed = double.MaxValue;
            foreach (var w in r.WayPoints) minSpeed = Math.Min(minSpeed, w.Speed);
            Assert.That(minSpeed, Is.LessThan(s.Planner.Config.MaxSpeed),
                        "v uzke brane se nesmi jet plnou rychlosti");
        }

        [Test]
        public void PrilisUzkaBrana_NeprojedeAOdstupSeNeporusi()
        {
            // Brana 0,6 m: stred je 0,3 m od kazde strany -> pod SafeDist 0,4 -> neprujezdna.
            // Zdi pres cely grid, takze jinou cestou to nejde.
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(1.0, 0.3, 1.2, 3.2);
            s.MarkObstacle(1.0, -3.2, 1.2, -0.3);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);

            // Cil za branou nelze dosahnout; planovac dojede jen k nejlepsi dosazitelne bunce
            // PRED zdi a tvrdy odstup pritom neporusi.
            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Partial),
                        "cil za neprujezdnou branou nesmi byt hlasen jako dosazeny");
            Assert.That(r.ReachedGoalX, Is.LessThan(1.0), "plan se nesmi dostat za zed");
            Assert.That(MinClearanceAlongPath(s, r.WayPoints),
                        Is.GreaterThanOrEqualTo(s.Planner.Config.SafeDist - 1e-6),
                        "plan projel prilis uzkou branou");
        }

        [Test]
        public void MimoCestuJeStejneNeprujezdneJakoPrekazka()
        {
            // Stejna geometrie jako u prilis uzke brany, ale hranice je jen semanticka (RGB).
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkOffRoad(1.0, 0.3, 1.2, 3.2);
            s.MarkOffRoad(1.0, -3.2, 1.2, -0.3);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Partial),
                        "semanticka hranice musi blokovat stejne jako geometricka");
            Assert.That(r.ReachedGoalX, Is.LessThan(1.0));
            Assert.That(MinClearanceAlongPath(s, r.WayPoints),
                        Is.GreaterThanOrEqualTo(s.Planner.Config.SafeDist - 1e-6));
        }

        // ---------------- neznamo ----------------

        [Test]
        public void SkrzNeznamo_SePlanujeAleRychlostJeOmezenaBrzdnouObalkou()
        {
            // Potvrzene volno jen do 0,5 m pred robotem; dal je vse Unknown. Cil je za tim.
            var s = Scene.Create();
            s.MarkFree(-0.5, -1.0, 0.5, 1.0);
            s.Rebuild();

            var r = s.Plan(3.0, 0.0);

            Assert.That(r.HasPath, Is.True, "skrz neznamo se planovat SMI");

            // Strop uz v PRVNIM uzlu (u robotu) musi byt srazeny brzdnou obalkou k hranici
            // potvrzeneho (0,5 m): sqrt(2*0,3*0,5) = 0,548 m/s, tedy vyrazne pod v_max 0,8.
            var cfg = s.Planner.Config;
            double expected = Math.Sqrt(2 * cfg.MaxDeceleration * 0.5);
            Assert.That(r.WayPoints[0].Speed, Is.LessThan(cfg.MaxSpeed - 0.1),
                        "u blizke hranice potvrzeneho se nesmi jet plnou rychlosti");
            Assert.That(r.WayPoints[0].Speed, Is.EqualTo(expected).Within(0.15),
                        "strop ma odpovidat brzdne obalce k hranici potvrzeneho");
        }

        [Test]
        public void ZaHraniciPotvrzeneho_JeStropJenPlouzeni()
        {
            // Cil daleko za hranici potvrzeneho volna: uzly za ni maji mit strop jen na plouzeni
            // (nikdy presnou nulu - PathPlanner by ji chapal jako "bez stropu").
            var s = Scene.Create();
            s.MarkFree(-0.5, -1.0, 0.5, 1.0);
            s.Rebuild();

            var r = s.Plan(50.0, 0.0);   // cil mimo grid -> Partial, posledni uzel neni skutecny cil
            Assert.That(r.HasPath, Is.True);

            var last = r.WayPoints[r.WayPoints.Length - 1];
            Assert.That(last.Speed, Is.LessThanOrEqualTo(s.Planner.Config.MinCostSpeed + 1e-9),
                        "za hranici potvrzeneho se smi jen plouzit");

            for (int k = 0; k < r.WayPoints.Length - 1; k++)
                Assert.That(r.WayPoints[k].Speed, Is.GreaterThan(0.0),
                            $"mezilehly uzel {k} nesmi mit Speed 0 (PathPlanner by strop zrusil)");
        }

        [Test]
        public void PotvrzeneVolno_RobotJedePlnouRychlostiPresToZeHorizontKonci()
        {
            // Cely grid potvrzene volny, cil daleko za nim. Konec planu je vzdy hranici znameho
            // (za nim nic overeneho neni), ale je tak daleko, ze robot NA SVEM MISTE muze jet plnou
            // rychlosti - z 0,8 m/s se ubrzdi na 1,07 m, horizont je 3 m dal.
            var s = Scene.Create();
            s.MarkFree(-3.1, -3.1, 3.1, 3.1);
            s.Rebuild();

            var r = s.Plan(50.0, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Partial));
            var cfg = s.Planner.Config;
            Assert.That(r.WayPoints[0].Speed, Is.EqualTo(cfg.MaxSpeed).Within(1e-6),
                        "v potvrzene volnem prostoru se robot nema plazit");

            // Kontrola konzistence: z rychlosti v prvnim uzlu se musi dat zastavit do konce planu.
            double brakeDist = r.WayPoints[0].Speed * r.WayPoints[0].Speed / (2 * cfg.MaxDeceleration);
            Assert.That(brakeDist, Is.LessThanOrEqualTo(r.LengthM + 1e-9),
                        "povolena rychlost prevysuje brzdnou drahu k hranici znameho");
        }

        // ---------------- cil ----------------

        [Test]
        public void CilMimoGrid_SeOrizneNaHranici()
        {
            var s = Scene.Create();
            s.MarkFree(-3.1, -3.1, 3.1, 3.1);
            s.Rebuild();

            var r = s.Plan(100.0, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Partial));
            Assert.That(r.RequestedGoalX, Is.EqualTo(100.0));
            Assert.That(r.ReachedGoalX, Is.LessThan(N * Res / 2));
            Assert.That(r.ReachedGoalX, Is.GreaterThan(0));
        }

        [Test]
        public void CilVeStejneBunce_HlasiAlreadyAtGoal()
        {
            var s = Scene.Create();
            s.MarkFree(-1, -1, 1, 1);
            s.Rebuild();

            var r = s.Plan(0.02, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.AlreadyAtGoal));
            Assert.That(r.HasPath, Is.False);
        }

        [Test]
        public void RobotMimoGrid_Hlasi()
        {
            var g = new OccupancyGrid(GridCfg());
            g.MoveOrigin(10000, 10000);
            var f = new ClearanceField(g);
            f.Build(g);
            var p = new LocalPathPlanner(N, PlannerCfg());

            var r = p.Plan(g, f, 0, 0, 0, 1, 0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.RobotOutsideGrid));
        }

        [Test]
        public void RobotVPrekazce_Hlasi()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(-0.1, -0.1, 0.1, 0.1);   // prekazka presne pod robotem
            s.Rebuild();

            var r = s.Plan(2.0, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.RobotBlocked));
        }

        [Test]
        public void RobotBlizkoPrekazky_MuzePrestoOdjet()
        {
            // Robot stoji 0,25 m od zdi (mensi odstup nez SafeDist). Do 3. 9. 2026 ho pustila
            // "eskapovaci zona" (vyjimka z odstupu kolem aktualni bunky); dnes je to UNIK - tentyz
            // rezim jako u blokovane bunky: plan vede k nejblizsi bunce, odkud jde planovat bezne,
            // a tam zastavi. Zona padla, protoze byla symetricka a posouvala se s robotem, takze
            // pustila i BLIZ k prekazce (viz CilZaOkrajemCesty_RobotSeNikdyNedopliziPodSafeDist).
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(-3.0, 0.25, 3.0, 0.45);
            s.Rebuild();
            Assert.That(s.ClearanceAt(0, 0), Is.LessThan(s.Planner.Config.SafeDist), "predpoklad: start je tesny");

            var r = s.Plan(2.0, -1.0);

            Assert.Multiple(() =>
            {
                Assert.That(r.HasPath, Is.True, "robot blizko prekazky musi mit moznost odjet");
                Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.EscapingBlocked), "tesny start = unik");
                Assert.That(LegalCell(s, r.ReachedGoalX, r.ReachedGoalY), Is.True, "unik konci na bezpecne bunce");
                Assert.That(r.WayPoints[^1].Speed, Is.EqualTo(0.0), "na konci uniku robot zastavi");
            });
        }

        /// <summary>
        /// REGRESE na plizeni k okraji (3. 9. 2026): mrkev lezi za okrajem cesty (v trave), robot
        /// stoji na bezpecne bunce. Drivejsi eskapovaci zona pustila kazdy cyklus o bunku bliz
        /// k trave, protoze se posouvala s robotem - s libovolne velkym SafeDist robot dojel az
        /// k hranici. Dnes: plan vede k nejblizsi bezpecne bunce, hlasi <c>GoalBlocked</c> (ne
        /// <c>Partial</c>, ne <c>AlreadyAtGoal</c>), na konci zastavi, a ani po nekolika cyklech
        /// "dojel jsem, kam plan vedl" se robot nedostane pod SafeDist.
        /// </summary>
        [Test]
        public void CilZaOkrajemCesty_RobotSeNikdyNedopliziPodSafeDist()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkOffRoad(-3.0, 1.0, 3.0, 3.0);     // trava od y = 1,0 dal (semantika = konec cesty)
            s.Rebuild();
            double safe = s.Planner.Config.SafeDist;
            Assert.That(s.ClearanceAt(0, 0), Is.GreaterThanOrEqualTo(safe), "predpoklad: start je bezpecny");

            double rx = 0, ry = 0;
            LocalPlanResult r = null;
            for (int cyklus = 0; cyklus < 6; cyklus++)
            {
                r = s.PlanFrom(rx, ry, 0.0, 1.5);   // mrkev 1,5 m pred robotem, v trave
                Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.GoalBlocked), $"cyklus {cyklus}: cil v trave se hlasi poctive");
                if (!r.HasPath) break;                // stoji na nejblizsi bezpecne bunce, dal to nejde
                Assert.That(MinClearanceAlongPath(s, r.WayPoints), Is.GreaterThanOrEqualTo(safe - 1e-9),
                            $"cyklus {cyklus}: draha nesmi pod SafeDist");
                Assert.That(r.WayPoints[^1].Speed, Is.EqualTo(0.0), $"cyklus {cyklus}: na konci zastavi");
                rx = r.ReachedGoalX;
                ry = r.ReachedGoalY;
            }

            Assert.Multiple(() =>
            {
                Assert.That(r.HasPath, Is.False, "nakonec robot stoji: cil je nedosazitelny a blizsi bezpecna bunka neni");
                Assert.That(s.ClearanceAt(rx, ry), Is.GreaterThanOrEqualTo(safe - 1e-9),
                            "robot skoncil na bunce s odstupem alespon SafeDist");
                Assert.That(ry, Is.LessThanOrEqualTo(1.0 - safe + Res + 1e-9), "robot stoji o SafeDist pred travou");
            });
        }

        /// <summary>
        /// Hystereze uniku: spousti se az pod <c>SafeDist - Res/2</c>, konci (IsEscapeExit) na
        /// plnem <c>SafeDist</c>. Robot s odstupem v tom pasmu planuje bezne a z pasma vyjede
        /// bez "uniku" - bez hystereze by robot, ktery unikem vyjel na bunku tesne nad SafeDist,
        /// po sumu gridu priste zase unikal o bunku dal a na hranici kmital.
        /// </summary>
        [Test]
        public void TesnyStart_UnikMaHysterezPulBunky()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(-3.0, 0.40, 3.0, 0.60);
            s.Rebuild();
            double c = s.ClearanceAt(0, 0);         // odstup startu, kvantovany na bunky

            LocalPlanResult Run(double safeDist)
            {
                var cfg = PlannerCfg();
                cfg.SafeDist = safeDist;
                return new LocalPathPlanner(N, cfg).Plan(s.Grid, s.Field, 0, 0, 0, 2.0, -1.0);
            }

            var vPasmu = Run(c + Res / 4);     // start je o ctvrt bunky pod SafeDist -> v hysterezi
            var pod = Run(c + Res);            // start je o celou bunku pod SafeDist -> unik

            Assert.Multiple(() =>
            {
                Assert.That(vPasmu.Status, Is.EqualTo(LocalPlanStatus.Ok), "v pasmu se planuje bezne, zadny unik");
                Assert.That(pod.Status, Is.EqualTo(LocalPlanStatus.EscapingBlocked), "pod pasmem je to unik");
            });
        }

        /// <summary>
        /// Cil je volna bunka, ale blize k prekazce nez SafeDist: <c>GoalUnsafe</c>. Plan vede
        /// k nejblizsi bezpecne bunce a tam ZASTAVI - dal to nejde a cekat na dalsi mrkev nema smysl.
        /// </summary>
        [Test]
        public void CilTesneUPrekazky_HlasiGoalUnsafeAZastaviNaBezpecneBunce()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(-3.0, 1.0, 3.0, 1.2);
            s.Rebuild();
            double safe = s.Planner.Config.SafeDist;
            Assert.That(s.Grid.StateAtWorld(0, 0.8), Is.Not.EqualTo(CellState.Blocked), "predpoklad: cil je volny");
            Assert.That(s.ClearanceAt(0, 0.8), Is.LessThan(safe), "predpoklad: cil je blize nez SafeDist");

            var r = s.Plan(0.0, 0.8);

            Assert.Multiple(() =>
            {
                Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.GoalUnsafe));
                Assert.That(r.HasPath, Is.True);
                Assert.That(LegalCell(s, r.ReachedGoalX, r.ReachedGoalY), Is.True, "konci na bezpecne bunce");
                Assert.That(r.WayPoints[^1].Speed, Is.EqualTo(0.0), "na konci zastavi");
                Assert.That(MinClearanceAlongPath(s, r.WayPoints), Is.GreaterThanOrEqualTo(safe - 1e-9));
            });
        }

        // ---------------- cena otoceni a determinismus ----------------

        [Test]
        public void CenaOtoceni_PreferujeSmerKuremuJeRobotNatoceny()
        {
            // Symetricka prekazka pred robotem: obejit ji lze vlevo i vpravo za stejnou cenu.
            // Kurz robotu (mirne doleva / doprava) ma rozhodnout, kterou stranou.
            LocalPlanResult Run(double heading)
            {
                var s = Scene.Create();
                s.MarkFree(-3, -3, 3, 3);
                s.MarkObstacle(1.0, -0.3, 1.2, 0.3);
                s.Rebuild();
                return s.Plan(2.5, 0.0, heading);
            }

            double SideOf(LocalPlanResult r)
            {
                // Prumerne Y drahy = strana objezdu.
                double sum = 0;
                foreach (var w in r.WayPoints) sum += w.Y;
                return sum / r.WayPoints.Length;
            }

            var left = Run(Math.PI / 4);     // natoceny doleva (na severovychod)
            var right = Run(-Math.PI / 4);   // natoceny doprava

            Assert.That(left.HasPath, Is.True);
            Assert.That(right.HasPath, Is.True);
            Assert.That(SideOf(left), Is.GreaterThan(0), "natoceny doleva ma objizdet vlevo");
            Assert.That(SideOf(right), Is.LessThan(0), "natoceny doprava ma objizdet vpravo");
        }

        [Test]
        public void Determinismus_StejnyVstupDavaStejnyVystup()
        {
            var results = new List<RegulatorWayPoint[]>();
            for (int run = 0; run < 3; run++)
            {
                var s = Scene.Create();
                s.MarkFree(-3, -3, 3, 3);
                s.MarkObstacle(1.0, -0.3, 1.2, 0.3);
                s.MarkObstacle(1.8, 0.6, 2.0, 1.4);
                s.Rebuild();
                var r = s.Plan(2.5, 0.5);
                Assert.That(r.HasPath, Is.True);
                results.Add(r.WayPoints);
            }

            for (int k = 1; k < results.Count; k++)
            {
                Assert.That(results[k].Length, Is.EqualTo(results[0].Length), $"beh {k}: jiny pocet bodu");
                for (int i = 0; i < results[0].Length; i++)
                {
                    Assert.That(results[k][i].X, Is.EqualTo(results[0][i].X).Within(1e-12), $"beh {k}, bod {i}");
                    Assert.That(results[k][i].Y, Is.EqualTo(results[0][i].Y).Within(1e-12), $"beh {k}, bod {i}");
                    Assert.That(results[k][i].Speed, Is.EqualTo(results[0][i].Speed).Within(1e-12));
                }
            }
        }

        [Test]
        public void OpakovaneVolani_NealokujeNeomezene()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(1.0, -0.3, 1.2, 0.3);
            s.Rebuild();
            s.Plan(2.5, 0.0);   // prohrati (JIT, kapacita fronty)

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 10; k++) s.Plan(2.5, 0.0);
            long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 10;

            // Waypointy + pomocne seznamy se alokuji zamerne (predavaji se ven), ale rad musi byt
            // kilobajty, ne stovky kB (tj. zadne per-volani buffery velikosti gridu).
            Assert.That(perCall, Is.LessThan(50_000), $"Plan alokuje {perCall} B na volani");
        }

        // ---------------- tolerance epsilon ----------------

        [Test]
        public void MaxPositionError_NikdyNeukousneZBezpecnostnihoOdstupu()
        {
            var s = Scene.Create();
            s.MarkFree(-3, -3, 3, 3);
            s.MarkObstacle(1.0, 0.5, 1.2, 3.0);
            s.MarkObstacle(1.0, -3.0, 1.2, -0.5);
            s.Rebuild();

            var r = s.Plan(2.5, 0.0);
            Assert.That(r.HasPath, Is.True);

            var cfg = s.Planner.Config;
            for (int k = 0; k < r.WayPoints.Length; k++)
            {
                var w = r.WayPoints[k];
                double clr = s.Field.Distance(s.Grid.CellX(w.X), s.Grid.CellY(w.Y));
                Assert.That(w.MaxPositionError, Is.LessThanOrEqualTo(cfg.EpsMax + 1e-9));
                Assert.That(w.MaxPositionError, Is.GreaterThanOrEqualTo(cfg.EpsMin - 1e-9));
                // Tolerance nesmi presahnout volnou rezervu (az na spodni mez EpsMin).
                if (w.MaxPositionError > cfg.EpsMin)
                    Assert.That(w.MaxPositionError, Is.LessThanOrEqualTo(clr - cfg.SafeDist + 1e-6),
                                $"waypoint {k}: tolerance ukusuje z bezpecnostniho odstupu");
            }
        }

        // ---------------- unik z blokovane bunky (18. 8. 2026) ----------------
        //
        // Nalez ze zaznamu 20260818-093903.rec: robot dobrzdil na bunce, kterou blokoval JEN
        // semanticky kanal (hloubka na zapornem dorazu -5 = jiste volno, barva na kladnem +5),
        // planovac vratil RobotBlocked a robot uz se nehnul. Delici cara je proto "kanal":
        // ven se smi pres semanticky blokovane bunky, pres geometricky NIKDY.
        // Viz doc/occupancy-and-local-planning.md.

        /// <summary>Je bunka prujezdna beznym pravidlem (odtud muze pokracovat normalni planovani)?</summary>
        private static bool LegalCell(Scene s, double x, double y)
        {
            int cx = s.Grid.CellX(x), cy = s.Grid.CellY(y);
            return s.Grid.State(cx, cy) != CellState.Blocked
                   && s.Field.Distance(cx, cy) >= s.Planner.Config.SafeDist - 1e-9;
        }

        [Test]
        public void StojimMimoCestu_PlanujeUnikNaLegalniBunku()
        {
            var s = Scene.Create();
            s.MarkFree(0.8, -1.5, 3.0, 1.5);          // cesta pred robotem
            s.MarkOffRoad(-1.5, -1.5, 0.7, 1.5);      // robot stoji mimo cestu (jen semantika)
            s.Rebuild();

            Assert.That(s.Grid.StateAtWorld(0, 0), Is.EqualTo(CellState.Blocked),
                        "predpoklad testu: robot stoji na blokovane bunce");

            var r = s.Plan(3.0, 0.0);

            Assert.Multiple(() =>
            {
                Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.EscapingBlocked));
                Assert.That(r.HasPath, Is.True, "unik se musi predat regulatoru");
                Assert.That(LegalCell(s, r.ReachedGoalX, r.ReachedGoalY), Is.True,
                            "unik konci na bunce, odkud muze pokracovat normalni planovani");
            });
        }

        [Test]
        public void UnikNikdyNejdePresGeometrickouPrekazku()
        {
            var s = Scene.Create();
            s.MarkFree(0.8, -1.5, 3.0, 1.5);
            s.MarkOffRoad(-1.5, -1.5, 0.7, 1.5);
            s.MarkObstacle(0.5, -1.5, 0.7, 1.5);      // zed mezi robotem a cestou
            s.Rebuild();

            var r = s.Plan(3.0, 0.0);

            if (r.WayPoints != null)
                foreach (var w in r.WayPoints)
                    Assert.That(s.Grid.BlockReasonAtWorld(w.X, w.Y).HasFlag(CellBlockReason.Geometry),
                                Is.False, $"draha uniku vede pres geometrickou prekazku ({w.X:F2}, {w.Y:F2})");
        }

        [Test]
        public void StojimUprostredGeometrickePrekazky_NemaKamJit()
        {
            var s = Scene.Create();
            s.MarkObstacle(-1.5, -1.5, 1.5, 1.5);     // geometricky zablokovano vsude dokola
            s.Rebuild();

            var r = s.Plan(3.0, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.RobotBlocked),
                        "pres geometrii se ven nesmi - zadny unik neexistuje");
        }

        [Test]
        public void LegalniBunkaDal_NezStropUniku_Neuteka()
        {
            var s = Scene.Create();
            var cfg = PlannerCfg();
            cfg.EscapeMaxLength = 0.3;                // kratsi, nez je nejblizsi legalni bunka
            var s2 = Scene.Create(cfg);
            s2.MarkFree(2.5, -1.5, 3.0, 1.5);
            s2.MarkOffRoad(-1.5, -1.5, 2.4, 1.5);
            s2.Rebuild();

            var r = s2.Plan(3.0, 0.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.RobotBlocked),
                        "bloudit metry mimo cestu se nesmi");
            Assert.That(s, Is.Not.Null);              // scena s vychozi konfiguraci se nepouziva
        }

        [Test]
        public void BezneePlanovani_PresMimoCestu_Neprojede()
        {
            var s = Scene.Create();
            s.MarkFree(-1.0, -1.5, 3.0, 1.5);
            s.MarkOffRoad(1.0, -1.5, 1.4, 1.5);       // pas mimo cestu napric koridorem
            s.Rebuild();

            Assert.That(s.Grid.StateAtWorld(0, 0), Is.EqualTo(CellState.Free),
                        "predpoklad testu: robot stoji legalne");

            var r = s.Plan(2.5, 0.0);

            if (r.WayPoints != null)
                foreach (var w in r.WayPoints)
                    Assert.That(s.Grid.StateAtWorld(w.X, w.Y), Is.Not.EqualTo(CellState.Blocked),
                                "bezny plan nesmi vest pres blokovanou bunku ani po zavedeni uniku");
        }

        // ---------------- Drzi se plan cesty? (podnet autora 27. 8. 2026) ----------------

        /// <summary>
        /// <b>Zkratka mimo potvrzenou cestu se nebere, i kdyz je geometricky kratsi.</b> Cesta vede
        /// do „L" (2 m na vychod, pak 2 m na sever), zkratka po uhloprícce meri 2,83 m proti 4 m po
        /// ceste — ale vede <b>neznamem</b>, ktere stoji <c>UnknownCostFactor</c> (3x) za bunku.
        ///
        /// <para>Tohle je to, co autor 27. 8. 2026 videl pri odbocovani: robot se drzi cesty sam,
        /// bez jakekoliv informace z mapy. Drzi ho <b>semanticky kanal z vize</b> (mimo cestu =
        /// <see cref="CellState.Blocked"/>) a cena neznama — ne trasa z OSM. Test to pribiji, aby
        /// se ta vlastnost nedala omylem odladit pryc.</para>
        /// </summary>
        [Test]
        public void ZkratkaPresNeznamo_SeNebere_PlanZustaneNaCeste()
        {
            var s = Scene.Create();
            s.MarkFree(-0.5, -0.5, 2.5, 0.5);        // rameno na vychod
            s.MarkFree(1.5, -0.5, 2.5, 2.5);         // rameno na sever
            s.Rebuild();

            var r = s.Plan(2.0, 2.0);                 // cil na konci "L"

            Assert.That(r.HasPath, Is.True);
            Assert.Multiple(() =>
            {
                foreach (var w in r.WayPoints)
                    Assert.That(s.Grid.StateAtWorld(w.X, w.Y), Is.EqualTo(CellState.Free),
                                $"waypoint ({w.X:F2}, {w.Y:F2}) opustil potvrzenou cestu");

                Assert.That(r.LengthM, Is.GreaterThan(3.0),
                            "kdyby se zkratka vzala, vysla by drahu kratsi nez 3 m (uhloprícka 2,83 m)");
            });
        }

        /// <summary>
        /// <b>Bez semantiky z vize plan nema DUVOD drzet se cesty</b> — a to je zbytek otevreneho
        /// ukolu „koridor trasy jako cena v A*".
        ///
        /// <para>Scena: robot stoji na potvrzene ploche, ale vsude kolem je <c>Unknown</c> (kamera
        /// tam jeste nedohledla). Cil je stranou. Planovac jde <b>rovne</b>, protoze vsechny smery
        /// stoji stejne — nic mu nerekne, kudy vede cesta z mapy. Vysledek NENI vada: skrz neznamo
        /// se planovat musi, jinak by robot nikdy nevyjel. Je to hranice toho, co dnesni plan umi —
        /// dokud vize okraj cesty nevidi, mapa se ho nezastane.</para>
        ///
        /// <para>Az koridor z mapy do ceny pribude, tenhle test se zmeni na svuj opak; proto je
        /// napsany tak, aby popisoval <b>dnesek</b>, ne cil.</para>
        /// </summary>
        [Test]
        public void BezSemantikyZVize_NicPlanKCesteNetahne()
        {
            var s = Scene.Create();
            s.MarkFree(-0.5, -0.5, 0.5, 0.5);        // jen ostruvek pod robotem; okoli je Unknown
            s.Rebuild();

            var r = s.Plan(2.0, 2.0);                 // cil na severovychod

            Assert.That(r.HasPath, Is.True, "skrz neznamo se planovat MUSI, jinak by robot nevyjel");

            // Drahu vzorkujeme hustě a hledame nejvetsi odchylku od primky robot -> cil.
            double maxDev = 0;
            foreach (var w in r.WayPoints)
            {
                // Vzdalenost bodu od primky (0,0)-(2,2), tedy od y = x.
                maxDev = Math.Max(maxDev, Math.Abs(w.X - w.Y) / Math.Sqrt(2.0));
            }

            Assert.That(maxDev, Is.LessThan(0.25),
                        "bez semantiky jde plan po primce - zadna preference cesty tam dnes neni");
        }
}
}
