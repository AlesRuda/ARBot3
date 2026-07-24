using System;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Overuje, ze <see cref="AsyncFusionEngine"/> je thread-safe: soubezne volani
    /// <see cref="AsyncFusionEngine.Enqueue"/> (vlakno fuze) a
    /// <see cref="AsyncFusionEngine.GetStateAt"/> (vlakno rizeni) neskonci vyjimkou
    /// ani deadlockem.
    /// </summary>
    [TestFixture]
    public class AsyncFusionEngineConcurrencyTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 0, 0, 0);

        [Test]
        public void ParallelEnqueueAndGetStateAt_DoNotThrowOrDeadlock()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            const int iterations = 5000;
            Exception failure = null;

            // vlakno "fuze" - vklada mereni s rostoucim casem
            var producer = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var t = T0.AddMilliseconds(i * 5);
                        engine.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, t, "Odo"));
                        engine.Enqueue(ScalarStateMeasurement.AngularRate(0.1, 0.05, t, "Gyro"));
                    }
                }
                catch (Exception ex) { Volatile.Write(ref failure, ex); }
            });

            // vlakno "rizeni" - opakovane se ptá na aktualni odhad
            var consumer = Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var t = T0.AddMilliseconds(i * 5);
                        var rs = engine.GetStateAt(t);
                        Assert.That(rs, Is.Not.Null);
                        _ = engine.BufferedCount;
                        _ = engine.FilterTime;
                    }
                }
                catch (Exception ex) { Volatile.Write(ref failure, ex); }
            });

            bool finished = Task.WaitAll(new[] { producer, consumer }, TimeSpan.FromSeconds(30));
            Assert.That(finished, Is.True, "deadlock: vlakna nedobehla do 30 s");
            Assert.That(failure, Is.Null, () => $"soubezny pristup vyhodil vyjimku: {failure}");
        }
    }
}
