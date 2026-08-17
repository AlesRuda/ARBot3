using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Jeden udaj vytazeny z tabulky jako <b>rada pro graf</b>: dvojice (cas, hodnota) v poradi
    /// casu.
    ///
    /// <para>Berou se jen <b>skutecne prichody</b> (bunky <see cref="TelemetryColumn.IsFresh"/>),
    /// ne drzene hodnoty - drzena hodnota je jen opakovani te predchozi a v grafu by z ni byla
    /// hustsi rada bez jedine nove informace. Ze samotnych prichodu jde nakreslit obojí: schod
    /// (hodnota plati az do dalsiho prichodu) i rampa (mezi prichody se interpoluje).</para>
    ///
    /// <para>Viz doc/telemetry-view.md, sekce Faze 2 - grafy.</para>
    /// </summary>
    public sealed class TelemetrySeries
    {
        private readonly long[] ticks;
        private readonly double[] values;

        private TelemetrySeries(ColumnSpec spec, long[] ticks, double[] values,
                                double min, double max)
        {
            Spec = spec;
            this.ticks = ticks;
            this.values = values;
            Min = min;
            Max = max;
        }

        /// <summary>Definice sloupce, ze ktereho rada je (zahlavi, format, popis).</summary>
        public ColumnSpec Spec { get; }

        /// <summary>Pocet bodu rady.</summary>
        public int Count => ticks.Length;

        /// <summary>Nejmensi hodnota v rade (0 u prazdne rady).</summary>
        public double Min { get; }

        /// <summary>Nejvetsi hodnota v rade (0 u prazdne rady).</summary>
        public double Max { get; }

        /// <summary>Cas bodu v tickach.</summary>
        public long TicksAt(int i) => ticks[i];

        /// <summary>Hodnota bodu.</summary>
        public double ValueAt(int i) => values[i];

        /// <summary>Cas prvniho bodu (0 u prazdne rady).</summary>
        public long FirstTicks => ticks.Length > 0 ? ticks[0] : 0L;

        /// <summary>Cas posledniho bodu (0 u prazdne rady).</summary>
        public long LastTicks => ticks.Length > 0 ? ticks[ticks.Length - 1] : 0L;

        /// <summary>
        /// Vytahne radu jednoho sloupce z tabulky. Sloupec musi patrit te tabulce (bere se z
        /// <see cref="TelemetryTable.Columns"/>).
        /// </summary>
        public static TelemetrySeries From(TelemetryTable table, TelemetryColumn column)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (column == null) throw new ArgumentNullException(nameof(column));

            var t = new List<long>();
            var v = new List<double>();
            double min = double.MaxValue, max = double.MinValue;

            for (int r = 0; r < table.RowCount; r++)
            {
                if (!column.IsFresh(r)) continue;      // drzena hodnota neni novy bod

                double value = column.ValueAt(r) ?? 0.0;
                t.Add(column.TimeAt(r).Ticks);
                v.Add(value);

                if (value < min) min = value;
                if (value > max) max = value;
            }

            if (t.Count == 0) { min = 0; max = 0; }

            var ticksArray = t.ToArray();
            var valuesArray = v.ToArray();

            // Radky tabulky jdou v poradi ZAZNAMU (Seq), ale cas radku je cas PORIZENI (T_in) -
            // a ten nemusi byt rostouci: kazda zprava putuje pipeline jinak dlouho a nektere nesou
            // cas svych vstupnich dat. V realnem zaznamu se to opravdu deje (dva sousedni
            // LocalPlanMsg s klesajicim T_in). Rada je osa X grafu, takze musi byt setridena -
            // jinak by krivka delala klikyhaky a puleni v ValueAtTime by vracelo nesmysly.
            if (!IsSorted(ticksArray))
                Array.Sort(ticksArray, valuesArray);

            return new TelemetrySeries(column.Spec, ticksArray, valuesArray, min, max);
        }

        /// <summary>Jsou casy neklesajici? (Bezny pripad - tridit se pak nemusi.)</summary>
        private static bool IsSorted(long[] ticks)
        {
            for (int i = 1; i < ticks.Length; i++)
                if (ticks[i] < ticks[i - 1]) return false;
            return true;
        }

        /// <summary>
        /// Hodnota rady v danem case jako <b>schod</b>: posledni prichod &le; <paramref name="atTicks"/>.
        /// <c>null</c>, dokud prvni prichod nenastal. Pouziva to kurzor prehrávání v grafu (a je to
        /// tataz semantika jako drzeni hodnot v tabulce).
        /// </summary>
        public double? ValueAtTime(long atTicks)
        {
            if (ticks.Length == 0 || ticks[0] > atTicks) return null;

            int lo = 0, hi = ticks.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (ticks[mid] <= atTicks) lo = mid;
                else hi = mid - 1;
            }
            return values[lo];
        }

        /// <summary>
        /// Hodnota rady v danem case <b>s interpolaci</b> mezi sousednimi prichody (rampa).
        /// Mimo rozsah rady vraci krajni hodnotu; <c>null</c> u prazdne rady.
        /// <para>Pouziva to odectitko hodnot pod mysi u rad kreslenych jako rampa - u schodu
        /// plati <see cref="ValueAtTime"/>.</para>
        /// </summary>
        public double? InterpolatedAt(long atTicks)
        {
            if (ticks.Length == 0) return null;
            if (atTicks <= ticks[0]) return values[0];
            if (atTicks >= ticks[ticks.Length - 1]) return values[values.Length - 1];

            int lo = 0, hi = ticks.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (ticks[mid] <= atTicks) lo = mid;
                else hi = mid - 1;
            }

            long t0 = ticks[lo], t1 = ticks[lo + 1];
            if (t1 == t0) return values[lo];

            double k = (double)(atTicks - t0) / (t1 - t0);
            return values[lo] + (values[lo + 1] - values[lo]) * k;
        }

        /// <summary>Hodnota jako text podle definice sloupce (vycet jmenem, jinak <c>Format</c>).</summary>
        public string TextOf(double value)
            => Spec?.Text != null
                ? Spec.Text(value)
                : value.ToString(Spec?.Format ?? "F2", CultureInfo.CurrentCulture);
    }
}
