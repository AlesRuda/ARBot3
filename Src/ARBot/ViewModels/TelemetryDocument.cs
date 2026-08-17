using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Communication;
using ARBot.Common.Telemetry;
using ARBot.Robot;
using ARBot.Telemetry;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Telemetricky pohled: stav robotu, ridici zasahy a udaje z dalsich zprav v JEDNE tabulce
    /// razene podle casu (radek = jedna zprava ze zaznamu, sloupec = jeden udaj, tucne = hodnota
    /// prave prisla). Data vznikaji jednim skenem indexu zaznamu pri otevreni dokumentu.
    ///
    /// <para>Rezim <b>View</b> (nad hotovym zaznamem); v Run se tabulka neplni - viz
    /// doc/telemetry-view.md, sekce Rozsah faze 1.</para>
    /// </summary>
    public partial class TelemetryDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.TelemetryDocumentView);

        /// <summary>Sloupce (z registru) - View podle nich staví sloupce tabulky.</summary>
        public IReadOnlyList<ColumnSpec> Columns => TelemetryColumns.All;

        /// <summary>Radky tabulky; plni se najednou po dokonceni skenu.</summary>
        public ObservableCollection<TelemetryRow> Rows { get; } = new ObservableCollection<TelemetryRow>();

        [ObservableProperty] private string status = "-";
        [ObservableProperty] private double progress;
        [ObservableProperty] private bool isScanning;

        /// <summary>Vybrany radek - plni panel detailu.</summary>
        [ObservableProperty] private TelemetryRow selectedRow;

        /// <summary>Zahlavi detailu (zakladajici zprava, oba casy).</summary>
        [ObservableProperty] private string detailHeader = "Vyber řádek";

        /// <summary>Rozpis hodnot vybraneho radku vcetne jejich stari.</summary>
        public ObservableCollection<TelemetryDetailLine> Detail { get; }
            = new ObservableCollection<TelemetryDetailLine>();

        private CancellationTokenSource cts;

        public TelemetryDocument()
        {
            Id = "Telemetry";
            Title = "Telemetrie";

            // Navrhar nesmi sahnout na runtime ani na soubory (viz Views/README.md).
            if (Design.IsDesignMode)
            {
                Status = "(návrhový režim)";
                return;
            }

            StartScan();
        }

        /// <summary>Spusti sken zaznamu na vlakne mimo UI.</summary>
        private void StartScan()
        {
            var runtime = ARBotRuntime.Current;
            string path = runtime?.RecordPath;
            if (string.IsNullOrEmpty(path))
            {
                Status = "Není otevřený záznam — Runtime → View…";
                return;
            }

            var index = runtime.FileSource?.Index;
            if (index == null || index.Count == 0)
            {
                Status = "Záznam nemá sidecar index (*.idx) — tabulku z něj postavit nelze.";
                return;
            }

            cts = new CancellationTokenSource();
            var ct = cts.Token;
            var progressReport = new Progress<double>(p => Progress = p);
            var columns = TelemetryColumns.All;
            var catalog = ARBotRuntime.BuildCatalog();

            IsScanning = true;
            Status = "Načítám záznam…";

            Task.Run(() =>
            {
                // Vlastni read-only stream: zaznam je otevreny s FileShare.Read, takze sken
                // nekoliduje s prehravanim (to ma svuj vlastni stream a svou pozici).
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return TelemetryScanner.Scan(fs, index, catalog, columns, Encoding.UTF8,
                                             progress: progressReport, ct: ct);
            }, ct).ContinueWith(t => Dispatcher.UIThread.Post(() => Apply(t)));
        }

        /// <summary>Prevezme vysledek skenu (uz na UI vlakne).</summary>
        private void Apply(Task<TelemetryTable> task)
        {
            IsScanning = false;

            if (task.IsCanceled)
            {
                Status = "Načítání zrušeno.";
                return;
            }
            if (task.IsFaulted)
            {
                Status = "Chyba při čtení záznamu: " + task.Exception?.GetBaseException().Message;
                System.Diagnostics.Debug.WriteLine(task.Exception);
                return;
            }

            var table = task.Result;
            Rows.Clear();
            for (int r = 0; r < table.RowCount; r++)
                Rows.Add(new TelemetryRow(table, r));

            if (table.RowCount == 0)
            {
                Status = "Záznam neobsahuje žádnou ze sledovaných zpráv.";
                return;
            }

            Status = $"{table.RowCount} řádků · {table.RowTime(0):HH:mm:ss.fff} – "
                   + $"{table.RowTime(table.RowCount - 1):HH:mm:ss.fff}"
                   + (table.Truncated ? "  ⚠ oříznuto stropem řádků (záznam pokračuje dál)" : "");
        }

        /// <summary>Zmena vyberu -> prestav detail. Generuje CommunityToolkit z ObservableProperty.</summary>
        partial void OnSelectedRowChanged(TelemetryRow value)
        {
            Detail.Clear();
            if (value == null)
            {
                DetailHeader = "Vyber řádek";
                return;
            }

            // Zahlavi: ktera zprava radek zalozila a oba jeji casy. Rozdil T_in/T_out rika,
            // jak dlouho mereni putovalo pipeline (viz doc/record-replay.md).
            double pipelineMs = (value.ArrivalTime - value.Time).TotalMilliseconds;
            DetailHeader = $"#{value.Seq} {value.MsgName} · T_in {value.Time:HH:mm:ss.fff} · "
                         + $"T_out {value.ArrivalTime:HH:mm:ss.fff} ({pipelineMs:F1} ms v pipeline)";

            foreach (var cell in value.Cells)
            {
                if (!cell.HasValue) continue;   // co jeste neprislo, do detailu neplet

                double ageMs = (value.Time - cell.Time).TotalMilliseconds;
                Detail.Add(new TelemetryDetailLine
                {
                    Header = cell.Header,
                    Text = cell.Text,
                    Fresh = cell.Fresh,
                    Age = cell.Fresh ? "právě přišlo"
                                     : $"{cell.Time:HH:mm:ss.fff} · o {ageMs:F0} ms starší",
                });
            }
        }

        /// <summary>
        /// Skoci v prehravani na vybrany radek. <c>SeekTo</c> je povolen jen v Paused, takze se
        /// nejdriv pauzuje - jinak by vyhodil vyjimku (viz FileMessageSource).
        /// </summary>
        [RelayCommand]
        private void SeekToSelected()
        {
            var row = SelectedRow;
            var src = ARBotRuntime.Current?.FileSource;
            if (row == null || src == null) return;

            try
            {
                src.Pause();
                src.SeekTo(row.Seq);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        public void Dispose()
        {
            try { cts?.Cancel(); cts?.Dispose(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            cts = null;
        }
    }

    /// <summary>Jeden radek panelu detailu: udaj, hodnota a jak je stara.</summary>
    public sealed class TelemetryDetailLine
    {
        public string Header { get; set; }
        public string Text { get; set; }
        public string Age { get; set; }
        public bool Fresh { get; set; }
    }

    /// <summary>
    /// "Prave prislo" → tucne, jinak normalni. Tentyz vyznam jako v tabulce, jen tady to nejde
    /// nastavit v code-behind (detail je bezny ItemsControl s XAML sablonou).
    /// </summary>
    public sealed class BoolToWeight : Avalonia.Data.Converters.IValueConverter
    {
        public static readonly BoolToWeight Instance = new BoolToWeight();

        public object Convert(object value, Type targetType, object parameter,
                              System.Globalization.CultureInfo culture)
            => value is bool b && b ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;

        public object ConvertBack(object value, Type targetType, object parameter,
                                  System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Jeden radek tabulky pro binding. Drzi jen odkaz na tabulku a index radku - hodnoty se
    /// ctou az pri vykresleni, takze desetitisice radku nestoji desetitisice kopii dat.
    /// </summary>
    public sealed class TelemetryRow
    {
        private readonly TelemetryTable table;
        private readonly int row;

        public TelemetryRow(TelemetryTable table, int row)
        {
            this.table = table;
            this.row = row;
            Cells = new TelemetryCell[table.Columns.Count];
            for (int c = 0; c < Cells.Length; c++)
                Cells[c] = new TelemetryCell(table.Columns[c], row);
        }

        /// <summary>Cas radku (T_in zakladajici zpravy, jinak T_out).</summary>
        public DateTime Time => table.RowTime(row);

        /// <summary>Cas prichodu zakladajici zpravy na Stream (T_out).</summary>
        public DateTime ArrivalTime => table.RowArrivalTime(row);

        /// <summary>Cas jako text pro sloupec tabulky.</summary>
        public string TimeText => Time.ToString("HH:mm:ss.fff");

        /// <summary>Poradove cislo zpravy v zaznamu - pro seek.</summary>
        public long Seq => table.RowSeq(row);

        /// <summary>Typ zpravy, ktera radek zalozila.</summary>
        public string MsgName => table.RowMsgName(row);

        /// <summary>Bunky v poradi sloupcu registru.</summary>
        public TelemetryCell[] Cells { get; }
    }

    /// <summary>Jedna bunka: text a priznak, zda hodnota prave prisla (tucne) nebo se drzi z minula.</summary>
    public sealed class TelemetryCell
    {
        private readonly TelemetryColumn column;
        private readonly int row;

        public TelemetryCell(TelemetryColumn column, int row)
        {
            this.column = column;
            this.row = row;
        }

        /// <summary>Hodnota k zobrazeni; prazdna, dokud zprava poprve neprisla.</summary>
        public string Text => column.TextAt(row);

        /// <summary>Prisla hodnota prave na tomto radku? (Jinak se drzi z minula.)</summary>
        public bool Fresh => column.IsFresh(row);

        /// <summary>Ma bunka vubec hodnotu?</summary>
        public bool HasValue => column.HasValue(row);

        /// <summary>Cas zpravy, ze ktere hodnota je (u drzene hodnoty starsi nez cas radku).</summary>
        public DateTime Time => column.TimeAt(row);

        /// <summary>Zahlavi sloupce - pouziva detail radku.</summary>
        public string Header => column.Spec.Header;
    }
}
