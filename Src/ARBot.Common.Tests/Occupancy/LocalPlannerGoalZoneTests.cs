using System;
using System.Collections.Generic;
using ARBot.Common.Occupancy;
using NUnit.Framework;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// <b>Cílová zóna („poloměr mrkve")</b> — <see cref="LocalPlannerConfig.GoalRadiusM"/>
    /// a per-cíl parametr <c>LocalPathPlanner.Plan(..., goalRadiusM)</c>.
    ///
    /// <para><b>Proč to vzniklo (14. 9. 2026):</b> cílem A* byla jediná buňka, takže mrkev položená
    /// do trávy nebo těsně k překážce byla nedosažitelná <b>jako celek</b> — plán skončil na
    /// nejbližší bezpečné buňce, stav <see cref="LocalPlanStatus.GoalBlocked"/> a robot tam
    /// zastavil a čekal, ačkoli jiná část cílové zóny dosažitelná byla. Naměřeno nad
    /// <c>20260914-170945.rec</c>: <c>GoalBlocked</c> 24 % a <c>GoalUnsafe</c> 19 % plánů, mrkev
    /// nedosažitelná v <b>52 %</b> plánů (p90 rozdílu 2,52 m), a v oknech s velkým rozdílem robot
    /// ujel 0,1–0,6 m za 10 s místo 8–9 m.</para>
    ///
    /// <para>Scénu si testy staví vlastní (malý grid, hrubší buňka), aby byly čitelné a rychlé —
    /// geometrie je tu podstatou tvrzení, ne vedlejší okolností.</para>
    /// </summary>
    public class LocalPlannerGoalZoneTests
    {
        private const int N = 128;
        private const double Res = 0.05;

        // ---------------- scéna ----------------

        private sealed class Scena
        {
            public OccupancyGrid Grid;
            public ClearanceField Field;
            public LocalPathPlanner Planner;
            public LocalPlannerConfig Cfg;

            public static Scena Vytvor(LocalPlannerConfig cfg = null)
            {
                cfg ??= new LocalPlannerConfig();
                var g = new OccupancyGrid(new OccupancyGridConfig { Size = N, Resolution = Res });
                g.Recenter(0, 0);
                return new Scena
                {
                    Grid = g,
                    Field = new ClearanceField(g),
                    Planner = new LocalPathPlanner(N, cfg),
                    Cfg = cfg,
                };
            }

            public void Volno(double x0, double y0, double x1, double y1)
                => Bunky(x0, y0, x1, y1, (cx, cy) =>
                {
                    for (int k = 0; k < 10; k++) { Grid.ObserveFree(cx, cy, 1f); Grid.ObserveRoad(cx, cy, 1f, 1f); }
                });

            /// <summary>Jistě mimo cestu (semantika) — tráva, ne geometrická překážka.</summary>
            public void MimoCestu(double x0, double y0, double x1, double y1)
                => Bunky(x0, y0, x1, y1, (cx, cy) =>
                {
                    for (int k = 0; k < 10; k++) Grid.ObserveRoad(cx, cy, 0f, 1f);
                });

            public void Prekazka(double x0, double y0, double x1, double y1)
                => Bunky(x0, y0, x1, y1, (cx, cy) =>
                {
                    for (int k = 0; k < 10; k++) Grid.ObserveOccupied(cx, cy, 1f);
                });

            private void Bunky(double x0, double y0, double x1, double y1, Action<int, int> a)
            {
                for (int cx = Grid.CellX(x0); cx <= Grid.CellX(x1); cx++)
                    for (int cy = Grid.CellY(y0); cy <= Grid.CellY(y1); cy++)
                        a(cx, cy);
            }

            public void Prepocti() => Field.Build(Grid);

            public LocalPlanResult Plan(double gx, double gy, double polomer = double.NaN,
                                        double rx = 0, double ry = 0)
                => Planner.Plan(Grid, Field, rx, ry, 0.0, gx, gy, polomer);

            public double OdstupV(double x, double y) => Field.Distance(Grid.CellX(x), Grid.CellY(y));
        }

        /// <summary>Volná plocha s pruhem trávy od <paramref name="travaOd"/> dál (v ose y).</summary>
        private static Scena TravaScena(double travaOd = 1.0, LocalPlannerConfig cfg = null)
        {
            var s = Scena.Vytvor(cfg);
            s.Volno(-3, -3, 3, 3);
            s.MimoCestu(-3.0, travaOd, 3.0, 3.0);
            s.Prepocti();
            return s;
        }

        // ---------------- vlastní tvrzení ----------------

        /// <summary>
        /// REGRESE: poloměr 0 musí dát <b>přesně</b> dosavadní chování. Mrkev v trávě = cíl je
        /// neprůjezdný jako celek, tedy <c>GoalBlocked</c> — ne <c>Ok</c>, ne <c>AlreadyAtGoal</c>.
        /// Bez tohohle testu by se zóna dala zavést tak, že by tiše změnila i případ, kde se
        /// o žádnou zónu nežádá.
        /// </summary>
        [Test]
        public void PolomerNula_ChovaSeStejneJakoPredZonou()
        {
            var s = TravaScena();

            var r = s.Plan(0.0, 1.5, polomer: 0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.GoalBlocked),
                        "cil v trave je neprujezdny a ma se to rict");
        }

        /// <summary>Totéž bez zadaného poloměru — výchozí konfigurace je 0, tedy cíl je bod.</summary>
        [Test]
        public void BezZadanehoPolomeru_JeCilBod()
        {
            var s = TravaScena();

            var r = s.Plan(0.0, 1.5);

            Assert.Multiple(() =>
            {
                Assert.That(s.Cfg.GoalRadiusM, Is.EqualTo(0.0), "vychozi konfigurace: mrkev je bod");
                Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.GoalBlocked));
            });
        }

        /// <summary>
        /// Jádro věci: mrkev leží v trávě, ale část zóny kolem ní je na cestě — robot tam má dojet
        /// a hlásit <b>úspěch</b>, ne „cíl zablokován". Právě tohle je ten případ z 14. 9. 2026:
        /// *„když by tam robot dojel, došlo by k dosažení track pointu."*
        /// </summary>
        [Test]
        public void MrkevVTrave_DojedeDoDosazitelneCastiZony()
        {
            var s = TravaScena(travaOd: 1.0);

            var r = s.Plan(0.0, 1.5, polomer: 1.0);

            double odCile = Math.Sqrt(r.ReachedGoalX * r.ReachedGoalX
                                      + (r.ReachedGoalY - 1.5) * (r.ReachedGoalY - 1.5));
            Assert.Multiple(() =>
            {
                Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Ok), "cast zony je dosazitelna -> uspech");
                Assert.That(r.HasPath, Is.True, "je podle ceho jet");
                Assert.That(odCile, Is.LessThanOrEqualTo(1.0 + Res), "dosazeny bod lezi v zone");
                Assert.That(s.OdstupV(r.ReachedGoalX, r.ReachedGoalY),
                            Is.GreaterThanOrEqualTo(s.Cfg.SafeDist - 1e-9),
                            "a je to bezpecne misto, zona neni vyjimka z odstupu");
            });
        }

        /// <summary>
        /// Zóna <b>nesmí</b> spolknout skutečnou poruchu: když je neprůjezdná celá, zůstává
        /// <c>GoalBlocked</c>. Jinak by mrkev ve zdi začala vypadat jako dojezd — tichý pád místo
        /// hlášené poruchy, tedy přesně ten druh vady, který se v tomhle projektu hledá nejhůř.
        /// </summary>
        [Test]
        public void CelaZonaVTrave_ZustavaGoalBlocked()
        {
            var s = TravaScena(travaOd: 1.0);

            // Mrkev 1 m hluboko v trave a zona 0,5 m -> nikde v ni neni cesta.
            var r = s.Plan(0.0, 2.0, polomer: 0.5);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.GoalBlocked));
        }

        /// <summary>
        /// Zóna se posuzuje podle <b>celé zóny</b>, ne podle středu: mrkev stojí na buňce, která
        /// sama průjezdná není (těsně u překážky), ale zóna sahá na volno.
        /// </summary>
        [Test]
        public void MrkevTesneUPrekazky_ZonaDosahneNaVolno()
        {
            var s = Scena.Vytvor();
            s.Volno(-3, -3, 3, 3);
            s.Prekazka(-3.0, 1.5, 3.0, 2.0);
            s.Prepocti();
            double stredCile = 1.5 - Res;          // tesne pod prekazkou -> odstup < SafeDist

            var bod = s.Plan(0.0, stredCile, polomer: 0);
            var zona = s.Plan(0.0, stredCile, polomer: 0.8);

            Assert.Multiple(() =>
            {
                Assert.That(bod.Status, Is.EqualTo(LocalPlanStatus.GoalUnsafe), "bez zony: cil je tesny");
                Assert.That(zona.Status, Is.EqualTo(LocalPlanStatus.Ok), "se zonou: dosahne na volno");
                Assert.That(s.OdstupV(zona.ReachedGoalX, zona.ReachedGoalY),
                            Is.GreaterThanOrEqualTo(s.Cfg.SafeDist - 1e-9));
            });
        }

        /// <summary>
        /// <b>„Nejbližší" znamená podle kritéria A*, tedy nejkratší čas</b> — ne geometricky
        /// nejbližší bod zóny. Ověřuje se to přímo: plán do zóny nesmí stát víc než nejlevnější
        /// z plánů do jednotlivých bodů té zóny braných jako bod.
        ///
        /// <para>Tenhle test padne, když se heuristika měří ke <b>středu</b> zóny místo k jejímu
        /// okraji: <c>h</c> je pak na cílových buňkách nenulová (až poloměr), pořadí vytahování
        /// z fronty přestane odpovídat ceně a A* vrátí dražší dosažitelný bod. Projevilo by se to
        /// jako tiše horší dráha, ne jako chyba.</para>
        /// </summary>
        [Test]
        public void VraciNejlevnejsiBodZony_NeGeometrickyNejblizsi()
        {
            const double cx = 0.0, cy = 2.0, polomer = 1.0;

            var s = Scena.Vytvor();
            s.Volno(-3, -3, 3, 3);
            // Prekazka mezi robotem a stredem zony: kolem ni vede objizdka, takze stred je
            // dosazitelny, ale DRAZ nez blizsi cast zony.
            s.Prekazka(-0.6, 1.4, 0.6, 1.6);
            s.Prepocti();

            var zona = s.Plan(cx, cy, polomer: polomer);
            Assert.That(zona.Status, Is.EqualTo(LocalPlanStatus.Ok), "predpoklad: zona je dosazitelna");

            // Nejlevnejsi bod zony zjisteny hrubou silou - kazdy vzorek jako BODOVY cil.
            double nejlevnejsi = double.PositiveInfinity;
            foreach (var (bx, by) in VzorkyZony(cx, cy, polomer))
            {
                var bod = s.Plan(bx, by, polomer: 0);
                if (bod.Status == LocalPlanStatus.Ok && bod.CostSeconds < nejlevnejsi)
                    nejlevnejsi = bod.CostSeconds;
            }

            Assert.Multiple(() =>
            {
                Assert.That(nejlevnejsi, Is.LessThan(double.PositiveInfinity),
                            "predpoklad: aspon jeden bod zony je dosazitelny jako bod");
                // Tolerance na diskretizaci vzorku: hledani zony neni omezene na moje vzorky.
                Assert.That(zona.CostSeconds, Is.LessThanOrEqualTo(nejlevnejsi + 1e-6),
                            "plan do zony nesmi stat vic nez nejlevnejsi bod te zony");
            });
        }

        /// <summary>
        /// Robot stojící <b>uvnitř</b> zóny je v cíli — plánovat dráhu ke středu, který může být
        /// neprůjezdný, by znamenalo marně se do něj tlačit, ačkoli je cíl dosažen.
        /// </summary>
        [Test]
        public void RobotUvnitrZony_HlasiAlreadyAtGoal()
        {
            var s = TravaScena(travaOd: 1.0);

            // Mrkev 0,6 m pred robotem (v trave), zona 1,0 m -> robot uz v ni stoji.
            var r = s.Plan(0.0, 0.6, polomer: 1.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.AlreadyAtGoal));
        }

        /// <summary>Záporný poloměr je vadný vstup, ne důvod k nesmyslnému plánu — bere se jako bod.</summary>
        [Test]
        public void ZapornyPolomer_SeBereJakoBod()
        {
            var s = TravaScena();

            var r = s.Plan(0.0, 1.5, polomer: -2.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.GoalBlocked));
        }

        /// <summary>
        /// Poloměr zadaný u cíle přebíjí konfiguraci: běžná mrkev může být bod, ale dojezd do cíle
        /// si přinese dojezdový poloměr.
        /// </summary>
        [Test]
        public void PolomerUCile_PrebijiKonfiguraci()
        {
            var cfg = new LocalPlannerConfig { GoalRadiusM = 0.0 };
            var s = TravaScena(travaOd: 1.0, cfg: cfg);

            var r = s.Plan(0.0, 1.5, polomer: 1.0);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Ok),
                        "cil rekl 1,0 m, ackoli konfigurace ma 0");
        }

        /// <summary>A opačně: bez hodnoty u cíle (NaN) se vezme ta z konfigurace.</summary>
        [Test]
        public void BezPolomeruUCile_SeVezmeKonfigurace()
        {
            var cfg = new LocalPlannerConfig { GoalRadiusM = 1.0 };
            var s = TravaScena(travaOd: 1.0, cfg: cfg);

            var r = s.Plan(0.0, 1.5);

            Assert.That(r.Status, Is.EqualTo(LocalPlanStatus.Ok));
        }

        private static IEnumerable<(double X, double Y)> VzorkyZony(double cx, double cy, double r)
        {
            for (double dx = -r; dx <= r + 1e-9; dx += Res)
                for (double dy = -r; dy <= r + 1e-9; dy += Res)
                    if (dx * dx + dy * dy <= r * r) yield return (cx + dx, cy + dy);
        }
    }
}
