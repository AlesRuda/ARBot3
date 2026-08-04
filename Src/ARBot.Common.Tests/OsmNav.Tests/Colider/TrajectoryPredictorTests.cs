using ARBot.Common.Maps.OsmNav.Colider;

namespace ARBot.Common.Tests.OsmNav.Colider;

public class TrajectoryPredictorTests
{
    private static readonly PoseCovariance NoCov = new(0, 0, 0);

    private static RobotState State(double speed, double heading = 0, double yaw = 0, PoseCovariance? cov = null)
        => new(new Point2D(0, 0), heading, speed, yaw, cov ?? NoCov);

    [Test]
    public void Predict_StraightLine_ArcsStayOnAxis()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 2),
            new ControlCommand(RequestedSpeed: 2, RequestedYawRate: 0, TimeToFullStopSeconds: 1),
            new PerceptionOptions());

        Assert.That(traj.Arcs, Is.Not.Empty);
        foreach (var arc in traj.Arcs)
        {
            Assert.That(arc.IsStraight);
            Assert.That(arc.End.Y, Is.EqualTo(0).Within(1e-6));
        }
        Assert.That(traj.Arcs[^1].End.X > 0);
    }

    [Test]
    public void Predict_StraightLine_HorizonCoversReactionPlusBraking()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 2),
            new ControlCommand(RequestedSpeed: 2, RequestedYawRate: 0, TimeToFullStopSeconds: 1),
            new PerceptionOptions(ReactionTimeSeconds: 0.3, SafetyMarginMeters: 0.5));

        // d_react (0.6) + d_brake (1.0) + safety (0.5) = 2.1 m
        Assert.That(traj.HorizonMeters, Is.InRange(2.0, 2.25));
    }

    [Test]
    public void Predict_PositiveYaw_CurvesLeft()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 2, yaw: 0.5),
            new ControlCommand(RequestedSpeed: 2, RequestedYawRate: 0.5, TimeToFullStopSeconds: 1),
            new PerceptionOptions());

        Assert.That(traj.Arcs[0].IsStraight, Is.False);
        Assert.That(traj.Arcs[^1].End.Y > 0.01);
    }

    [Test]
    public void Predict_NegativeYaw_CurvesRight()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 2, yaw: -0.5),
            new ControlCommand(RequestedSpeed: 2, RequestedYawRate: -0.5, TimeToFullStopSeconds: 1),
            new PerceptionOptions());

        Assert.That(traj.Arcs[^1].End.Y < -0.01);
    }

    [Test]
    public void Predict_Stopped_ReturnsNoArcs()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 0),
            new ControlCommand(RequestedSpeed: 0, RequestedYawRate: 0, TimeToFullStopSeconds: 1),
            new PerceptionOptions());

        Assert.That(traj.Arcs, Is.Empty);
        Assert.That(traj.HorizonMeters, Is.EqualTo(0).Within(1e-6));
    }

    [Test]
    public void Predict_MinHorizonMeters_Respected()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 0.5),
            new ControlCommand(RequestedSpeed: 0.5, RequestedYawRate: 0, TimeToFullStopSeconds: 0.5),
            new PerceptionOptions(MinHorizonMeters: 5));

        Assert.That(traj.HorizonMeters >= 5.0 - 0.2);
    }

    [Test]
    public void Predict_SigmaGrowsAlongPath()
    {
        var predictor = new TrajectoryPredictor();
        var traj = predictor.Predict(
            State(speed: 2, cov: new PoseCovariance(SigmaX: 0.2, SigmaY: 0.1, SigmaHeading: 0.1)),
            new ControlCommand(RequestedSpeed: 2, RequestedYawRate: 0, TimeToFullStopSeconds: 1),
            new PerceptionOptions());

        Assert.That(traj.Arcs[0].StartSigma, Is.EqualTo(0.2).Within(1e-6));           // start = poziční σ
        Assert.That(traj.Arcs[^1].EndSigma > traj.Arcs[0].StartSigma);
        // σ navazuje mezi úseky a neklesá
        for (int i = 0; i < traj.Arcs.Count; i++)
            Assert.That(traj.Arcs[i].EndSigma >= traj.Arcs[i].StartSigma);
    }
}
