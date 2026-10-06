using System;
using ARBot.Common.Diagnostics;
using NUnit.Framework;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Pocitadlo ujete drahy pro webovy nahled (<see cref="Odometer"/>): draha z odometricke pozy,
    /// cas v pohybu a dotaz „od casu T" pres historii kontrolnich bodu (zacatek mise).
    /// </summary>
    [TestFixture]
    public class OdometerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 12, 0, 0);

        /// <summary>Jizda po ose X rychlosti <paramref name="v"/> po 0,1 s od <paramref name="from"/> do <paramref name="to"/> [s].</summary>
        private static void Jed(Odometer o, double from, double to, double x0, double v)
        {
            for (double t = from; t <= to + 1e-9; t += 0.1)
                o.Add(T0.AddSeconds(t), x0 + v * (t - from), 0, v);
        }

        [Test]
        public void RovnaJizda_DrahaACasVPohybu()
        {
            var o = new Odometer();
            Jed(o, 0, 10, 0, 1.0);

            Assert.That(o.TotalDistanceM, Is.EqualTo(10).Within(0.06));
            Assert.That(o.TotalMovingSec, Is.EqualTo(10).Within(0.11));
        }

        [Test]
        public void Stani_SumPolohySeNepricita()
        {
            // Robot stoji, odometricka poza se klepe o milimetry - nesmi z toho vzniknout metry.
            var o = new Odometer();
            var rnd = new Random(1);
            for (int i = 0; i < 10_000; i++)
                o.Add(T0.AddSeconds(i * 0.1), (rnd.NextDouble() - 0.5) * 0.02, (rnd.NextDouble() - 0.5) * 0.02, 0);

            Assert.That(o.TotalDistanceM, Is.EqualTo(0));
            Assert.That(o.TotalMovingSec, Is.EqualTo(0));
        }

        [Test]
        public void Nespojitost_SeNepocitaJakoJizda()
        {
            var o = new Odometer();
            Jed(o, 0, 5, 0, 1.0);                       // 5 m
            Jed(o, 5.1, 10, 500, 1.0);                  // skok o ~495 m za 0,1 s, pak dalsich ~4,9 m

            Assert.That(o.TotalDistanceM, Is.EqualTo(9.9).Within(0.1));
        }

        [Test]
        public void Since_OdZacatkuMise_JenDrahaPoNem()
        {
            // Pred misi robot 20 m popojel (treba ho tlacili na start), pak 30 s stal, pak jel 40 s.
            var o = new Odometer();
            Jed(o, 0, 20, 0, 1.0);
            Jed(o, 20.1, 50, 20, 0);
            Jed(o, 50.1, 90, 20, 0.5);

            // Mise zacala v 35 s (uprostred stani).
            var r = o.Since(T0.AddSeconds(35));
            Assert.That(r.DistanceM, Is.EqualTo(20).Within(0.1));
            Assert.That(r.MovingSec, Is.EqualTo(40).Within(0.2));
            Assert.That(r.Truncated, Is.False);
        }

        [Test]
        public void Since_InterpolujeMeziKontrolnimiBody()
        {
            var o = new Odometer();
            Jed(o, 0, 20, 0, 1.0);

            // 7,3 s neni na kontrolnim bodu (ty jsou po 2 s) - interpolace musi dat ~12,7 m.
            Assert.That(o.Since(T0.AddSeconds(7.3)).DistanceM, Is.EqualTo(12.7).Within(0.1));
            // Za poslednim kontrolnim bodem.
            Assert.That(o.Since(T0.AddSeconds(19.5)).DistanceM, Is.EqualTo(0.5).Within(0.1));
        }

        [Test]
        public void Since_StarsiNezHistorie_PodhodnoceneAOznacene()
        {
            var o = new Odometer(capacity: 5);          // 5 bodu po 2 s = ~10 s historie
            Jed(o, 0, 30, 0, 1.0);

            var r = o.Since(T0);
            Assert.That(r.Truncated, Is.True);
            Assert.That(r.DistanceM, Is.LessThan(30));
        }

        /// <summary>
        /// Mise FreeRun meri cas od prvniho SNIMKU, ktery prijde driv nez prvni stav fuze, takze se
        /// dotazuje kousek pred prvni vzorek. To neni zahozena historie — pred prvnim vzorkem robot
        /// nic neujel. Stranka pritom psala „a vic — starsi nez historie" (simulace 6. 10. 2026).
        /// </summary>
        [Test]
        public void Since_PredPrvnimVzorkem_NeniPodhodnocene()
        {
            var o = new Odometer();
            Jed(o, 0, 10, 0, 1.0);

            var r = o.Since(T0.AddSeconds(-0.3));
            Assert.That(r.Truncated, Is.False);
            Assert.That(r.DistanceM, Is.EqualTo(10).Within(0.06));
        }

        [Test]
        public void BezVzorku_Nuly()
        {
            var o = new Odometer();
            var r = o.Since(T0);
            Assert.That(r.DistanceM, Is.EqualTo(0));
            Assert.That(o.LastTime, Is.EqualTo(default(DateTime)));
        }
    }
}
