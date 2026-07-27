using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// Testy <see cref="RoleRouter"/>: primarni zprava (<see cref="IPrimaryMessage"/>)
    /// jde na Stream i do zpracovani; odvozena jen na Stream.
    /// </summary>
    public class RoleRouterTests
    {
        /// <summary>Jednoduchy sink sbirajici zpravy do seznamu.</summary>
        private sealed class CollectSink : IMessageSink
        {
            public readonly List<Message> Msgs = new List<Message>();
            public void Post(Message msg) => Msgs.Add(msg);
        }

        [Test]
        public void PrimaryMessage_GoesToBothStreamAndProcessing()
        {
            var stream = new CollectSink();
            var processing = new CollectSink();
            var router = new RoleRouter(stream, processing);

            var imu = new IMUState();   // SensorStateBase => IPrimaryMessage
            Assert.That(imu is IPrimaryMessage, Is.True, "predpoklad: IMUState je primarni");

            router.Post(imu);

            Assert.That(stream.Msgs, Has.Count.EqualTo(1));
            Assert.That(processing.Msgs, Has.Count.EqualTo(1));
            Assert.That(stream.Msgs[0], Is.SameAs(imu));
            Assert.That(processing.Msgs[0], Is.SameAs(imu));
        }

        [Test]
        public void DerivedMessage_GoesOnlyToStream()
        {
            var stream = new CollectSink();
            var processing = new CollectSink();
            var router = new RoleRouter(stream, processing);

            var derived = new RobotStateMsg();   // odvozena, bez markeru
            Assert.That(derived is IPrimaryMessage, Is.False, "predpoklad: RobotStateMsg neni primarni");

            router.Post(derived);

            Assert.That(stream.Msgs, Has.Count.EqualTo(1));
            Assert.That(stream.Msgs[0], Is.SameAs(derived));
            Assert.That(processing.Msgs, Is.Empty, "odvozena zprava nesmi jit do zpracovani");
        }

        [Test]
        public void NullMessage_IsIgnored()
        {
            var stream = new CollectSink();
            var processing = new CollectSink();
            var router = new RoleRouter(stream, processing);

            router.Post(null);

            Assert.That(stream.Msgs, Is.Empty);
            Assert.That(processing.Msgs, Is.Empty);
        }
    }
}
