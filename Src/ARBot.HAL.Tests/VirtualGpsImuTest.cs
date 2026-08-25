using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Runtime;
using ARBot.Common.Simulation;
using ARBot.HAL.Devices;
using ARBot.HAL.Devices.AHRS;
using ARBot.HAL.Devices.GPSs;

namespace ARBot.HAL.Tests;

/// <summary>
/// Testy virtualni GPS a IMU (viz doc/virtual-hw.md). Klicove je, ze skutecny stav robota
/// projde senzorem i <see cref="DefaultMeasurementMapper"/> a vrati se nezmeneny - tim se hlida
/// past se stupni vs. radiany u GPS a konvence kvaternionu u IMU.
/// </summary>
public class VirtualGpsImuTest
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    private static SimulatedRobot StandingRobot(double x, double y, double theta)
    {
        var robot = new SimulatedRobot(0.5, TimeBase.Now) { X = x, Y = y, Theta = theta };
        robot.SetAcceleration(1000.0);
        return robot;
    }

    private static T? WaitFor<T>(Func<T?> poll, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var v = poll();
            if (v != null) return v;
            Thread.Sleep(5);
        }
        return null;
    }

    [Test]
    public void Gps_RoundTripsPositionThroughMapper()
    {
        var origin = Origin();
        // Robot stoji 120 m na vychod a 80 m na sever od pocatku.
        var robot = StandingRobot(120.0, 80.0, 0.0);

        var options = new VirtualSensorOptions { GpsPositionNoiseM = 0 };   // bez sumu = presna kontrola
        using var gps = new VirtualGps(robot, origin, options);

        var state = WaitFor(() => gps.GetLastMeasurement(), TimeSpan.FromSeconds(5));
        Assert.That(state, Is.Not.Null, "virtualni GPS ma merit");
        Assert.That(state!.IsFixed, Is.True, "fix musi byt platny, jinak ho mapper zahodi");

        var cfg = new FusionConfig { GeoReference = origin };
        var mapper = new DefaultMeasurementMapper(cfg);   // bez enginu -> zadna inicializace, jen mereni
        var position = mapper.ToMeasurements(state).FirstOrDefault(m => m.Source == "GPS/position");

        Assert.That(position, Is.Not.Null, "z fixu ma vzniknout mereni polohy");
        Assert.Multiple(() =>
        {
            Assert.That(position!.Value[0], Is.EqualTo(120.0).Within(0.5), "X (na vychod)");
            Assert.That(position.Value[1], Is.EqualTo(80.0).Within(0.5), "Y (na sever)");
        });
    }

    [Test]
    public void Imu_RoundTripsHeadingThroughMapper()
    {
        const double heading = 0.7;   // rad, matematicky (0 = vychod)
        var robot = StandingRobot(0, 0, heading);

        var options = new VirtualSensorOptions { ImuHeadingNoiseRad = 0 };
        using var imu = new VirtualImu(robot, options);

        var state = WaitFor(() => imu.GetLastMeasurement(), TimeSpan.FromSeconds(5));
        Assert.That(state, Is.Not.Null, "virtualni IMU ma merit");

        var mapper = new DefaultMeasurementMapper(new FusionConfig());
        var yaw = mapper.ToMeasurements(state!).FirstOrDefault(m => m.Source == "IMU/heading");

        Assert.That(yaw, Is.Not.Null, "z IMU ma vzniknout mereni kurzu");
        Assert.That(yaw!.Value[0], Is.EqualTo(heading).Within(0.02),
                    "kurz musi projit kvaternionem beze zmeny");
    }

    [Test]
    public void Imu_ReportsAngularRateInBodyFrame()
    {
        var robot = StandingRobot(0, 0, 0);
        robot.Drive(0.0, 0.25);   // omega = 2*0.25/0.5 = 1 rad/s

        var options = new VirtualSensorOptions { ImuGyroNoiseRad = 0 };
        using var imu = new VirtualImu(robot, options);

        // Prvni vzorky mohou padnout jeste pred rozjezdem - pockame, az se rychlost ustali.
        Thread.Sleep(200);
        var state = WaitFor(() => imu.GetLastMeasurement(), TimeSpan.FromSeconds(5));

        Assert.That(state, Is.Not.Null);
        var mapper = new DefaultMeasurementMapper(new FusionConfig());
        var rate = mapper.ToMeasurements(state!).FirstOrDefault(m => m.Source == "IMU/gyro");

        Assert.That(rate, Is.Not.Null, "z gyra ma vzniknout mereni uhlove rychlosti");
        Assert.That(rate!.Value[0], Is.EqualTo(1.0).Within(0.05));
    }

    // ==================== Systematicke chyby (22. 8. 2026) ====================
    //
    // Bily sum ma nulovou stredni hodnotu, takze se vyprumeruje a chyba odhadu nikam neroste.
    // Bias se neprumeruje - a bias gyra se navic integruje do rostouci chyby kurzu. Prave to ma
    // hranova lokalizace lecit. Viz doc/virtual-hw.md.

    [Test]
    public void Imu_HeadingBias_ShiftsReportedHeading()
    {
        const double heading = 0.7;         // rad, skutecny kurz
        const double bias = 0.05;           // rad, systematicka chyba (~2,9 deg)
        var robot = StandingRobot(0, 0, heading);

        var options = new VirtualSensorOptions
        {
            ImuHeadingNoiseRad = 0,
            ImuHeadingBiasRad = bias,
        };
        using var imu = new VirtualImu(robot, options);

        var state = WaitFor(() => imu.GetLastMeasurement(), TimeSpan.FromSeconds(5));
        Assert.That(state, Is.Not.Null, "virtualni IMU ma merit");

        var mapper = new DefaultMeasurementMapper(new FusionConfig());
        var yaw = mapper.ToMeasurements(state!).FirstOrDefault(m => m.Source == "IMU/heading");

        Assert.That(yaw, Is.Not.Null);
        Assert.That(yaw!.Value[0], Is.EqualTo(heading + bias).Within(0.02),
                    "hlaseny kurz je skutecny plus bias");
    }

    [Test]
    public void Imu_GyroBias_ShiftsReportedRate_EvenWhenStanding()
    {
        var robot = StandingRobot(0, 0, 0);   // stoji: skutecna uhlova rychlost je nula
        const double bias = 0.02;             // rad/s

        var options = new VirtualSensorOptions
        {
            ImuGyroNoiseRad = 0,
            ImuGyroBiasRadPerSec = bias,
        };
        using var imu = new VirtualImu(robot, options);

        var state = WaitFor(() => imu.GetLastMeasurement(), TimeSpan.FromSeconds(5));
        Assert.That(state, Is.Not.Null);

        var mapper = new DefaultMeasurementMapper(new FusionConfig());
        var rate = mapper.ToMeasurements(state!).FirstOrDefault(m => m.Source == "IMU/gyro");

        Assert.That(rate, Is.Not.Null);
        Assert.That(rate!.Value[0], Is.EqualTo(bias).Within(1e-6),
                    "stojici robot s biasem gyra hlasi otaceni - z toho vznikne rostouci chyba kurzu");
    }

    /// <summary>
    /// <b>Kurz z GPS je DRUHA absolutni reference kurzu</b>, nezavisla na magnetometru — a bez ni
    /// nema fuze proti cemu zmerit bias kompasu. Simulace ho do 25. 8. 2026 nehlasila vubec, i kdyz
    /// skutecny prijimac ano (<c>NmeaGps</c> z VTG, <c>uBloxGps</c> jako atan2 z vektoru rychlosti).
    /// </summary>
    [Test]
    public void Gps_HlasiKurzNadZemi_KdyzRobotJede()
    {
        var origin = Origin();
        var robot = StandingRobot(0, 0, Math.PI / 4);      // kurz 45 stupnu
        robot.Drive(1.0, 0.0);                             // jede 1 m/s vpred
        robot.Advance(TimeBase.Now);

        var options = new VirtualSensorOptions { GpsCrossTrackNoiseMps = 0 };   // bez sumu = presna kontrola
        using var gps = new VirtualGps(robot, origin, options);

        var state = WaitFor(() => gps.GetLastMeasurement(), TimeSpan.FromSeconds(5));
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.DynamicOrientation, Is.Not.Null,
                    "jedouci robot musi hlasit kurz - bez nej neni bias kompasu observabilni");
        Assert.That(state.DynamicOrientation!.Value, Is.EqualTo(Math.PI / 4).Within(1e-6),
                    "kurz nad zemi je u diferencialniho podvozku pravy kurz robotu");
    }

    /// <summary>
    /// Pri stani se kurz <b>nehlasi vubec</b>. Neni to komfort: <c>atan2</c> ze sumu je rovnomerne
    /// rozdeleny uhel, tedy cista dezinformace, a skutecny prijimac se chova stejne.
    /// </summary>
    [Test]
    public void Gps_PriStaniKurzNehlasi()
    {
        var origin = Origin();
        var robot = StandingRobot(0, 0, Math.PI / 4);      // stoji

        using var gps = new VirtualGps(robot, origin, new VirtualSensorOptions());

        var state = WaitFor(() => gps.GetLastMeasurement(), TimeSpan.FromSeconds(5));
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.DynamicOrientation, Is.Null,
                    "atan2 ze sumu je pri stani rovnomerne rozdeleny uhel, ne merenie");
    }

    /// <summary>
    /// <b>Nejistota kurzu klesa s rychlosti</b> — to je to podstatne tvrzeni celeho modelu. Kurz
    /// neni merena velicina, je to <c>atan2</c> z vektoru rychlosti, takze
    /// <c>sigma_kurz ≈ sigma_v / v</c>. Prave tahle zavislost rozhoduje, jestli je kurz z GPS
    /// pouzitelny jako reference: pri 0,5 m/s je sum ~11 stupnu, pri 3 m/s ~1,9.
    /// </summary>
    [Test]
    public void Gps_SumKurzuKlesaSRychlosti()
    {
        var origin = Origin();

        double SpreadAt(double speed)
        {
            var robot = StandingRobot(0, 0, 0.0);
            robot.Drive(speed, 0.0);
            robot.Advance(TimeBase.Now);

            var options = new VirtualSensorOptions { GpsCrossTrackNoiseMps = 0.1, GpsRateHz = 200 };
            using var gps = new VirtualGps(robot, origin, options);

            // Posbira se vic vzorku; sum je deterministicky podle poradi vzorku, takze rozptyl
            // je reprodukovatelny.
            var seen = new List<double>();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            double last = double.NaN;
            while (DateTime.UtcNow < deadline && seen.Count < 40)
            {
                var s = gps.GetLastMeasurement();
                if (s?.DynamicOrientation != null && s.DynamicOrientation.Value != last)
                {
                    last = s.DynamicOrientation.Value;
                    seen.Add(last);
                }
                Thread.Sleep(5);
            }
            Assert.That(seen.Count, Is.GreaterThan(5), $"pri {speed} m/s se nesebralo dost vzorku");

            double mean = seen.Average();
            return Math.Sqrt(seen.Sum(a => (a - mean) * (a - mean)) / (seen.Count - 1));
        }

        double slow = SpreadAt(0.5);
        double fast = SpreadAt(3.0);

        TestContext.Out.WriteLine($"sum kurzu: pri 0,5 m/s {slow * 180 / Math.PI:F1} deg, "
                                  + $"pri 3,0 m/s {fast * 180 / Math.PI:F1} deg "
                                  + $"(podil {slow / Math.Max(1e-9, fast):F1}x)");

        Assert.That(fast, Is.LessThan(slow),
                    "kurz je atan2 z rychlosti, takze rychleji = presneji");
        // Ceka se pomer ~6x (3,0 / 0,5); tolerance je siroka, protoze vzorku je malo.
        Assert.That(slow / Math.Max(1e-9, fast), Is.EqualTo(6.0).Within(3.0));
    }

    [Test]
    public void VirtualSensorOptions_DefaultsHaveNoSystematicError()
    {
        var options = new VirtualSensorOptions();

        Assert.Multiple(() =>
        {
            Assert.That(options.HasSystematicError, Is.False, "drift se zapina vedome, ne omylem");
            Assert.That(options.ImuHeadingBiasRad, Is.EqualTo(0.0));
            Assert.That(options.ImuGyroBiasRadPerSec, Is.EqualTo(0.0));
            Assert.That(options.LeftWheelSlip, Is.EqualTo(1.0));
            Assert.That(options.RightWheelSlip, Is.EqualTo(1.0));
        });

        options.RightWheelSlip = 0.98;
        Assert.That(options.HasSystematicError, Is.True);

        options.ResetSystematicError();
        Assert.That(options.HasSystematicError, Is.False);
        Assert.That(options.RightWheelSlip, Is.EqualTo(1.0));
    }
}
