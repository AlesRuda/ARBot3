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
    }
}
