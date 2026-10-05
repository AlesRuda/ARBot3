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
                    // Koncové uzly: bez rohu. Start = v_max nebo vlastní strop waypointu (skutečná
                    // rychlost je runtime). POZOR: strop startovního uzlu se z VLimit[0] v Control
                    // nikdy nečte (smyčka jde jen po uzlech před robotem) — podél prvního úseku ho
                    // od 3. 9. 2026 vynucuje Control přímo z WayPoints[seg].Speed. Viz PathResult.
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

                double cornerSpeed = CornerSpeed(theta, waypoints[i].MaxPositionError, EpsilonMargin,
                                                 segments[i - 1].Length, segments[i].Length, wMax,
                                                 out cornerRadius[i]);

                vNode[i] = CapFromWaypoint(Math.Min(vMax, cornerSpeed), waypoints[i]);
            }

            // 3) Zpětný průchod — brzdná obálka. Z každého uzlu musí jít ubrzdit na strop dalšího uzlu.
            //    Brzdný zákon patří profilu (Dist2MaxSpeed), ne sem: opsaný vzorec by se rozešel
            //    s profilem, který brzdí jinak než konstantní decelerací (SqrtMotionProfile).
            for (int i = n - 2; i >= 0; i--)
            {
                double brakeable = profile.Dist2MaxSpeed(segments[i].Length, vNode[i + 1]);
                if (vNode[i] > brakeable)
                    vNode[i] = brakeable;
            }

            return new PathResult(profile, waypoints, segments, turnAngle, cornerRadius, vNode, totalLength,
                                  lookaheadTime, lookaheadMin);
        }

        /// <summary>
        /// Strop rychlosti v rohu [m/s]: kruhový oblouk vepsaný do rohu z tolerance uzlu
        /// (<c>ε − rezerva</c>, nejméně desetina ε), oseknutý tak, aby tečná délka nepřesáhla
        /// polovinu kratšího sousedního úseku, a rychlost <c>ω_max · r</c>. Rovný průjezd = ∞,
        /// otočka = 0.
        ///
        /// <para>Veřejné kvůli lokálnímu plánovači: ten při vyhlazování porovnává čas zkratky
        /// s časem jemného dělení a musí znát, co na jeho rozích udělá regulátor (od 5. 10. 2026,
        /// <c>smoothcorners=</c>). Opsaný vzorec by se časem rozešel s tím, co se opravdu pojede.</para>
        /// </summary>
        /// <param name="theta">Deflexe směru v uzlu [rad], 0..π.</param>
        /// <param name="maxPositionError">Tolerance uzlu ε [m].</param>
        /// <param name="epsilonMargin">Rezerva odečtená od ε [m] (<see cref="EpsilonMargin"/>).</param>
        /// <param name="lenPrev">Délka úseku do uzlu [m].</param>
        /// <param name="lenNext">Délka úseku z uzlu [m].</param>
        /// <param name="maxRotationSpeed">ω_max [rad/s].</param>
        /// <param name="radius">Poloměr oblouku [m] (∞ = rovný, 0 = otočka).</param>
        public static double CornerSpeed(double theta, double maxPositionError, double epsilonMargin,
                                         double lenPrev, double lenNext, double maxRotationSpeed,
                                         out double radius)
        {
            if (theta < 1e-6)
            {
                // Rovný průjezd — žádné omezení z rohu.
                radius = double.PositiveInfinity;
                return double.PositiveInfinity;
            }
            if (theta > Math.PI - 1e-6)
            {
                // Otočka — nutné zastavení.
                radius = 0;
                return 0;
            }

            double eps = Math.Max(maxPositionError * 0.1, maxPositionError - epsilonMargin);
            double c = Math.Cos(theta / 2.0);
            double r = eps * c / (1.0 - c);
            // Osekání: tečná délka rohu nesmí přesáhnout ½ kratšího sousedního úseku.
            double tan = Math.Tan(theta / 2.0);
            double tMax = 0.5 * Math.Min(lenPrev, lenNext);
            if (r * tan > tMax)
                r = tMax / tan;
            radius = r;
            return maxRotationSpeed * r;
        }

        /// <summary>Volitelný strop rychlosti z waypointu (aplikuje se jen když Speed &gt; 0).</summary>
        private static double CapFromWaypoint(double cap, RegulatorWayPoint wp)
            => wp.Speed > 0 ? Math.Min(cap, wp.Speed) : cap;
    }
}
