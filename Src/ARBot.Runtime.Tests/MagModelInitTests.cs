using System;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.HAL;
using ARBot.Robot;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// Model magnetickeho pole v senzoru (registr 83) se nastavuje <b>jednorazove po prvnim
    /// kvalitnim fixu</b>. Bez nej je kurz z VN100 magneticky (u nas o ~5° proti pravemu severu)
    /// a VPE porovnava sklon proti referenci, ktera je o ~5° mimo. Viz doc/imu-and-frames.md.
    /// </summary>
    public class MagModelInitTests
    {
        private sealed class FakeImu : IMagneticModel
        {
            public int Volani;
            public LLA Posledni;
            public bool Vyhod;

            public void SetModelParams(LLA lla)
            {
                Volani++;
                Posledni = lla;
                if (Vyhod) throw new InvalidOperationException("zkouska");
            }
        }

        /// <summary>Fix v Praze; <b>radiany</b>, jak je projekt pouziva vsude.</summary>
        private static GPSState Fix(bool platny = true, int druzic = 9, double hdop = 1.2)
            => new GPSState
            {
                Quality = platny ? GPSState.FixQuality.GpsFix : GPSState.FixQuality.Invalid,
                Latitude = Conversions.Deg2Rad(50.087451),
                Longitude = Conversions.Deg2Rad(14.420671),
                Altitude = 235.0,
                NumberOfSatellites = druzic,
                Hdop = hdop,
                TimeStamp = TimeBase.Now,
            };

        [Test]
        public void PrvniKvalitniFix_NastaviModel()
        {
            var imu = new FakeImu();
            var m = new MagModelInit(imu, new FusionConfig());

            m.Post(Fix());

            Assert.Multiple(() =>
            {
                Assert.That(imu.Volani, Is.EqualTo(1));
                Assert.That(m.Done, Is.True);
                Assert.That(imu.Posledni, Is.Not.Null);
                Assert.That(Conversions.Rad2Deg(imu.Posledni.Latitude),
                            Is.EqualTo(50.087451).Within(1e-6),
                            "poloha se predava v RADIANECH; prevod na stupne dela az driver");
            });
        }

        [Test]
        public void DalsiFixy_ModelZnovuNENASTAVUJI()
        {
            // Jednorazove: registr 83 ma RecalcThreshold 1000 m, prepisovat ho pri kazdem fixu
            // (5 Hz) by znamenalo zaplavit senzor prikazy bez jakehokoli zisku.
            var imu = new FakeImu();
            var m = new MagModelInit(imu, new FusionConfig());

            for (int i = 0; i < 20; i++) m.Post(Fix());

            Assert.That(imu.Volani, Is.EqualTo(1));
        }

        [Test]
        public void SpatnyFix_ModelNenastavi_AZapocitaSe()
        {
            var imu = new FakeImu();
            var m = new MagModelInit(imu, new FusionConfig());

            m.Post(Fix(platny: false));
            m.Post(Fix(druzic: 2));                  // pod gpsminsat
            m.Post(Fix(hdop: 99));                   // nad gpsmaxdop

            Assert.Multiple(() =>
            {
                Assert.That(imu.Volani, Is.EqualTo(0),
                            "proti spatnemu fixu by se model nastavil na spatne misto");
                Assert.That(m.Done, Is.False);
                Assert.That(m.Rejected, Is.EqualTo(3));
            });
        }

        [Test]
        public void PoZamitnutychFixech_PrvniDobryProjde()
        {
            var imu = new FakeImu();
            var m = new MagModelInit(imu, new FusionConfig());

            m.Post(Fix(platny: false));
            m.Post(Fix());

            Assert.That(imu.Volani, Is.EqualTo(1));
            Assert.That(m.Rejected, Is.EqualTo(1));
        }

        [Test]
        public void SelhaniZapisu_NeoznaciZaHotovo_AZkusiToZnovu()
        {
            // Nenastaveny model je vada diagnosticka, ne fatalni - kurz zustane magneticky.
            // Tvarit se, ze hotovo, by ale znamenalo, ze se to uz nikdy nezkusi.
            var imu = new FakeImu { Vyhod = true };
            var m = new MagModelInit(imu, new FusionConfig());

            m.Post(Fix());
            Assert.That(m.Done, Is.False);

            imu.Vyhod = false;
            m.Post(Fix());

            Assert.That(m.Done, Is.True);
            Assert.That(imu.Volani, Is.EqualTo(2));
        }

        [Test]
        public void BezIMU_SeNicNedeje()
        {
            // T265 model pole neumi a ARBotHW muze vratit null - nesmi to spadnout.
            var m = new MagModelInit(null, new FusionConfig());

            Assert.DoesNotThrow(() => m.Post(Fix()));
            Assert.That(m.Done, Is.False);
        }

        [Test]
        public void JineZpravy_SeIgnoruji()
        {
            var imu = new FakeImu();
            var m = new MagModelInit(imu, new FusionConfig());

            m.Post(new ARBot.Common.Models.IMUState());

            Assert.That(imu.Volani, Is.EqualTo(0));
        }
    }
}
