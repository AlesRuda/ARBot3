using System;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Round-trip serializace HAL senzorovych stavu (nyni Message): zapis pres
    /// MessageWriter, cteni pres MessageReader s katalogem prototypu.
    /// </summary>
    public class StateSerializationTest
    {
        private static readonly Encoding Enc = Encoding.UTF8;

        private static System.Collections.Generic.Dictionary<string, Message> Catalog()
        {
            return MessageCatalog.CommonDefaults()
                .Register(new GPSState())
                .Register(new MotorStateBase())
                .ToPrototypeMap();
        }

        private static Message RoundTrip(Message msg)
        {
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, Enc);
            w.Write(msg);
            w.Flush();
            var bytes = ms.ToArray();

            var rs = new MemoryStream(bytes);
            var r = new MessageReader(rs, Enc, Catalog());
            return r.Read();
        }

        [Test]
        public void MotorStateBase_RoundTrips()
        {
            var t = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var m = new MotorStateBase(true, 1.5, -2.5, 24.0, 0.1, 0.2, 0.7, 0.9)
            {
                TimeStamp = t,
                FrameNum = 7
            };

            var back = RoundTrip(m) as MotorStateBase;

            Assert.That(back, Is.Not.Null);
            Assert.That(back.IsEmergencyStop, Is.EqualTo(true));
            Assert.That(back.LeftEncoder, Is.EqualTo(1.5));
            Assert.That(back.RightEncoder, Is.EqualTo(-2.5));
            Assert.That(back.Voltage, Is.EqualTo(24.0));
            Assert.That(back.LeftMotorCurrent, Is.EqualTo(0.1));
            Assert.That(back.RightMotorCurrent, Is.EqualTo(0.2));
            Assert.That(back.LeftWheelSpeed, Is.EqualTo(0.7), "rychlosti kol se od verze 2 serializuji");
            Assert.That(back.RightWheelSpeed, Is.EqualTo(0.9));
            Assert.That(back.TimeStamp, Is.EqualTo(t));
            Assert.That(back.FrameNum, Is.EqualTo(7u));
        }

        [Test]
        public void GpsState_RoundTrips()
        {
            var t = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            var g = new GPSState
            {
                TimeStamp = t,
                FixTime = TimeSpan.FromSeconds(12345),
                Latitude = 50.123456,
                Longitude = 14.654321,
                Quality = GPSState.FixQuality.Rtk,
                NumberOfSatellites = 11,
                Hdop = 0.8,
                Altitude = 320.5,
                DynamicOrientation = 1.23,
                DynamicSpeed = 2.34,
                Orientation = null,
                Speed = 3.45
            };

            var back = RoundTrip(g) as GPSState;

            Assert.That(back, Is.Not.Null);
            Assert.That(back.TimeStamp, Is.EqualTo(t));
            Assert.That(back.FixTime, Is.EqualTo(TimeSpan.FromSeconds(12345)));
            Assert.That(back.Latitude, Is.EqualTo(50.123456));
            Assert.That(back.Longitude, Is.EqualTo(14.654321));
            Assert.That(back.Quality, Is.EqualTo(GPSState.FixQuality.Rtk));
            Assert.That(back.NumberOfSatellites, Is.EqualTo(11));
            Assert.That(back.Hdop, Is.EqualTo(0.8));
            Assert.That(back.Altitude, Is.EqualTo(320.5));
            Assert.That(back.DynamicOrientation, Is.EqualTo(1.23));
            Assert.That(back.DynamicSpeed, Is.EqualTo(2.34));
            Assert.That(back.Orientation, Is.Null);
            Assert.That(back.Speed, Is.EqualTo(3.45));
        }
    }
}
