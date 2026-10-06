using System;
using System.Collections.Generic;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using NUnit.Framework;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Hlidani napeti baterie (<see cref="BatteryMonitor"/>, prov-baterie-na-strance). Vzorky
    /// z motorove jednotky jsou hlucne (5-17 V), takze se bere MEDIAN za okno; varuje se pod
    /// prahem s hysterezi a do Trace jde jen PRECHOD stavu.
    /// </summary>
    [TestFixture]
    public class BatteryMonitorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 12, 0, 0);

        private static BatteryMonitor Monitor(double warn, List<string> hlasky = null)
            => new BatteryMonitor(warn, report: s => hlasky?.Add(s));

        /// <summary>Vzorky po 10 ms od <paramref name="from"/> [s], hodnoty dokola z pole.</summary>
        private static void Feed(BatteryMonitor m, double from, double to, params double[] volts)
        {
            int i = 0;
            for (double t = from; t < to; t += 0.01, i++)
                m.Add(T0.AddSeconds(t), volts[i % volts.Length]);
        }

        [Test]
        public void Median_OdolaSumuJednotlivychVzorku()
        {
            var m = Monitor(11.6);
            // Kazdy paty vzorek je ulet (5 V nebo 17 V) - median ho nevidi.
            Feed(m, 0, 3, 12.1, 12.1, 5.0, 12.1, 17.0);

            var r = m.Read(T0.AddSeconds(3));
            Assert.That(r.Volts, Is.EqualTo(12.1).Within(1e-9));
            Assert.That(r.Level, Is.EqualTo(BatteryLevel.Ok));
        }

        [Test]
        public void BezVzorku_NeboStareVzorky_JeNeznamo()
        {
            var m = Monitor(11.6);
            Assert.That(m.Read(T0).Level, Is.EqualTo(BatteryLevel.Unknown));
            Assert.That(double.IsNaN(m.Read(T0).Volts), Is.True);

            Feed(m, 0, 1, 10.0);
            Assert.That(m.Read(T0.AddSeconds(1)).Level, Is.EqualTo(BatteryLevel.Low));
            // Motory zmlkly - posledni medián uz neplati, stranka nesmi ukazovat stare cislo.
            Assert.That(m.Read(T0.AddSeconds(1 + BatteryMonitor.DefaultWindowSec + 1)).Level,
                        Is.EqualTo(BatteryLevel.Unknown));
        }

        [Test]
        public void PodPrahem_Varuje_AHlasiPrechodDoTraceJednou()
        {
            var hlasky = new List<string>();
            var m = Monitor(11.6, hlasky);
            Feed(m, 0, 6, 12.0);
            Feed(m, 6, 20, 11.3);

            Assert.That(m.Read(T0.AddSeconds(20)).Level, Is.EqualTo(BatteryLevel.Low));
            Assert.That(hlasky.Count, Is.EqualTo(1), "jen prechod, ne kazdy vzorek");
            Assert.That(hlasky[0], Does.Contain("11.3"));
        }

        [Test]
        public void Hystereze_NavratAzONadPrahem()
        {
            var hlasky = new List<string>();
            var m = Monitor(11.6, hlasky);
            Feed(m, 0, 6, 11.4);                 // nizko
            Feed(m, 6, 12, 11.7);                // nad prahem, ale pod prahem + hystereze
            Assert.That(m.Read(T0.AddSeconds(12)).Level, Is.EqualTo(BatteryLevel.Low),
                        "11,7 V je pod 11,6 + 0,2 V - varovani nesmi blikat");

            Feed(m, 12, 18, 12.0);               // nad prahem + hystereze
            Assert.That(m.Read(T0.AddSeconds(18)).Level, Is.EqualTo(BatteryLevel.Ok));
            Assert.That(hlasky.Count, Is.EqualTo(2), "do nizke a zpet");
        }

        [Test]
        public void PrahNula_NeVaruje()
        {
            var hlasky = new List<string>();
            var m = Monitor(0, hlasky);
            Feed(m, 0, 6, 9.0);
            var r = m.Read(T0.AddSeconds(6));
            Assert.That(r.Level, Is.EqualTo(BatteryLevel.Ok));
            Assert.That(r.Volts, Is.EqualTo(9.0).Within(1e-9), "napeti se ukazuje i bez varovani");
            Assert.That(hlasky, Is.Empty);
        }

        [Test]
        public void ZpravaBezMereni_SeIgnoruje()
        {
            var m = Monitor(11.6);
            // Zastupny ramec driveru (port nejde otevrit): napeti 0 by jinak vypadalo jako vybita baterie.
            m.Add(new MotorStateBase(true, 0, 0, 0, 0, 0, 0, 0, hasMeasurement: false) { TimeStamp = T0 });
            Assert.That(m.Read(T0).Level, Is.EqualTo(BatteryLevel.Unknown));

            // Platnych vzorku musi byt aspon MinSamples, nez se stav vyhodnoti.
            for (int i = 1; i <= BatteryMonitor.MinSamples; i++)
                m.Add(new MotorStateBase(false, 0, 0, 12.4, 0, 0, 0, 0) { TimeStamp = T0.AddMilliseconds(10 * i) });
            var r = m.Read(T0.AddMilliseconds(10 * BatteryMonitor.MinSamples));
            Assert.That(r.Volts, Is.EqualTo(12.4).Within(1e-9), "zastupny ramec s 0 V se do medianu nepocita");
        }
    }
}
