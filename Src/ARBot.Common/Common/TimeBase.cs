using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Poskytuje presnejsi cas: cas startu aplikace + monotonni beh <see cref="Stopwatch"/>.
    /// Zamerne NEsleduje skoky systemovych hodin (NTP), aby razitka zprav sla monotonne za sebou.
    /// </summary>
    public static class TimeBase
    {
        private static Stopwatch sw = new Stopwatch();
        private static long start;

        static TimeBase()
        {
            start = DateTime.Now.Ticks;
            sw.Start();
        }
        /// <summary>
        /// Ziskava aktualni presnejsi cas.
        ///
        /// <para><b>Musi to byt <c>sw.Elapsed.Ticks</c>, NE <c>sw.ElapsedTicks</c></b> (bez tecky).
        /// <c>ElapsedTicks</c> vraci SUROVE tiky casovace v jednotkach <see cref="Stopwatch.Frequency"/>,
        /// kdezto <see cref="DateTime"/> pocita ve 100 ns. Na Windows to vychazi nastejno jen
        /// SHODOU OKOLNOSTI (QPC ma 10 MHz = <c>TimeSpan.TicksPerSecond</c>), takze chyba je tam
        /// neviditelna. Na Linux/ARM64 (OrangePi) je <c>Frequency</c> 1 GHz, tedy <b>100x jinak</b>,
        /// a cas aplikace bezel stonasobnou rychlosti: po 5 minutach behu byla razitka o ~8 hodin
        /// napred a periody senzoru vychazely 100x delsi (kamera 30 Hz se hlasila jako 0,3 Hz).
        /// Nalezeno na zarizeni 31. 8. 2026, viz doc/decisions.md.</para>
        /// </summary>
        public static DateTime Now => new DateTime(start + sw.Elapsed.Ticks);
    }
}
