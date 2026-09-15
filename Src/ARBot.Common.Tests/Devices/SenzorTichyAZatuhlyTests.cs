using System;
using System.Threading;
using ARBot.Common.Devices;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Dvě poruchy, po kterých vypadá mrtvý senzor jako zdravý, resp. zastaví celý runtime.
    /// Obojí našel audit 15. 9. 2026.
    ///
    /// <para><b>Tichý senzor (V4).</b> Po odpojení USB převodníku zůstane <c>sp.IsOpen</c> true,
    /// <c>Read</c> vrací 0 bajtů a driver vrátí <c>null</c> <b>bez výjimky</b> — takže
    /// <c>Process</c> nastaví <c>isError = false</c>. V Release je pak IMU/GPS mrtvé, stav zelený
    /// a v journalu ani řádek. „Nic neměřím" musí být chyba, ne ticho.</para>
    ///
    /// <para><b>Zatuhlé zastavení (V5).</b> <c>Stop()</c> čekal na vlákno <b>bez timeoutu</b>.
    /// Ovladač, který uvízne uvnitř <c>GetMeasurement</c> (u-blox točil <c>while (pos == null)</c>
    /// bez kontroly zastavení, <c>sp.ReadLine()</c> měl nekonečný <c>ReadTimeout</c>), tím zastaví
    /// <c>ARBotRuntime.Stop()</c> — a ten běží pod zámkem, takže zatuhne celý runtime.</para>
    /// </summary>
    [NonParallelizable]
    public class SenzorTichyAZatuhlyTests
    {
        /// <summary>Senzor, který nikdy nic nenaměří (tichý port) — ale ani nehází.</summary>
        private sealed class TichySenzor : SensorBase<SensorStateBase>
        {
            public override string Name => "Tichy";
            public override TimeSpan SilentTimeout => TimeSpan.FromMilliseconds(100);
            protected override SensorStateBase GetMeasurement() => null;
        }

        /// <summary>
        /// Senzor, který měří normálně.
        ///
        /// <para>Práh je tu <b>o řád nad</b> dobou, po kterou test čeká, a měření chodí při každé
        /// otáčce smyčky. Bez toho by test byl vratký: na zatíženém stroji stačí, aby vlákno
        /// senzoru chvíli nedostalo procesor, a „zdravý" senzor by práh přetáhl.</para>
        /// </summary>
        private sealed class ZdravySenzor : SensorBase<SensorStateBase>
        {
            public override string Name => "Zdravy";
            public override TimeSpan SilentTimeout => TimeSpan.FromSeconds(3);
            protected override SensorStateBase GetMeasurement()
                => new MotorStateBase { TimeStamp = ARBot.Common.Common.TimeBase.Now };
        }

        /// <summary>Ovladač, který uvízne uvnitř čtení a na zastavení nereaguje.</summary>
        private sealed class ZatuhlySenzor : SensorBase<SensorStateBase>
        {
            public readonly ManualResetEventSlim Pust = new ManualResetEventSlim(false);
            public override string Name => "Zatuhly";
            public override TimeSpan StopTimeout => TimeSpan.FromMilliseconds(300);
            protected override SensorStateBase GetMeasurement()
            {
                Pust.Wait();          // ceka, dokud ho test nepusti - Stop() ho neodblokuje
                return null;
            }
        }

        /// <summary>Počká na podmínku s termínem (smyčka běží na vlastním vlákně).</summary>
        private static bool Pockej(Func<bool> podminka, int ms = 2000)
        {
            var konec = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < konec)
            {
                if (podminka()) return true;
                Thread.Sleep(5);
            }
            return podminka();
        }

        [Test]
        public void TichySenzor_PoPrahuHlasiChybu()
        {
            using var s = new TichySenzor();
            s.Start();
            try
            {
                Assert.That(Pockej(() => s.IsError), Is.True,
                            "senzor, ktery nic nemeri, se nesmi tvarit jako zdravy");
            }
            finally { s.Stop(); }
        }

        [Test]
        public void MericiSenzor_ChybuNehlasi()
        {
            using var s = new ZdravySenzor();
            s.Start();
            try
            {
                // Dost dlouho na to, aby se tichy senzor (prah 100 ms) uz davno ozval - a zaroven
                // hluboko pod prahem tohoto senzoru (3 s).
                Thread.Sleep(300);
                Assert.That(s.IsError, Is.False, "merici senzor nesmi byt oznacen za tichy");
            }
            finally { s.Stop(); }
        }

        [Test]
        public void PredPrvnimMerenim_SeChybaNehlasiHned()
        {
            using var s = new TichySenzor();
            s.Start();
            try
            {
                // Hned po startu jeste zadne mereni byt nemusi - okno na nabehnuti senzoru.
                Assert.That(s.IsError, Is.False, "prah se pocita od startu, ne od nuly");
            }
            finally { s.Stop(); }
        }

        [Test]
        public void NespustenySenzor_ChybuNehlasi()
        {
            using var s = new TichySenzor();

            Thread.Sleep(150);

            Assert.That(s.IsError, Is.False, "senzor, ktery nebezi, nemeri - a to neni porucha");
        }

        [Test]
        public void ZatuhlyOvladac_NezastaviStop()
        {
            var s = new ZatuhlySenzor();
            s.Start();
            Assert.That(Pockej(() => s.IsRunning), Is.True, "predpoklad testu: smycka bezi");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            s.Stop();
            sw.Stop();

            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                        "Stop() cekal na zatuhly ovladac bez timeoutu - tim zatuhne cely runtime");

            s.Pust.Set();   // uklid: pust vlakno, at dobehne
        }
    }
}
