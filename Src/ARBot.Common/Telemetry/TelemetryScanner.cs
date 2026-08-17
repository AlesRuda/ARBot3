using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Postavi <see cref="TelemetryTable"/> jednim pruchodem indexem zaznamu.
    ///
    /// <para>Zpravy, ktere zadny sloupec nepotrebuje, se <b>vubec necti</b> - index dava cas i
    /// offset, takze staci polozku preskocit. Tim se preskoci <c>CameraFrame</c> a <c>Blob</c>,
    /// tedy vetsina objemu zaznamu: projit cely beh znamena precist jednotky MB, ne gigabajty.
    /// Viz doc/telemetry-view.md.</para>
    ///
    /// <para>Bere <see cref="Stream"/>, ne cestu - volajici si soubor otevre sam (v UI s
    /// <c>FileShare.Read</c>, aby sken nekolidoval s prehravanim) a testy pouziji
    /// <c>MemoryStream</c>.</para>
    /// </summary>
    public static class TelemetryScanner
    {
        /// <param name="data">Datovy soubor zaznamu. Pozice streamu se behem skenu meni, takze
        /// to musi byt stream vyhrazeny skenu (ne ten, ze ktereho prave hraje replay).</param>
        /// <param name="index">Index zaznamu (sidecar <c>*.idx</c>) - bez nej nejsou offsety
        /// ani casova osa.</param>
        /// <param name="catalog">Katalog prototypu zprav; musi byt tentyz jako pro replay,
        /// jinak by sken nektere typy neprecetl.</param>
        /// <param name="columns">Registr sloupcu - urcuje i to, ktere zpravy se ctou.</param>
        /// <param name="encoding">Kodovani zaznamu (typicky UTF-8).</param>
        /// <param name="maxRows">Strop radku; po jeho dosazeni se tabulka oznaci
        /// <see cref="TelemetryTable.Truncated"/>.</param>
        /// <param name="progress">Volitelne hlaseni postupu 0..1.</param>
        /// <param name="ct">Zruseni skenu (zavreni dokumentu, otevreni jineho zaznamu).</param>
        public static TelemetryTable Scan(Stream data, IReadOnlyList<IndexEntry> index,
                                          MessageCatalog catalog, IReadOnlyList<ColumnSpec> columns,
                                          Encoding encoding, int maxRows = 500_000,
                                          IProgress<double> progress = null,
                                          CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var enc = encoding ?? Encoding.UTF8;
            var proto = catalog.ToPrototypeMap();

            // Typy, ktere ma aspon jeden sloupec - ostatni polozky indexu se preskoci bez cteni.
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in columns)
                if (!string.IsNullOrEmpty(c.MsgName)) wanted.Add(c.MsgName);

            var builder = new TelemetryTableBuilder(columns, maxRows);
            int reportEvery = Math.Max(1, index.Count / 100);

            for (int i = 0; i < index.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var e = index[i];
                if (e.MsgName == null || !wanted.Contains(e.MsgName)) continue;

                var msg = ReadFrameAt(data, e.Offset, enc, proto);
                if (msg != null) builder.Add(msg, e);

                if (builder.IsFull)
                {
                    // Strop. Truncated hlasime jen kdyz dal opravdu jeste neco zajimaveho je -
                    // zaznam koncici presne na stropu o nic neprisel.
                    if (HasWantedAfter(index, i + 1, wanted))
                        builder.MarkTruncated();
                    break;
                }

                if (progress != null && i % reportEvery == 0)
                    progress.Report((double)i / index.Count);
            }

            progress?.Report(1.0);
            return builder.Build();
        }

        /// <summary>Je za danou pozici jeste nejaka zprava, kterou by sken cetl?</summary>
        private static bool HasWantedAfter(IReadOnlyList<IndexEntry> index, int from,
                                           HashSet<string> wanted)
        {
            for (int j = from; j < index.Count; j++)
                if (index[j].MsgName != null && wanted.Contains(index[j].MsgName))
                    return true;
            return false;
        }

        /// <summary>
        /// Precte jeden ramec z daneho offsetu. Stejny postup jako
        /// <c>FileMessageSource.ReadFrameAt</c> - bufferovany sekvencni reader nelze preseekovat,
        /// takze se pro kazdy ramec zaklada docasny ctenar.
        /// </summary>
        private static Message ReadFrameAt(Stream data, long offset, Encoding enc,
                                           Dictionary<string, Message> proto)
        {
            try
            {
                data.Position = offset;
                var reader = new MessageReader(data, enc, proto);
                return reader.Read();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return null;   // poskozeny ramec sken nezastavi
            }
        }
    }
}
