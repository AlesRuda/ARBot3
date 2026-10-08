using System;
using System.Threading;
using ARBot.Common.Devices;
using ARBot.HAL.Devices;
using ARBot.HAL.Devices.Bms;

namespace ARBot.HAL.Tests
{
    public class VirtualBmsTest
    {
        [Test]
        public void HlasiStavZNastaveni()
        {
            var o = new VirtualSensorOptions { BmsSocPercent = 18, BmsCurrentA = -2.5 };
            BmsState s = null;
            using var bms = new VirtualBms(o, periodMs: 20);
            bms.MeasurementArived += (_, m) => s ??= m;
            SpinWait.SpinUntil(() => s != null, TimeSpan.FromSeconds(2));

            Assert.That(s, Is.Not.Null);
            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.SocPercent, Is.EqualTo(18));
            Assert.That(s.Current, Is.EqualTo(-2.5));
            Assert.That(s.CellVoltages.Length, Is.EqualTo(4));
        }

        [Test]
        public void OdpojenaBms_NicNeposila()
        {
            var o = new VirtualSensorOptions { BmsPresent = false };
            int pocet = 0;
            using var bms = new VirtualBms(o, periodMs: 20);
            bms.MeasurementArived += (_, _) => Interlocked.Increment(ref pocet);
            Thread.Sleep(200);
            Assert.That(pocet, Is.EqualTo(0), "odpojena BMS = monitor se vrati k napeti z motoru");
        }
    }
}
