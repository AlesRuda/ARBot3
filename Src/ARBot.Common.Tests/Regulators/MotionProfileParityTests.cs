using System;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Regulators
{
    /// <summary>
    /// Numerický guard kinematických profilů. <see cref="TrapezoidMotionProfile"/> hlídají golden hodnoty
    /// (zachycené z původního <c>Regulator</c> před jeho smazáním — parita byla dokázána v <see cref="PointRegulatorTests"/>).
    /// <see cref="SqrtMotionProfile"/> se ověřuje proti closed-form odmocninovému zákonu (nezávislý oracle).
    /// </summary>
    public class MotionProfileParityTests
    {
        private const double VMax = 0.8;
        private const double WMax = Math.PI / 6;
        private const double Accel = 0.20;
        private const double Rozchod = 0.41;
        private const double Tol = 1e-9;

        // Golden hodnoty TrapezoidMotionProfile.Dist2Speed (VMax=0.8, a=0.20).
        private static readonly (double dist, double vs, double ve, double speed, double time)[] TrapezoidGolden =
        {
            (0.05, 0.0, 0.0, 0.07200000000000002, 0.9600000000000002),
            (0.3,  0.0, 0.0, 0.19800000000000004, 2.29),
            (1.0,  0.2, 0.3, 0.45,                2.45),
            (2.0,  0.5, 0.0, 0.6300000000000001,  4.3500000000000005),
            (10.0, 0.0, 0.0, 0.8,                 16.599999999999998),
            (-6.1, 0.0, 0.0, -0.8,                11.725000000000001),
        };

        [Test]
        public void Trapezoid_Dist2Speed_MatchesGolden()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (var g in TrapezoidGolden)
            {
                var r = p.Dist2Speed(g.dist, g.vs, g.ve);
                Assert.That(r.Speed, Is.EqualTo(g.speed).Within(Tol), $"Speed @ d={g.dist} vs={g.vs} ve={g.ve}");
                Assert.That(r.RegulationTime, Is.EqualTo(g.time).Within(Tol), $"Time @ d={g.dist} vs={g.vs} ve={g.ve}");
            }
        }

        [Test]
        public void Trapezoid_Rot2RotSpeed_MatchesGolden()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            var r1 = p.Rot2RotSpeed(1.0, 0, 0);
            Assert.That(r1.RotationSpeed, Is.EqualTo(0.5235987755982988).Within(Tol));   // clamp na WMax
            Assert.That(r1.RegulationTime, Is.EqualTo(2.5465480620910004).Within(Tol));
            var r2 = p.Rot2RotSpeed(0.1, 0, 0);
            Assert.That(r2.RotationSpeed, Is.EqualTo(0.17560975609756105).Within(Tol));
            Assert.That(r2.RegulationTime, Is.EqualTo(0.5800000000000001).Within(Tol));
        }

        [Test]
        public void Sqrt_Dist2Speed_MatchesClosedForm()
        {
            var p = new SqrtMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (var dist in new[] { -6.1, -0.1, 0.05, 0.3, 2.0, 10.0 })
            {
                double expSpeed = Math.Sign(dist) * Math.Min(Math.Sqrt(4 * Accel * Math.Abs(dist)) / 2, VMax);
                double expTime = Math.Sqrt(Math.Abs(dist) / (4 * Accel));
                var r = p.Dist2Speed(dist, 0, 0);
                Assert.That(r.Speed, Is.EqualTo(expSpeed).Within(Tol), $"Speed @ {dist}");
                Assert.That(r.RegulationTime, Is.EqualTo(expTime).Within(Tol), $"Time @ {dist}");
            }
        }

        [Test]
        public void Sqrt_Speed2Dist_MatchesClosedForm()
        {
            var p = new SqrtMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (var vs in new[] { 0.0, 0.2, 0.5 })
                foreach (var ve in new[] { 0.0, 0.1 })
                    Assert.That(p.Speed2Dist(vs, ve),
                                Is.EqualTo((vs - ve) * (vs - ve) / (2 * Accel)).Within(Tol));
        }

        [Test]
        public void SpeedLimit_CouplesToRotationTime()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            // rt=0 -> bez omezeni; rt>0 -> min(speed, d/(stability*rt)), stability=4.
            Assert.That(p.SpeedLimit(0.8, 1.0, new RegulatorResult { RegulationTime = 0 }), Is.EqualTo(0.8).Within(Tol));
            Assert.That(p.SpeedLimit(0.8, 1.0, new RegulatorResult { RegulationTime = 1.0 }),
                        Is.EqualTo(Math.Min(0.8, 1.0 / (4 * 1.0))).Within(Tol));
        }
    }
}
