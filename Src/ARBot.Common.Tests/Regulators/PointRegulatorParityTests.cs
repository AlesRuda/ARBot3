using System;
using ARBot.Common.Common;
using ARBot.Common.Fusion;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Regulators
{
    /// <summary>
    /// Chování bodového regulátoru <see cref="PointRegulator"/> (nahradil původní Regulator/SimplRegulator;
    /// parita s nimi byla dokázána před jejich smazáním). Ověřuje směr rotace k cíli, otočku na místě a
    /// dojezd/zastavení. Funguje s libovolným <see cref="IMotionProfile"/>.
    /// </summary>
    public class PointRegulatorTests
    {
        private const double VMax = 2.0;
        private const double WMax = 0.5;
        private const double Accel = 1.0;
        private const double Rozchod = 1.0;

        private static PointRegulator Make(double tx, double ty, double speed = 0, double eps = 0.1)
            => new PointRegulator(new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod),
                                  new RegulatorWayPoint { X = tx, Y = ty, Speed = speed, MaxPositionError = eps });

        [Test]
        public void RotatesLeft_WhenTargetToLeft()
        {
            // Robot v (1,2) orientovaný na východ (0), cíl (2,3) je vlevo vpředu -> rotace doleva (+).
            var pr = Make(2, 3);
            var state = new RobotState { X = 1, Y = 2, Orientation = 0 };
            Assert.That(pr.Control(state).RotationSpeed, Is.GreaterThan(0), "musí rotovat doleva");
        }

        [Test]
        public void RotatesRight_WhenTargetBehind()
        {
            // Stejný cíl, ale robot orientovaný na západ (π) -> cíl je vzadu vpravo -> rotace doprava (−).
            var pr = Make(2, 3);
            var state = new RobotState { X = 1, Y = 2, Orientation = Math.PI };
            var r = pr.Control(state);
            Assert.That(r.RotationSpeed, Is.LessThan(0), "musí rotovat doprava");
            Assert.That(r.Speed, Is.EqualTo(0), "otočený od cíle -> dopredná rychlost 0 (otočka na místě)");
        }

        [Test]
        public void Arrived_IsFinished_AndStops()
        {
            var pr = Make(0, 0, speed: 0, eps: 0.1);
            var state = new RobotState { X = 0.02, Y = 0.0, Orientation = 0 };  // uvnitř tolerance
            var r = pr.Control(state);
            Assert.That(pr.IsFinished, Is.True);
            Assert.That(r.Speed, Is.EqualTo(0), "v cíli stojí");
        }

        [Test]
        public void NotArrived_WhenOutsideTolerance()
        {
            var pr = Make(0, 0, speed: 0, eps: 0.1);
            var state = new RobotState { X = 1.0, Y = 0.0, Orientation = 0 };
            pr.Control(state);
            Assert.That(pr.IsFinished, Is.False);
        }

        [Test]
        public void WorksWithSqrtProfile()
        {
            var pr = new PointRegulator(new SqrtMotionProfile(VMax, WMax, Accel, Rozchod),
                                        new RegulatorWayPoint { X = 5, Y = 0, MaxPositionError = 0.1 });
            var r = pr.Control(new RobotState { X = 0, Y = 0, Orientation = 0 });
            Assert.That(r.Speed, Is.GreaterThan(0), "míří dopředu k cíli");
            Assert.That(double.IsFinite(r.RotationSpeed), Is.True);
        }
    }
}
