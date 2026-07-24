using ARBot.Common.Devices;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>Testy fiktivnich motoru <see cref="DummyMotors"/>.</summary>
    public class DummyMotorsTests
    {
        [Test]
        public void Drive_And_SetAcceleration_DoNotThrow()
        {
            var m = new DummyMotors();
            Assert.DoesNotThrow(() => m.Drive(1.0, 0.5));
            Assert.DoesNotThrow(() => m.SetAcceleration(0.2));
        }

        [Test]
        public void GetLastMeasurement_ReturnsNonNullZeroState()
        {
            var m = new DummyMotors();
            IMotorState s = m.GetLastMeasurement();
            Assert.That(s, Is.Not.Null);
            Assert.That(s.LeftEncoder, Is.EqualTo(0.0));
            Assert.That(s.RightEncoder, Is.EqualTo(0.0));
            Assert.That(s.IsEmergencyStop, Is.False);
        }

        [Test]
        public void Name_And_IsError_HaveExpectedValues()
        {
            var m = new DummyMotors();
            Assert.That(m.Name, Is.EqualTo("Dummy"));
            Assert.That(m.IsError, Is.False);
        }
    }
}
