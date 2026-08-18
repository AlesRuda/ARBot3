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


    /// <summary>
    /// REGRESE (18. 8. 2026, zaznam 20260818-093903.rec): zataceni se nesmi zastavit tim, ze obe
    /// kola prave brzdi na dorazu akcelerace.
    /// <para>Puvodni model rampoval KAZDE KOLO zvlast, takze pri saturaci obou se rozdil kol
    /// zmrazil a robot jel rovne, i kdyz se poructila zatacka: pri pozadavku +30 °/s vyslo jen
    /// +5,8 °/s. Skutecny ridici SW motoru (Src/RoboRun/RizeniDiffPodvozku.mbs) rampuje ZVLAST
    /// doprednou a zvlast rotacni slozku, takze tam tenhle jev nastat nemuze.</para>
    /// </summary>
    [Test]
    public void HardBrakeWhileTurning_RotationStillReachesRequest()
    {
        // Zrychleni jako na robotu (Profile.MaxAcceleration), aby doraz rampy vubec nastal.
        var robot = AtOrigin(acceleration: 0.5);

        robot.Drive(1.2, 0.0);                       // rozjezd rovne na plnou rychlost
        robot.Advance(T0.AddSeconds(5));
        Assert.That(robot.Speed, Is.EqualTo(1.2).Within(0.01), "predpoklad testu: rozjeto");

        robot.Drive(0.17, 0.107);                    // tvrde brzdeni A zatacka najednou
        robot.Advance(T0.AddSeconds(5.5));           // dif rampa potrebuje 0,107/0,5 = 0,21 s

        Assert.That(robot.AngularSpeed, Is.EqualTo(2 * 0.107 / WheelBase).Within(0.02),
                    "rotace se ustavi i kdyz dopredna rychlost jde na dorazu dolu");
    }

    /// <summary>Totez zrcadlove - jev nesouvisel se smerem, takze obe strany musi vyjit stejne.</summary>
    [Test]
    public void HardBrakeWhileTurning_IsMirrorSymmetric()
    {
        double Turn(double dif)
        {
            var robot = AtOrigin(acceleration: 0.5);
            robot.Drive(1.2, 0.0);
            robot.Advance(T0.AddSeconds(5));
            robot.Drive(0.17, dif);
            robot.Advance(T0.AddSeconds(5.5));
            return robot.AngularSpeed;
        }

        Assert.That(Turn(+0.107), Is.EqualTo(-Turn(-0.107)).Within(1e-9));
    }

    /// <summary>
    /// Saturace rychlosti kola: <b>ustupuje dopredna rychlost, rotace se drzi</b> - tak to dela
    /// skutecny radic (`curSpeed = 1000000 - Abs(curRotSpeed)` v .mbs, totez v SDC2160.Drive).
    /// </summary>
    [Test]
    public void WheelSpeedSaturation_ForwardYieldsToRotation()
    {
        var robot = new SimulatedRobot(WheelBase, T0, maxWheelSpeed: 1.0) { Theta = 0 };
        robot.SetAcceleration(1000.0);

        robot.Drive(1.0, 0.3);                       // rychlejsi kolo by chtelo 1,3 m/s
        robot.Advance(T0.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(robot.AngularSpeed, Is.EqualTo(2 * 0.3 / WheelBase).Within(1e-6),
                        "rotace se nekrati");
            Assert.That(Math.Max(Math.Abs(robot.LeftWheelSpeed), Math.Abs(robot.RightWheelSpeed)),
                        Is.EqualTo(1.0).Within(1e-6), "zadne kolo neprekroci maximum");
            Assert.That(robot.Speed, Is.EqualTo(0.7).Within(1e-6),
                        "dopredna ustoupila na max - |dif|");
        });
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
