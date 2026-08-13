using ARBot.Common.Simulation;

namespace ARBot.Common.Tests.Simulation;

/// <summary>
/// Testy ground-truth modelu pohybu (viz doc/virtual-hw.md).
/// Konvence: Theta matematicky (0 = vychod, +CCW), kladny difSpeed = otaceni DOLEVA.
/// </summary>
public class SimulatedRobotTests
{
    private const double WheelBase = 0.5;

    private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Robot v pocatku otoceny na vychod, s prakticky neomezenym zrychlenim.</summary>
    private static SimulatedRobot AtOrigin(double acceleration = 1000.0)
    {
        var r = new SimulatedRobot(WheelBase, T0) { X = 0, Y = 0, Theta = 0 };
        r.SetAcceleration(acceleration);
        return r;
    }

    [Test]
    public void DriveStraight_AdvancesAlongHeading()
    {
        var robot = AtOrigin();
        robot.Drive(2.0, 0.0);

        robot.Advance(T0.AddSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(robot.X, Is.EqualTo(6.0).Within(0.05), "6 m na vychod za 3 s pri 2 m/s");
            Assert.That(robot.Y, Is.EqualTo(0.0).Within(0.01));
            Assert.That(robot.Theta, Is.EqualTo(0.0).Within(1e-6));
        });
    }

    /// <summary>
    /// Otaceni na miste: omega = 2*difSpeed/rozchod, kladny difSpeed = DOLEVA (CCW).
    /// Pri rozchodu 0,5 m a difSpeed 0,25 m/s vyjde 1 rad/s.
    /// </summary>
    [Test]
    public void RotateInPlace_PositiveDifSpeed_TurnsLeft()
    {
        var robot = AtOrigin();
        robot.Drive(0.0, 0.25);

        robot.Advance(T0.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(robot.Theta, Is.EqualTo(1.0).Within(0.02), "kladny difSpeed ma tocit doleva");
            Assert.That(robot.X, Is.EqualTo(0.0).Within(0.01), "otaceni na miste nema posouvat");
            Assert.That(robot.Y, Is.EqualTo(0.0).Within(0.01));
        });
    }

    /// <summary>Rampa zrychleni: za 1 s pri 0,5 m/s^2 nelze prekrocit 0,5 m/s.</summary>
    [Test]
    public void Acceleration_LimitsSpeedRamp()
    {
        var robot = AtOrigin(acceleration: 0.5);
        robot.Drive(2.0, 0.0);

        robot.Advance(T0.AddSeconds(1));

        // Ujeta draha za rampu 0 -> 0,5 m/s je 0,25 m; bez omezeni by to byly 2 m.
        Assert.That(robot.X, Is.EqualTo(0.25).Within(0.02));
    }
}
