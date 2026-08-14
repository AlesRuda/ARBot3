using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Tests.Runtime;   // TestHelpers, DelegateTarget
using NUnit.Framework;
using System;
using System.IO;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Round-trip zpravy <see cref="Info"/> pres zaznam a replay. Info nese textovy log
    /// (Trace/Debug vystup aplikace) - od verze 2 i cas, oblast a uroven, aby se dal pri cteni
    /// zaznamu filtrovat a parovat s ostatnimi zpravami. Viz doc/record-replay.md.
    /// </summary>
    public class InfoMessageTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 14, 12, 34, 56, DateTimeKind.Utc);

        /// <summary>Zapise a znovu precte zpravu pres zaznam/replay.</summary>
        private static T RoundTrip<T>(T msg) where T : Message
        {
            using var ms = new MemoryStream();
            var rec = new RecordingTarget(ms, null, TestHelpers.Enc);
            rec.Start(); rec.Post(msg); rec.Stop();

            var catalog = MessageCatalog.CommonDefaults();
            T result = null;
            var sink = new DelegateTarget(m => { if (m is T t) result = t; });
            sink.Start();
            using (var rms = new MemoryStream(ms.ToArray()))
            {
                var src = new FileMessageSource(rms, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(result, Is.Not.Null, $"{typeof(T).Name} se neprecetl (chybi v katalogu?)");
            return result;
        }

        [Test]
        public void Info_RoundTrip_NeseCasOblastIUroven()
        {
            var msg = new Info("Occupancy[Left] touched=9517")
            {
                TimeStamp = T0,
                Area = "App",
                Level = "Debug",
            };

            var r = RoundTrip(msg);

            Assert.Multiple(() =>
            {
                Assert.That(r.Message, Is.EqualTo("Occupancy[Left] touched=9517"));
                Assert.That(r.TimeStamp, Is.EqualTo(T0));
                Assert.That(r.Area, Is.EqualTo("App"));
                Assert.That(r.Level, Is.EqualTo("Debug"));
            });
        }

        /// <summary>Prazdna oblast/uroven (bezny Debug.WriteLine) nesmi shodit serializaci.</summary>
        [Test]
        public void Info_RoundTrip_BezOblastiAUrovne()
        {
            var r = RoundTrip(new Info("holy text") { TimeStamp = T0 });

            Assert.Multiple(() =>
            {
                Assert.That(r.Message, Is.EqualTo("holy text"));
                Assert.That(r.Area, Is.Empty);
                Assert.That(r.Level, Is.Empty);
            });
        }

        /// <summary>
        /// STARE ZAZNAMY (verze 1 = jen text) se musi dat precist dal. MessageReader nastavuje
        /// <see cref="Message.Verze"/> jeste PRED <see cref="Message.FromData(BinaryReader)"/>,
        /// takze se na ni lze vetvit.
        /// </summary>
        [Test]
        public void Info_Verze1_SeStaleCte()
        {
            // Payload tak, jak ho zapisovala verze 1: jen retezec, nic dalsiho.
            byte[] data;
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms, TestHelpers.Enc, leaveOpen: true))
                    bw.Write("stary zaznam");
                data = ms.ToArray();
            }

            var msg = new Info { Verze = 1 };
            msg.FromData(TestHelpers.Enc, data);

            Assert.Multiple(() =>
            {
                Assert.That(msg.Message, Is.EqualTo("stary zaznam"));
                Assert.That(msg.Area, Is.Empty, "verze 1 oblast nenesla");
                Assert.That(msg.Level, Is.Empty, "verze 1 uroven nenesla");
            });
        }
    }
}

