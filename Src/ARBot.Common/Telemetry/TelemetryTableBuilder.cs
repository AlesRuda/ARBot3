using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Sklada <see cref="TelemetryTable"/> ze zprav v poradi, v jakem jsou v zaznamu.
    ///
    /// <para>Pravidla (doc/telemetry-view.md): <b>radek = jedna prijata zprava</b>; zpravy se
    /// SHODNYM casem radku se slevaji do jednoho radku (tentyz takt ridici smycky nema byt na dvou
    /// radcich); neaktualizovane sloupce si nesou hodnotu i cas z minula (drzeni).</para>
    /// </summary>
    public sealed class TelemetryTableBuilder
    {
        private readonly ColumnSpec[] specs;
        private readonly List<double>[] values;
        private readonly List<long>[] ticks;
        private readonly List<long> rowTicks = new List<long>();
        private readonly List<long> rowArrivalTicks = new List<long>();
        private readonly List<long> rowSeq = new List<long>();
        private readonly List<string> rowMsgName = new List<string>();
        private readonly int maxRows;
        private bool truncated;

        /// <param name="columns">Registr sloupcu; poradi urcuje poradi sloupcu v tabulce.</param>
        /// <param name="maxRows">Strop radku (ochrana pameti u dlouhych zaznamu).</param>
        public TelemetryTableBuilder(IReadOnlyList<ColumnSpec> columns, int maxRows = 500_000)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));
            this.maxRows = maxRows > 0 ? maxRows : 1;

            specs = new ColumnSpec[columns.Count];
            values = new List<double>[columns.Count];
            ticks = new List<long>[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                specs[i] = columns[i];
                values[i] = new List<double>();
                ticks[i] = new List<long>();
            }
        }

        /// <summary>Je dosazen strop radku? Dalsi <see cref="Add"/> uz novy radek nezalozi.</summary>
        public bool IsFull => rowTicks.Count >= maxRows;

        /// <summary>
        /// Zaradi zpravu. Vola se v poradi <see cref="IndexEntry.Seq"/> (tedy jak zpravy lezi
        /// v zaznamu).
        /// </summary>
        /// <param name="msg">Prectena zprava.</param>
        /// <param name="entry">Jeji zaznam v indexu (casy a Seq).</param>
        public void Add(Message msg, in IndexEntry entry)
        {
            if (msg == null) return;

            // Cas radku: T_in, a kdyz zprava vlastni cas nenese, T_out z indexu.
            long t = entry.CaptureTicks != 0 ? entry.CaptureTicks : entry.ArrivalTicks;
            int row;

            if (rowTicks.Count > 0 && rowTicks[rowTicks.Count - 1] == t)
            {
                row = rowTicks.Count - 1;      // tentyz takt -> doplnit stavajici radek
            }
            else
            {
                if (IsFull) { truncated = true; return; }

                row = rowTicks.Count;
                rowTicks.Add(t);
                rowArrivalTicks.Add(entry.ArrivalTicks);
                rowSeq.Add(entry.Seq);
                rowMsgName.Add(msg.MsgName);

                // Novy radek zdedi vsechny hodnoty i jejich casy = drzeni z minula. Prvni radek
                // zacina prazdny (cas 0 = "jeste nikdy neprislo").
                for (int c = 0; c < specs.Length; c++)
                {
                    values[c].Add(row == 0 ? 0.0 : values[c][row - 1]);
                    ticks[c].Add(row == 0 ? 0L : ticks[c][row - 1]);
                }
            }

            // Sloupce, ktere tato zprava plni, prepsat na novou hodnotu a jeji cas.
            string name = (msg is INamedMessage nm) ? (nm.Name ?? string.Empty) : string.Empty;
            for (int c = 0; c < specs.Length; c++)
            {
                var spec = specs[c];
                if (!string.Equals(spec.MsgName, msg.MsgName, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(spec.Name)
                    && !string.Equals(spec.Name, name, StringComparison.Ordinal)) continue;
                if (spec.Value == null) continue;

                var v = spec.Value(msg);
                if (!v.HasValue) continue;      // tato zprava sloupec neplni

                values[c][row] = v.Value;
                ticks[c][row] = t;
            }
        }

        /// <summary>
        /// Oznaci tabulku jako useknutou. Vola to sken, kdyz kvuli stropu prestal cist, i kdyz
        /// posledni pokus o pridani uz neprobehl (jinak by se o useknuti nevedelo).
        /// </summary>
        public void MarkTruncated() => truncated = true;

        /// <summary>Uzavre skladani a vyrobi hotovou tabulku (kopie do polí).</summary>
        public TelemetryTable Build()
        {
            var cols = new TelemetryColumn[specs.Length];
            for (int c = 0; c < specs.Length; c++)
                cols[c] = new TelemetryColumn(specs[c], values[c].ToArray(), ticks[c].ToArray());

            return new TelemetryTable(rowTicks.ToArray(), rowArrivalTicks.ToArray(), rowSeq.ToArray(),
                                      rowMsgName.ToArray(), cols, truncated);
        }
    }
}
