using System;
using System.Linq;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Runtime
{
    public class MeasurementMapperTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Imu_MapsToHeadingAndAngularRate()
        {
            var mapper = new DefaultMeasurementMapper();
            var imu = TestHelpers.MakeImu(T0, yaw: 0.5, omega: 0.2);

            var ms = mapper.ToMeasurements(imu).ToList();

            Assert.That(ms.Count, Is.EqualTo(2));

            var heading = ms.OfType<HeadingMeasurement>().Single();
            Assert.That(heading.Value[0], Is.EqualTo(Conversions.NormalizeOrientation(0.5)).Within(1e-4));
            Assert.That(heading.TimeStamp, Is.EqualTo(T0));

            var rate = ms.OfType<ScalarStateMeasurement>().Single();
            Assert.That(rate.Value[0], Is.EqualTo(0.2).Within(1e-4));
        }

        [Test]
        public void Imu_WithoutRotation_OnlyAngularRate()
        {
            var mapper = new DefaultMeasurementMapper();
            var imu = new IMUState { TimeStamp = T0, AngularVelocity = new Vector3(0, 0, 0.3f) };

            var ms = mapper.ToMeasurements(imu).ToList();

            Assert.That(ms.Count, Is.EqualTo(1));
            Assert.That(ms[0], Is.InstanceOf<ScalarStateMeasurement>());
        }

        [Test]
        public void Imu_WithoutAngularVelocity_OnlyHeading()
        {
            var mapper = new DefaultMeasurementMapper();
            var q = new YawPitchRoll(0.1f, 0f, 0f).ToQuaternion(YawPitchRoll.Euler.zxy);
            var imu = new IMUState { TimeStamp = T0, Rotation = q };

            var ms = mapper.ToMeasurements(imu).ToList();

            Assert.That(ms.Count, Is.EqualTo(1));
            Assert.That(ms[0], Is.InstanceOf<HeadingMeasurement>());
        }
    }
}
