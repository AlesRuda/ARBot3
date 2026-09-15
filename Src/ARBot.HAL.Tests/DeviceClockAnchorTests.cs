using System;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Převod razítka <b>hodin kamery</b> na <see cref="ARBot.Common.Common.TimeBase"/>
    /// (<see cref="DeviceClockAnchor"/>).
    ///
    /// <para><b>Nač to je.</b> Do 15. 9. 2026 se razítka streamů počítala jako
    /// <c>epocha 1970 + offset časové zóny + ms z kamery</c> — tedy úplně jiná časová základna
    /// než zbytek systému, a navíc závislá na časové zóně stroje. Pravidlo projektu zní
    /// <b>všechen čas vychází z <c>TimeBase</c></b> (viz CLAUDE.md), takže druhá základna
    /// v záznamu nemá co dělat, i když ji dnes čte jen offline rozbor.</para>
    ///
    /// <para><b>Co se přitom nesmí ztratit.</b> <c>RGBTimeStamp</c>/<c>DepthTimeStamp</c> mají
    /// v záznamu jediný účel: poznat <b>zamrzlý stream</b> — „razítko se nehýbe, přestože
    /// framesety chodí". Proto se nesmí prostě dosadit <c>TimeBase.Now</c> (to by se hýbalo
    /// vždycky): ukotví se jen <b>počátek</b>, přírůstky zůstávají z kamery.</para>
    /// </summary>
    public class DeviceClockAnchorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void PrvniSnimek_DostaneRovnouCasZTimeBase()
        {
            var a = new DeviceClockAnchor();

            var ts = a.ToTimeBase(deviceMs: 1_234_567.0, now: T0);

            Assert.That(ts, Is.EqualTo(T0), "prvni snimek kotvi - razitko je prave ted");
        }

        [Test]
        public void DalsiSnimky_NesouPrirustkyZKamery()
        {
            var a = new DeviceClockAnchor();
            a.ToTimeBase(1000.0, T0);

            // Hodiny stroje se mezitim posunuly o 100 ms, kamera ale o 33.
            var ts = a.ToTimeBase(1033.0, T0.AddMilliseconds(100));

            Assert.That(ts, Is.EqualTo(T0.AddMilliseconds(33)),
                        "rozestup snimku musi zustat ten z kamery, ne ze stroje");
        }

        /// <summary>
        /// ⚠️ <b>Vlastnost, kvůli které to pole v záznamu je.</b> Zamrzlý stream posílá pořád
        /// totéž razítko — a po převodu to musí platit taky, jinak se zamrznutí ze záznamu
        /// nepozná.
        /// </summary>
        [Test]
        public void ZamrzlyStream_MaPoradToteRazitko()
        {
            var a = new DeviceClockAnchor();
            a.ToTimeBase(5000.0, T0);

            var prvni = a.ToTimeBase(5000.0, T0.AddSeconds(1));
            var druhe = a.ToTimeBase(5000.0, T0.AddSeconds(2));

            Assert.That(prvni, Is.EqualTo(druhe));
            Assert.That(prvni, Is.EqualTo(T0), "zamrzle razitko se nesmi hybat s hodinami stroje");
        }

        /// <summary>
        /// Barva a hloubka sdílejí <b>jednu</b> kotvu — jsou z týchž hodin kamery, takže jejich
        /// vzájemný rozdíl (rozsynchronizování streamů) musí zůstat přesně zachovaný.
        /// </summary>
        [Test]
        public void BarvaAHloubka_SdilejiKotvuAZachovajiRozdil()
        {
            var a = new DeviceClockAnchor();

            var barva = a.ToTimeBase(2000.0, T0);
            var hloubka = a.ToTimeBase(2007.5, T0);

            Assert.That((hloubka - barva).TotalMilliseconds, Is.EqualTo(7.5).Within(1e-9));
        }

        /// <summary>
        /// Po zbourání a znovupostavení pipeline začínají hodiny kamery odjinud (klidně od nuly).
        /// Bez nové kotvy by razítka skočila o roky zpět.
        /// </summary>
        [Test]
        public void PoResetu_SeUkotviZnovu()
        {
            var a = new DeviceClockAnchor();
            a.ToTimeBase(900_000.0, T0);

            a.Reset();
            var ts = a.ToTimeBase(deviceMs: 0.0, now: T0.AddSeconds(30));

            Assert.That(ts, Is.EqualTo(T0.AddSeconds(30)),
                        "po restartu pipeline se kotvi znovu, jinak by razitko skocilo zpet");
        }

        [Test]
        public void NekonecneRazitkoKamery_Nekotvi()
        {
            var a = new DeviceClockAnchor();

            var ts = a.ToTimeBase(double.NaN, T0);

            Assert.That(ts, Is.EqualTo(T0), "nesmyslne razitko nesmi otravit kotvu");
            Assert.That(a.IsAnchored, Is.False);
            Assert.That(a.ToTimeBase(100.0, T0.AddSeconds(5)), Is.EqualTo(T0.AddSeconds(5)),
                        "kotvi se az prvnim rozumnym razitkem");
        }
    }
}
