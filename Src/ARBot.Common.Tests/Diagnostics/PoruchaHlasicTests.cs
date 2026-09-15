using System;
using System.Collections.Generic;
using System.Diagnostics;
using ARBot.Common.Diagnostics;
using NUnit.Framework;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Testy škrtiče hlášení poruch (<see cref="PoruchaHlasic"/>).
    ///
    /// <para><b>Nač to je:</b> diagnostika poruch musí jít do <c>Trace</c> (v Release je to jediná
    /// stopa — viz CLAUDE.md), jenže poruchová místa jsou v <b>horkých cestách</b>: takt řídicí
    /// smyčky 10×/s, rámce IMU 100×/s, čtení UARTu při každém pokusu. Trvalá porucha by bez
    /// škrcení zaplavila <c>Trace</c> i záznam, ve kterém se ta porucha hledá, a narazila by na
    /// strop <c>TraceInfoBridge.MaxPerSecond</c> — takže by se ztratila právě ta první, nejvíc
    /// vypovídající hláška.</para>
    /// </summary>
    [NonParallelizable]
    public class PoruchaHlasicTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);

        /// <summary>Posluchač Trace, který si hlášky schová.</summary>
        private sealed class Odposlech : TraceListener
        {
            public readonly List<string> Radky = new List<string>();
            public override void Write(string message) { }
            public override void WriteLine(string message) { Radky.Add(message); }
        }

        private static (PoruchaHlasic h, Odposlech o) Rig(Func<DateTime> hodiny)
        {
            var o = new Odposlech();
            Trace.Listeners.Add(o);
            return (new PoruchaHlasic(TimeSpan.FromSeconds(5), hodiny), o);
        }

        private static void Uklid(Odposlech o) => Trace.Listeners.Remove(o);

        [Test]
        public void PrvniVyskyt_JdeVenCely()
        {
            var cas = T0;
            var (h, o) = Rig(() => cas);
            try
            {
                h.Hlas("port", "Uart (COM3): port nejde otevrit");

                Assert.That(o.Radky, Has.Count.EqualTo(1));
                Assert.That(o.Radky[0], Is.EqualTo("Uart (COM3): port nejde otevrit"));
            }
            finally { Uklid(o); }
        }

        [Test]
        public void OpakovaniTehozKlice_SeSkrti()
        {
            var cas = T0;
            var (h, o) = Rig(() => cas);
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    h.Hlas("port", "porucha portu");
                    cas = cas.AddMilliseconds(10);   // celkem 1 s, tedy pod periodou
                }

                Assert.That(o.Radky, Has.Count.EqualTo(1), "trvala porucha nesmi zaplavit Trace");
            }
            finally { Uklid(o); }
        }

        [Test]
        public void PoUplynutiPeriody_SeHlasiZnovuIsPoctemPotlacenych()
        {
            var cas = T0;
            var (h, o) = Rig(() => cas);
            try
            {
                h.Hlas("port", "porucha portu");
                for (int i = 0; i < 9; i++)
                {
                    cas = cas.AddMilliseconds(100);
                    h.Hlas("port", "porucha portu");
                }

                cas = cas.AddSeconds(6);
                h.Hlas("port", "porucha portu");

                Assert.That(o.Radky, Has.Count.EqualTo(2));
                Assert.That(o.Radky[1], Does.Contain("potlaceno 9"),
                            "bez poctu potlacenych nejde poznat, jestli je porucha trvala nebo ojedinela");
            }
            finally { Uklid(o); }
        }

        /// <summary>
        /// ⚠️ <b>Jiná porucha není totéž</b> — druhý druh závady musí jít ven hned, i když první
        /// zrovna škrtí. Jinak by první porucha zamaskovala tu, která přišla po ní.
        /// </summary>
        [Test]
        public void JinyKlic_JdeVenHned()
        {
            var cas = T0;
            var (h, o) = Rig(() => cas);
            try
            {
                h.Hlas("port", "porucha portu");
                h.Hlas("crc", "chybne CRC");

                Assert.That(o.Radky, Has.Count.EqualTo(2));
            }
            finally { Uklid(o); }
        }
    }
}
