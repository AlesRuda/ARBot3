using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.HAL.Devices.Bms;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Protokol JBD BMS nad bajty (doc/plan-bms-jbd.md). Součet v pomocné funkci <see cref="Ramec"/>
    /// se počítá ZVLÁŠŤ od produkčního kódu; pevné vektory (dotazy, odpověď 0x04) jsou spočítané ručně.
    /// </summary>
    public class JbdProtocolTests
    {
        /// <summary>Odpověď BMS: DD reg 00 len data chkH chkL 77, součet = 0x10000 − Σ(stav, délka, data).</summary>
        internal static byte[] Ramec(byte reg, byte[] data, byte status = 0)
        {
            int sum = status + data.Length + data.Sum(b => b);
            int chk = (0x10000 - sum) & 0xFFFF;
            return new byte[] { 0xDD, reg, status, (byte)data.Length }
                .Concat(data).Concat(new[] { (byte)(chk >> 8), (byte)chk, (byte)0x77 }).ToArray();
        }

        /// <summary>Data registru 0x03 (big-endian) se zadanými hodnotami.</summary>
        internal static byte[] ZakladniData(double napetiV = 13.16, double proudA = -4.3, double zbyvaAh = 9.6,
            double jmenovitaAh = 15.0, int cykly = 12, int vyvazovani = 0, int ochrany = 0, int soc = 64,
            int spinace = 0x03, int clanku = 4, double[] teplotyC = null)
        {
            teplotyC ??= new[] { 24.5 };
            var d = new List<byte>();
            void U16(int v) { d.Add((byte)(v >> 8)); d.Add((byte)v); }
            U16((int)Math.Round(napetiV * 100));
            U16((ushort)(short)Math.Round(proudA * 100));
            U16((int)Math.Round(zbyvaAh * 100));
            U16((int)Math.Round(jmenovitaAh * 100));
            U16(cykly);
            U16(0x5A21);                 // datum vyroby - nepouziva se
            U16(vyvazovani & 0xFFFF);
            U16(vyvazovani >> 16);
            U16(ochrany);
            d.Add(0x10);                 // verze SW
            d.Add((byte)soc);
            d.Add((byte)spinace);
            d.Add((byte)clanku);
            d.Add((byte)teplotyC.Length);
            foreach (var t in teplotyC) U16((int)Math.Round(t * 10) + 2731);
            return d.ToArray();
        }

        [Test]
        public void Dotazy_PresneNaBajt()
        {
            Assert.That(JbdProtocol.ReadRequest(JbdProtocol.RegBasic),
                        Is.EqualTo(new byte[] { 0xDD, 0xA5, 0x03, 0x00, 0xFF, 0xFD, 0x77 }));
            Assert.That(JbdProtocol.ReadRequest(JbdProtocol.RegCells),
                        Is.EqualTo(new byte[] { 0xDD, 0xA5, 0x04, 0x00, 0xFF, 0xFC, 0x77 }));
        }

        [Test]
        public void Ramec_PevnyVektorClanku_SeVytahne()
        {
            // 4 clanky po 3290 mV (0x0CDA); soucet 0x10000 − (0+8+4·(0x0C+0xDA)) = 0xFC60, rucne.
            var buf = new List<byte> { 0xDD, 0x04, 0x00, 0x08, 0x0C, 0xDA, 0x0C, 0xDA, 0x0C, 0xDA, 0x0C, 0xDA, 0xFC, 0x60, 0x77 };

            var v = JbdProtocol.TryExtract(buf, out var r, out _);

            Assert.That(v, Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x04));
            Assert.That(r.Data.Length, Is.EqualTo(8));
            Assert.That(buf, Is.Empty, "ramec se z bufferu spotrebuje");
        }

        [Test]
        public void Ramec_NeuplnyACastecny_ChceVicDat()
        {
            var cely = Ramec(0x04, new byte[] { 0x0C, 0xDA });
            var buf = cely.Take(5).ToList();
            Assert.That(JbdProtocol.TryExtract(buf, out _, out _), Is.EqualTo(JbdVysledek.MaloDat));
            buf.AddRange(cely.Skip(5));
            Assert.That(JbdProtocol.TryExtract(buf, out _, out _), Is.EqualTo(JbdVysledek.Ramec));
        }

        [Test]
        public void Ramec_SmetiPredZacatkem_SeZahodi()
        {
            var buf = new List<byte> { 0x00, 0x13, 0x77 };
            buf.AddRange(Ramec(0x04, new byte[] { 0x0C, 0xDA }));
            Assert.That(JbdProtocol.TryExtract(buf, out var r, out _), Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x04));
        }

        [Test]
        public void Ramec_OzvenaDotazu_SePreskoci()
        {
            var buf = JbdProtocol.ReadRequest(JbdProtocol.RegCells).ToList();
            buf.AddRange(Ramec(0x04, new byte[] { 0x0C, 0xDA }));
            Assert.That(JbdProtocol.TryExtract(buf, out var r, out _), Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x04));
        }

        [Test]
        public void Ramec_DDUprostredDat_NesmyslnaDelka_NeceKaNaDalsiBajty()
        {
            // Ztraceny zacatek ramce: zbyde "... 0C DD FF ..." - 0xFF jako delka je nad MaxDataLength.
            var buf = new List<byte> { 0xDD, 0x0C, 0x00, 0xFF, 0x01 };
            buf.AddRange(Ramec(0x03, ZakladniData()));
            Assert.That(JbdProtocol.TryExtract(buf, out var r, out _), Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x03));
        }

        [Test]
        public void Ramec_SpatnySoucet_JeChyba()
        {
            var b = Ramec(0x04, new byte[] { 0x0C, 0xDA });
            b[4] ^= 0x01;
            var buf = b.ToList();
            Assert.That(JbdProtocol.TryExtract(buf, out _, out var chyba), Is.EqualTo(JbdVysledek.Chyba));
            Assert.That(chyba, Does.Contain("soucet"));
            Assert.That(buf, Is.Empty, "vadny ramec se spotrebuje, nezustane viset");
        }

        [Test]
        public void Ramec_ChybovyStav_JeChyba()
        {
            var buf = Ramec(0x03, Array.Empty<byte>(), status: 0x80).ToList();
            Assert.That(JbdProtocol.TryExtract(buf, out _, out var chyba), Is.EqualTo(JbdVysledek.Chyba));
            Assert.That(chyba, Does.Contain("0x80"));
        }

        [Test]
        public void Zakladni_RozeberHodnoty()
        {
            var s = new BmsState();
            bool ok = JbdProtocol.TryParseBasic(
                ZakladniData(napetiV: 13.16, proudA: -4.3, zbyvaAh: 9.6, jmenovitaAh: 15, cykly: 12,
                             vyvazovani: 0x0001_0005, ochrany: 0x0002, soc: 64, spinace: 0x01, clanku: 4,
                             teplotyC: new[] { 24.5, -3.0 }),
                s, out int clanku, out var chyba);

            Assert.That(ok, Is.True, chyba);
            Assert.Multiple(() =>
            {
                Assert.That(clanku, Is.EqualTo(4));
                Assert.That(s.PackVoltage, Is.EqualTo(13.16).Within(1e-9));
                Assert.That(s.Current, Is.EqualTo(-4.3).Within(1e-9), "proud je se znamenkem");
                Assert.That(s.RemainingAh, Is.EqualTo(9.6).Within(1e-9));
                Assert.That(s.NominalAh, Is.EqualTo(15).Within(1e-9));
                Assert.That(s.Cycles, Is.EqualTo(12));
                Assert.That(s.BalanceMask, Is.EqualTo(0x0001_0005u), "spodni slovo = clanky 1-16, horni = 17-32");
                Assert.That(s.Protection, Is.EqualTo(BmsProtection.CellUndervoltage));
                Assert.That(s.SocPercent, Is.EqualTo(64));
                Assert.That(s.ChargeFetOn, Is.True);
                Assert.That(s.DischargeFetOn, Is.False);
                Assert.That(s.Temperatures, Is.EqualTo(new[] { 24.5, -3.0 }).Within(1e-9), "0,1 K - 273,1");
            });
        }

        [TestCase(0, TestName = "Zakladni_NulaClanku_Odmitne")]
        [TestCase(33, TestName = "Zakladni_PrilisClanku_Odmitne")]
        public void Zakladni_NesmyslnyPocetClanku(int clanku)
        {
            Assert.That(JbdProtocol.TryParseBasic(ZakladniData(clanku: clanku), new BmsState(), out _, out var chyba), Is.False);
            Assert.That(chyba, Does.Contain("clanku"));
        }

        [Test]
        public void Zakladni_StavNabitiNad100_Odmitne()
            => Assert.That(JbdProtocol.TryParseBasic(ZakladniData(soc: 101), new BmsState(), out _, out _), Is.False);

        [Test]
        public void Zakladni_KratkaData_Odmitne()
            => Assert.That(JbdProtocol.TryParseBasic(new byte[10], new BmsState(), out _, out _), Is.False);

        [Test]
        public void Zakladni_ChybiTeploty_Odmitne()
        {
            var d = ZakladniData(teplotyC: new[] { 20.0, 21.0 });
            Assert.That(JbdProtocol.TryParseBasic(d.Take(d.Length - 2).ToArray(), new BmsState(), out _, out _), Is.False);
        }

        [Test]
        public void Clanky_RozeberNapeti()
        {
            bool ok = JbdProtocol.TryParseCells(new byte[] { 0x0C, 0xDA, 0x0C, 0xD0 }, 2, out var v, out _);
            Assert.That(ok, Is.True);
            Assert.That(v, Is.EqualTo(new[] { 3.290, 3.280 }).Within(1e-9));
        }

        [Test]
        public void Clanky_JinyPocetNezZakladni_Odmitne()
            => Assert.That(JbdProtocol.TryParseCells(new byte[] { 0x0C, 0xDA }, 4, out _, out _), Is.False);
    }
}
