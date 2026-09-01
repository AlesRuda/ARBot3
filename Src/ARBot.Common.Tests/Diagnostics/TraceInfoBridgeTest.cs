using ARBot.Common.Logs;
using ARBot.Common.Tests.Runtime;   // DelegateTarget
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Most <see cref="TraceInfoBridge"/>: Debug/Trace vystup -&gt; zprava <see cref="Info"/> do proudu.
    /// Diky nemu je debugovaci vystup soucasti zaznamu a da se precist zpetne.
    /// Viz doc/record-replay.md.
    /// </summary>
    public class TraceInfoBridgeTest
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>Most se sberacem zprav; po dobehu se vzdy odregistruje z Trace.Listeners.</summary>
        private sealed class Rig : IDisposable
        {
            public readonly TraceInfoBridge Bridge;
            public readonly List<Info> Received = new List<Info>();
            private readonly DelegateTarget sink;

            public Rig(Action<Info> onReceive = null)
            {
                Bridge = new TraceInfoBridge(clock: () => T0);
                sink = new DelegateTarget(m =>
                {
                    if (!(m is Info i)) return;
                    lock (Received) Received.Add(i);
                    onReceive?.Invoke(i);
                });
                sink.Start();
                Bridge.Output.Connect(sink);
                Bridge.Start();
                Bridge.Attach();
            }

            /// <summary>Pocka, az dorazi aspon <paramref name="count"/> zprav (nebo vyprsi cas).</summary>
            public bool WaitFor(int count, int ms = 2000)
            {
                var end = Environment.TickCount64 + ms;
                while (Environment.TickCount64 < end)
                {
                    lock (Received) if (Received.Count >= count) return true;
                    Thread.Sleep(5);
                }
                lock (Received) return Received.Count >= count;
            }

            public void Dispose()
            {
                Bridge.Detach();
                Bridge.Stop();
                sink.Stop();
            }
        }

        /// <summary>
        /// INTEGRACE (20. 8. 2026): zahozeni opozdeneho merenia musi byt videt i v zaznamu.
        /// (1. 9. 2026 upresneno znenie: tenhle scenar zahazuje kvuli ZAKLADU filtru, ne kvuli
        /// oknu - buffer je po InitializePosition prazdny. Drive hlaska vinila okno i tady.)
        /// Do teto zmeny to hlasil <c>Debug.WriteLine</c>, ktery je <c>[Conditional("DEBUG")]</c> -
        /// v Release se vypustil beze stopy, a prave v Release se meri na zarizeni. U korekce
        /// z korelace s mapou (stara o celou dobu vypoctu) je to rozdil mezi "funkce jede"
        /// a "funkce nedela nic". Viz doc/map-correlation-localization.md.
        /// </summary>
        [Test]
        public void ZahozeneMerenieVeFuzi_DorazidoProudu()
        {
            using var rig = new Rig();

            var engine = new ARBot.Common.Fusion.AsyncFusionEngine(new ARBot.Common.Fusion.EKFModel());
            engine.InitializePosition(0, 0, 1.0, T0.AddSeconds(2));   // tBase = T0 + 2 s

            engine.Enqueue(new ARBot.Common.Fusion.HeadingMeasurement(0.5, 0.1, T0, "MapCorr"));

            Assert.That(rig.WaitFor(1), Is.True, "zahozeni se neobjevilo v proudu");
            lock (rig.Received)
            {
                string msg = rig.Received[0].Message;
                Assert.Multiple(() =>
                {
                    // Buffer je po InitializePosition prazdny, takze duvodem je ZAKLAD filtru,
                    // ne okno - viz DroppedTooOldReasonTests.
                    Assert.That(msg, Does.Contain("starsi nez zaklad filtru"));
                    Assert.That(msg, Does.Contain("MapCorr"),
                                "ze zpravy musi byt poznat ZDROJ, jinak nepozna, co se zahazuje");
                    Assert.That(msg, Does.Contain(nameof(ARBot.Common.Fusion.HeadingMeasurement)),
                                "a taky TYP merenia - u korelace jde o tri rozdilna (osa, osa, kurz)");
                    Assert.That(msg, Does.Contain("opozdeno"),
                                "nejakcnejsi cislo je, O KOLIK bylo pozde - z toho se pozna, "
                                + "jestli pomoct zvetsenim okna nebo zrychlenim vypoctu");
                    Assert.That(msg, Does.Contain("okno"), "a proti cemu se to merilo");
                    Assert.That(engine.DroppedTooOld, Is.EqualTo(1), "a soucasne se to pocita");
                });
            }
        }

        [Test]
        public void DebugWriteLine_ProjdeDoProudu()
        {
            using var rig = new Rig();

            Trace.WriteLine("Occupancy[Left] touched=9517");

            Assert.That(rig.WaitFor(1), Is.True, "zprava se neobjevila v proudu");
            lock (rig.Received)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(rig.Received[0].Message, Is.EqualTo("Occupancy[Left] touched=9517"));
                    Assert.That(rig.Received[0].TimeStamp, Is.EqualTo(T0));
                    Assert.That(rig.Received[0].Area, Is.EqualTo(TraceInfoBridge.DefaultArea));
                    Assert.That(rig.Received[0].Level, Is.EqualTo(TraceInfoBridge.DefaultLevel));
                });
            }
        }

        [Test]
        public void TraceLogContext_DoplniOblastAUroven()
        {
            using var rig = new Rig();

            using (TraceLogContext.Scope("Avalonia:Layout", "Warning"))
                Trace.WriteLine("VectorStyleRenderer can not render feature");

            Assert.That(rig.WaitFor(1), Is.True);
            lock (rig.Received)
            {
                Assert.That(rig.Received[0].Area, Is.EqualTo("Avalonia:Layout"));
                Assert.That(rig.Received[0].Level, Is.EqualTo("Warning"));
            }
        }

        /// <summary>Po opusteni bloku uz kontext neplati (jinak by "obarvil" cizi radky).</summary>
        [Test]
        public void TraceLogContext_PoBlokuNeplati()
        {
            using var rig = new Rig();

            using (TraceLogContext.Scope("Avalonia", "Warning")) { }
            Trace.WriteLine("nas radek");

            Assert.That(rig.WaitFor(1), Is.True);
            lock (rig.Received)
                Assert.That(rig.Received[0].Area, Is.EqualTo(TraceInfoBridge.DefaultArea));
        }

        /// <summary>
        /// KLICOVE: odberatele proudu (zaznam, UI) samy loguji, a to z VLASTNIHO vlakna - vznika
        /// smycka log -&gt; Info -&gt; odberatel -&gt; log, na kterou thread-static ochrana nedosahne.
        /// Utne ji az rychlostni strop. Bez nej test vyrobil pres 24 000 zprav za 200 ms.
        /// </summary>
        [Test]
        public void Odberatel_KteryLoguje_SeUtneNaStropu()
        {
            using var rig = new Rig(onReceive: _ => Trace.WriteLine("odberatel taky loguje"));
            rig.Bridge.MaxPerSecond = 50;

            Trace.WriteLine("prvni");

            Assert.That(rig.WaitFor(1), Is.True, "ani prvni zprava neprosla");
            Thread.Sleep(300);   // kdyby strop nedrzel, za tu dobu to vybuchne

            lock (rig.Received)
                Assert.That(rig.Received.Count, Is.LessThan(200),
                            $"smycka neni ohranicena - prislo {rig.Received.Count} zprav");
        }

        /// <summary>Pri prekroceni stropu se ztrata nezamlci - v dalsim okne prijde souhrn.</summary>
        [Test]
        public void PriPrekroceniStropu_PrijdeSouhrnZahozenych()
        {
            using var rig = new Rig();
            rig.Bridge.MaxPerSecond = 10;

            for (int i = 0; i < 50; i++) Trace.WriteLine($"radek {i}");
            Thread.Sleep(1100);              // pockat na dalsi okno
            Trace.WriteLine("po okne");

            Assert.That(rig.WaitFor(11), Is.True);
            lock (rig.Received)
                Assert.That(rig.Received.Exists(i => i.Message.Contains("zahozeno")
                                                     && i.Level == "Warning"),
                            Is.True, "chybi souhrnny radek o zahozenych");
        }

        /// <summary>Po Detach uz se nic nesbira (jinak by most zil dal po zastaveni runtime).</summary>
        [Test]
        public void PoDetach_SeNesbira()
        {
            using var rig = new Rig();

            Trace.WriteLine("pred");
            Assert.That(rig.WaitFor(1), Is.True);

            rig.Bridge.Detach();
            Trace.WriteLine("po");
            Thread.Sleep(150);

            lock (rig.Received)
                Assert.That(rig.Received.Count, Is.EqualTo(1), "po Detach se sbiralo dal");
        }
    }
}

