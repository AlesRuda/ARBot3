using System;
using ARBot.Common.Common;
using ARBot.Common.Models;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Naplánovaná dráha (výstup <see cref="PathPlanner.Plan"/>) připravená k řízení. Drží geometrii
    /// úseků a rohů a brzdnou obálku rychlosti (<see cref="VLimit"/>). <see cref="Control"/> každý tik
    /// lokalizuje robota na trase a spočte zásah. Viz <c>doc/path-following.md</c>.
    /// </summary>
    /// <remarks>
    /// Stavová instance (drží progres <see cref="lastSegment"/>), určená pro jednoho konzumenta;
    /// není thread-safe. Nová dráha = nová instance (progres se re-lokalizuje globálně).
    /// </remarks>
    public sealed class PathResult : IRegulator
    {
        private readonly IMotionProfile profile;
        private readonly double lookaheadTime;
        private readonly double lookaheadMin;

        // Progres: index úseku, na kterém byl robot naposledy lokalizován (-1 = zatím nikde -> globální hledání).
        private int lastSegment = -1;

        /// <summary>Waypointy (vstup plánu).</summary>
        public RegulatorWayPoint[] WayPoints { get; }
        /// <summary>Úseky mezi waypointy (geometrie).</summary>
        public PathSegment[] Segments { get; }
        /// <summary>Deflexe směru v uzlu (0 na koncích) [rad].</summary>
        public double[] TurnAngle { get; }
        /// <summary>Poloměr oblouku v uzlu (∞ = rovný průjezd, 0 = otočka) [m].</summary>
        public double[] CornerRadius { get; }
        /// <summary>Brzdná obálka — strop rychlosti v uzlu (po zpětném průchodu) [m/s].</summary>
        public double[] VLimit { get; }
        /// <summary>Celková délka trasy [m].</summary>
        public double TotalLength { get; }

        internal PathResult(IMotionProfile profile, RegulatorWayPoint[] wayPoints, PathSegment[] segments,
                            double[] turnAngle, double[] cornerRadius, double[] vLimit, double totalLength,
                            double lookaheadTime, double lookaheadMin)
        {
            this.profile = profile;
            this.lookaheadTime = lookaheadTime;
            this.lookaheadMin = lookaheadMin;
            WayPoints = wayPoints;
            Segments = segments;
            TurnAngle = turnAngle;
            CornerRadius = cornerRadius;
            VLimit = vLimit;
            TotalLength = totalLength;
        }

        /// <inheritdoc/>
        public bool IsFinished { get; private set; }

        /// <inheritdoc/>
        public RegulatorResult Control(IModelState state)
        {
            // 1) Lokalizace na trase (arc-length + index úseku).
            Localize(state.X, state.Y, out int seg, out double globalS);

            // 2) Cíl dosažen? Robot je v toleranci posledního waypointu -> stop.
            var last = WayPoints[WayPoints.Length - 1];
            double dxFin = last.X - state.X, dyFin = last.Y - state.Y;
            if (Math.Sqrt(dxFin * dxFin + dyFin * dyFin) <= last.MaxPositionError)
            {
                IsFinished = true;
                return new RegulatorResult { Speed = 0, RotationSpeed = 0, RegulationTime = 0 };
            }
            IsFinished = false;

            // 3) Dopredná rychlost: min přes všechny uzly před robotem "dojet do uzlu k na jeho VLimit".
            //    VLimit už folduje budoucnost, ale min přes více uzlů je hladké přes přechod uzlem.
            double v = state.Velocity;
            double vCmd = profile.MaxSpeed;
            double distToNext = double.PositiveInfinity;
            for (int k = seg + 1; k < WayPoints.Length; k++)
            {
                double sVertex = (k - 1 < Segments.Length)
                    ? Segments[k - 1].CumStart + Segments[k - 1].Length
                    : TotalLength;
                double distK = sVertex - globalS;
                if (distK < 0) continue;
                if (k == seg + 1) distToNext = distK;
                double vk = distK < 1e-6 ? VLimit[k] : profile.Dist2Speed(distK, v, VLimit[k]).Speed;
                if (vk < vCmd) vCmd = vk;
            }

            // 4) Rotační rychlost z lookahead bodu (řídí jen směr).
            double ld = Math.Max(lookaheadMin, lookaheadTime * Math.Abs(v));
            PointAtArcLength(globalS + ld, out double tx, out double ty);
            double beta = Conversions.NormalizeOrientation(Math.Atan2(ty - state.Y, tx - state.X) - state.Orientation);
            var rot = profile.Rot2RotSpeed(beta, state.OrientationVelocity, 0);

            // 5) Vazba dopredné rychlosti na dobu rotace + otočka na místě při velké odchylce.
            double dxT = tx - state.X, dyT = ty - state.Y;
            double distToTarget = Math.Sqrt(dxT * dxT + dyT * dyT);
            double s = profile.SpeedLimit(vCmd, distToTarget, rot);
            if (Math.Abs(beta) > Math.PI / 2)
                s = 0;

            return new RegulatorResult
            {
                Speed = s,
                RotationSpeed = rot.RotationSpeed,
                RegulationTime = Math.Max(rot.RegulationTime, 0),
            };
        }

        /// <summary>
        /// Lokalizuje robota na trase — projekce pózy na úseky. Lokální hledání kolem posledního úseku
        /// (progres), při prvním volání globální. Vrací index úseku a arc-length na trase.
        /// </summary>
        private void Localize(double px, double py, out int seg, out double globalS)
        {
            int start = lastSegment < 0 ? 0 : lastSegment;
            int end = lastSegment < 0 ? Segments.Length - 1 : Math.Min(Segments.Length - 1, lastSegment + 3);

            double bestCross = double.PositiveInfinity;
            int bestSeg = start;
            double bestS = Segments[start].CumStart;

            for (int i = start; i <= end; i++)
            {
                var sg = Segments[i];
                double t = (px - sg.StartX) * sg.DirX + (py - sg.StartY) * sg.DirY;
                if (t < 0) t = 0; else if (t > sg.Length) t = sg.Length;
                double projX = sg.StartX + t * sg.DirX;
                double projY = sg.StartY + t * sg.DirY;
                double cx = px - projX, cy = py - projY;
                double cross = Math.Sqrt(cx * cx + cy * cy);
                if (cross < bestCross)
                {
                    bestCross = cross;
                    bestSeg = i;
                    bestS = sg.CumStart + t;
                }
            }

            lastSegment = bestSeg;
            seg = bestSeg;
            globalS = bestS;
        }

        /// <summary>Bod na trase v daném arc-length (za koncem = poslední waypoint).</summary>
        private void PointAtArcLength(double s, out double x, out double y)
        {
            if (s >= TotalLength)
            {
                var lastWp = WayPoints[WayPoints.Length - 1];
                x = lastWp.X; y = lastWp.Y;
                return;
            }
            for (int i = 0; i < Segments.Length; i++)
            {
                var sg = Segments[i];
                if (s <= sg.CumStart + sg.Length || i == Segments.Length - 1)
                {
                    double t = s - sg.CumStart;
                    if (t < 0) t = 0;
                    x = sg.StartX + t * sg.DirX;
                    y = sg.StartY + t * sg.DirY;
                    return;
                }
            }
            x = Segments[0].StartX; y = Segments[0].StartY;
        }
    }
}
