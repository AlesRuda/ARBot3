using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Jeden sloupec hotove tabulky. Ulozena je hodnota a <b>cas zpravy, ze ktere hodnota je</b> -
    /// u drzene hodnoty tedy starsi cas, nez ma radek.
    ///
    /// <para>Priznak "prisla prave na tomto radku" se ZAMERNE neuklada: vyplyne ze zmeny casu
    /// (<see cref="IsFresh"/>). Setri to pole a hlavne se to nemuze s hodnotami rozejit.
    /// Znama mez: dve zpravy se stejnym casem i hodnotou se jako dve prichody nepoznaji -
    /// pro diagnostiku bezvyznamne.</para>
    /// </summary>
    public sealed class TelemetryColumn
    {
        private readonly double[] value;
        private readonly long[] ticks;

        internal TelemetryColumn(ColumnSpec spec, double[] value, long[] ticks)
        {
            Spec = spec;
            this.value = value;
            this.ticks = ticks;
        }

        /// <summary>Definice sloupce (zahlavi, format, jde-li do grafu).</summary>
        public ColumnSpec Spec { get; }

        /// <summary>
        /// Konvence, ve ktere sloupec VYDAVA uhlove hodnoty. Nastavuje ji tabulka
        /// (<see cref="TelemetryTable.AngleMode"/>) - uklada se vzdy matematicky, prepocet je az
        /// tady, takze prepnuti rezimu nesaha na data. Sloupce s <see cref="AngleKind.None"/>
        /// se netykaji.
        /// </summary>
        public AngleMode AngleMode { get; internal set; } = AngleMode.Math;

        /// <summary>Prisla uz nekdy (do tohoto radku vcetne) hodnota tohoto sloupce?</summary>
        public bool HasValue(int row) => ticks[row] != 0;

        /// <summary>Prisla hodnota PRAVE na tomto radku? Jinak se drzi z minula.</summary>
        public bool IsFresh(int row)
            => ticks[row] != 0 && (row == 0 || ticks[row] != ticks[row - 1]);

        /// <summary>
        /// Hodnota <b>k zobrazeni</b> (uhly prepoctene podle <see cref="AngleMode"/>), nebo
        /// <c>null</c> kdyz jeste nikdy neprisla. Tabulka, detail i graf ctou tudy, aby vsude
        /// platila tataz konvence.
        /// </summary>
        public double? ValueAt(int row)
            => ticks[row] == 0
                ? (double?)null
                : AnglePresentation.Present(value[row], Spec.Angle, AngleMode);

        /// <summary>Hodnota tak, jak je ULOZENA (uhly matematicky) - bez prepoctu konvence.</summary>
        public double? RawValueAt(int row) => ticks[row] == 0 ? (double?)null : value[row];

        /// <summary>Cas zpravy, ze ktere hodnota je.</summary>
        public DateTime TimeAt(int row) => new DateTime(ticks[row]);

        /// <summary>Hodnota k zobrazeni; prazdny retezec, kdyz jeste neprisla.</summary>
        public string TextAt(int row)
        {
            if (ticks[row] == 0) return string.Empty;
            double v = AnglePresentation.Present(value[row], Spec.Angle, AngleMode);
            if (Spec.Text != null) return Spec.Text(v);
            return v.ToString(Spec.Format ?? "F2", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// Telemetricka tabulka: radky razene podle casu, sloupec = jeden udaj. Radek zaklada jedna
    /// zprava ze zaznamu (drzi si jeji <see cref="RowSeq"/> pro seek), hodnoty ostatnich sloupcu
    /// se drzi z minula. Sklada ji <see cref="TelemetryTableBuilder"/>, plni
    /// <see cref="TelemetryScanner"/>. Viz doc/telemetry-view.md.
    /// </summary>
    public sealed class TelemetryTable
    {
        private readonly long[] rowTicks;
        private readonly long[] rowArrivalTicks;
        private readonly long[] rowSeq;
        private readonly string[] rowMsgName;

        internal TelemetryTable(long[] rowTicks, long[] rowArrivalTicks, long[] rowSeq,
                                string[] rowMsgName, TelemetryColumn[] columns, bool truncated)
        {
            this.rowTicks = rowTicks;
            this.rowArrivalTicks = rowArrivalTicks;
            this.rowSeq = rowSeq;
            this.rowMsgName = rowMsgName;
            Columns = columns;
            Truncated = truncated;
        }

        /// <summary>Pocet radku.</summary>
        public int RowCount => rowTicks.Length;

        /// <summary>Cas radku: T_in zakladajici zpravy, nebo T_out kdyz T_in chybi
        /// (zpravy bez <c>IHasCaptureTime</c>).</summary>
        public DateTime RowTime(int row) => new DateTime(rowTicks[row]);

        /// <summary>Cas prichodu zakladajici zpravy na Stream (T_out z indexu). Rozdil proti
        /// <see cref="RowTime"/> rika, jak dlouho mereni putovalo pipeline - viz doc/record-replay.md.</summary>
        public DateTime RowArrivalTime(int row) => new DateTime(rowArrivalTicks[row]);

        /// <summary>Poradove cislo zakladajici zpravy v zaznamu - pro seek v replay.</summary>
        public long RowSeq(int row) => rowSeq[row];

        /// <summary>Typ zpravy, ktera radek zalozila.</summary>
        public string RowMsgName(int row) => rowMsgName[row];

        /// <summary>Sloupce v poradi, v jakem byly zadany v registru.</summary>
        public IReadOnlyList<TelemetryColumn> Columns { get; }

        /// <summary>
        /// Konvence zobrazeni uhlovych udaju pro CELOU tabulku (kurzy i uhlove rychlosti naraz -
        /// jinak by pulka tabulky mluvila jinym jazykem nez druha). Meni jen zobrazeni, ne data;
        /// kdo uz si hodnoty vytahl (radky v UI, rady grafu), musi si je po zmene vzit znovu.
        /// </summary>
        public AngleMode AngleMode
        {
            get => angleMode;
            set
            {
                angleMode = value;
                foreach (var c in Columns) c.AngleMode = value;
            }
        }

        private AngleMode angleMode = AngleMode.Math;

        /// <summary>Narazilo skladani na strop radku? Tabulka pak <b>nekonci s koncem zaznamu</b>
        /// a je potreba to uzivateli rict.</summary>
        public bool Truncated { get; }
    }
}
