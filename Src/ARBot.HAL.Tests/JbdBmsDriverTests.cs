using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARBot.Common.Devices;
using ARBot.HAL.Devices.Bms;

namespace ARBot.HAL.Tests
{
    /// <summary>Driver BMS JBD nad náhradním portem, který odpovídá jako BMS (doc/plan-bms-jbd.md).</summary>
    public class JbdBmsDriverTests
    {
        /// <summary>Port, kterému test řekne, co má na který dotaz vrátit (null = mlčet).</summary>
        private sealed class BmsUart : IUart
        {
            private readonly ConcurrentQueue<byte> rx = new ConcurrentQueue<byte>();
            public readonly List<byte[]> Zapsano = new List<byte[]>();
            public Func<byte[], byte[]> Odpovez = _ => null;

            public void Write(byte[] buffer)
            {
                lock (Zapsano) Zapsano.Add(buffer.ToArray());
                var o = Odpovez(buffer);
                if (o != null) foreach (var b in o) rx.Enqueue(b);
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                int n = 0;
                while (n < count && rx.TryDequeue(out var b)) buffer[offset + n++] = b;
                return n;
            }

            public bool IsOpen => true;
            public int ReadTimeout { get; set; }
            public byte[] Read(int count) => Array.Empty<byte>();
            public Task<byte[]> ReadAsync(int count) => Task.FromResult(Array.Empty<byte>());
            public string ReadLine() => null;
            public string ReadAll() => null;
            public Task<string> ReadLineAsync() => Task.FromResult<string>(null);
            public void WriteLine(string txt) { }
            public void CancelRead() { }
        }

        /// <summary>Driver bez vlastního vlákna: test si měření vyzvedne sám.</summary>
        private sealed class TestBms : JbdBms
        {
            public TestBms(IUart uart, List<string> hlasky = null)
                : base(uart, perioda: TimeSpan.Zero, odpovedDo: TimeSpan.FromMilliseconds(50),
                       start: false, report: s => hlasky?.Add(s)) { }
            public BmsState Zmer() => GetMeasurement();
        }

        private static readonly byte[] Clanky4 = { 0x0C, 0xDA, 0x0C, 0xDA, 0x0C, 0xD0, 0x0C, 0xDA };

        /// <summary>Zdravá BMS: na 0x03 základní údaje, na 0x04 čtyři články.</summary>
        private static byte[] ZdravaBms(byte[] dotaz) => dotaz[2] switch
        {
            0x03 => JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData(soc: 64)),
            0x04 => JbdProtocolTests.Ramec(0x04, Clanky4),
            _ => null,
        };

        [Test]
        public void ZdravaBms_JedenCyklus_VydaMereni()
        {
            var uart = new BmsUart { Odpovez = ZdravaBms };
            var s = new TestBms(uart).Zmer();

            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.SocPercent, Is.EqualTo(64));
            Assert.That(s.CellVoltages, Is.EqualTo(new[] { 3.290, 3.290, 3.280, 3.290 }).Within(1e-9));
        }

        [Test]
        public void ZapisujeJenDvaDotazyNaCteni()
        {
            var uart = new BmsUart { Odpovez = ZdravaBms };
            var bms = new TestBms(uart);
            bms.Zmer();
            bms.Zmer();

            var povolene = new[] { JbdProtocol.ReadRequest(0x03), JbdProtocol.ReadRequest(0x04) };
            Assert.That(uart.Zapsano, Is.Not.Empty);
            Assert.That(uart.Zapsano.All(z => povolene.Any(p => p.SequenceEqual(z))), Is.True,
                        "driver smi BMS jen cist - zadny zapis parametru ani spinacu");
        }

        [Test]
        public void OpozdenaOdpovedNaPredchoziDotaz_SeZahodi()
        {
            // BMS odpovi na 0x04 nejdriv opozdenou odpovedi na 0x03 a az pak napetim clanku.
            var uart = new BmsUart
            {
                Odpovez = d => d[2] == 0x03
                    ? JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData())
                    : JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData())
                        .Concat(JbdProtocolTests.Ramec(0x04, Clanky4)).ToArray(),
            };

            var s = new TestBms(uart).Zmer();

            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.CellVoltages.Length, Is.EqualTo(4), "ramec 0x03 se nesmi rozebrat jako clanky");
        }

        [Test]
        public void ClankySeNepodariloPrecist_StavNabitiSePrestoVyda()
        {
            // Jiny firmware odpovi na 0x04 jinou delkou - stav nabiti, proud a ochrany z 0x03 plati dal.
            var uart = new BmsUart
            {
                Odpovez = d => d[2] == 0x03
                    ? JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData(soc: 64, clanku: 4))
                    : JbdProtocolTests.Ramec(0x04, new byte[] { 0x0C, 0xDA }),
            };
            var bms = new TestBms(uart);

            BmsState s = null;
            for (int i = 0; i < JbdBms.ChybPoSobeProPoruchu + 1; i++) s = bms.Zmer();

            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.SocPercent, Is.EqualTo(64));
            Assert.That(s.CellVoltages, Is.Empty);
            Assert.That(bms.IsError, Is.False, "komunikace s BMS funguje, jen clanky nejdou rozebrat");
        }

        [Test]
        public void MlcenlivaBms_ZpravaBezMereni()
        {
            var s = new TestBms(new BmsUart()).Zmer();
            Assert.That(s, Is.Not.Null);
            Assert.That(s.HasMeasurement, Is.False);
        }

        [Test]
        public void TrvaleMlceni_IsError_AJednaHlaskaOVypadku()
        {
            var hlasky = new List<string>();
            var uart = new BmsUart();
            var bms = new TestBms(uart, hlasky);

            for (int i = 0; i < JbdBms.ChybPoSobeProPoruchu + 5; i++) bms.Zmer();

            Assert.That(bms.IsError, Is.True, "zpravy bez mereni nesmi senzor schovat pred hlidanim ticha");
            Assert.That(hlasky.Count(h => h.Contains("neodpovida")), Is.EqualTo(1));

            uart.Odpovez = ZdravaBms;
            bms.Zmer();
            Assert.That(bms.IsError, Is.False);
            Assert.That(hlasky.Count(h => h.Contains("zase odpovida")), Is.EqualTo(1));
        }

        [Test]
        public void SpatnySoucet_ZpravaBezMereni()
        {
            var uart = new BmsUart
            {
                Odpovez = d =>
                {
                    var r = ZdravaBms(d);
                    r[5] ^= 0x01;
                    return r;
                },
            };
            Assert.That(new TestBms(uart).Zmer().HasMeasurement, Is.False);
        }

        [Test]
        public void Stop_DobehneVcas()
        {
            var bms = new JbdBms(new BmsUart(), perioda: TimeSpan.FromSeconds(1),
                                 odpovedDo: TimeSpan.FromMilliseconds(500), report: _ => { });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bms.Stop();
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
            Assert.That(bms.IsRunning, Is.False);
        }
    }
}
