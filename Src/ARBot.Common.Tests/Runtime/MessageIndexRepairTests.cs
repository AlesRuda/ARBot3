using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using NUnit.Framework;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// Oprava poskozeneho indexu zaznamu ze samotnych dat (<see cref="MessageIndex.Load"/>).
    ///
    /// <para>Motivace: 2. 9. 2026 dosla robotu baterie uprostred zaznamu. Data prezila (az na
    /// useknuty posledni snimek), ale sidecar <c>.idx</c> u jednoho zaznamu koncil uprostred
    /// polozky a u druheho obsahoval nuly a polozky ukazujici za konec dat. <c>MessageIndex.Read</c>
    /// padal na <c>EndOfStreamException</c> a View zaznam vubec neotevrel. Tyhle testy vyrabeji
    /// tytez tri druhy poskozeni synteticky.</para>
    /// </summary>
    public class MessageIndexRepairTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 2, 22, 58, 30, DateTimeKind.Utc);

        /// <summary>Zaznam s N zpravami IMU (cas T0 + i*20 ms) a jednou pojmenovanou zpravou uprostred.</summary>
        private static (byte[] data, byte[] idx, List<IndexEntry> entries) MakeRecord(int n)
        {
            using var dataMs = new MemoryStream();
            using var idxMs = new MemoryStream();
            var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc, OverflowPolicy.Block, null);
            for (int i = 0; i < n; i++)
            {
                rec.Post(TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: i * 0.01, omega: 0.1));
                if (i == n / 2)
                    rec.Post(new ImageMsg(new ARBot.Common.Common.Image<ARBot.Common.Common.RGB>(1, 1)
                                          { Data = new byte[] { 1, 2, 3 } }, "camX"));
            }
            rec.Start();
            rec.Stop();
            var entries = MessageIndex.Read(new MemoryStream(idxMs.ToArray()), TestHelpers.Enc);
            return (dataMs.ToArray(), idxMs.ToArray(), entries);
        }

        private static Dictionary<string, Message> Protos()
            => MessageCatalog.RecordDefaults().ToPrototypeMap();

        private static void AssertSameFrames(List<IndexEntry> expected, List<IndexEntry> actual, string proc)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), proc + ": pocet polozek");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].Seq, Is.EqualTo(expected[i].Seq), $"{proc}: Seq #{i}");
                Assert.That(actual[i].Offset, Is.EqualTo(expected[i].Offset), $"{proc}: Offset #{i}");
                Assert.That(actual[i].Length, Is.EqualTo(expected[i].Length), $"{proc}: Length #{i}");
                Assert.That(actual[i].MsgName, Is.EqualTo(expected[i].MsgName), $"{proc}: MsgName #{i}");
                Assert.That(actual[i].Name, Is.EqualTo(expected[i].Name), $"{proc}: Name #{i}");
                Assert.That(actual[i].CaptureTicks, Is.EqualTo(expected[i].CaptureTicks), $"{proc}: CaptureTicks #{i}");
            }
        }

        [Test]
        public void Read_UseknutyIndex_VratiCelePolozkyANepadne()
        {
            var (_, idx, full) = MakeRecord(20);
            // Useknout uprostred posledni polozky (jako zapis pres page cache pri vypadku napajeni).
            var cut = idx.Take(idx.Length - 7).ToArray();

            var read = MessageIndex.Read(new MemoryStream(cut), TestHelpers.Enc, out bool truncated);

            Assert.That(truncated, Is.True, "useknuti se musi ohlasit");
            Assert.That(read.Count, Is.EqualTo(full.Count - 1), "vsechny cele polozky zustavaji, nekompletni pryc");
            Assert.That(read.Last().Seq, Is.EqualTo(full[full.Count - 2].Seq));
        }

        [Test]
        public void Load_UseknutyIndex_DoplniOcasZDat()
        {
            var (data, idx, full) = MakeRecord(30);
            // Index koncí o 5 polozek driv a jeste uprostred sesté (index zaostal za daty).
            int keep = 0;
            using (var ms = new MemoryStream(idx))
            {
                var partial = MessageIndex.Read(ms, TestHelpers.Enc);
                long endOfKept = partial[full.Count - 6].Offset + partial[full.Count - 6].Length;
                // bajtova delka prvnich (Count-5) polozek: znovu zapsat jen ty
                using var w = new MemoryStream();
                using var iw = new MessageIndexWriter(w, TestHelpers.Enc);
                foreach (var e in partial.Take(full.Count - 5)) iw.Write(e);
                iw.Flush();
                keep = (int)w.Length;
                Assert.That(endOfKept, Is.LessThan(data.Length));
            }
            var cut = idx.Take(keep + 9).ToArray();   // + 9 B = zacatek dalsi polozky

            var loaded = MessageIndex.Load(new MemoryStream(data), new MemoryStream(cut), TestHelpers.Enc,
                                           Protos(), out var report);

            Assert.That(report.SidecarTruncated, Is.True);
            Assert.That(report.Rebuilt, Is.True);
            Assert.That(report.RebuiltEntries, Is.EqualTo(5));
            Assert.That(report.TrailingBytes, Is.EqualTo(0), "data jsou cela");
            AssertSameFrames(full, loaded, "doplneny index");
            // Doplnene polozky maji T_out = T_in (sken T_out nezna).
            foreach (var e in loaded.Skip(full.Count - 5))
                Assert.That(e.ArrivalTicks, Is.EqualTo(e.CaptureTicks), "T_out doplnene polozky = T_in");
        }

        [Test]
        public void Load_IndexUkazujeZaKonecDatANuly_ZahodiAPrepocita()
        {
            var (data, idx, full) = MakeRecord(30);
            // Data useknuta uprostred posledniho ramce; index cely, navic s pruhem nul na konci
            // (presne obraz zaznamu 20260902-225830).
            var dataCut = data.Take(data.Length - 40).ToArray();
            var idxBad = idx.Concat(new byte[38 * 3]).ToArray();

            var loaded = MessageIndex.Load(new MemoryStream(dataCut), new MemoryStream(idxBad), TestHelpers.Enc,
                                           Protos(), out var report);

            Assert.That(report.SidecarDiscarded, Is.GreaterThanOrEqualTo(1), "polozka za koncem dat i nuly jdou pryc");
            Assert.That(report.TrailingBytes, Is.GreaterThan(0), "useknuty posledni ramec se hlasi jako ztraceny");
            AssertSameFrames(full.Take(full.Count - 1).ToList(), loaded, "index bez posledniho ramce");
            Assert.That(report.Damaged, Is.True);
            Assert.That(report.ToString(), Does.Contain("POSKOZENY"));
        }

        [Test]
        public void Load_BezSidecaru_PostaviIndexCelyZDat()
        {
            var (data, _, full) = MakeRecord(25);

            var loaded = MessageIndex.Load(new MemoryStream(data), null, TestHelpers.Enc, Protos(), out var report);

            Assert.That(report.SidecarFound, Is.False);
            Assert.That(report.Rebuilt, Is.True);
            Assert.That(report.RebuiltFromOffset, Is.EqualTo(0));
            AssertSameFrames(full, loaded, "index ze skenu");
        }

        [Test]
        public void Load_ZdravyZaznam_NicNemeniANehlasiPoskozeni()
        {
            var (data, idx, full) = MakeRecord(25);

            var loaded = MessageIndex.Load(new MemoryStream(data), new MemoryStream(idx), TestHelpers.Enc,
                                           Protos(), out var report);

            Assert.That(report.Damaged, Is.False, report.ToString());
            Assert.That(loaded.Select(e => e.ArrivalTicks), Is.EqualTo(full.Select(e => e.ArrivalTicks)),
                        "u zdraveho indexu zustava puvodni T_out");
            AssertSameFrames(full, loaded, "zdravy index");
        }

        [Test]
        public void Rebuild_SmetiMistoHlavicky_SkonciBezVyjimky()
        {
            var (data, _, full) = MakeRecord(10);
            // Za platnymi ramci nasleduje binarni smeti s obri "delkou" - nesmi se alokovat nic obriho.
            var garbage = data.Concat(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 }).ToArray();

            var into = new List<IndexEntry>();
            long lost = MessageIndex.Rebuild(new MemoryStream(garbage), 0, 0, 0, TestHelpers.Enc, Protos(), into);

            Assert.That(into.Count, Is.EqualTo(full.Count));
            Assert.That(lost, Is.EqualTo(8));
        }

        [Test]
        public void LoadFile_OpraviSidecarNaDisku()
        {
            var (data, idx, full) = MakeRecord(20);
            string dir = Path.Combine(Path.GetTempPath(), "arbot-idx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string rec = Path.Combine(dir, "z.rec");
                File.WriteAllBytes(rec, data);
                File.WriteAllBytes(rec + ".idx", idx.Take(idx.Length - 5).ToArray());

                var loaded = MessageIndex.LoadFile(rec, TestHelpers.Enc, Protos(), repairSidecar: true, out var report);

                Assert.That(report.Damaged, Is.True);
                AssertSameFrames(full, loaded, "opraveny");
                Assert.That(File.Exists(rec + ".idx.bad"), Is.True, "puvodni sidecar se schova jako .bad");
                // Druhe otevreni uz je bez opravy.
                var again = MessageIndex.LoadFile(rec, TestHelpers.Enc, Protos(), repairSidecar: true, out var report2);
                Assert.That(report2.Damaged, Is.False, report2.ToString());
                AssertSameFrames(full, again, "druhe cteni");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
