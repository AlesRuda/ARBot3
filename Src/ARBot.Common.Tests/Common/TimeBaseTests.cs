using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Common;

namespace ARBot.Common.Tests.Common
{
    /// <summary>
    /// Hlídá, že <see cref="TimeBase"/> běží <b>reálnou rychlostí</b>.
    ///
    /// <para>Regrese, kterou to chytá (nalezená na zařízení 31. 8. 2026): <c>TimeBase.Now</c>
    /// sčítalo <c>Stopwatch.ElapsedTicks</c>, což jsou surové tiky v jednotkách
    /// <see cref="Stopwatch.Frequency"/>, ne 100ns tiky <see cref="DateTime"/>. Na Windows
    /// je QPC shodou okolností 10 MHz, takže to sedělo; na Linux/ARM64 je Frequency 1 GHz,
    /// takže čas aplikace běžel <b>100× rychleji</b> — periody senzorů vycházely 100× delší
    /// (kamera 30 Hz se hlásila jako 0,3 Hz) a razítka po pár minutách ujela o hodiny.</para>
    ///
    /// <para>Test schválně <b>neporovnává s <c>DateTime.Now</c></b>: TimeBase záměrně nesleduje
    /// skoky systémových hodin. Porovnává se s nezávislým <see cref="Stopwatch"/>em, tedy
    /// s reálně uplynulým časem. Meze jsou volné (±100 %), aby test nebyl citlivý na plánovač —
    /// chyba v jednotkách je 100×, takže se schová i tak.</para>
    /// </summary>
    public class TimeBaseTests
    {
        [Test]
        public void Now_PostupujeRealnouRychlosti()
        {
            // rozehřátí: první dotyk spustí statický konstruktor (nesmí se počítat do měření)
            _ = TimeBase.Now;

            // Obě hodiny se čtou TĚSNĚ ZA SEBOU a porovnává se stejné okno; kdyby se čtení
            // rozdělila (start Stopwatche zvlášť od prvního TimeBase.Now), spadla by do mezery
            // pauza plánovače nebo GC a test by byl náhodně červený - což se 31. 8. 2026 stalo.
            // Navíc se měří pětkrát a bere se MEDIÁN, takže jeden zádrhel výsledek nerozhodí.
            var pomery = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                DateTime t0 = TimeBase.Now;
                double m0 = sw.Elapsed.TotalMilliseconds;
                Thread.Sleep(120);
                DateTime t1 = TimeBase.Now;
                double m1 = sw.Elapsed.TotalMilliseconds;

                double skutecneMs = m1 - m0;
                if (skutecneMs > 0)
                    pomery.Add((t1 - t0).TotalMilliseconds / skutecneMs);
            }

            Assert.That(pomery, Is.Not.Empty, "Stopwatch nenaměřil žádný čas.");
            pomery.Sort();
            double pomer = pomery[pomery.Count / 2];

            // Meze jsou schválně velmi volné (±100 %) - hlídaná chyba je 100x, takže se
            // neschová, a test přitom nezčervená kvůli zatížení stroje.
            Assert.That(pomer, Is.InRange(0.5, 2.0),
                $"TimeBase postupuje {pomer:F1}x rychleji/pomaleji než skutečný čas " +
                $"(medián z {pomery.Count} měření: {string.Join(", ", pomery.ConvertAll(x => x.ToString("F2")))}). " +
                $"Stopwatch.Frequency={Stopwatch.Frequency:N0}, TimeSpan.TicksPerSecond={TimeSpan.TicksPerSecond:N0}. " +
                "Typická příčina: sw.ElapsedTicks místo sw.Elapsed.Ticks.");
        }

        /// <summary>
        /// Druhá pojistka, nezávislá na čase: kdyby se jednotky rozešly, projeví se to i tady.
        /// Na Windows je poměr 1 (QPC 10 MHz), na Linux/ARM64 100 — a právě proto se surové tiky
        /// nesmí sčítat s tiky <see cref="DateTime"/>.
        /// </summary>
        [Test]
        public void StopwatchElapsedTicks_NejsouTikyTimeSpanu_KdyzSeFrekvenceLisi()
        {
            if (Stopwatch.Frequency == TimeSpan.TicksPerSecond)
                Assert.Pass($"Na této platformě jsou jednotky shodné ({Stopwatch.Frequency:N0} Hz) — " +
                            "záměna by tu byla neviditelná, hlídá ji test Now_PostupujeRealnouRychlosti.");

            var sw = Stopwatch.StartNew();
            Thread.Sleep(50);
            sw.Stop();

            Assert.That(sw.ElapsedTicks, Is.Not.EqualTo(sw.Elapsed.Ticks),
                "Na této platformě se surové tiky Stopwatch liší od tiků TimeSpan — " +
                "kód je nesmí zaměňovat.");
        }
    }
}
