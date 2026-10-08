using System;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using NUnit.Framework;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>Zpráva z BMS (plan-bms-jbd.md): přežije záznam a popíše ochrany česky.</summary>
    [TestFixture]
    public class BmsStateTests
    {
        private static BmsState RoundTrip(BmsState s)
        {
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, Encoding.UTF8);
            w.Write(s);
            w.Flush();
            var r = new MessageReader(new MemoryStream(ms.ToArray()), Encoding.UTF8,
                                      MessageCatalog.RecordDefaults().ToPrototypeMap());
            return (BmsState)r.Read();
        }

        [Test]
        public void ZapisACteni_VratiVsechnaPole()
        {
            var t = new DateTime(2026, 10, 8, 12, 0, 0);
            var s = new BmsState
            {
                TimeStamp = t, PackVoltage = 13.21, Current = -4.3, RemainingAh = 9.6, NominalAh = 15,
                SocPercent = 64, Cycles = 12, CellVoltages = new[] { 3.28, 3.30, 3.31, 3.30 },
                Temperatures = new[] { 24.5, 23.0 }, BalanceMask = 0b0101,
                Protection = BmsProtection.CellUndervoltage, ChargeFetOn = true, DischargeFetOn = false,
            };

            var b = RoundTrip(s);

            Assert.Multiple(() =>
            {
                Assert.That(b.HasMeasurement, Is.True);
                Assert.That(b.TimeStamp, Is.EqualTo(t));
                Assert.That(b.PackVoltage, Is.EqualTo(13.21));
                Assert.That(b.Current, Is.EqualTo(-4.3));
                Assert.That(b.RemainingAh, Is.EqualTo(9.6));
                Assert.That(b.NominalAh, Is.EqualTo(15));
                Assert.That(b.SocPercent, Is.EqualTo(64));
                Assert.That(b.Cycles, Is.EqualTo(12));
                Assert.That(b.CellVoltages, Is.EqualTo(new[] { 3.28, 3.30, 3.31, 3.30 }));
                Assert.That(b.Temperatures, Is.EqualTo(new[] { 24.5, 23.0 }));
                Assert.That(b.BalanceMask, Is.EqualTo(0b0101u));
                Assert.That(b.Protection, Is.EqualTo(BmsProtection.CellUndervoltage));
                Assert.That(b.ChargeFetOn, Is.True);
                Assert.That(b.DischargeFetOn, Is.False);
            });
        }

        [Test]
        public void BezMereni_PrezijeZaznam()
        {
            var b = RoundTrip(BmsState.NoMeasurement(new DateTime(2026, 10, 8)));
            Assert.That(b.HasMeasurement, Is.False);
            Assert.That(b.CellVoltages, Is.Empty);
        }

        [Test]
        public void OdvozeneHodnoty_ZClankuATeplot()
        {
            var s = new BmsState { CellVoltages = new[] { 3.28, 3.31, 3.30 }, Temperatures = new[] { 20.0, 26.5 } };
            Assert.That(s.CellMinV, Is.EqualTo(3.28));
            Assert.That(s.CellMaxV, Is.EqualTo(3.31));
            Assert.That(s.TempMaxC, Is.EqualTo(26.5));
            Assert.That(double.IsNaN(new BmsState().CellMinV), Is.True, "bez clanku neni minimum, ne nula");
        }

        [Test]
        public void PopisOchran_Cesky_IVicePriznaku()
        {
            Assert.That(BmsProtectionText.Popis(BmsProtection.None), Is.EqualTo("žádná"));
            Assert.That(BmsProtectionText.Popis(BmsProtection.CellUndervoltage | BmsProtection.ShortCircuit),
                        Is.EqualTo("podpětí článku, zkrat"));
        }

        [Test]
        public void PopisOchran_NeznamyBit_NeztratiSe()
        {
            var p = (BmsProtection)(1 << 14);
            Assert.That(BmsProtectionText.Popis(p), Is.EqualTo("neznámý příznak 0x4000"));
        }
    }
}
