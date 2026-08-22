using System;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// Round-trip <see cref="GroundTruthMsg"/> přes plnou serializaci (MessageWriter/Reader
    /// + katalog). Zpráva nese skutečnou pózu simulovaného robota — bez ní se chyba lokalizace
    /// ze záznamu spočítat nedá (viz doc/virtual-hw.md), takže musí projít i zápisem na disk,
    /// ne jen v paměti.
    /// </summary>
    public class GroundTruthMsgSerializationTests
    {
        private static GroundTruthMsg RoundTrip(GroundTruthMsg msg)
        {
            var enc = Encoding.UTF8;
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(msg);
            w.Flush();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new MemoryStream(ms.ToArray()), enc, map);
            return reader.Read() as GroundTruthMsg;
        }

        [Test]
        public void RoundTrip_KeepsAllFields()
        {
            var stamp = new DateTime(2026, 8, 22, 10, 47, 59, DateTimeKind.Utc);
            var src = new GroundTruthMsg
            {
                X = 12.25,
                Y = -3.5,
                Theta = 1.2345,
                V = 0.99,
                Omega = -0.04,
                LeftEncoder = 100.5,
                RightEncoder = 100.5,
                LeftWheelSlip = 1.0,
                RightWheelSlip = 0.98,
                TimeStamp = stamp,
            };

            var back = RoundTrip(src);

            Assert.That(back, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(back.X, Is.EqualTo(src.X));
                Assert.That(back.Y, Is.EqualTo(src.Y));
                Assert.That(back.Theta, Is.EqualTo(src.Theta));
                Assert.That(back.V, Is.EqualTo(src.V));
                Assert.That(back.Omega, Is.EqualTo(src.Omega));
                Assert.That(back.LeftEncoder, Is.EqualTo(src.LeftEncoder));
                Assert.That(back.RightEncoder, Is.EqualTo(src.RightEncoder));
                Assert.That(back.LeftWheelSlip, Is.EqualTo(src.LeftWheelSlip));
                Assert.That(back.RightWheelSlip, Is.EqualTo(src.RightWheelSlip));
                Assert.That(back.TimeStamp, Is.EqualTo(stamp));
            });
        }

        /// <summary>
        /// Zpráva musí být v katalogu — jinak by se sice zapsala, ale při přehrávání se tiše
        /// přeskočila jako neznámý typ a analýza záznamu by neměla co číst.
        /// </summary>
        [Test]
        public void Catalog_KnowsGroundTruthMsg()
        {
            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();

            Assert.That(map.ContainsKey("GroundTruthMsg"), Is.True);
        }
    }
}
