using System;
using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.HAL;
using ARBot.Robot;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// Stav robota → LED pasek (<see cref="NeoPixelBridge"/>). Pravidla jsou prevzata z ARBot2
    /// (<c>State.cs</c>: couvani, brzda, blinkry z prikazu; <c>Program.cs</c>: Alert pri cekani
    /// na stisk stopu), takze testy hlidaji, ze se prenesla beze zmeny smyslu.
    /// </summary>
    public class NeoPixelBridgeTests
    {
        private sealed class NicNeposilej : INeoPixelDriver
        {
            public void Send(Color[] values) { }
        }

        private static readonly DateTime T0 = new DateTime(2026, 9, 28, 12, 0, 0);

        private static (NeoPixelProcessor Leds, NeoPixelBridge Most) Novy()
        {
            var leds = new NeoPixelProcessor(new NicNeposilej());
            return (leds, new NeoPixelBridge(leds));
        }

        private static DriveCommandMsg Jizda(double t, double v, double w = 0, bool held = false, bool estop = false)
            => new DriveCommandMsg { TimeStamp = T0.AddSeconds(t), Speed = v, RotationSpeed = w, Held = held, EmergencyStop = estop };

        [Test]
        public void Blinkry_PodleRotace_PlusJeVlevo()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, 0.5, 0.3));
            Assert.That(leds.LeftBlinker, Is.True);
            Assert.That(leds.RightBlinker, Is.False);

            most.Post(Jizda(0.1, 0.5, -0.3));
            Assert.That(leds.LeftBlinker, Is.False);
            Assert.That(leds.RightBlinker, Is.True);

            most.Post(Jizda(0.2, 0.5, 0.1));   // pod prahem 0,2 rad/s
            Assert.That(leds.LeftBlinker || leds.RightBlinker, Is.False);
        }

        [Test]
        public void Couvani_ZapornaRychlost()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, -0.2));
            Assert.That(leds.Backward, Is.True);
            most.Post(Jizda(0.1, 0.2));
            Assert.That(leds.Backward, Is.False);
        }

        [Test]
        public void Brzda_PriPrudkemPoklesu_DrziSePulSekundy()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, 1.0));
            most.Post(Jizda(0.1, 0.6));   // pokles 0,4 > 0,3
            Assert.That(leds.Break, Is.True);
            most.Post(Jizda(0.4, 0.6));
            Assert.That(leds.Break, Is.True, "0,3 s po poklesu jeste sviti");
            most.Post(Jizda(0.7, 0.6));
            Assert.That(leds.Break, Is.False, "po 0,5 s zhasne");
        }

        [Test]
        public void Brzda_PozvolnePlynuleZpomaleniNeRozsviti()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, 1.0));
            most.Post(Jizda(0.1, 0.8));
            Assert.That(leds.Break, Is.False);
        }

        [Test]
        public void Brzda_PriDrzenemZastaveni()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, 0.0, held: true));
            Assert.That(leds.Break, Is.True);
        }

        [Test]
        public void NouzoveZastaveni_ZPrikazuJizdy()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, 0, estop: true));
            Assert.That(leds.EmergencyStop, Is.True);
            most.Post(Jizda(0.1, 0));
            Assert.That(leds.EmergencyStop, Is.False);
        }

        [Test]
        public void PredniSvetla_AlertKdyzMiseCekaNaStiskStopu()
        {
            var (leds, most) = Novy();
            most.Post(new MissionMsg { Phase = (int)RobotourPhase.AwaitingEStop });
            Assert.That(leds.FrontLights, Is.EqualTo(NeoPixelProcessor.FrontLightsEnum.Alert));

            most.Post(new MissionMsg { Phase = (int)RobotourPhase.DrivingToPickup });
            Assert.That(leds.FrontLights, Is.EqualTo(NeoPixelProcessor.FrontLightsEnum.KnightRider));
        }

        [Test]
        public void Dispose_VratiKlidovyStav()
        {
            var (leds, most) = Novy();
            most.Post(Jizda(0, -0.5, 0.5, held: true, estop: true));
            most.Post(new MissionMsg { Phase = (int)RobotourPhase.AwaitingEStop });
            most.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(leds.Backward || leds.Break || leds.LeftBlinker || leds.RightBlinker || leds.EmergencyStop, Is.False);
                Assert.That(leds.FrontLights, Is.EqualTo(NeoPixelProcessor.FrontLightsEnum.KnightRider));
            });
        }
    }
}
