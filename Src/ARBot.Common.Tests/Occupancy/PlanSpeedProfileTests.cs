using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Occupancy
{
    /// <summary>
    /// Rychlostni profil planu (rychlost jako funkce vzdalenosti od robota) pro World pohled.
    /// Viz doc/world-view.md.
    /// </summary>
    [TestFixture]
    public class PlanSpeedProfileTests
    {
        private static RegulatorWayPoint Wp(double x, double y, double v)
            => new RegulatorWayPoint { X = x, Y = y, Speed = v };

        [Test]
        public void VzdalenostJeKumulativniPoDraze_NePrimoOdRobota()
        {
            // Draha zatoci o 90 stupnu: 1 m na vychod, pak 1 m na sever. Primo od robota je konec
            // sqrt(2) m, po draze 2 m - graf ma ukazovat to druhe.
            var p = PlanSpeedProfile.From(new[] { Wp(0, 0, 0.5), Wp(1, 0, 0.5), Wp(1, 1, 0) },
                                          robotV: 0.3, vMax: 0.8, LocalPlanStatus.Ok, 0.5);

            Assert.That(p.S, Is.EqualTo(new[] { 0.0, 1.0, 2.0 }).Within(1e-12));
            Assert.That(p.LengthM, Is.EqualTo(2.0).Within(1e-12));
            Assert.That(p.RobotV, Is.EqualTo(0.3));
        }

        [Test]
        public void MenezNezDvaBody_NeniCoKreslit()
        {
            Assert.That(PlanSpeedProfile.From(new[] { Wp(0, 0, 0.5) }, 0, 0.8, LocalPlanStatus.AlreadyAtGoal, 1), Is.Null);
            Assert.That(PlanSpeedProfile.From((RegulatorWayPoint[])null, 0, 0.8, LocalPlanStatus.NoRoute, 1), Is.Null);
        }

        [Test]
        public void KoncovaNulaJeZastaveni_AleUsekNebarvi()
        {
            var p = PlanSpeedProfile.From(new[] { Wp(0, 0, 0.8), Wp(1, 0, 0.4), Wp(2, 0, 0) },
                                          double.NaN, 0.8, LocalPlanStatus.Ok, 0.5);

            Assert.Multiple(() =>
            {
                Assert.That(p.StopsAtEnd, Is.True);
                Assert.That(p.SegmentV(0), Is.EqualTo(0.8), "z prvniho uzlu se odjizdi plnou");
                Assert.That(p.SegmentV(1), Is.EqualTo(0.4), "posledni usek ma strop uzlu, ze ktereho vyjizdi - ne koncovou nulu");
                Assert.That(p.MinIntermediateV, Is.EqualTo(0.4), "koncova nula se do 'kde to brzdi nejvic' nepocita");
            });
        }

        [Test]
        public void OsaRychlostiJeStropRizeni_NeboVic_KdyzHoPlanPrekracuje()
        {
            var vRamci = PlanSpeedProfile.From(new[] { Wp(0, 0, 0.5), Wp(1, 0, 0.5) }, 0, 1.2, LocalPlanStatus.Ok, 1);
            var nad = PlanSpeedProfile.From(new[] { Wp(0, 0, 1.5), Wp(1, 0, 1.5) }, 0, 1.2, LocalPlanStatus.Ok, 1);

            Assert.Multiple(() =>
            {
                Assert.That(vRamci.VMax, Is.EqualTo(1.2), "osa drzi strop rizeni, aby byly grafy srovnatelne");
                Assert.That(nad.VMax, Is.EqualTo(1.5), "kdyz plan strop prekroci, osa se roztahne, aby se nic neorezalo");
                Assert.That(vRamci.Normalized(0.6), Is.EqualTo(0.5).Within(1e-12));
                Assert.That(vRamci.Normalized(5.0), Is.EqualTo(1.0), "normalizace je orezana na 1");
            });
        }

        [Test]
        public void ZapornaRychlostSeBereJakoNula()
        {
            // Planovac zapornou nedava, ale zprava ze zaznamu muze byt cokoli - graf nesmi spadnout pod osu.
            var p = PlanSpeedProfile.From(new[] { Wp(0, 0, -0.2), Wp(1, 0, 0.3) }, 0, 0.8, LocalPlanStatus.Ok, 1);
            Assert.That(p.V[0], Is.EqualTo(0));
        }
    }
}
