using System;
using System.Threading;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using NUnit.Framework;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Verze capture slotu (lp-navigator-zivy-slot-kamery, 13. 10. 2026): slot z <see cref="CaptureFramePool"/>
    /// publikuje (verze na sudou) <see cref="SensorBase{TState}"/> az po zapisu metadat - drivery ne. Kdyby
    /// publikace chybela, kazda kopie (navigace, zaznam, web) by snimek povazovala za rozepsany a zahodila ho.
    /// </summary>
    public class CaptureSlotPublishTests
    {
        /// <summary>Falesna kamera: snimek z capture poolu, jako skutecne drivery (bez vlastniho Publish).</summary>
        private sealed class FalesnaKamera : SensorBase<CameraFrame>
        {
            private readonly CaptureFramePool pool = new CaptureFramePool();
            public override string Name => "Left 0";

            protected override CameraFrame GetMeasurement()
            {
                Thread.Sleep(5);
                var f = pool.Next(true, 8, 8, false, 0, 0);
                f.Name = Name;
                f.TimeStamp = DateTime.UtcNow;
                return f;
            }
        }

        [Test]
        public void SensorBase_PublikujeSlot_KopieProjde()
        {
            using var k = new FalesnaKamera();
            k.Start();
            CameraFrame f = null;
            var konec = DateTime.UtcNow.AddSeconds(2);
            while (f == null && DateTime.UtcNow < konec) { f = k.GetLastMeasurement(); Thread.Sleep(5); }
            k.Stop();

            Assert.That(f, Is.Not.Null, "kamera ma dat snimek");
            Assert.That(f.FrameNum, Is.GreaterThanOrEqualTo(0));
            var pool = new CameraFramePool(1);
            var kopie = pool.Acquire(f);
            Assert.That(kopie, Is.Not.Null, "publikovany slot (suda verze) se musi dat zkopirovat");
            Assert.That(pool.TornDrops, Is.EqualTo(0));
        }

        [Test]
        public void AktualniJadro_VraciPlatneCislo()
        {
            int c = AktualniJadro.Cislo();
            Assert.That(c, Is.InRange(0, Environment.ProcessorCount - 1));
        }
    }
}
