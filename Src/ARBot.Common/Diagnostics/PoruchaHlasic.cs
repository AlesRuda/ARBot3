using System;
using System.Diagnostics;
using ARBot.Common.Common;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// <b>Škrtič hlášení poruch do <see cref="Trace"/>.</b> První výskyt pustí celý, další stejného
    /// druhu až po uplynutí periody — a řekne, kolik jich mezitím potlačil.
    ///
    /// <para><b>Nač to je.</b> Diagnostika poruch musí jít do <c>Trace</c>, ne do <c>Debug</c>:
    /// <c>Debug.WriteLine</c> je <c>[Conditional("DEBUG")]</c>, takže v Release buildu — a právě ten
    /// běží na zařízení — po poruše nezůstane <b>žádná</b> stopa (pravidlo z CLAUDE.md, past, která
    /// už dvakrát kousla). Jenže poruchová místa leží v <b>horkých cestách</b>: takt řídicí smyčky
    /// 10×/s, rámce VN100 100×/s, čtení UARTu při každém pokusu. Trvalá porucha by tedy zaplavila
    /// <c>Trace</c> i záznam, ve kterém se ta porucha hledá, a narazila by na strop
    /// <c>TraceInfoBridge.MaxPerSecond</c> (200/s) — ztratila by se právě ta <b>první</b> hláška,
    /// tedy ta nejvíc vypovídající.</para>
    ///
    /// <para><b>Klíč rozlišuje druh poruchy</b>, ne jednotlivý výskyt: jiná závada jde ven hned,
    /// i když ta první zrovna škrtí. Bez toho by první porucha zamaskovala tu, která přišla po ní.</para>
    ///
    /// <para>Čas se bere z <see cref="TimeBase"/> (ne <c>DateTime.Now</c>) — viz CLAUDE.md;
    /// v testech jde podstrčit vlastní hodiny.</para>
    /// </summary>
    public sealed class PoruchaHlasic
    {
        /// <summary>Výchozí perioda opakování hlášení téhož druhu poruchy.</summary>
        public static readonly TimeSpan VychoziPerioda = TimeSpan.FromSeconds(5);

        private readonly TimeSpan perioda;
        private readonly Func<DateTime> hodiny;
        private readonly object gate = new object();

        private string klic;
        private DateTime naposled;
        private int potlaceno;

        /// <param name="perioda">Jak často smí týž druh poruchy znovu do Trace; <c>null</c> =
        /// <see cref="VychoziPerioda"/>.</param>
        /// <param name="hodiny">Zdroj času; <c>null</c> = <see cref="TimeBase.Now"/>.</param>
        public PoruchaHlasic(TimeSpan? perioda = null, Func<DateTime> hodiny = null)
        {
            this.perioda = perioda ?? VychoziPerioda;
            this.hodiny = hodiny ?? (() => TimeBase.Now);
        }

        /// <summary>Kolik hlášení se od začátku potlačilo (diagnostika a testy).</summary>
        public long PotlacenoCelkem { get; private set; }

        /// <summary>
        /// Ohlas poruchu. <paramref name="klicPoruchy"/> rozlišuje <b>druh</b> (typicky typ výjimky
        /// nebo místo), <paramref name="text"/> je to, co se vypíše.
        /// </summary>
        public void Hlas(string klicPoruchy, string text)
        {
            lock (gate)
            {
                var ted = hodiny();
                if (klicPoruchy == klic && ted - naposled < perioda)
                {
                    potlaceno++;
                    PotlacenoCelkem++;
                    return;
                }

                string dodatek = potlaceno > 0 && klicPoruchy == klic
                               ? $" (potlaceno {potlaceno} shodnych hlaseni)" : string.Empty;
                klic = klicPoruchy;
                naposled = ted;
                potlaceno = 0;

                Trace.WriteLine(text + dodatek);
            }
        }

        /// <summary>Ohlas výjimku; druh se odvodí z jejího typu a zprávy.</summary>
        public void Hlas(string kde, Exception ex)
            => Hlas(kde + "|" + ex.GetType().FullName + "|" + ex.Message, $"{kde}: {ex}");
    }
}
