using System;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Zkoumá, KDY přesně fúze měření zahodí jako „příliš staré" — a jestli tomu odpovídá hláška.
    ///
    /// <para>Vzniklo z pozorování na zařízení (1. 9. 2026): v logu se objevilo
    /// <c>zahozeno mereni starsi nez okno historie: 'Odo/speed' … opozdeno o 7 ms za nejnovejsim,
    /// okno je 3000 ms</c>. Zahodit měření zpožděné o 7 ms při třísekundovém okně vypadá jako
    /// nesmysl, takže je potřeba oddělit dvě různé věci: <b>okno historie</b> (jak hluboko do
    /// minulosti se umí filtr přepočítat) a <b>základ filtru <c>tBase</c></b> (stav, před který
    /// se jít nedá).</para>
    /// </summary>
    [TestFixture]
    public class DroppedTooOldReasonTests
    {
        private static readonly DateTime T0 = new DateTime(2024, 1, 1, 0, 0, 0);

        /// <summary>
        /// Ustálený běh: buffer je naplněný přes celé okno. Měření zpožděné o pár ms za
        /// nejnovějším MÁ projít — je hluboko uvnitř okna.
        /// </summary>
        [Test]
        public void UstalenyBeh_MereniZpozdeneOParMs_NeniZahozeno()
        {
            var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(3));

            // 5 s provozu po 10 ms -> buffer pokrývá celé okno
            for (int i = 0; i < 500; i++)
                e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(i * 0.01), "Odo/speed"));

            DateTime newest = e.FilterTime;
            int pred = e.BufferedCount;

            // měření o 7 ms starší než nejnovější - přesně případ z logu
            e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, newest.AddMilliseconds(-7), "Odo/speed"));

            Assert.That(e.BufferedCount, Is.EqualTo(pred + 1),
                "Měření 7 ms za nejnovějším musí v ustáleném běhu projít - je hluboko uvnitř okna.");
            Assert.That(e.DroppedTooOld, Is.Zero, "V ustáleném běhu se nemá zahazovat nic.");
        }

        /// <summary>
        /// Skutečná příčina zahození: měření starší než <c>tBase</c>. Po
        /// <c>InitializePosition</c> je <c>tBase</c> nastavené na čas inicializace a buffer
        /// prázdný, takže i měření o pár ms starší propadne — **bez ohledu na velikost okna**.
        /// </summary>
        [Test]
        public void PoInicializaci_MereniOParMsStarsi_JeZahozeno_ByloliOknoJakkoliVelke()
        {
            foreach (double oknoS in new[] { 3.0, 60.0 })
            {
                var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(oknoS));
                DateTime tInit = T0.AddSeconds(10);

                e.InitializePosition(0, 0, 1.0, tInit);
                e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, tInit.AddMilliseconds(-7), "Odo/speed"));

                Assert.That(e.DroppedTooOld, Is.EqualTo(1),
                    $"Okno {oknoS} s: měření 7 ms před tBase se zahodí bez ohledu na velikost okna " +
                    "- rozhoduje tBase, ne okno.");
            }
        }

        /// <summary>
        /// Hláška musí pojmenovat SKUTEČNÝ důvod. Po inicializaci je historie kratší než okno,
        /// takže okno za zahození nemůže — a hláška to musí říct, ne vinit okno.
        /// </summary>
        [Test]
        public void Hlaska_PoInicializaci_NeviniOkno()
        {
            string zprava = Zachyt(() =>
            {
                var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(3));
                e.InitializePosition(0, 0, 1.0, T0.AddSeconds(10));
                e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05,
                          T0.AddSeconds(10).AddMilliseconds(-7), "Odo/speed"));
            });

            Assert.That(zprava, Does.Contain("starsi nez zaklad filtru"));
            Assert.That(zprava, Does.Contain("OKNO ZA TO NEMUZE"));
            Assert.That(zprava, Does.Not.Contain("starsi nez okno historie"),
                "Okno v tuhle chvíli o zahození nerozhoduje - hláška ho nesmí uvádět jako důvod.");

            // Měření tu NENÍ opožděné: dorazilo v pořádku, jen základ filtru byl postaven
            // až za ním. Slovo "opozdeno" by poslalo hledat chybu do doručování měření.
            Assert.That(zprava, Does.Not.Contain("opozdeno"),
                "Měření nedošlo pozdě - je jen starší než základ filtru.");
            Assert.That(zprava, Does.Contain("NEMUSELO byt opozdene"));
        }

        /// <summary>
        /// Naopak když historie opravdu pokrývá celé okno, je „starší než okno historie"
        /// správné pojmenování — a hláška ho použít má.
        /// </summary>
        [Test]
        public void Hlaska_PriPlnemOkne_ViniOknoSpravne()
        {
            string zprava = Zachyt(() =>
            {
                var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(0.2));
                for (int i = 0; i < 200; i++)      // 2 s provozu -> okno 0,2 s je plné
                    e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddSeconds(i * 0.01), "Odo/speed"));

                // hluboko v minulosti, tedy skutečně mimo okno
                e.Enqueue(ScalarStateMeasurement.Velocity(9.0, 0.05, T0.AddSeconds(0.05), "Odo/speed"));
            });

            Assert.That(zprava, Does.Contain("starsi nez okno historie"));
            Assert.That(zprava, Does.Contain("je plne"));
        }

        /// <summary>Posbírá, co kód pošle do <see cref="System.Diagnostics.Trace"/>.</summary>
        private static string Zachyt(Action akce)
        {
            var sb = new System.Text.StringBuilder();
            var listener = new System.Diagnostics.TextWriterTraceListener(new System.IO.StringWriter(sb));
            System.Diagnostics.Trace.Listeners.Add(listener);
            try
            {
                akce();
                System.Diagnostics.Trace.Flush();
            }
            finally
            {
                System.Diagnostics.Trace.Listeners.Remove(listener);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Doklad, že velikost okna na tohle zahození nemá vliv: tytéž vstupy, okna 3 s a 60 s,
        /// stejný výsledek. Hláška, která u takového zahození uvádí „okno je 3000 ms", tedy
        /// ukazuje na veličinu, která s rozhodnutím nemá co dělat.
        /// </summary>
        [Test]
        public void VelikostOkna_NaTohleZahozeniNemaVliv()
        {
            static long Zahozeno(double oknoS)
            {
                var e = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(oknoS));
                e.InitializePosition(0, 0, 1.0, T0.AddSeconds(10));
                for (int i = 0; i < 5; i++)
                    e.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05,
                              T0.AddSeconds(10).AddMilliseconds(-10 + i), "Odo/speed"));
                return e.DroppedTooOld;
            }

            Assert.That(Zahozeno(3.0), Is.EqualTo(Zahozeno(60.0)),
                "Zahození před tBase nezávisí na okně - hláška ho zmiňovat nemá jako důvod.");
        }
    }
}
