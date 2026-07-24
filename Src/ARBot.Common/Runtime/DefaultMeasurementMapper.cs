using System.Collections.Generic;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Vychozi prevod senzor -&gt; <see cref="IMeasurement"/> pro fuzni jadro (M1).
    /// IMUState -&gt; kurz (HeadingMeasurement) + uhlova rychlost (AngularRate).
    /// GPS / odometrie / kamera se doplni pozdeji.
    /// </summary>
    public sealed class DefaultMeasurementMapper : IMeasurementMapper
    {
        private readonly FusionConfig cfg;

        public DefaultMeasurementMapper(FusionConfig config = null)
        {
            cfg = config ?? new FusionConfig();
        }

        /// <inheritdoc/>
        public IEnumerable<IMeasurement> ToMeasurements(Message msg)
        {
            switch (msg)
            {
                case IMUState imu:
                    // Kurz z absolutni atitude (ENU): yaw = matematicka orientace.
                    if (imu.Rotation.HasValue)
                    {
                        var ypr = imu.YPR();
                        if (ypr != null)
                        {
                            double std = imu.OrientationUncertainty?.X ?? cfg.CompassHeadingStd;
                            yield return new HeadingMeasurement(ypr.Yaw, std, imu.TimeStamp, "IMU/heading");
                        }
                    }
                    // Uhlova rychlost (yaw rate) z gyroskopu (slozka Z v BODY framu).
                    if (imu.AngularVelocity.HasValue)
                    {
                        yield return ScalarStateMeasurement.AngularRate(
                            imu.AngularVelocity.Value.Z, cfg.GyroRateStd, imu.TimeStamp, "IMU/gyro");
                    }
                    break;
            }
        }
    }
}
