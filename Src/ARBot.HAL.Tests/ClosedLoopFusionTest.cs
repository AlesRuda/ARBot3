using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Runtime;
using ARBot.Common.Simulation;
using ARBot.HAL.Devices;
using ARBot.HAL.Devices.AHRS;
using ARBot.HAL.Devices.GPSs;
using ARBot.HAL.Devices.MotorDrivers;

namespace ARBot.HAL.Tests;

/// <summary>
/// Uzavrena smycka: simulovany robot jede, virtualni senzory ho meri a SKUTECNA fuze
/// (<see cref="AsyncFusionEngine"/> + <see cref="DefaultMeasurementMapper"/>) jeho pozu odhaduje
/// zpet. Hlavni test cele simulace - ostatni jen lokalizuji, kde je chyba (viz doc/virtual-hw.md).
/// </summary>
public class ClosedLoopFusionTest
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    [Test]
    public void FusedPose_TracksGroundTruth_WhileDrivingStraightAndCurving()
    {
        var origin = Origin();
        var robot = new SimulatedRobot(Profile.Rozchod, TimeBase.Now);

        var cfg = new FusionConfig { GeoReference = origin };
        var engine = new AsyncFusionEngine(new EKFModel(cfg));
        var mapper = new DefaultMeasurementMapper(cfg, engine);

        // Mensi sum nez vychozi, aby test nebyl na hrane tolerance kvuli nahode.
        var options = new VirtualSensorOptions
        {
            GpsPositionNoiseM = 0.5,
            GpsSpeedNoiseMps = 0.05,
            ImuHeadingNoiseRad = 0.01,
            ImuGyroNoiseRad = 0.005,
        };

        using var motors = new VirtualMotors(robot);
        using var gps = new VirtualGps(robot, origin, options);
        using var imu = new VirtualImu(robot, options);

        motors.SetAcceleration(1.0);

        // Senzory se pumpuji pres GetLastMeasurement; od verze 2 zpravy uz by fungoval i pouhy
        // odber udalosti (rychlost kol je vlastni pole, ne dopocet z doby vyzvednuti).
        using var pump = new CancellationTokenSource();
        var pumping = Task.Run(() =>
        {
            while (!pump.IsCancellationRequested)
            {
                Feed(motors.GetLastMeasurement() as Message);
                Feed(gps.GetLastMeasurement());
                Feed(imu.GetLastMeasurement());
                Thread.Sleep(10);
            }
        });

        void Feed(Message m)
        {
            if (m == null) return;
            foreach (var meas in mapper.ToMeasurements(m))
                engine.Enqueue(meas);
        }

        motors.Drive(1.0, 0.0);       // 1,5 s rovne
        Thread.Sleep(1500);
        motors.Drive(1.0, 0.1);       // 1,5 s v oblouku (omega = 2*0,1/rozchod)
        Thread.Sleep(1500);
        motors.Drive(0.0, 0.0);
        Thread.Sleep(300);

        pump.Cancel();
        pumping.Wait(TimeSpan.FromSeconds(2));

        // Porovnavame k casu, ke kteremu fuze jeste ma data (okno historie je 1 s).
        var t = engine.FilterTime;
        var estimate = engine.GetStateAt(t);
        Assert.That(estimate, Is.Not.Null, "fuze ma mit odhad stavu");

        robot.Advance(t);
        robot.Read(out double trueX, out double trueY, out double trueTheta, out _, out _, out _, out _);

        double dx = estimate!.X - trueX;
        double dy = estimate.Y - trueY;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double headingError = Math.Abs(Wrap(estimate.Theta - trueTheta));

        TestContext.Out.WriteLine(
            $"truth X={trueX:F2} Y={trueY:F2} Th={trueTheta:F2} | odhad X={estimate.X:F2} Y={estimate.Y:F2} Th={estimate.Theta:F2}");

        Assert.Multiple(() =>
        {
            Assert.That(trueX * trueX + trueY * trueY, Is.GreaterThan(1.0), "robot se musel rozjet");
            Assert.That(distance, Is.LessThan(1.5), "odhad polohy ma sledovat skutecnost");
            Assert.That(headingError, Is.LessThan(0.2), "odhad kurzu ma sledovat skutecnost");
        });
    }

    /// <summary>Uhlovy rozdil do intervalu (-pi, pi].</summary>
    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a <= -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
