using ARBot.Common.Maps.OsmNav.Colider;

namespace ARBot.Common.Tests.OsmNav.Colider;

public class ObstacleCollisionDetectorTests
{
    private static readonly RobotFootprint Footprint = new(Length: 0.8, RearRadius: 0.3, FrontRadius: 0.3);

    private static RobotState State(double speed, double yaw = 0, PoseCovariance? cov = null)
        => new(new Point2D(0, 0), 0, speed, yaw, cov ?? new PoseCovariance(0, 0, 0));

    private static ControlCommand Drive(double speed, double yaw = 0)
        => new(RequestedSpeed: speed, RequestedYawRate: yaw, TimeToFullStopSeconds: 1);

    [Test]
    public void Detect_ObstacleInPath_ReturnsThreat()
    {
        var detector = new ObstacleCollisionDetector();
        var obstacles = new[] { new Obstacle(1, new Point2D(1.0, 0), RadiusMeters: 0.3) };

        var threats = detector.Detect(State(2), Drive(2), Footprint, obstacles, new PerceptionOptions());

        Assert.That(threats, Has.Exactly(1).Items);
        var threat = threats.Single();
        Assert.That(threat.Obstacle.Id, Is.EqualTo(1));
        Assert.That(threat.TimeToCollisionSeconds > 0);
        Assert.That(threat.DistanceAlongPathMeters, Is.InRange(0.0, 1.2));
        Assert.That(threat.Severity, Is.EqualTo(ThreatSeverity.Unavoidable));
    }

    [Test]
    public void Detect_ObstacleToTheSide_NoThreat()
    {
        var detector = new ObstacleCollisionDetector();
        var obstacles = new[] { new Obstacle(1, new Point2D(1.0, 5.0), RadiusMeters: 0.3) };

        var threats = detector.Detect(State(2), Drive(2), Footprint, obstacles, new PerceptionOptions());

        Assert.That(threats, Is.Empty);
    }

    [Test]
    public void Detect_ObstacleBehind_NoThreat()
    {
        var detector = new ObstacleCollisionDetector();
        var obstacles = new[] { new Obstacle(1, new Point2D(-2.0, 0), RadiusMeters: 0.3) };

        var threats = detector.Detect(State(2), Drive(2), Footprint, obstacles, new PerceptionOptions());

        Assert.That(threats, Is.Empty);
    }

    [Test]
    public void Detect_FarObstacleWithinHorizon_IsImminentNotUnavoidable()
    {
        var detector = new ObstacleCollisionDetector();
        // stopping dist ≈ 0.6 + 1.0 = 1.6 m; překážka za ním, ale v horizontu (~2.1 m)
        var obstacles = new[] { new Obstacle(1, new Point2D(1.9, 0), RadiusMeters: 0.15) };

        var threats = detector.Detect(State(2), Drive(2), Footprint, obstacles, new PerceptionOptions());

        Assert.That(threats, Has.Exactly(1).Items);
        var threat = threats.Single();
        Assert.That(threat.Severity, Is.EqualTo(ThreatSeverity.Imminent));
    }

    [Test]
    public void Detect_UncertaintyInflation_FlagsMarginalObstacleAsWatch()
    {
        var detector = new ObstacleCollisionDetector();
        var cov = new PoseCovariance(SigmaX: 0.2, SigmaY: 0.2, SigmaHeading: 0.0);
        // boční odstup 0.8 m; nominální polokoridor = 0.3 + 0.2 = 0.5 → geometricky mimo
        var obstacles = new[] { new Obstacle(1, new Point2D(0.6, 0.8), RadiusMeters: 0.2) };

        var noInflation = detector.Detect(State(2, cov: cov), Drive(2), Footprint, obstacles,
            new PerceptionOptions(SigmaK: 0));
        var withInflation = detector.Detect(State(2, cov: cov), Drive(2), Footprint, obstacles,
            new PerceptionOptions(SigmaK: 5));

        Assert.That(noInflation, Is.Empty);
        Assert.That(withInflation, Has.Exactly(1).Items);
        var threat = withInflation.Single();
        Assert.That(threat.Severity, Is.EqualTo(ThreatSeverity.Watch));
        Assert.That(threat.LateralClearanceMeters > 0); // nominálně je odstup kladný
    }

    [Test]
    public void Detect_MultipleObstacles_SortedBySeverityThenTime()
    {
        var detector = new ObstacleCollisionDetector();
        var obstacles = new[]
        {
            new Obstacle(10, new Point2D(1.9, 0), RadiusMeters: 0.15), // Imminent, dál
            new Obstacle(20, new Point2D(0.8, 0), RadiusMeters: 0.3),  // Unavoidable, blíž
        };

        var threats = detector.Detect(State(2), Drive(2), Footprint, obstacles, new PerceptionOptions());

        Assert.That(threats.Count, Is.EqualTo(2));
        Assert.That(threats[0].Obstacle.Id, Is.EqualTo(20)); // Unavoidable první
        Assert.That(threats[0].Severity, Is.EqualTo(ThreatSeverity.Unavoidable));
        Assert.That(threats[1].Obstacle.Id, Is.EqualTo(10));
    }

    [Test]
    public void Detect_TurningLeft_FindsObstacleOnCurve()
    {
        var predictor = new TrajectoryPredictor();
        var detector = new ObstacleCollisionDetector(predictor);
        var state = State(2, yaw: 0.6);   // zatáčení vlevo (kladný yaw) stáčí dráhu do +Y
        var cmd = Drive(2, yaw: 0.6);
        var options = new PerceptionOptions();

        // překážka umístěná přímo na zakřivenou dráhu (bod na prvním oblouku)
        var traj = predictor.Predict(state, cmd, options);
        var onCurve = traj.Arcs[0].PointAt(0.5);
        var obstacles = new[] { new Obstacle(1, onCurve, RadiusMeters: 0.2) };

        var threats = detector.Detect(state, cmd, Footprint, obstacles, options);

        Assert.That(threats, Has.Exactly(1).Items);
        Assert.That(onCurve.Y > 0.01, "dráha se měla stočit do +Y");
    }
}
