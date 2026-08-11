using System;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Overuje, ze <see cref="RobotState"/> spravne implementuje <see cref="IModelState"/>
    /// (mapovani Orientation/Velocity/OrientationVelocity a matice Rotation/Transformation).
    /// </summary>
    [TestFixture]
    public class RobotStateModelStateTests
    {
        [Test]
        public void ImplementsIModelState()
        {
            Assert.That(new RobotState() is IModelState, Is.True);
        }

        [Test]
        public void Mapping_OrientationVelocityXY()
        {
            var rs = new RobotState { X = 2.5, Y = -1.0, Theta = 0.75, V = 0.9, Omega = 0.3 };
            IModelState s = rs;

            Assert.That(s.Orientation, Is.EqualTo(0.75).Within(1e-12));
            Assert.That(s.Velocity, Is.EqualTo(0.9).Within(1e-12));
            Assert.That(s.OrientationVelocity, Is.EqualTo(0.3).Within(1e-12));
            Assert.That(s.X, Is.EqualTo(2.5).Within(1e-12));
            Assert.That(s.Y, Is.EqualTo(-1.0).Within(1e-12));

            // Orientation setter zapisuje zpet do Theta
            s.Orientation = 1.25;
            Assert.That(rs.Theta, Is.EqualTo(1.25).Within(1e-12));
        }

        [Test]
        public void Rotation_And_Transformation_DoNotThrow_AndTranslationMatches()
        {
            var rs = new RobotState { X = 3.0, Y = 4.0, Theta = 0.5, Pitch = 0.1, Roll = -0.2 };
            IModelState s = rs;

            var rot = s.Rotation;            // nesmi vyhodit
            var tr = s.Transformation;       // nesmi vyhodit

            // Rotace nema translacni cast
            Assert.That(rot.M41, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(rot.M42, Is.EqualTo(0f).Within(1e-5f));

            // Transformace nese posun (X, Y) v translacni radce
            Assert.That(tr.M41, Is.EqualTo(3.0f).Within(1e-4f));
            Assert.That(tr.M42, Is.EqualTo(4.0f).Within(1e-4f));
            Assert.That(tr.M43, Is.EqualTo(0f).Within(1e-5f));
        }
    }
}
