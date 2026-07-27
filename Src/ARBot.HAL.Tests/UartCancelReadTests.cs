using System;
using System.Threading;
using System.Threading.Tasks;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Overuje kooperativni zruseni blokujiciho cteni UARTu: <see cref="UartSensorBase{TState}.Stop"/>
    /// musi odblokovat cteci vlakno uviznute v <c>uart.Read(...)</c> na nedostupnem portu
    /// (jinak by <c>task.Wait()</c> uvnitr Stop cekal donekonecna).
    /// </summary>
    public class UartCancelReadTests
    {
        /// <summary>Fake UART: blokujici <see cref="Read(int)"/> se odemkne az pres <see cref="CancelRead"/>.</summary>
        private sealed class BlockingUart : IUart
        {
            private volatile bool readCancel;

            public bool IsOpen => false;
            public int ReadTimeout { get; set; }

            public byte[] Read(int count)
            {
                readCancel = false;                 // novy pozadavek na cteni (jako realny Uart)
                while (true)
                {
                    if (readCancel)
                        throw new OperationCanceledException("UART read cancelled.");
                    Thread.Sleep(5);
                }
            }

            public int Read(byte[] buffer, int offset, int count) => 0;
            public Task<byte[]> ReadAsync(int count) => Task.Run(() => Read(count));
            public string ReadLine() => null;
            public string ReadAll() => null;
            public Task<string> ReadLineAsync() => Task.FromResult<string>(null);
            public void Write(byte[] buffer) { }
            public void WriteLine(string txt) { }
            public void CancelRead() => readCancel = true;
        }

        /// <summary>Senzor, jehoz mereni visi v blokujicim <c>uart.Read(1)</c>.</summary>
        private sealed class HangingSensor : UartSensorBase<object>
        {
            public HangingSensor(IUart uart) : base(uart) { }
            public override string Name => "HangingSensor";
            protected override object GetMeasurement()
            {
                uart.Read(1);   // blokuje, dokud nedorazi CancelRead (pres Stop)
                return null;
            }
        }

        [Test]
        public void Stop_UnblocksHangingRead()
        {
            var sensor = new HangingSensor(new BlockingUart());
            sensor.Start();
            Thread.Sleep(100);   // nech cteci vlakno vstoupit do blokujiciho Read

            var stop = Task.Run(() => sensor.Stop());

            Assert.That(stop.Wait(TimeSpan.FromSeconds(3)), Is.True,
                "Stop() musi odblokovat visici Read a vratit se (jinak by task.Wait cekal donekonecna).");
            Assert.That(sensor.IsRunning, Is.False);
        }
    }
}
