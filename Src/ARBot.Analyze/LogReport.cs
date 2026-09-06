using System;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Textový log aplikace ze záznamu</b> — zprávy <see cref="Info"/>, které do streamu posílá
    /// <c>TraceInfoBridge</c> z každého <c>Trace.WriteLine</c>.
    ///
    /// <para><b>Nač to je:</b> že je debugovací výstup součástí nahrávky, projekt tvrdí od začátku
    /// (viz doc/record-replay.md) — ale <b>nebylo čím ho přečíst</b>. Kdo chtěl vědět, co aplikace
    /// při běhu na zařízení hlásila, musel hledat v journalu, a ten po reinstalaci nebo přetečení
    /// nemusí existovat, zatímco <c>.rec</c> leží na disku. Vzniklo 6. 9. 2026 při pátrání, proč
    /// v záznamu není o T265 ani zmínka.</para>
    ///
    /// <para>Čte <b>jen index a zprávy <c>Info</c></b>, takže je to rychlé i na gigabajtovém
    /// záznamu — na rozdíl od rozborů, které musí načítat obrazy.</para>
    /// </summary>
    public static class LogReport
    {
        /// <param name="filter">Vypíše jen řádky obsahující tenhle podřetězec (bez ohledu na
        /// velikost písmen); <c>null</c> = vše.</param>
        /// <param name="limit">Nejvýš tolik řádků; 0 = bez omezení.</param>
        public static void Run(RecordFile rec, string filter = null, int limit = 0)
        {
            var entries = rec.Index.Where(e => e.MsgName == "Info").ToList();
            Console.WriteLine($"Info v indexu: {entries.Count}"
                              + (filter != null ? $", filtr \"{filter}\"" : string.Empty));
            if (entries.Count == 0)
            {
                Console.WriteLine("Zaznam neobsahuje zadny textovy log. Bud bezel s vypnutou "
                                  + "diagnostikou, nebo je z doby pred TraceInfoBridge.");
                return;
            }

            int vypsano = 0, preskoceno = 0;
            foreach (var e in entries)
            {
                if (!(rec.Read(e) is Info info)) continue;

                string text = info.Message ?? string.Empty;
                if (filter != null && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                                   && (info.Area ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    preskoceno++;
                    continue;
                }

                if (limit > 0 && vypsano >= limit) { preskoceno++; continue; }
                vypsano++;

                // Cas razitka zpravy je z TimeBase (viz TraceInfoBridge) - tedy tataz zakladna jako
                // u merenii, takze se s nimi da srovnat v case.
                string cas = info.TimeStamp == default
                    ? "        "
                    : info.TimeStamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                string uroven = string.IsNullOrEmpty(info.Level) ? string.Empty : $" [{info.Level}]";

                Console.WriteLine($"{cas}{uroven} {text}");
            }

            if (preskoceno > 0)
                Console.WriteLine($"\n({preskoceno} radku nevypsano - filtr nebo --limit)");
        }
    }
}
