using System;
using ARBot.HAL.Devices.MotorDrivers;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Prevod zrychleni [m/s^2] na jednotky ridici jednotky motoru vcetne pojistek.
    ///
    /// <para>Proc pojistky: hodnota jde do <c>VAR 1</c>/<c>VAR 2</c> ridiciho skriptu, ktery z ni
    /// dela rampu <c>curSpeed += time*acceleration</c>. <b>Zaporna</b> hodnota by rampu hnala OD
    /// cile (druha vetev uz nenastane) az na saturaci, tedy plnou rychlost opacnym smerem;
    /// <b>nula</b> rampu zmrazi, takze uz jedouci robot by nezastavil ani pod nouzovym zastavenim
    /// (a protoze se rotace nuluje az pri <c>curSpeed=0</c>, jel by dal i v zatacce).
    /// Viz doc/virtual-hw.md.</para>
    /// </summary>
    public class MotorAccelerationTests
    {
        /// <summary>Obvod kola zvoleny tak, aby cisla vychazela kulate (600 * a / 0,5).</summary>
        private const double WheelCircumference = 0.5;

        [Test]
        public void TypicalValue_ConvertsToUnits()
        {
            Assert.That(MotorAcceleration.ToUnits(0.5, WheelCircumference), Is.EqualTo(600));
        }

        [Test]
        public void NegativeValue_IsTakenAsMagnitude()
        {
            Assert.That(MotorAcceleration.ToUnits(-0.5, WheelCircumference), Is.EqualTo(600),
                        "zaporne zrychleni by v jednotce znamenalo rozjezd na plnou opacnym smerem");
        }

        [Test]
        public void Zero_NeverReachesController()
        {
            Assert.That(MotorAcceleration.ToUnits(0.0, WheelCircumference), Is.EqualTo(1),
                        "nula by zmrazila rampu a nouzove zastaveni by nemelo cim brzdit");
        }

        [Test]
        public void TinyValue_RoundsUpInsteadOfToZero()
        {
            // 600 * 0,0001 / 0,5 = 0,12 -> zaokrouhleni by dalo 0
            Assert.That(MotorAcceleration.ToUnits(0.0001, WheelCircumference), Is.EqualTo(1));
        }

        [Test]
        public void InvalidWheelCircumference_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MotorAcceleration.ToUnits(0.5, 0));
        }

        // ---------------- Jednotky ridiciho skriptu (SDC2160Ex, od 5. 10. 2026) ----------------

        /// <summary>Plny rozsah rychlosti robotu: obvod kola · 260/60 ot/s (Profile.MaxTheoreticalSpeed).</summary>
        private const double MaxPossibleSpeed = 2.1598;

        /// <summary>
        /// Skript dela <c>curSpeed += time[ms] · v</c> nad miliontinami plneho rozsahu, takze za
        /// sekundu pribyde <c>v / 1000</c> rozsahu. Zpetny prepocet musi dat zadane zrychleni —
        /// to je jedina kontrola, ktera chyti zamenu jednotek (do 5. 10. 2026 sel do skriptu
        /// nativni prevod a rampa byla 2,6x strmejsi: 0,40 m/s² → 1,04 m/s², zmereno ze zaznamu).
        /// </summary>
        [TestCase(0.40)]
        [TestCase(0.50)]
        [TestCase(1.00)]
        public void ScriptUnits_DajiZpetZadaneZrychleni(double a)
        {
            int v = MotorAcceleration.ToScriptUnits(a, MaxPossibleSpeed);
            double zpet = v / 1000.0 * MaxPossibleSpeed;
            Assert.That(zpet, Is.EqualTo(a).Within(0.5 / 1000 * MaxPossibleSpeed), "zaokrouhleni na celou jednotku");
        }

        [Test]
        public void ScriptUnits_Typicka_0_40()
            => Assert.That(MotorAcceleration.ToScriptUnits(0.40, MaxPossibleSpeed), Is.EqualTo(185));

        [Test]
        public void ScriptUnits_PojistkyJakoNativni()
        {
            Assert.That(MotorAcceleration.ToScriptUnits(-0.4, MaxPossibleSpeed), Is.EqualTo(185), "zaporne = velikost");
            Assert.That(MotorAcceleration.ToScriptUnits(0.0, MaxPossibleSpeed), Is.EqualTo(1), "nula by zmrazila rampu");
            Assert.Throws<ArgumentOutOfRangeException>(() => MotorAcceleration.ToScriptUnits(0.4, 0));
        }
    }
}
