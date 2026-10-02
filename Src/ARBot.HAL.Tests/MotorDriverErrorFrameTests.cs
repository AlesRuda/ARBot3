using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.HAL.Devices.MotorDrivers;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Driver motoru musi rozlisit <b>merenie</b> od <b>zastupneho ramce po chybe</b>.
    ///
    /// <para>Pri neparsovatelne odpovedi (nebo nedostupnem portu) vraci <c>SDC2160</c> stav se
    /// <c>IsEmergencyStop = true</c> a samymi nulami. Stop je tam spravne — je to fail-safe „nevim,
    /// co se deje, at robot stoji" — ale <b>nuly nikdo nemeril</b>. Dokud se to nerozlisovalo,
    /// dostavala fuze „stojim" prave v okamziku, kdy o robotu nevi nic, a robot se pritom muze
    /// pohybovat (dobrzduje, jede ze setrvacnosti).</para>
    ///
    /// <para>Rozlisovat se to musi <b>priznakem</b>, ne stopem: pod drzenym nouzovym zastavenim je
    /// nulova rychlost <i>plnohodnotne merenie</i> (ridici jednotka ma prikaz stat a motory jsou
    /// rizene pozicne ve zpetne vazbe), zatimco po chybe parsovani je nula <i>vymysl</i>.</para>
    /// </summary>
    public class MotorDriverErrorFrameTests
    {
        /// <summary>UART, ktery vraci predem nachystane radky; po jejich vycerpani <c>null</c>.</summary>
        private sealed class ScriptedUart : IUart
        {
            private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();

            public void Feed(params string[] text)
            {
                foreach (var s in text) lines.Enqueue(s);
            }

            public bool IsOpen => true;
            public int ReadTimeout { get; set; }

            public string ReadLine() => lines.TryDequeue(out var s) ? s : null;

            public byte[] Read(int count) => Array.Empty<byte>();
            public int Read(byte[] buffer, int offset, int count) => 0;
            public Task<byte[]> ReadAsync(int count) => Task.FromResult(Array.Empty<byte>());
            public string ReadAll() => null;
            public Task<string> ReadLineAsync() => Task.FromResult<string>(null);
            public void Write(byte[] buffer) { }
            public void WriteLine(string txt) { }
            public void CancelRead() { }
        }

        /// <summary>
        /// Driver se ctecim vlaknem zastavenym, aby si test mohl vyzvednout jeden ramec sam.
        /// <para>Konstruktor <c>SDC2160Ex</c> vola <c>Start()</c>, takze vlakno bezi hned — a dokud
        /// nedobehne <c>Stop()</c>, sahalo by na tyz UART jako test.</para>
        /// </summary>
        private sealed class TestDriver : SDC2160Ex
        {
            public TestDriver(IUart uart)
                : base(uart, maxPossibleSpeed: 1.0, speedLimit: 1.0,
                       wheelCircumference: 1.0, enc2Rotation: 1000) { }

            public IMotorState ReadOneFrame() => GetMeasurement();

            /// <summary>
            /// Zruší příznak zastavení, aby si test mohl vyzvednout rámec ručně.
            ///
            /// <para><c>GetMeasurement</c> od 15. 9. 2026 při <c>stopRequired</c> vrací <c>null</c>
            /// (aby se při vypínání nečekalo celé 500ms okno a hlavně aby nevznikl <b>falešný
            /// fail-rámec</b> — ten dnes brány mise čtou jako „neznámo"). <c>Stop()</c> se tu ale
            /// volá jen kvůli zastavení <b>vlákna</b>, ne proto, že by se vypínal robot.</para>
            /// </summary>
            public void ObnovCteni() => stopRequired = false;
        }

        private static TestDriver StoppedDriver(ScriptedUart uart)
        {
            var driver = new TestDriver(uart);
            driver.Stop();               // ceka na dobehnuti ctecího vlakna
            while (uart.ReadLine() != null) { }   // zahod, co vlakno nestihlo precist
            driver.ObnovCteni();         // ...a ted uz smi test cist sam
            return driver;
        }

        /// <summary>
        /// <b>Neparsovatelna odpoved → zastupny ramec.</b> Stop plati (fail-safe), ale ramec
        /// o sobe rekne, ze <b>merenie nenese</b> — fuze ho pak zahodi misto aby z nej vzala „v = 0".
        /// </summary>
        [Test]
        public void UnparsableResponse_YieldsFrameWithoutMeasurement()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);

            uart.Feed("DI=1", "?C=nesmysl", "?V=taky ne", "?A=vubec");

            var state = driver.ReadOneFrame();

            Assert.That(state, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(state.HasMeasurement, Is.False,
                            "nuly po chybe parsovani nikdo nemeril - fuze je nesmi dostat jako 'stojim'");
                Assert.That(state.IsEmergencyStop, Is.True,
                            "stop zustava fail-safe: nevime, co se deje, at robot stoji");
            });
        }

        /// <summary>
        /// Platna odpoved → normalni merenie. Kontrola k testu vyse: priznak nesmi byt <c>false</c>
        /// vzdycky (to by odometrii utnulo uplne).
        /// </summary>
        [Test]
        public void ValidResponse_YieldsMeasurement()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);

            uart.Feed("DI=1", "?C=1000:2000", "?V=240", "?A=10:20");

            var state = driver.ReadOneFrame();

            Assert.That(state, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(state.HasMeasurement, Is.True);
                Assert.That(state.IsEmergencyStop, Is.False, "DI=1 znamena, ze stop NENI aktivni");
                Assert.That(state.Voltage, Is.EqualTo(24.0).Within(1e-9));
            });
        }

        // ==================== Cas jednotky (radek T=) ====================
        // lok-fuze-poza-pred-koly: razitko z casu prichodu neslo cist (USB CDC v davkach, vzor
        // 12 / 12 / 9 ms) a rychlost Δenc/Δrazitko po kratkem intervalu nadsazovala. Skript
        // posila svuj citac v ms, driver z nej bere interval i razitko.

        /// <summary>
        /// Rychlost kol z intervalu JEDNOTKY: dva ramce prectene hned po sobe (cas prichodu se
        /// skoro nelisi), ale jednotka mezi nimi napocitala 11 ms a enkoder 11 pulzu -> 1 m/s.
        /// Z casu prichodu by vysla rychlost o rady vyssi.
        /// </summary>
        [Test]
        public void CasJednotky_RychlostZIntervaluJednotky()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);

            // enc2Dist = 1 m / 1000 pulzu = 1 mm na pulz; pravy kanal kladne, levy zaporne.
            uart.Feed("T=5000", "DI=1", "?C=1000:-1000", "?V=240", "?A=10:20");
            var first = driver.ReadOneFrame() as MotorStateBase;
            uart.Feed("T=5011", "DI=1", "?C=1011:-1011", "?V=240", "?A=10:20");
            var second = driver.ReadOneFrame() as MotorStateBase;

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(second!.RightWheelSpeed, Is.EqualTo(1.0).Within(1e-9));
                Assert.That(second.LeftWheelSpeed, Is.EqualTo(1.0).Within(1e-9));
                Assert.That(second.DeviceTimeMs, Is.EqualTo(5011), "surovy cas jednotky jde do zpravy");
                // Interval razitek se tu netestuje: ramce prisly 1 ms po sobe, ale jednotka mezi
                // nimi napocitala 11 ms - prvni tedy mel o 10 ms vetsi latenci a odhad posunu se
                // spravne posune (minimum). Ustaleny stav kryje DeviceClockTests.
                Assert.That(second.TimeStamp, Is.LessThanOrEqualTo(ARBot.Common.Common.TimeBase.Now),
                            "razitko nesmi byt v budoucnosti");
                Assert.That(first!.DeviceTimeMs, Is.EqualTo(5000));
                Assert.That(driver.DeviceClockSyncs, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// Stary skript (bez radku T=): driver jede po staru z casu prichodu a zprava cas jednotky
        /// nenese. Novy driver musi jit pustit proti jednotce, do ktere se skript jeste nenahral.
        /// </summary>
        [Test]
        public void BezCasuJednotky_PoStaru()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);

            uart.Feed("DI=1", "?C=1000:-1000", "?V=240", "?A=10:20");
            var state = driver.ReadOneFrame() as MotorStateBase;

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.HasDeviceTime, Is.False);
            Assert.That(state.DeviceTimeMs, Is.EqualTo(-1));
            Assert.That(driver.DeviceClockSyncs, Is.EqualTo(0));
        }

        /// <summary>Nesmyslny radek T= neshodi ramec: cas jednotky se jen nepouzije.</summary>
        [Test]
        public void NeplatnyCasJednotky_RamecPlati()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);

            uart.Feed("T=xyz", "DI=1", "?C=1000:-1000", "?V=240", "?A=10:20");
            var state = driver.ReadOneFrame() as MotorStateBase;

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.HasMeasurement, Is.True);
            Assert.That(state.HasDeviceTime, Is.False);
        }
    }
}
