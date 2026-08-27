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
        }

        private static TestDriver StoppedDriver(ScriptedUart uart)
        {
            var driver = new TestDriver(uart);
            driver.Stop();               // ceka na dobehnuti ctecího vlakna
            while (uart.ReadLine() != null) { }   // zahod, co vlakno nestihlo precist
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
    }
}
