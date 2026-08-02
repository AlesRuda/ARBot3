using System;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Jednoduchý odmocninový kinematický profil: akční zásah = <c>√(a·d)</c> brzdná křivka (rychlost
    /// úměrná odmocnině zbývající vzdálenosti). Přebírá zákon z původního <c>SimplRegulator</c>,
    /// ale implementuje ho <b>konzistentně</b> podle kontraktu <see cref="IMotionProfile"/> (na rozdíl od
    /// <c>SimplRegulator.Control</c>, který rotaci počítal nekonzistentně). Viz <c>doc/path-following.md</c>.
    /// </summary>
    /// <remarks>
    /// <b>Omezení:</b> <see cref="Dist2Speed"/> ignoruje <c>startSpeed</c>/<c>endSpeed</c> — je to čistě
    /// polohový zákon brzdění do zastavení. Pro path controller (průjezd nenulovou rychlostí) je proto
    /// méně vhodný než <see cref="TrapezoidMotionProfile"/>; hodí se pro dojezd na bod.
    /// </remarks>
    public sealed class SqrtMotionProfile : IMotionProfile
    {
        private readonly double maxSpeed;
        private readonly double maxOrientationSpeed;
        private readonly double acceleration;
        private readonly double rozchod2;
        private readonly double stability;

        public double MaxSpeed => maxSpeed;
        public double MaxRotationSpeed => maxOrientationSpeed;
        public double Acceleration => acceleration;

        /// <param name="maxSpeed">maximální dopredná rychlost [m/s]</param>
        /// <param name="maxOrientationSpeed">maximální rychlost otáčení [rad/s]</param>
        /// <param name="acceleration">zrychlení [m/s^2]</param>
        /// <param name="rozchod">rozchod kol [m]</param>
        /// <param name="stability">koeficient vazby dopredné rychlosti na dobu rotace (viz <see cref="SpeedLimit"/>)</param>
        public SqrtMotionProfile(double maxSpeed, double maxOrientationSpeed, double acceleration,
                                 double rozchod, double stability = 2)
        {
            this.maxSpeed = maxSpeed;
            this.maxOrientationSpeed = maxOrientationSpeed;
            this.acceleration = acceleration;
            this.rozchod2 = rozchod / 2.0;
            this.stability = stability;
        }

        public double Speed2Dist(double startSpeed, double endSpeed)
        {
            double s = Math.Abs(startSpeed - endSpeed);
            return s * s / (2 * acceleration);
        }

        public RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed)
        {
            return SqrtLaw(dist, maxSpeed, acceleration);
        }

        public RegulatorResult Rot2RotSpeed(double beta, double startRotSpeed, double endRotSpeed)
        {
            // Konzistentně jako TrapezoidMotionProfile: linearizace na bod ve vzdálenosti rozchod/2,
            // clamp maximální otáčivou rychlostí (přepočtenou na lineární), zpět na rad/s.
            var r = SqrtLaw(beta * rozchod2, maxOrientationSpeed * rozchod2, acceleration);
            return new RegulatorResult() { RegulationTime = r.RegulationTime, RotationSpeed = r.Speed / rozchod2 };
        }

        public double SpeedLimit(double speed, double d, RegulatorResult rotationResul)
        {
            if (rotationResul.RegulationTime != 0)
                return Math.Min(speed, d / (stability * rotationResul.RegulationTime));
            return speed;
        }

        /// <summary>Odmocninový zákon: <c>v = sign(d)·min(√(4a·|d|)/2, v_max)</c>, čas = <c>√(|d|/(4a))</c>.</summary>
        public static RegulatorResult SqrtLaw(double dist, double maxSpeed, double acceleration)
        {
            double d = Math.Abs(dist);
            double v = Math.Sign(dist) * Math.Min(Math.Sqrt(4 * acceleration * d) / 2, maxSpeed);
            return new RegulatorResult() { Speed = v, RegulationTime = Math.Sqrt(d / (4 * acceleration)) };
        }
    }
}
