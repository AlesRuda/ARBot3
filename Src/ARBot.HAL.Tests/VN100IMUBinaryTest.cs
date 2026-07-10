using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Models;
using ARBot.HAL.Devices.AHRS;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Syntetický round-trip test dekódování binárního paketu VN-100 (bez UARTu/HW).
    /// Ověřuje délku payloadu, extrakci polí a převod VN Ypr (azimut) → ENU/matematická
    /// orientace v Rotation, plus YprU → OrientationUncertainty.
    /// </summary>
    [TestFixture]
    public class VN100IMUBinaryTest
    {
        // groups: Imu (bit2 = 4) + Attitude (bit4 = 16) = 20
        private const byte Groups = 4 | 16;

        private static ushort[] Masks()
        {
            var m = new ushort[6];
            m[2] = 256 | 512 | 1024;   // Imu: Mag | Accel | Gyro
            m[4] = 2 | 256 | 512;      // Attitude: Ypr | YprU | YprRate
            return m;
        }

        private static byte[] Payload(Vector3 mag, Vector3 acc, Vector3 gyro,
                                      Vector3 yprDeg, Vector3 ypru, Vector3 yprRate)
        {
            var b = new List<byte>();
            void F(float f) => b.AddRange(BitConverter.GetBytes(f));
            void V(Vector3 v) { F(v.X); F(v.Y); F(v.Z); }
            // pořadí: Imu(Mag,Accel,Gyro) pak Attitude(Ypr,YprU,YprRate)
            V(mag); V(acc); V(gyro);
            V(yprDeg); V(ypru); V(yprRate);
            return b.ToArray();
        }

        [Test]
        public void PayloadLength_MatchesConfiguredFields()
        {
            // Mag+Accel+Gyro = 3×12, Ypr+YprU+YprRate = 3×12
            Assert.That(VN100IMUBinary.PayloadLength(Groups, Masks()), Is.EqualTo(3 * 12 + 3 * 12));
        }

        [Test]
        public void DecodePacket_ExtractsFieldsAndConvertsYawToEnu()
        {
            var mag = new Vector3(1, 2, 3);
            var acc = new Vector3(4, 5, 6);
            var gyro = new Vector3(7, 8, 9);
            var yprDeg = new Vector3(0, 5, -3);   // yaw=0 (sever), pitch=5°, roll=-3°
            var ypru = new Vector3(10, 20, 30);   // stupně (1σ)

            var payload = Payload(mag, acc, gyro, yprDeg, ypru, Vector3.Zero);
            var s = VN100IMUBinary.DecodePacket(Groups, Masks(), payload);

            // VN výstup FRD → projektové FLU: negace Y a Z
            Assert.That(s, Is.Not.Null);
            Assert.That(s.Magnetometer, Is.EqualTo(new Vector3(mag.X, -mag.Y, -mag.Z)));
            Assert.That(s.Acceleration, Is.EqualTo(new Vector3(acc.X, -acc.Y, -acc.Z)));
            Assert.That(s.AngularVelocity, Is.EqualTo(new Vector3(gyro.X, -gyro.Y, -gyro.Z)));

            // yaw azimut 0 (sever) → matematická orientace π/2 (sever); pitch/roll zpět round-tripem
            var y = s.YPR();
            Assert.That(y.Yaw, Is.EqualTo(Math.PI / 2).Within(1e-3));
            Assert.That(y.Pitch, Is.EqualTo(Conversions.Deg2Rad(5)).Within(1e-3));
            Assert.That(y.Roll, Is.EqualTo(Conversions.Deg2Rad(-3)).Within(1e-3));

            // YprU (stupně) → radiány
            Assert.That(s.OrientationUncertainty, Is.Not.Null);
            var u = s.OrientationUncertainty.Value;
            Assert.That(u.X, Is.EqualTo(Conversions.Deg2Rad(10)).Within(1e-6));
            Assert.That(u.Y, Is.EqualTo(Conversions.Deg2Rad(20)).Within(1e-6));
            Assert.That(u.Z, Is.EqualTo(Conversions.Deg2Rad(30)).Within(1e-6));
        }

        [Test]
        public void DecodePacket_NoOrientation_ReturnsNull()
        {
            // jen Imu skupina, žádná orientace (Ypr)
            var m = new ushort[6];
            m[2] = 512;   // jen Accel
            var payload = new byte[12];
            Assert.That(VN100IMUBinary.DecodePacket(4, m, payload), Is.Null);
        }
    }
}
