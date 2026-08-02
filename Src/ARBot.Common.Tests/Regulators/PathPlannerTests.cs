using System;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Regulators
{
    /// <summary>
    /// Testy plánovače dráhy <see cref="PathPlanner"/>: geometrie rohů (kruhový oblouk z tolerance),
    /// vrcholové stropy a brzdná obálka (zpětný průchod). Viz <c>doc/path-following.md</c>.
    /// </summary>
    public class PathPlannerTests
    {
        private const double VMax = 0.8;
        private const double WMax = Math.PI / 6;   // ω_max
        private const double Accel = 0.20;
        private const double Rozchod = 0.41;
        private const double Tol = 1e-4;

        private static PathPlanner MakePlanner(double epsilonMargin = 0.0)
            => new PathPlanner(new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod), epsilonMargin);

        private static RegulatorWayPoint Wp(double x, double y, double speed = 0, double eps = 0.1)
            => new RegulatorWayPoint { X = x, Y = y, Speed = speed, MaxPositionError = eps };

        [Test]
        public void Straight_SegmentGeometry()
        {
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(2, 0) });
            Assert.That(path.Segments.Length, Is.EqualTo(1));
            Assert.That(path.Segments[0].DirX, Is.EqualTo(1.0).Within(Tol));
            Assert.That(path.Segments[0].DirY, Is.EqualTo(0.0).Within(Tol));
            Assert.That(path.Segments[0].Length, Is.EqualTo(2.0).Within(Tol));
            Assert.That(path.Segments[0].CumStart, Is.EqualTo(0.0).Within(Tol));
            Assert.That(path.TotalLength, Is.EqualTo(2.0).Within(Tol));
        }

        [Test]
        public void BackwardPass_ShortFinalSegment_LimitsEntrySpeed()
        {
            // Tvůj příklad: 2 m rovně, pak 10 cm rovně s koncem v 0. Do posledního úseku nelze
            // vletět naplno — v uzlu P1 musí být rychlost už jen taková, aby stihl zastavit v 10 cm.
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(2, 0), Wp(2.1, 0, speed: 0) });

            Assert.That(path.VLimit[2], Is.EqualTo(0.0).Within(Tol), "konec = zastavení");
            // sqrt(0 + 2*a*0.1) = sqrt(0.04) = 0.2
            Assert.That(path.VLimit[1], Is.EqualTo(0.2).Within(Tol), "vstup do 10 cm úseku");
            // sqrt(0.2^2 + 2*a*2) = sqrt(0.84) = 0.9165 -> omezeno v_max
            Assert.That(path.VLimit[0], Is.EqualTo(VMax).Within(Tol), "start omezen v_max");
        }

        [Test]
        public void Corner90_RadiusAndCornerSpeed()
        {
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(1, 0), Wp(1, 1, speed: 0) });

            Assert.That(path.TurnAngle[1], Is.EqualTo(Math.PI / 2).Within(Tol));
            // R = eps*cos(45)/(1-cos(45)), eps=0.1
            double c = Math.Cos(Math.PI / 4);
            double rExpected = 0.1 * c / (1 - c);
            Assert.That(path.CornerRadius[1], Is.EqualTo(rExpected).Within(Tol));
            // corner speed = ω_max * R (nižší než v_max) -> je to strop v uzlu (backward pass ho nezvedne)
            Assert.That(path.VLimit[1], Is.EqualTo(WMax * rExpected).Within(Tol));
        }

        [Test]
        public void Corner_ShortSegments_ClampsRadiusToHalfSegment()
        {
            // Úseky 0.2 m -> tečná délka rohu (R*tan45 = R) je omezená na ½ úseku = 0.1 m.
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(0.2, 0), Wp(0.2, 0.2, speed: 0) });
            Assert.That(path.CornerRadius[1], Is.EqualTo(0.1).Within(Tol));
            Assert.That(path.VLimit[1], Is.EqualTo(WMax * 0.1).Within(Tol));
        }

        [Test]
        public void UTurn_ForcesStop()
        {
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(1, 0), Wp(0, 0, speed: 0) });
            Assert.That(path.TurnAngle[1], Is.EqualTo(Math.PI).Within(Tol));
            Assert.That(path.CornerRadius[1], Is.EqualTo(0.0).Within(Tol));
            Assert.That(path.VLimit[1], Is.EqualTo(0.0).Within(Tol), "otočka = zastavení v uzlu");
        }

        [Test]
        public void Collinear_NoCornerLimit()
        {
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(1, 0), Wp(2, 0), Wp(3, 0, speed: 0) });
            Assert.That(path.CornerRadius[1], Is.EqualTo(double.PositiveInfinity));
            Assert.That(path.CornerRadius[2], Is.EqualTo(double.PositiveInfinity));
            // Poslední úsek: sqrt(2*a*1) = sqrt(0.4) = 0.6325 v uzlu P2.
            Assert.That(path.VLimit[2], Is.EqualTo(Math.Sqrt(0.4)).Within(Tol));
            Assert.That(path.VLimit[0], Is.EqualTo(VMax).Within(Tol));
        }

        [Test]
        public void EpsilonMargin_ReducesCornerRadius()
        {
            var noMargin = (PathResult)MakePlanner(0.0).Plan(new[] { Wp(0, 0), Wp(1, 0), Wp(1, 1, speed: 0) });
            var withMargin = (PathResult)MakePlanner(0.01).Plan(new[] { Wp(0, 0), Wp(1, 0), Wp(1, 1, speed: 0) });
            Assert.That(withMargin.CornerRadius[1], Is.LessThan(noMargin.CornerRadius[1]));
        }

        [Test]
        public void WaypointSpeed_CapsVertex()
        {
            var path = (PathResult)MakePlanner().Plan(new[] { Wp(0, 0), Wp(1, 0, speed: 0.3), Wp(2, 0, speed: 0) });
            Assert.That(path.VLimit[1], Is.LessThanOrEqualTo(0.3 + Tol), "waypoint.Speed omezuje uzel");
        }

        [Test]
        public void Plan_InvalidInput_Throws()
        {
            Assert.Throws<ArgumentException>(() => MakePlanner().Plan(new[] { Wp(0, 0) }));
            Assert.Throws<ArgumentException>(() => MakePlanner().Plan(new[] { Wp(0, 0), Wp(0, 0) }));
            Assert.Throws<ArgumentNullException>(() => MakePlanner().Plan(null));
        }
    }
}
