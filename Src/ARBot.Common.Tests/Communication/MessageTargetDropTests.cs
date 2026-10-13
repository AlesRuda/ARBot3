using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using NUnit.Framework;

namespace ARBot.Common.Tests.Communication
{
    /// <summary>
    /// Plna fronta s politikou zahazovani musi zahozenou zpravu ohlasit (<c>OnDropped</c>) - kdo do fronty
    /// vklada poolovane zdroje (kopie snimku v LocalNavigator), musi je vratit, jinak by pool vyschl
    /// (lp-navigator-zivy-slot-kamery, 13. 10. 2026).
    /// </summary>
    public class MessageTargetDropTests
    {
        private sealed class Zachyt : MessageTarget
        {
            public readonly List<Message> Zahozene = new List<Message>();
            public Zachyt(OverflowPolicy p, int kapacita) : base(p, kapacita) { }
            protected override void Consume(Message msg) { }
            protected override void OnDropped(Message msg) { lock (Zahozene) Zahozene.Add(msg); }
        }

        private static CameraFrame M(string jm) => new CameraFrame { Name = jm };

        [Test]
        public void DropOldest_OhlasiZahozeneNejstarsi()
        {
            var t = new Zachyt(OverflowPolicy.DropOldest, 2);   // nenastartovany -> nic se nekonzumuje
            for (int i = 1; i <= 5; i++) t.Post(M(i.ToString()));

            Assert.That(t.Zahozene.ConvertAll(m => ((CameraFrame)m).Name), Is.EqualTo(new[] { "1", "2", "3" }));
        }

        [Test]
        public void DropNewest_OhlasiZahozene()
        {
            var t = new Zachyt(OverflowPolicy.DropNewest, 2);
            for (int i = 1; i <= 4; i++) t.Post(M(i.ToString()));

            Assert.That(t.Zahozene.Count, Is.EqualTo(2));
        }

        [Test]
        public void NeomezenaFronta_NicNezahazuje()
        {
            var t = new Zachyt(OverflowPolicy.Block, 0);
            for (int i = 1; i <= 10; i++) t.Post(M(i.ToString()));
            Assert.That(t.Zahozene, Is.Empty);
        }
    }
}
