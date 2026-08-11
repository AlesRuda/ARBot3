using ARBot.Common.Fusion;
using ARBot.Common.Models;
using NUnit.Framework;
using System;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Testy kontraktu <see cref="AsyncFusionEngine.GetStateAt"/> mimo okno historie: vraci
    /// <c>null</c> misto tiche "nejlepsi snahy" (bazoveho, az o sekundu stareho stavu).
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    [TestFixture]
    public class GetStateAtOutsideWindowTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static AsyncFusionEngine Engine(double windowSeconds = 0.5)
        {
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(windowSeconds));
            for (int i = 0; i < 20; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(0.5, 0.05, T0.AddSeconds(i * 0.1), "Odo"));
            return e;
        }

        [Test]
        public void MimoOknoHistorie_VraciNull()
        {
            var e = Engine();

            // Okno 0,5 s, merenia po 0,1 s do T0+1,9 s -> tBase je hluboko za T0.
            Assert.That(e.GetStateAt(T0), Is.Null, "cas pred oknem historie");
            Assert.That(e.GetStateAt(T0.AddSeconds(-5)), Is.Null, "cas hluboko v minulosti");
        }

        [Test]
        public void PresneNaBaziOkna_VraciStav()
        {
            // Hranice okna sama uvnitr je - tam bazovy odhad plati bez extrapolace. Kdyby vracela
            // null, pri prvnim tiku (cas == cas prvniho merenia) by smycka zbytecne zastavila.
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(0.5));
            e.Enqueue(ScalarStateMeasurement.Velocity(0.5, 0.05, T0, "Odo"));

            Assert.That(e.GetStateAt(T0), Is.Not.Null, "cas presne na bazi okna");
            Assert.That(e.GetStateAt(T0.AddMilliseconds(-1)), Is.Null, "milisekundu pred bazi uz ne");
        }

        [Test]
        public void VOkneHistorie_VraciStav()
        {
            var e = Engine();
            var last = e.FilterTime;

            var s = e.GetStateAt(last.AddSeconds(-0.2));

            Assert.That(s, Is.Not.Null, "cas v okne historie musi dat odhad");
            Assert.That(double.IsFinite(s.X), Is.True);
        }

        [Test]
        public void Ted_ABudoucnost_VraciStav()
        {
            var e = Engine();
            var last = e.FilterTime;

            Assert.That(e.GetStateAt(last), Is.Not.Null, "cas posledniho merenia");
            Assert.That(e.GetStateAt(last.AddSeconds(0.5)), Is.Not.Null, "dopredna predikce");
        }

        [Test]
        public void BezMereni_VraciPocatecniStav()
        {
            // Neinicializovany engine zustava beze zmeny (aby se pri startu emitoval RobotStateMsg).
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(0.5));

            Assert.That(e.GetStateAt(T0), Is.Not.Null, "pred prvnim merenim se vraci pocatecni stav");
        }
    }
}
