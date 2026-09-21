using System;
using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// <b>Jedno vadné měření nesmí otrávit filtr natrvalo.</b>
    ///
    /// <para><b>Nač to je:</b> EKF počítá inovaci a <c>S.Inverse()</c> bez kontroly konečnosti,
    /// takže NaN/∞ projde celým krokem — a <b>projde i gatingem</b>, protože porovnání
    /// <c>nis &gt; práh</c> je pro NaN nepravdivé. <c>AsyncFusionEngine</c> pak NaN zapeče do
    /// checkpointů i do <c>xBase/pBase</c> a zpět už cesta nevede: každý další dotaz na polohu
    /// vrátí NaN, řídicí smyčka podle něj spočítá NaN příkaz (nebo spadne) a robot jede podle
    /// posledního platného příkazu dál.</para>
    ///
    /// <para>Zdroje takového měření jsou reálné: poškozený rámec z UARTu, <c>YprU = 0</c> ze
    /// senzoru (→ nulové R → singulární S), degenerovaná kovariance z korelace s mapou.
    /// <c>VN100IMUBinary</c> dnes hlídá jen yaw, ne gyro, <c>YprU</c> ani akcelerometr.</para>
    /// </summary>
    [TestFixture]
    public class NanOchranaTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 15, 8, 0, 0, DateTimeKind.Utc);

        /// <summary>Měření libovolné hodnoty rychlosti — s volitelně rozbitým R nebo jakobiánem.</summary>
        private sealed class VadneMereni : IMeasurement
        {
            public DateTime TimeStamp { get; set; }
            public string Source => "Test/vada";
            public Vector<double> Value { get; set; }
            public Matrix<double> NoiseCovariance { get; set; }
            public double? GateThreshold => null;
            public GateMode GateMode => GateMode.Reject;
            public double? MaxStep => null;
            /// <summary>NaN v jakobianu — hodnota i R jsou pritom v poradku.</summary>
            public bool NanJakobian;

            public Vector<double> Predict(Vector<double> x)
                => Vector<double>.Build.Dense(1, x[EKFModel.IV]);

            public Matrix<double> Jacobian(Vector<double> x)
            {
                var h = Matrix<double>.Build.Dense(1, EKFModel.N, 0.0);
                h[0, EKFModel.IV] = NanJakobian ? double.NaN : 1.0;
                return h;
            }

            public Vector<double> Residual(Vector<double> z, Vector<double> hx) => z - hx;
        }

        private static VadneMereni Mereni(double hodnota, double sigma, DateTime at)
            => new VadneMereni
            {
                TimeStamp = at,
                Value = Vector<double>.Build.Dense(1, hodnota),
                NoiseCovariance = Matrix<double>.Build.Dense(1, 1, sigma * sigma),
            };

        private static void Rozjed(AsyncFusionEngine e)
        {
            for (int i = 0; i < 5; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(i * 0.1), "Odo"));
        }

        [Test]
        public void NekonecnaHodnotaMereni_SeZahodi()
        {
            var e = new AsyncFusionEngine(new EKFModel());
            Rozjed(e);
            int pred = e.BufferedCount;

            e.Enqueue(Mereni(double.NaN, 0.05, T0.AddSeconds(0.6)));

            Assert.That(e.BufferedCount, Is.EqualTo(pred), "vadne merenie se do okna nesmi dostat");
            Assert.That(e.DroppedNotFinite, Is.EqualTo(1), "a musi se to dat spocitat");
        }

        [Test]
        public void NekonecneR_SeZahodi()
        {
            var e = new AsyncFusionEngine(new EKFModel());
            Rozjed(e);
            int pred = e.BufferedCount;

            e.Enqueue(Mereni(1.0, double.PositiveInfinity, T0.AddSeconds(0.6)));

            Assert.That(e.BufferedCount, Is.EqualTo(pred));
            Assert.That(e.DroppedNotFinite, Is.EqualTo(1));
        }

        [Test]
        public void PoVadnemMereni_ZustaneOdhadPouzitelny()
        {
            var e = new AsyncFusionEngine(new EKFModel());
            Rozjed(e);

            e.Enqueue(Mereni(double.NaN, 0.05, T0.AddSeconds(0.6)));
            e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(0.7), "Odo"));

            var s = e.GetStateAt(T0.AddSeconds(0.7));

            Assert.That(s, Is.Not.Null);
            Assert.That(double.IsFinite(s.X) && double.IsFinite(s.Y) && double.IsFinite(s.Theta)
                        && double.IsFinite(s.V) && double.IsFinite(s.Omega), Is.True,
                        "jedno vadne merenie otravilo filtr natrvalo");
            Assert.That(s.V, Is.EqualTo(1.0).Within(0.2), "a odhad porad sleduje platna merenia");
        }

        /// <summary>
        /// Druhá vrstva: hodnota i R jsou konečné, ale krok <b>vyrobí</b> NaN (tady NaN v jakobiánu;
        /// v provozu spíš singulární <c>S</c> při nulovém R). Krok musí měření zamítnout, ne stav
        /// přepsat.
        /// </summary>
        [Test]
        public void NekonecnyVysledekKroku_MereniZamitne()
        {
            var model = new EKFModel();
            var x = model.X.Clone();
            var P = model.P.Clone();

            var m = Mereni(1.0, 0.05, T0);
            m.NanJakobian = true;

            var r = model.UpdateStep(x, P, m);

            Assert.That(r.Accepted, Is.False, "krok, ktery vyrobi NaN, nesmi projit");
            Assert.That(r.X.ToArray(), Is.EqualTo(x.ToArray()).AsCollection, "stav musi zustat beze zmeny");
        }

        /// <summary>
        /// ⚠️ <b>Singulární <c>S</c> nesmí stav přepsat nekonečny.</b> Nastane, když je nulové R
        /// <i>i</i> nejistota stavu v měřeném směru — tedy <c>imuheadingstd=0</c> a <c>YprU = 0</c>
        /// nad již sběhnutým filtrem. MathNet u singulární matice nehází výjimku, vrátí nekonečná
        /// čísla, takže se <c>K</c> a s ním celý stav tiše rozpadne.
        ///
        /// <para>Testuje se přímo přes <c>UpdateStep</c>, protože přes <c>Enqueue</c> se sem nedá
        /// dostat: tam měření zastaví už brána na konečnost, a nenulové <c>P</c> ze živého filtru
        /// dělá <c>S</c> regulární.</para>
        /// </summary>
        [Test]
        public void SingularniS_NeprepiseStav()
        {
            var model = new EKFModel();
            var x = model.X.Clone();
            var P = Matrix<double>.Build.Dense(EKFModel.N, EKFModel.N, 0.0);   // zadna nejistota

            var r = model.UpdateStep(x, P, Mereni(1.0, 0.0, T0));             // ani zadny sum

            Assert.That(r.Accepted, Is.False, "krok se singularnim S nesmi projit");
            Assert.That(r.X.ToArray(), Is.EqualTo(x.ToArray()).AsCollection, "stav musi zustat beze zmeny");
        }
    }
}
