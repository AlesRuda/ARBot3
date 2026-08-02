using System;
using ARBot.Common.Common;
using ARBot.Common.Models;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Bodový regulátor: dovede robota na jeden cílový bod a v něm dosáhne <see cref="RegulatorWayPoint.Speed"/>
    /// (typicky 0 = zastavení). Nahrazuje původní <c>Regulator</c> i <c>SimplRegulator</c> — jediný rozdíl
    /// mezi nimi byl <see cref="IMotionProfile"/> (lichoběžník vs. odmocnina) a koeficient <c>stability</c>,
    /// oboje je teď parametr profilu. Viz <c>doc/path-following.md</c>.
    /// </summary>
    /// <remarks>
    /// Chování: nos na cíl (<c>beta = atan2(dy,dx) − orientace</c>), dopredný zásah
    /// <see cref="IMotionProfile.Dist2Speed"/>, rotační <see cref="IMotionProfile.Rot2RotSpeed"/>, dopredná
    /// rychlost shora omezená <see cref="IMotionProfile.SpeedLimit"/>; při <c>|beta| &gt; π/2</c> otočka na místě.
    /// Cíl je pevný (dán konstruktorem) — pro změnu cíle se vymění instance (stejně jako <see cref="PathResult"/>).
    /// </remarks>
    public sealed class PointRegulator : IRegulator
    {
        private readonly IMotionProfile profile;
        private readonly RegulatorWayPoint target;

        public PointRegulator(IMotionProfile profile, RegulatorWayPoint target)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <inheritdoc/>
        public bool IsFinished { get; private set; }

        /// <inheritdoc/>
        public RegulatorResult Control(IModelState state)
        {
            var p = target;
            double dx = p.X - state.X;
            double dy = p.Y - state.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);

            double beta = 0;
            if (d > p.MaxPositionError || p.Speed > 0)
            {
                beta = Conversions.NormalizeOrientation(Math.Atan2(dy, dx) - state.Orientation);
                IsFinished = false;
            }
            else
            {
                d = 0;
                IsFinished = true;
            }

            var retRot = profile.Rot2RotSpeed(beta, state.OrientationVelocity, 0);
            var ret = profile.Dist2Speed(d, state.Velocity, p.Speed);

            // Dopredná rychlost je shora omezená, aby se robot stihnul natočit.
            double s = profile.SpeedLimit(ret.Speed, d, retRot);

            // Otočený od cíle -> dopredná rychlost nula (otočka na místě).
            if (Math.Abs(beta) > Math.PI / 2)
                s = 0;

            return new RegulatorResult
            {
                Speed = s,
                RotationSpeed = retRot.RotationSpeed,
                RegulationTime = Math.Max(ret.RegulationTime, retRot.RegulationTime),
            };
        }
    }
}
