using System;
using ARBot.Common.Common;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Plánovač dráhy. Z waypointů předpočítá geometrii rohů (kruhový oblouk z tolerance) a brzdnou
    /// obálku rychlosti (zpětný průchod) a vrátí <see cref="PathResult"/>. Kinematické limity (v_max,
    /// ω_max, zrychlení) přebírá z <see cref="IMotionProfile"/>. Viz <c>doc/path-following.md</c>.
    /// </summary>
    /// <remarks>
    /// Dopředný (akcelerační) průchod se nepočítá — akceleraci řeší runtime živě ze skutečné rychlosti
    /// (<see cref="Models.IModelState.Velocity"/>). Pro brzdný průchod se používá <see cref="IMotionProfile.Acceleration"/>
    /// (konzervativní, je-li skutečná decelerace vyšší). Bezpečnostní rezerva <see cref="EpsilonMargin"/>
    /// se odečítá od tolerance ε (kryje seříznutí zatáčky lookaheadem + oblouk-vs-klotoida ~1 cm).
    /// </remarks>
    public sealed class PathPlanner : IPathPlanner
    {
        private readonly IMotionProfile profile;
        private readonly double lookaheadTime;
        private readonly double lookaheadMin;

        /// <summary>Rezerva odečtená od tolerance ε při výpočtu poloměru rohu [m].</summary>
        public double EpsilonMargin { get; }

        /// <param name="profile">Kinematický profil (limity + zásahy).</param>
        /// <param name="epsilonMargin">Rezerva na toleranci ε [m] (kryje seříznutí lookaheadem + oblouk-vs-klotoida).</param>
        /// <param name="lookaheadTime">Čas dohledu τ_look [s] pro cílový bod řízení (<c>L_d = τ_look·v</c>).</param>
        /// <param name="lookaheadMin">Minimální vzdálenost cílového bodu [m] (floor při nízké rychlosti).</param>
        public PathPlanner(IMotionProfile profile, double epsilonMargin = 0.01,
                           double lookaheadTime = 0.3, double lookaheadMin = 0.15)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            EpsilonMargin = epsilonMargin;
            this.lookaheadTime = lookaheadTime;
            this.lookaheadMin = lookaheadMin;
        }

        /// <inheritdoc/>
        public IRegulator Plan(RegulatorWayPoint[] waypoints)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (waypoints.Length < 2) throw new ArgumentException("Dráha musí mít alespoň 2 body.", nameof(waypoints));

            int n = waypoints.Length;

            // 1) Úseky (geometrie).
            var segments = new PathSegment[n - 1];
            double cum = 0;
            for (int i = 0; i < n - 1; i++)
            {
                double dx = waypoints[i + 1].X - waypoints[i].X;
                double dy = waypoints[i + 1].Y - waypoints[i].Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) throw new ArgumentException($"Nulová délka úseku {i} (duplicitní body).", nameof(waypoints));
                segments[i] = new PathSegment
                {
                    StartX = waypoints[i].X,
                    StartY = waypoints[i].Y,
                    DirX = dx / len,
                    DirY = dy / len,
                    Length = len,
                    CumStart = cum,
                };
                cum += len;
            }
            double totalLength = cum;

            // 2) Rohy + vrcholové stropy rychlosti.
            var turnAngle = new double[n];      // deflexe směru v uzlu (0 na koncích)
            var cornerRadius = new double[n];   // poloměr oblouku (∞ = rovný, 0 = otočka)
            var vNode = new double[n];          // strop rychlosti v uzlu

            double vMax = profile.MaxSpeed;
            double wMax = profile.MaxRotationSpeed;

            for (int i = 0; i < n; i++)
            {
                if (i == 0 || i == n - 1)
                {
                    // Koncové uzly: bez rohu. Start = v_max (skutečná rychlost je runtime).
                    // Poslední uzel = požadovaná koncová rychlost (Speed, default 0 = zastavení).
                    turnAngle[i] = 0;
                    cornerRadius[i] = double.PositiveInfinity;
                    vNode[i] = (i == n - 1) ? waypoints[i].Speed : CapFromWaypoint(vMax, waypoints[i]);
                    continue;
                }

                double prevHeading = Math.Atan2(segments[i - 1].DirY, segments[i - 1].DirX);
                double nextHeading = Math.Atan2(segments[i].DirY, segments[i].DirX);
                double theta = Math.Abs(Conversions.NormalizeOrientation(nextHeading - prevHeading));
                turnAngle[i] = theta;

                double cornerSpeed;
                if (theta < 1e-6)
                {
                    // Rovný průjezd — žádné omezení z rohu.
                    cornerRadius[i] = double.PositiveInfinity;
                    cornerSpeed = double.PositiveInfinity;
                }
                else if (theta > Math.PI - 1e-6)
                {
                    // Otočka — nutné zastavení.
                    cornerRadius[i] = 0;
                    cornerSpeed = 0;
                }
                else
                {
                    double eps = Math.Max(waypoints[i].MaxPositionError * 0.1,
                                          waypoints[i].MaxPositionError - EpsilonMargin);
                    double c = Math.Cos(theta / 2.0);
                    double r = eps * c / (1.0 - c);
                    // Osekání: tečná délka rohu nesmí přesáhnout ½ kratšího sousedního úseku.
                    double tan = Math.Tan(theta / 2.0);
                    double tMax = 0.5 * Math.Min(segments[i - 1].Length, segments[i].Length);
                    if (r * tan > tMax)
                        r = tMax / tan;
                    cornerRadius[i] = r;
                    cornerSpeed = wMax * r;
                }

                vNode[i] = CapFromWaypoint(Math.Min(vMax, cornerSpeed), waypoints[i]);
            }

            // 3) Zpětný průchod — brzdná obálka. Z každého uzlu musí jít ubrzdit na strop dalšího uzlu.
            double a = profile.Acceleration;
            for (int i = n - 2; i >= 0; i--)
            {
                double brakeable = Math.Sqrt(vNode[i + 1] * vNode[i + 1] + 2.0 * a * segments[i].Length);
                if (vNode[i] > brakeable)
                    vNode[i] = brakeable;
            }

            return new PathResult(profile, waypoints, segments, turnAngle, cornerRadius, vNode, totalLength,
                                  lookaheadTime, lookaheadMin);
        }

        /// <summary>Volitelný strop rychlosti z waypointu (aplikuje se jen když Speed &gt; 0).</summary>
        private static double CapFromWaypoint(double cap, RegulatorWayPoint wp)
            => wp.Speed > 0 ? Math.Min(cap, wp.Speed) : cap;
    }
}
