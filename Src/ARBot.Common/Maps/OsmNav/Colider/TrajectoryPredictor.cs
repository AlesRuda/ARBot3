using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Modeluje budoucí dráhu robota unicycle modelem a rozkládá ji na málo úseků s konstantní
/// křivostí (<see cref="MotionArc"/>): fáze jízdy pod aktuálním řízením a fáze brzdění do
/// zastavení (s reprezentativním — průměrným — poloměrem). Horizont je natažen tak, aby koridor
/// sahal za bod posledního možného zabrzdění (reakční + brzdná dráha + rezerva), takže překážku
/// lze detekovat včas. Jízda i brzdění sdílejí požadovanou úhlovou rychlost.
/// </summary>
public sealed class TrajectoryPredictor
{
    private const double Eps = 1e-6;
    private const double StraightYawRate = 1e-4;   // pod touto |ω| bereme úsek jako rovný

    /// <summary>Doba brzdění do zastavení z cestovní rychlosti [s].</summary>
    public static double BrakingTimeSeconds(ControlCommand cmd)
    {
        double vc = Math.Max(0.0, cmd.RequestedSpeed);
        return cmd.BrakingDeceleration is > 0
            ? vc / cmd.BrakingDeceleration.Value
            : Math.Max(0.0, cmd.TimeToFullStopSeconds);
    }

    /// <summary>
    /// Dráha do úplného zastavení = reakční dráha + brzdná dráha [m].
    /// Za tímto bodem už robot nedokáže brzděním zastavit → kolize je „nevyhnutelná“.
    /// </summary>
    public static double StoppingDistanceMeters(ControlCommand cmd, PerceptionOptions options)
    {
        double vc = Math.Max(0.0, cmd.RequestedSpeed);
        double brakeDist = 0.5 * vc * BrakingTimeSeconds(cmd);
        double reactDist = vc * options.ReactionTimeSeconds;
        return reactDist + brakeDist;
    }

    public PredictedTrajectory Predict(RobotState state, ControlCommand cmd, PerceptionOptions options)
    {
        double omega = cmd.RequestedYawRate;
        double vc = Math.Max(0.0, cmd.RequestedSpeed);
        double speed = Math.Max(0.0, state.Speed);
        double sigma0 = state.Covariance.PositionSigma;
        double sigmaGrowth = state.Covariance.SigmaHeading * vc;   // lateralní drift z nejistoty směru

        var arcs = new List<MotionArc>();

        // robot stojí a nemá jet → stacionární trajektorie
        if (vc <= Eps && speed <= Eps)
            return new PredictedTrajectory(arcs, 0.0, 0.0);

        double brakeTime = BrakingTimeSeconds(cmd);
        double brakeDist = 0.5 * vc * brakeTime;
        double stoppingDist = StoppingDistanceMeters(cmd, options);
        double horizon = Math.Max(
            stoppingDist + options.SafetyMarginMeters,
            Math.Max(options.MinHorizonMeters, vc * options.MinHorizonSeconds));

        double cruiseTime = vc > Eps
            ? Math.Max(options.ReactionTimeSeconds, (horizon - brakeDist) / vc)
            : 0.0;
        double cruiseDist = vc * cruiseTime;

        // průběžný stav na konci posledního úseku
        var pos = state.Position;
        double heading = state.Heading;
        double dist = 0.0, time = 0.0;

        // fáze jízdy (konstantní vc)
        if (cruiseDist > Eps)
        {
            var arc = MakeArc(pos, heading, vc, omega, cruiseTime,
                dist, time, sigma0 + sigmaGrowth * time, sigma0 + sigmaGrowth * (time + cruiseTime));
            arcs.Add(arc);
            pos = arc.End; heading = arc.EndHeading;
            dist += arc.Length; time += cruiseTime;
        }

        // fáze brzdění (reprezentativní průměrná rychlost vc/2)
        if (brakeTime > Eps && vc > Eps)
        {
            double vAvg = vc / 2.0;
            var arc = MakeArc(pos, heading, vAvg, omega, brakeTime,
                dist, time, sigma0 + sigmaGrowth * time, sigma0 + sigmaGrowth * (time + brakeTime));
            arcs.Add(arc);
            dist += arc.Length; time += brakeTime;
        }

        return new PredictedTrajectory(arcs, time, dist);
    }

    /// <summary>Postaví úsek dráhy z rychlosti, úhlové rychlosti a doby (rovný, nebo oblouk).</summary>
    private static MotionArc MakeArc(Point2D start, double heading, double speed, double omega,
        double duration, double startDist, double startTime, double startSigma, double endSigma)
    {
        double length = speed * duration;
        if (Math.Abs(omega) < StraightYawRate)
        {
            return MotionArc.Straight(start, heading, length,
                startDist, startTime, startTime + duration, startSigma, endSigma);
        }
        double radiusSigned = speed / omega;     // kladné → střed vlevo (CCW)
        double sweep = omega * duration;
        return MotionArc.Curved(start, heading, radiusSigned, sweep,
            startDist, startTime, startTime + duration, startSigma, endSigma);
    }
}
