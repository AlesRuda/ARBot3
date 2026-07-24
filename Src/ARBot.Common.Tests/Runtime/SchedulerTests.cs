using System;
using System.Collections.Generic;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>Testy bezvlaknoveho <see cref="Scheduler"/> (mrizka, jitter, unregister).</summary>
    public class SchedulerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Ticks_FallOnGridMultiples_RegardlessOfJitter()
        {
            var sch = new Scheduler();
            var interval = TimeSpan.FromMilliseconds(20);
            var ticks = new List<DateTime>();
            using (sch.Register(interval, t => ticks.Add(t)))
            {
                // t0 = cas prvniho PumpDue; prvni takt padne rovnou na t0.
                sch.PumpDue(T0);                              // -> t0
                sch.PumpDue(T0.AddMilliseconds(25));          // -> t0+20 (jitter 25 nemeni mrizku)
                sch.PumpDue(T0.AddMilliseconds(39));          // nic noveho (dalsi je t0+40)
                sch.PumpDue(T0.AddMilliseconds(60));          // -> t0+40, t0+60
            }

            Assert.That(ticks, Is.EqualTo(new[]
            {
                T0,
                T0.AddMilliseconds(20),
                T0.AddMilliseconds(40),
                T0.AddMilliseconds(60),
            }));
        }

        [Test]
        public void Dispose_StopsFurtherTicks()
        {
            var sch = new Scheduler();
            int count = 0;
            var reg = sch.Register(TimeSpan.FromMilliseconds(10), _ => count++);
            sch.PumpDue(T0);                 // 1 takt
            reg.Dispose();
            sch.PumpDue(T0.AddMilliseconds(100));   // po zruseni uz nic
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void BigJump_EmitsAllGridTicksInBetween()
        {
            var sch = new Scheduler();
            var ticks = new List<DateTime>();
            using (sch.Register(TimeSpan.FromMilliseconds(20), ticks.Add))
            {
                sch.PumpDue(T0);                       // t0
                sch.PumpDue(T0.AddMilliseconds(100));  // t0+20,40,60,80,100
            }
            Assert.That(ticks.Count, Is.EqualTo(6));
            Assert.That(ticks[^1], Is.EqualTo(T0.AddMilliseconds(100)));
        }
    }
}
