using System;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Lichoběžníkový (accel-limitovaný) kinematický profil diskrétního regulátoru — motory
    /// akcelerují konstantní hodnotou, zásah se mění v periodě <c>tSam</c>. Matematika je převzatá
    /// z <see cref="Regulator.Dist2Speed2"/> (dokud ji nový kód nenahradí, zůstává i tam; shodu
    /// hlídá paritní test <c>MotionProfileParityTests</c>). Viz <c>doc/path-following.md</c>.
    /// </summary>
    public sealed class TrapezoidMotionProfile : IMotionProfile
    {
        private readonly double maxSpeed;
        private readonly double maxOrientationSpeed;
        private readonly double acceleration;
        private readonly double rozchod2;
        private readonly double stability;
        private readonly double tSam;

        public double MaxSpeed => maxSpeed;
        public double MaxRotationSpeed => maxOrientationSpeed;
        public double Acceleration => acceleration;

        /// <param name="maxSpeed">maximální dopredná rychlost [m/s]</param>
        /// <param name="maxOrientationSpeed">maximální rychlost otáčení [rad/s]</param>
        /// <param name="acceleration">zrychlení [m/s^2] (i decelerace)</param>
        /// <param name="rozchod">rozchod kol [m]</param>
        /// <param name="stability">koeficient vazby dopredné rychlosti na dobu rotace (viz <see cref="SpeedLimit"/>)</param>
        /// <param name="tSam">vzorkovací perioda diskrétního regulátoru [s]</param>
        public TrapezoidMotionProfile(double maxSpeed, double maxOrientationSpeed, double acceleration,
                                      double rozchod, double stability = 4, double tSam = 0.1)
        {
            this.maxSpeed = maxSpeed;
            this.maxOrientationSpeed = maxOrientationSpeed;
            this.acceleration = acceleration;
            this.rozchod2 = rozchod / 2.0;
            this.stability = stability;
            this.tSam = tSam;
        }

        public double Speed2Dist(double startSpeed, double endSpeed)
        {
            double s = Math.Abs(startSpeed - endSpeed);
            return s * s / (2 * acceleration);
        }

        public RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed)
        {
            return Compute(dist, startSpeed, endSpeed, maxSpeed, acceleration, tSam);
        }

        public RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed)
        {
            var ret = Compute(beta * rozchod2, startRotSpeed * rozchod2, endRotSpeed * rozchod2,
                              maxOrientationSpeed * rozchod2, acceleration, tSam);
            return new RegulatorResult() { RegulationTime = ret.RegulationTime, RotationSpeed = ret.Speed / rozchod2 };
        }

        public double SpeedLimit(double speed, double d, RegulatorResult rotationResul)
        {
            if (rotationResul.RegulationTime != 0)
            {
                var sl = d / (stability * rotationResul.RegulationTime);
                return Math.Min(speed, sl);
            }
            return speed;
        }

        /// <summary>
        /// Diskrétní lichoběžníkový profil. Bit-identická kopie <see cref="Regulator.Dist2Speed2"/>
        /// (parita ověřena testem) — při změně nutno synchronizovat obě, dokud starý <c>Regulator</c>
        /// nezmizí.
        /// </summary>
        public static RegulatorResult Compute(double dist, double startSpeed, double endSpeed,
                                              double maxSpeed, double acceleration, double tSam)
        {
            double x = dist;
            if (x < 0)
            {
                x = -x;
                endSpeed = -endSpeed;
                startSpeed = -startSpeed;
            }

            double a = acceleration;
            double d = acceleration;
            double a2 = a * a;
            double d2 = d * d;
            double tSam2 = tSam * tSam;
            double ve = endSpeed;
            double ve2 = ve * ve;
            double vs = startSpeed;
            double vs2 = vs * vs;

            double ne = (Math.Sqrt(a2 * d2 * tSam2 - a2 * d * tSam * ve - a2 * d * tSam * vs + 2 * x * a2 * d + a2 * ve2 - a * d2 * tSam * ve - a * d2 * tSam * vs + 2 * x * a * d2 + a * d * ve2 + a * d * vs2 + d2 * vs2) - a * ve - d * ve + d2 * tSam) / (d * tSam * (a + d));
            if (Math.Abs(ne) > 2)
                ne = Math.Floor(ne);

            double vm = (ve + (ne - 1) * d * tSam) * 0.9;
            double ns = Math.Max(0, (vm - vs) / (a * tSam) + 1);

            if (vm < maxSpeed)
            {
                return new RegulatorResult() { Speed = vm * Math.Sign(dist), RegulationTime = (ns + ne) * tSam };
            }
            vm = maxSpeed;
            ne = (vm - ve) / (d * tSam) + 1;
            ns = (vm - vs) / (a * tSam) + 1;

            double xs = (ns * vs + ns * (ns - 1) / 2 * a * tSam) * tSam;
            double xe = (ne * ve + ne * (ne - 1) / 2 * d * tSam) * tSam;
            double nm = (x - xs - xe) / (vm * tSam);

            return new RegulatorResult() { Speed = vm * Math.Sign(dist), RegulationTime = (ns + nm + ne) * tSam };
        }
    }
}
