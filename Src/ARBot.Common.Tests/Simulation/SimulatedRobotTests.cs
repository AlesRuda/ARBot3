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

    // ==================== Prokluz kol (22. 8. 2026) ====================
    //
    // Duvod: bez prokluzu je odometrie presna, takze chyba odhadu fuze je jen bily sum GPS/IMU -
    // nulova stredni hodnota a nikam nedriftuje. Pripad, ktery ma hranova lokalizace lecit
    // (pomalu rostouci chyba), tak v simulaci vubec nevznikl. Viz doc/virtual-hw.md.

    /// <summary>Vychozi stav musi byt idealni - prokluz se zapina vedome, ne omylem.</summary>
    [Test]
    public void WheelSlip_DefaultsToIdeal()
    {
        var robot = AtOrigin();

        Assert.Multiple(() =>
        {
            Assert.That(robot.LeftWheelSlip, Is.EqualTo(1.0));
            Assert.That(robot.RightWheelSlip, Is.EqualTo(1.0));
            Assert.That(robot.HasWheelSlip, Is.False);
        });
    }

    /// <summary>
    /// Stejny prokluz na obou kolech = chyba MERITKA drahy: robot ujede min, nez kola namerila,
    /// ale jede porad rovne.
    /// </summary>
    [Test]
    public void SymmetricSlip_ShortensDistance_ButKeepsHeading()
    {
        var robot = AtOrigin();
        robot.LeftWheelSlip = 0.9;
        robot.RightWheelSlip = 0.9;

        robot.Drive(1.0, 0.0);
        robot.Advance(T0.AddSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(robot.X, Is.EqualTo(1.8).Within(0.01), "skutecna draha je o 10 % kratsi");
            Assert.That(robot.LeftEncoder, Is.EqualTo(2.0).Within(0.01),
                        "enkoder hlasi NOMINAL - kolo se opravdu otocilo, jen to nikam nevedlo");
            Assert.That(robot.RightEncoder, Is.EqualTo(2.0).Within(0.01));
            Assert.That(robot.Theta, Is.EqualTo(0.0).Within(1e-9), "symetricky prokluz nesmi tocit");
        });
    }

    /// <summary>
    /// Ruzny prokluz vlevo/vpravo = DRIFT KURZU, i kdyz odometrie hlasi jizdu rovne. To je ta
    /// systematicka chyba, kterou ma hranova lokalizace opravit.
    /// </summary>
    [Test]
    public void AsymmetricSlip_DriftsHeading_WhileOdometrySaysStraight()
    {
        var robot = AtOrigin();
        robot.LeftWheelSlip = 1.0;
        robot.RightWheelSlip = 0.98;   // prave kolo o 2 % pomalejsi -> stoceni DOPRAVA (zaporne)

        robot.Drive(1.0, 0.0);
        robot.Advance(T0.AddSeconds(2));

        // omega = (vR*sR - vL*sL)/rozchod = (0,98 - 1,0)/0,5 = -0,04 rad/s; za 2 s tedy -0,08 rad.
        // Tolerance 2e-4: prvni krok integrace (5 ms) jeste dobiha rampa zrychleni z nuly, takze
        // uhel je o ~1e-4 rad mensi. Neni to nepresnost prokluzu, ale rozjezd.
        Assert.Multiple(() =>
        {
            Assert.That(robot.Theta, Is.EqualTo(-0.08).Within(2e-4));
            Assert.That(robot.HasWheelSlip, Is.True);
            Assert.That(robot.LeftEncoder, Is.EqualTo(robot.RightEncoder).Within(1e-9),
                        "odometrie o stoceni nevi - oba enkodery hlasi tutez drahu");
        });
    }

    /// <summary>
    /// Nominalni (odometrie) vs. skutecne (GPS, gyro) rychlosti se pri prokluzu musi rozejit -
    /// jinak by fuze mela z ceho chybu poznat a experiment by nemeril to, co ma.
    /// </summary>
    [Test]
    public void Slip_SplitsNominalFromActualSpeeds()
    {
        var robot = AtOrigin();
        robot.LeftWheelSlip = 1.0;
        robot.RightWheelSlip = 0.98;

        robot.Drive(1.0, 0.0);
        robot.Advance(T0.AddSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(robot.LeftWheelSpeed, Is.EqualTo(1.0).Within(1e-9), "odometrie: nominal");
            Assert.That(robot.RightWheelSpeed, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(robot.Speed, Is.EqualTo(0.99).Within(1e-9), "GPS: skutecna rychlost");
            Assert.That(robot.AngularSpeed, Is.EqualTo(-0.04).Within(1e-9), "gyro: skutecne otaceni");
        });
    }

    /// <summary>
    /// Zprava do zaznamu nese SKUTECNOST - bez ni se chyba lokalizace ze zaznamu spocitat neda
    /// (odhad tam je, skutecnost nikde). Nese i nastaveni prokluzu, aby slo dohledat, s cim beh jel.
    /// </summary>
    [Test]
    public void ToLogMessage_CarriesTruthAndSlipSetting()
    {
        var robot = AtOrigin();
        robot.LeftWheelSlip = 1.0;
        robot.RightWheelSlip = 0.98;

        robot.Drive(1.0, 0.0);
        robot.Advance(T0.AddSeconds(1));

        var stamp = T0.AddSeconds(1);
        var msg = robot.ToLogMessage(stamp);

        Assert.Multiple(() =>
        {
            Assert.That(msg.X, Is.EqualTo(robot.X).Within(1e-12));
            Assert.That(msg.Y, Is.EqualTo(robot.Y).Within(1e-12));
            Assert.That(msg.Theta, Is.EqualTo(robot.Theta).Within(1e-12));
            Assert.That(msg.V, Is.EqualTo(0.99).Within(1e-9), "skutecna, ne nominalni rychlost");
            Assert.That(msg.Omega, Is.EqualTo(-0.04).Within(1e-9));
            Assert.That(msg.LeftEncoder, Is.EqualTo(robot.LeftEncoder).Within(1e-12));
            Assert.That(msg.LeftWheelSlip, Is.EqualTo(1.0));
            Assert.That(msg.RightWheelSlip, Is.EqualTo(0.98));
            Assert.That(msg.TimeStamp, Is.EqualTo(stamp));
        });
    }
}
