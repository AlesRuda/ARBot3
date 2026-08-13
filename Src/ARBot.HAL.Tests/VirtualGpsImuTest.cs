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
}
