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

        /// <inheritdoc/>
        /// <remarks>
        /// Z VLASTNÍHO zákona profilu <c>v = √(a·d)</c> plyne <c>d = |v_s² − v_e²| / a</c> — tedy
        /// DVOJNÁSOBEK dráhy konstantní decelerace, protože tenhle profil brzdí dřív a měkčeji.
        /// <para><b>Opraveno 8. 9. 2026</b> — do té doby to počítalo <c>(v_s − v_e)²/(2a)</c>, což
        /// neodpovídalo ani pro <c>v_e = 0</c> (při <c>v_s = 0,8, a = 0,5</c> vyšlo 0,64 místo 1,28).
        /// Produkční volání nemělo, pinnul ho jen charakterizační test.</para>
        /// </remarks>
        public double Speed2Dist(double startSpeed, double endSpeed)
        {
            return Math.Abs(startSpeed * startSpeed - endSpeed * endSpeed) / acceleration;
        }

        public RegulatorResult Dist2Speed(double dist, double startSpeed, double endSpeed)
        {
            return SqrtLaw(dist, maxSpeed, acceleration);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Strop plyne z VLASTNIHO zákona profilu (<c>v = √(a·d)</c>), ne z konstantní decelerace —
        /// jinak by nebyl horní mezí jeho zásahu. Nenulové <paramref name="endSpeed"/> křivku
        /// POSUNE (<c>√(v_e² + a·d)</c>), aby v cílovém bodě dala právě <c>v_e</c>; podrazit ji jen
        /// podlahou <c>max(v_e, √(a·d))</c> by rozbilo inverzi vůči <see cref="Speed2Dist"/>.
        /// </remarks>
        public double Dist2MaxSpeed(double dist, double endSpeed)
        {
            double d = Math.Abs(dist);
            return Math.Min(maxSpeed, Math.Sqrt(endSpeed * endSpeed + acceleration * d));
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
