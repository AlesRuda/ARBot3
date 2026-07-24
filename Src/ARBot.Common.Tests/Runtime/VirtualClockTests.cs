using System;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Runtime
{
    public class VirtualClockTests
    {
        [Test]
        public void AdvanceTo_MovesForward_NeverBackward()
        {
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var clock = new VirtualClock(t0);

            Assert.That(clock.Now, Is.EqualTo(t0));

            var t1 = t0.AddSeconds(5);
            clock.AdvanceTo(t1);
            Assert.That(clock.Now, Is.EqualTo(t1));

            // zpet do minulosti = no-op
            clock.AdvanceTo(t0);
            Assert.That(clock.Now, Is.EqualTo(t1));
        }

        [Test]
        public void Default_StartsAtZero()
        {
            var clock = new VirtualClock();
            Assert.That(clock.Now, Is.EqualTo(new DateTime(0)));
        }
    }
}
