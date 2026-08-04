using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Z predikované trajektorie robota (pár úseků <see cref="MotionArc"/>) posbírá překážky,
/// které mohou robota ovlivnit. Pro každou překážku promítne její střed na osu každého úseku
/// (analyticky, O(1)) a porovná kolmý odstup s nafouknutým polokoridorem
/// <c>w = obal robota + velikost překážky + k·σ</c>. Vrací hrozby obohacené o čas do kolize,
/// vzdálenost podél dráhy, boční odstup a závažnost, setříděné dle závažnosti a času.
/// </summary>
public sealed class ObstacleCollisionDetector
{
    private readonly TrajectoryPredictor _predictor;

    public ObstacleCollisionDetector(TrajectoryPredictor? predictor = null)
        => _predictor = predictor ?? new TrajectoryPredictor();

    public IReadOnlyList<ObstacleThreat> Detect(
        RobotState state,
        ControlCommand cmd,
        RobotFootprint footprint,
        IEnumerable<Obstacle> obstacles,
        PerceptionOptions options)
    {
        var traj = _predictor.Predict(state, cmd, options);
        var threats = new List<ObstacleThreat>();

        // žádná trajektorie (stojící robot) → nic k proložení
        if (traj.Arcs.Count == 0)
            return threats;

        double k = options.SigmaK;
        double footprintRadius = footprint.BoundingRadius;
        double stoppingDist = TrajectoryPredictor.StoppingDistanceMeters(cmd, options);

        foreach (var obstacle in obstacles)
        {
            double margin = footprintRadius + obstacle.RadiusMeters;

            // promítni střed na každý úsek; drž nejbližší přiblížení a příznak zásahu
            bool hit = false;
            double minLateral = double.PositiveInfinity;
            double ttc = 0, distAlong = 0;

            foreach (var arc in traj.Arcs)
            {
                var proj = arc.Project(obstacle.Center);

                if (proj.Lateral < minLateral)
                {
                    minLateral = proj.Lateral;
                    distAlong = proj.ArcLength;
                    ttc = proj.Time;
                }

                if (proj.Lateral <= margin + k * proj.Sigma)
                    hit = true;
            }

            if (!hit)
                continue;

            double nominalClearance = minLateral - margin;
            ThreatSeverity severity = nominalClearance <= 0
                ? (distAlong <= stoppingDist ? ThreatSeverity.Unavoidable : ThreatSeverity.Imminent)
                : ThreatSeverity.Watch;

            threats.Add(new ObstacleThreat(obstacle, ttc, distAlong, nominalClearance, severity));
        }

        threats.Sort((l, r) =>
        {
            int bySeverity = r.Severity.CompareTo(l.Severity);        // závažnější první
            return bySeverity != 0
                ? bySeverity
                : l.TimeToCollisionSeconds.CompareTo(r.TimeToCollisionSeconds); // dřívější první
        });

        return threats;
    }
}
