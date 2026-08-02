using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Runtime
{
    public class RecordReplayTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static List<IMUState> SyntheticImu(int count)
        {
            var list = new List<IMUState>(count);
            for (int i = 0; i < count; i++)
                list.Add(TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: i * 0.01, omega: 0.1));
            return list;
        }

        // ---- 1) round-trip surovych zprav zaznam -> replay ----
        [Test]
        public void RecordThenReplay_RoundTripsImuMessages()
        {
            var catalog = MessageCatalog.CommonDefaults();
            var imus = SyntheticImu(25);

            byte[] dataBytes;
            using (var dataMs = new MemoryStream())
            {
                var rec = new RecordingTarget(dataMs, null, TestHelpers.Enc);
                rec.Start();
                foreach (var m in imus) rec.Post(m);
                rec.Stop();
                Assert.That(rec.Count, Is.EqualTo(imus.Count));
                dataBytes = dataMs.ToArray();
            }

            var collected = new List<IMUState>();
            var sink = new DelegateTarget(m => { if (m is IMUState s) collected.Add(s); });
            sink.Start();
            using (var readMs = new MemoryStream(dataBytes))
            {
                var src = new FileMessageSource(readMs, TestHelpers.Enc, catalog);
                src.Connect(sink);
                src.RunToEnd();
            }
            sink.Stop();

            Assert.That(collected.Count, Is.EqualTo(imus.Count));
            for (int i = 0; i < imus.Count; i++)
            {
                Assert.That(collected[i].TimeStamp, Is.EqualTo(imus[i].TimeStamp), $"TimeStamp[{i}]");
                Assert.That(collected[i].Rotation.Value.X, Is.EqualTo(imus[i].Rotation.Value.X), $"Rot.X[{i}]");
                Assert.That(collected[i].Rotation.Value.W, Is.EqualTo(imus[i].Rotation.Value.W), $"Rot.W[{i}]");
                Assert.That(collected[i].AngularVelocity.Value.Z, Is.EqualTo(imus[i].AngularVelocity.Value.Z), $"AngVel.Z[{i}]");
                Assert.That(collected[i].Confidence, Is.EqualTo(imus[i].Confidence), $"Confidence[{i}]");
            }
        }

        // ---- 2) index: seq monotonni, capture time, offset umoznuje seek ----
        [Test]
        public void Index_MatchesData_AndEnablesSeek()
        {
            var catalog = MessageCatalog.CommonDefaults();
            var imus = SyntheticImu(30);

            byte[] dataBytes, idxBytes;
            using (var dataMs = new MemoryStream())
            using (var idxMs = new MemoryStream())
            {
                var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc);
                rec.Start();
                foreach (var m in imus) rec.Post(m);
                rec.Stop();
                dataBytes = dataMs.ToArray();
                idxBytes = idxMs.ToArray();
            }

            var entries = MessageIndex.Read(new MemoryStream(idxBytes), TestHelpers.Enc);
            Assert.That(entries.Count, Is.EqualTo(imus.Count));

            for (int i = 0; i < entries.Count; i++)
            {
                Assert.That(entries[i].Seq, Is.EqualTo(i), $"Seq[{i}]");
                Assert.That(entries[i].MsgName, Is.EqualTo("IMUState"), $"MsgName[{i}]");
                Assert.That(entries[i].CaptureTime, Is.EqualTo(imus[i].TimeStamp), $"CaptureTime[{i}]");
                // T_out (ArrivalTicks) se stampuje pri prichodu; IMUState neni INamedMessage -> Name prazdne.
                Assert.That(entries[i].ArrivalTicks, Is.GreaterThan(0), $"ArrivalTicks[{i}]");
                Assert.That(entries[i].Name, Is.EqualTo(string.Empty), $"Name[{i}]");
                if (i > 0)
                    Assert.That(entries[i].Offset, Is.GreaterThan(entries[i - 1].Offset), $"Offset[{i}]");
            }

            // seek: precti zpravu primo z offsetu prostredniho zaznamu
            int k = imus.Count / 2;
            var seekMs = new MemoryStream(dataBytes) { Position = entries[k].Offset };
            var reader = new MessageReader(seekMs, TestHelpers.Enc, catalog.ToPrototypeMap());
            var msg = reader.Read() as IMUState;
            Assert.That(msg, Is.Not.Null);
            Assert.That(msg.TimeStamp, Is.EqualTo(imus[k].TimeStamp));
        }

        // Naplanovana draha (0,0)->(5,5) z parametru Profile. Kazde volani vraci novou (stavovou)
        // instanci PathResult, aby kazda smycka mela vlastni progres.
        private static IRegulator MakePath()
            => new PathPlanner(new TrapezoidMotionProfile(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed,
                                                          Profile.MaxAcceleration, Profile.Rozchod))
               .Plan(new[]
               {
                   new RegulatorWayPoint { X = 0, Y = 0 },
                   new RegulatorWayPoint { X = 5, Y = 5 },
               });

        /// <summary>
        /// Deterministicky scenar: pro kazdou IMU vlozi mereni do fuze a napumpuje scheduler
        /// jejim casem porizeni (mrizka t0 + k*ts). Ridici smycka na kazdem taktu vzorkuje
        /// engine.GetStateAt a emituje RobotStateMsg + DriveCommandMsg pres Output. Synchronni
        /// (jedno vlakno) - vysledek je reprodukovatelny bez zavislosti na planovani vlaken.
        /// </summary>
        private static void DriveScenario(AsyncFusionEngine engine,
                                          IScheduler scheduler, IMeasurementMapper mapper,
                                          IReadOnlyList<IMUState> imus, Action<IMUState>? onRaw)
        {
            foreach (var imu in imus)
            {
                onRaw?.Invoke(imu);
                foreach (var m in mapper.ToMeasurements(imu))
                    engine.Enqueue(m);
                scheduler.PumpDue(imu.TimeStamp);   // vyda takty az do casu tohoto mereni
            }
        }

        // ---- 3) golden replay regrese: RobotStateMsg emituje ridici smycka (ControlLoop) ----
        [Test]
        public void GoldenReplay_ReproducesControlLoopOutput()
        {
            var catalog = MessageCatalog.CommonDefaults();
            var imus = SyntheticImu(50);
            var ts = TimeSpan.FromMilliseconds(20);
            var mapper = new DefaultMeasurementMapper();

            // --- LIVE (record): surova IMU + odvozene RobotStateMsg/DriveCommandMsg do zaznamu ---
            byte[] dataBytes;
            using (var dataMs = new MemoryStream())
            {
                var rec = new RecordingTarget(dataMs, null, TestHelpers.Enc);
                var engine = new AsyncFusionEngine(new EKFModel());
                var scheduler = new Scheduler();
                var loop = new ControlLoop(engine, new DummyMotors(),
                                           new VirtualClock(), scheduler, period: ts) { Regulator = MakePath() };
                rec.Start();
                using (loop.Output.Connect(rec))
                {
                    DriveScenario(engine, scheduler, mapper, imus, raw => rec.Post(raw));
                }
                loop.Stop();
                rec.Stop();
                dataBytes = dataMs.ToArray();
            }

            // referencni odvozene stavy z ridici smycky
            var reference = TestHelpers.ReadMessages(dataBytes, catalog).OfType<RobotStateMsg>().ToList();
            Assert.That(reference.Count, Is.GreaterThan(0), "zadny referencni RobotStateMsg");

            // --- REPLAY: jen surova IMU ze souboru -> fresh fuze + ridici smycka -> porovnani ---
            var replayImus = new List<IMUState>();
            using (var readMs = new MemoryStream(dataBytes))
            {
                var sink = new DelegateTarget(m => { if (m is IMUState s) lock (replayImus) replayImus.Add(s); });
                sink.Start();
                var src = new FileMessageSource(readMs, TestHelpers.Enc, catalog,
                                                FileMessageSource.ReplayPacing.AsFastAsPossible);
                src.SetTypeFilter(new[] { "IMUState" });   // rez na surovych datech
                src.Connect(sink);
                src.RunToEnd();
                sink.Stop();
            }
            Assert.That(replayImus.Count, Is.EqualTo(imus.Count), "replay nenacetl vsechna surova IMU");

            var comparison = new ComparisonTarget(reference, tolerance: 1e-6);
            var engine2 = new AsyncFusionEngine(new EKFModel());
            var scheduler2 = new Scheduler();
            var loop2 = new ControlLoop(engine2, new DummyMotors(),
                                        new VirtualClock(), scheduler2, period: ts) { Regulator = MakePath() };
            comparison.Start();
            using (loop2.Output.Connect(comparison))
            {
                DriveScenario(engine2, scheduler2, mapper, replayImus, null);
            }
            loop2.Stop();
            comparison.Stop();

            Assert.That(comparison.Compared, Is.EqualTo(reference.Count), "jiny pocet taktu nez v referenci");
            Assert.That(comparison.FirstDivergence, Is.Null,
                        () => $"prvni odchylka: {comparison.FirstDivergence}");
            Assert.That(comparison.MaxStateError, Is.LessThan(1e-6),
                        () => $"MaxStateError={comparison.MaxStateError:G6}");
        }
    }
}
