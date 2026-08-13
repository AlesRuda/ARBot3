using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Runtime;
using ARBot.Common.Simulation;
using ARBot.HAL.Devices.MotorDrivers;

namespace ARBot.HAL.Tests;

/// <summary>
/// Testy virtualnich motoru (viz doc/virtual-hw.md). Klicova vlastnost: prikaz zadany
/// pres <see cref="IMotorControl.Drive"/> musi po pruchodu odometrii a mapperem vyjit zpet.
/// </summary>
public class VirtualMotorsTest
{
    private const double WheelBase = 0.5;

    private static SimulatedRobot NewRobot()
    {
        var robot = new SimulatedRobot(WheelBase, TimeBase.Now);
        robot.SetAcceleration(1000.0);   // rampa nas tu nezajima
        return robot;
    }

    /// <summary>Pocka na dalsi nevyzvednute mereni (vyzvednutim se resetuje baseline enkoderu).</summary>
    private static IMotorState? NextMeasurement(VirtualMotors motors, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var s = motors.GetLastMeasurement();
            if (s != null) return s;
            Thread.Sleep(5);
        }
        return null;
    }

    [Test]
    public void Drive_RoundTripsThroughOdometryMapper()
    {
        var robot = NewRobot();
        using var motors = new VirtualMotors(robot);

        const double forward = 1.0;
        const double dif = 0.25;          // omega = 2*dif/rozchod = 1 rad/s
        motors.SetAcceleration(1000.0);
        motors.Drive(forward, dif);

        // Chvili jedeme, at je rychlost ustalena.
        Thread.Sleep(300);
        var state = NextMeasurement(motors, TimeSpan.FromSeconds(5));

        Assert.That(state, Is.Not.Null);

        var cfg = new FusionConfig { WheelBase = WheelBase };
        var mapper = new DefaultMeasurementMapper(cfg);
        var measurements = mapper.ToMeasurements((Message)state!).ToList();

        var speed = measurements.FirstOrDefault(m => m.Source == "Odo/speed");
        var rate = measurements.FirstOrDefault(m => m.Source == "Odo/rate");

        Assert.Multiple(() =>
        {
            Assert.That(speed, Is.Not.Null, "odometrie ma dat rychlost");
            Assert.That(rate, Is.Not.Null, "odometrie ma dat uhlovou rychlost");
            Assert.That(speed!.Value[0], Is.EqualTo(forward).Within(0.05),
                        "dopredna rychlost ma vyjit zpet");
            Assert.That(rate!.Value[0], Is.EqualTo(2 * dif / WheelBase).Within(0.05),
                        "uhlova rychlost ma vyjit zpet (kladny difSpeed = doleva)");
        });
    }

    /// <summary>
    /// Rychlost kol musi byt spravna, i kdyz mereni nikdo NEVYZVEDAVA - v runtime se motory
    /// odebiraji jen udalosti (MotorSource). Driv se rychlost pocitala z FramePickupPeriod,
    /// takze bez vyzvednuti vychazela nula. Viz doc/virtual-hw.md.
    /// </summary>
    [Test]
    public void WheelSpeeds_AreCorrect_WithoutAnyPickup()
    {
        var robot = NewRobot();
        using var motors = new VirtualMotors(robot);

        IMotorState? last = null;
        motors.MeasurementArived += (_, s) => Volatile.Write(ref last, s);

        motors.SetAcceleration(1000.0);
        motors.Drive(1.0, 0.25);          // vL = 0,75; vR = 1,25

        Thread.Sleep(400);
        var state = Volatile.Read(ref last);

        Assert.That(state, Is.Not.Null, "udalost ma dorazit i bez vyzvedavani");
        Assert.Multiple(() =>
        {
            Assert.That(state!.LeftWheelSpeed, Is.EqualTo(0.75).Within(0.05));
            Assert.That(state.RightWheelSpeed, Is.EqualTo(1.25).Within(0.05));
        });
    }

    /// <summary>Enkodery jsou kumulativni - kazdy odberatel si spocte prirustek pres svuj interval.</summary>
    [Test]
    public void Encoders_AreCumulative()
    {
        var robot = NewRobot();
        using var motors = new VirtualMotors(robot);

        IMotorState? last = null;
        motors.MeasurementArived += (_, s) => Volatile.Write(ref last, s);

        motors.SetAcceleration(1000.0);
        motors.Drive(1.0, 0.0);

        Thread.Sleep(200);
        double first = Volatile.Read(ref last)!.LeftEncoder;
        Thread.Sleep(400);
        double second = Volatile.Read(ref last)!.LeftEncoder;

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.GreaterThan(0.05), "enkoder ma narustat od startu");
            Assert.That(second - first, Is.EqualTo(0.4).Within(0.1), "prirustek odpovida ujete draze");
        });
    }

    [Test]
    public void Drive_MovesTheSimulatedRobot()
    {
        var robot = NewRobot();
        using var motors = new VirtualMotors(robot);

        motors.SetAcceleration(1000.0);
        motors.Drive(1.0, 0.0);

        Assert.That(NextMeasurement(motors, TimeSpan.FromSeconds(5)), Is.Not.Null);
        Thread.Sleep(300);
        NextMeasurement(motors, TimeSpan.FromSeconds(5));

        Assert.That(robot.X, Is.GreaterThan(0.1), "robot se ma za tu dobu posunout vpred");
    }
}
