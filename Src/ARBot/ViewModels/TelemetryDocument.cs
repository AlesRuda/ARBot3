using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// Zobrazene radky. Pri zmene filtru se <b>vymeni cela kolekce</b> (ne polozka po polozce) -
        /// radku jsou desetitisice a jednotlive notifikace by tabulku na vteriny zastavily.
        /// </summary>
        [ObservableProperty] private IReadOnlyList<TelemetryRow> rows = Array.Empty<TelemetryRow>();

        /// <summary>Vsechny radky tabulky (nefiltrovane) - z nich se sklada <see cref="Rows"/>.</summary>
        private IReadOnlyList<TelemetryRow> allRows = Array.Empty<TelemetryRow>();

        /// <summary>Hotova tabulka - drzi se kvuli vytazeni rad do grafu.</summary>
        private TelemetryTable table;

        /// <summary>Zapinatelne sloupce (poradi = poradi v registru). Filtruje se JEN zobrazeni,
        /// sken i tabulka zustavaji cele.</summary>
        public ObservableCollection<TelemetryToggle> ColumnToggles { get; }
            = new ObservableCollection<TelemetryToggle>();

        /// <summary>Zapinatelne typy zakladajici zpravy - filtr radku (viz doc/telemetry-view.md).</summary>
        public ObservableCollection<TelemetryToggle> TypeToggles { get; }
            = new ObservableCollection<TelemetryToggle>();

        /// <summary>Zmenila se viditelnost sloupce - View prestavi <c>DataGridColumn.IsVisible</c>
        /// (sloupce tabulky nejsou ve visual tree, takze na ne nejde bindovat).</summary>
        public event EventHandler<TelemetryToggle> ColumnVisibilityChanged;

        /// <summary>Zmenil se vyber udaju do grafu. Dokument grafu zaklada a aktivuje
        /// <see cref="MainWindowViewModel"/> - tabulka o docich nic nevi.</summary>
        public event EventHandler<TelemetryChartRequest> ChartSeriesChanged;

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

        /// <summary>Casovac: hlida (a) zmenu prehravaneho zaznamu a (b) kurzor prehravani.</summary>
        private DispatcherTimer watchTimer;

        /// <summary>Zaznam, ze ktereho je soucasna tabulka - podle nej se pozna, ze uzivatel
        /// mezitim otevrel JINY zaznam a je potreba skenovat znovu.</summary>
        private string scannedPath;

        /// <summary>Zdroj, ze ktereho je soucasna tabulka. Porovnava se na REFERENCI: tentyz soubor
        /// otevreny znovu je novy <see cref="FileMessageSource"/> s novym indexem, a i ten se ma
        /// preskenovat (jinak by tabulka drzela radky navazane na zavreny zdroj).</summary>
        private FileMessageSource scannedSource;

        /// <summary>Probehl uz aspon jeden pokus o sken? (Bez toho by se stav „zadny zaznam"
        /// nedal odlisit od „jeste jsem se nedival".)</summary>
        private bool scanAttempted;

        /// <summary>Casovy rozsah tabulky do stavoveho radku (sklada se jen po skenu).</summary>
        private string rangeText = string.Empty;

        /// <summary>Probiha hromadna zmena prepinacu - filtr se prepocita az na jejim konci.</summary>
        private bool suppressFilter;

        /// <summary>Posledni videny kurzor prehravani. Slouzi k tomu, aby se vyber prestavoval
        /// JEN kdyz se prehravani opravdu pohnulo - jinak by uzivatel nemohl pri stojicim
        /// prehravani proklikat tabulku (kazdy tik by mu vyber vratil zpatky).</summary>
        private long lastCursor = -1;

        /// <summary>Radek vybralo prehravani (ne uzivatel) - View na to odscrolluje.</summary>
        public event EventHandler<TelemetryRow> PlaybackRowChanged;

        public TelemetryDocument()
        {
            Id = "Telemetry";
            Title = "Telemetrie";

            // Prepinace sloupcu jdou z registru, takze existuji jeste pred skenem (a v navrhari).
            for (int i = 0; i < Columns.Count; i++)
            {
                var spec = Columns[i];
                ColumnToggles.Add(new TelemetryToggle(spec.Header, spec.Description, i, OnColumnToggled));
            }

            // Navrhar nesmi sahnout na runtime ani na soubory (viz Views/README.md).
            if (Design.IsDesignMode)
            {
                Status = "(návrhový režim)";
                return;
            }

            // Casovac bezi od zacatku, ne az po prvnim uspesnem skenu: dokument muze byt otevreny
            // DRIV nez zaznam (pak se tabulka naplni, jakmile zaznam prijde) a stejnou cestou se
            // pozna i to, ze uzivatel mezitim otevrel jiny zaznam.
            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            watchTimer.Tick += (_, _) => Watch();
            watchTimer.Start();
            Watch();
        }

        /// <summary>Tik hlidace: nejdriv sprav tabulku podle aktualniho zaznamu, pak srovnej vyber.</summary>
        private void Watch()
        {
            EnsureCurrentRecord();
            SyncFromPlayback();
        }

        /// <summary>
        /// Odpovida tabulka tomu, co se prave prehrava? Kdyz ne (jiny zaznam, tyz zaznam otevreny
        /// znovu, nebo jeste zadny sken), spusti se novy sken. Porovnani je levne, takze to muze
        /// delat casovac - dokument se tim nemusi dozvedet o zmene rezimu zvlastni cestou.
        /// </summary>
        private void EnsureCurrentRecord()
        {
            var runtime = ARBotRuntime.Current;
            string path = runtime?.RecordPath;
            var source = runtime?.FileSource;

            if (scanAttempted
                && string.Equals(path, scannedPath, StringComparison.Ordinal)
                && ReferenceEquals(source, scannedSource))
                return;

            StartScan(path, source);
        }

        /// <summary>Spusti sken zaznamu na vlakne mimo UI (a zahodi vysledky toho predchoziho).</summary>
        private void StartScan(string path, FileMessageSource source)
        {
            // Predchozi sken uz nikoho nezajima - jeho vysledek by prepsal tabulku noveho zaznamu.
            // Jen Cancel, NE Dispose: sken jeste chvili dobiha a sahal by na uz zahozeny token.
            // Uklid dela az dokonceni skenu (ContinueWith nize).
            try { cts?.Cancel(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            cts = null;

            scanAttempted = true;
            scannedPath = path;
            scannedSource = source;

            // Stara data pryc hned, at se pri prepnuti zaznamu nekouka na cizi radky.
            table = null;
            allRows = Array.Empty<TelemetryRow>();
            Rows = Array.Empty<TelemetryRow>();
            TypeToggles.Clear();
            SelectedRow = null;
            rangeText = string.Empty;
            lastCursor = -1;
            IsScanning = false;
            Progress = 0;

            if (string.IsNullOrEmpty(path))
            {
                Status = "Není otevřený záznam — Runtime → View…";
                return;
            }

            var index = source?.Index;
            if (index == null || index.Count == 0)
            {
                Status = "Záznam nemá sidecar index (*.idx) — tabulku z něj postavit nelze.";
                return;
            }

            var own = cts = new CancellationTokenSource();
            var ct = own.Token;
            // Postup hlasi jen sken, ktery je porad ten aktualni - zruseny sken jeste chvili dobiha
            // a jinak by prepisoval ukazatel toho noveho.
            var progressReport = new Progress<double>(p => { if (ReferenceEquals(own, cts)) Progress = p; });
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
            }, ct).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                Apply(t, own);

                // Sken dobehl - token uz nikdo nepouzije, takze je bezpecne uklidit.
                if (ReferenceEquals(own, cts)) cts = null;
                own.Dispose();
            }));
        }

        /// <summary>Prevezme vysledek skenu (uz na UI vlakne).</summary>
        /// <param name="own">Token skenu, ktery vysledek prinesl - kdyz uz neni ten aktualni,
        /// mezitim se zacal skenovat jiny zaznam a tenhle vysledek se zahazuje.</param>
        private void Apply(Task<TelemetryTable> task, CancellationTokenSource own)
        {
            if (!ReferenceEquals(own, cts)) return;   // zastaraly sken

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

            table = task.Result;
            var built = new TelemetryRow[table.RowCount];
            for (int r = 0; r < table.RowCount; r++)
                built[r] = new TelemetryRow(table, r);

            allRows = built;
            BuildTypeToggles(built);
            ApplyRowFilter();

            // Uz otevreny graf ma ukazovat data TOHOTO zaznamu, ale sam se kvuli tomu nevytahne
            // dopredu (open: false) - to by pri kazdem prepnuti zaznamu prebilo aktivni tab.
            PublishChartSeries(open: false);

            if (table.RowCount == 0)
            {
                Status = "Záznam neobsahuje žádnou ze sledovaných zpráv.";
                return;
            }

            rangeText = $"{table.RowTime(0):HH:mm:ss.fff} – {table.RowTime(table.RowCount - 1):HH:mm:ss.fff}"
                      + (table.Truncated ? "  ⚠ oříznuto stropem řádků (záznam pokračuje dál)" : "");
            UpdateStatus();

            SyncFromPlayback();   // hned vyber radek, kde prehravani stoji
        }

        /// <summary>
        /// Postavi prepinace typu zprav z toho, co v zaznamu opravdu je (vcetne poctu radku) -
        /// nabizet typ, ktery v tomhle behu nikdy neprisel, by jen matlo.
        /// </summary>
        private void BuildTypeToggles(IReadOnlyList<TelemetryRow> source)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in source)
            {
                string key = row.MsgName ?? string.Empty;
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }

            TypeToggles.Clear();
            foreach (var pair in counts.OrderByDescending(p => p.Value))
                TypeToggles.Add(new TelemetryToggle($"{pair.Key}  ({pair.Value})", null, 0, OnTypeToggled)
                {
                    Key = pair.Key,
                });
        }

        /// <summary>Prepnuti sloupce: bud se skryva/ukazuje v tabulce, nebo prida/odebira z grafu.</summary>
        private void OnColumnToggled(TelemetryToggle toggle, TelemetryToggleKind kind)
        {
            if (kind == TelemetryToggleKind.Chart)
                PublishChartSeries(open: true);
            else
                ColumnVisibilityChanged?.Invoke(this, toggle);
        }

        /// <summary>Prepnuti typu zpravy - prestavi se seznam zobrazenych radku.</summary>
        private void OnTypeToggled(TelemetryToggle toggle, TelemetryToggleKind kind)
        {
            if (suppressFilter) return;   // probiha hromadna zmena, prefiltruje se az na konci
            ApplyRowFilter();
            UpdateStatus();
        }

        /// <summary>
        /// Vyrobi rady z prave zaskrtnutych sloupcu a posle je do grafu. Vytazeni rady projde
        /// tabulku, takze se dela az tady (pri zmene vyberu), ne pri kazdem prekresleni.
        /// </summary>
        /// <param name="open">Otevrit/aktivovat dokument grafu (po zaskrtnuti udaje ano; po
        /// preskenovani zaznamu ne - tam se jen aktualizuji data uz otevreneho grafu).</param>
        private void PublishChartSeries(bool open)
        {
            if (table == null) return;

            var series = new List<TelemetrySeries>();
            foreach (var t in ColumnToggles)
            {
                if (!t.InChart) continue;
                if (t.Index < 0 || t.Index >= table.Columns.Count) continue;
                series.Add(TelemetrySeries.From(table, table.Columns[t.Index]));
            }

            ChartSeriesChanged?.Invoke(this, new TelemetryChartRequest(series, open && series.Count > 0));
        }

        /// <summary>
        /// Prepocita <see cref="Rows"/> z <see cref="allRows"/> podle zapnutych typu. Vyber radku
        /// prezije, pokud zustal viditelny; jinak se prevezme nejblizsi predchozi (aby detail
        /// neblikl na prazdno a synchronizace s prehravanim navazala).
        /// </summary>
        private void ApplyRowFilter()
        {
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in TypeToggles)
                if (!t.IsVisible) hidden.Add(t.Key);

            var previous = SelectedRow;

            if (hidden.Count == 0)
            {
                Rows = allRows;
            }
            else
            {
                var list = new List<TelemetryRow>(allRows.Count);
                foreach (var row in allRows)
                    if (!hidden.Contains(row.MsgName ?? string.Empty)) list.Add(row);
                Rows = list;
            }

            // Vymena kolekce shodi vyber v tabulce. Obnovit ho jde az POTOM, co si DataGrid
            // prevezme novou ItemsSource - proto pres Post, ne rovnou.
            if (previous != null)
            {
                var restore = FindRowAt(previous.Seq) ?? previous;
                Dispatcher.UIThread.Post(() => SelectedRow = restore);
            }

            lastCursor = -1;   // at synchronizace s prehravanim znovu dohleda radek
        }

        /// <summary>Stavovy radek: kolik radku je videt (a z kolika, kdyz je filtr zapnuty).</summary>
        private void UpdateStatus()
        {
            if (allRows.Count == 0) return;

            string count = Rows.Count == allRows.Count
                ? $"{allRows.Count} řádků"
                : $"{Rows.Count} z {allRows.Count} řádků (filtr)";

            Status = $"{count} · {rangeText}";
        }

        /// <summary>Skryty tab nema co hlidat (a nema kam scrollovat) - viz Views/README.md.</summary>
        protected override void OnActiveChanged(bool active)
        {
            if (watchTimer == null) return;

            if (active)
            {
                watchTimer.Start();
                Watch();
            }
            else
            {
                watchTimer.Stop();
            }
        }

        /// <summary>
        /// Srovna vyber v tabulce s kurzorem prehravani: vybere posledni radek, jehoz zakladajici
        /// zprava uz byla prehrana. <c>Cursor</c> je <b>Seq NASLEDUJICI</b> zpravy, takze posledni
        /// prehrana je <c>Cursor - 1</c>.
        /// </summary>
        private void SyncFromPlayback()
        {
            var src = ARBotRuntime.Current?.FileSource;
            if (src == null || src.Index == null || Rows.Count == 0) return;

            long played = src.Cursor - 1;
            if (played == lastCursor) return;   // prehravani stoji -> nesahat uzivateli na vyber
            lastCursor = played;

            var row = FindRowAt(played);
            if (row == null || ReferenceEquals(row, SelectedRow)) return;

            SelectedRow = row;
            PlaybackRowChanged?.Invoke(this, row);
        }

        /// <summary>
        /// Posledni radek se <c>Seq</c> &le; <paramref name="seq"/> (pulenim - radku jsou
        /// desetitisice a hleda se 10x za sekundu). Null, kdyz je kurzor pred prvnim radkem.
        /// </summary>
        private TelemetryRow FindRowAt(long seq)
        {
            if (Rows.Count == 0 || Rows[0].Seq > seq) return null;

            int lo = 0, hi = Rows.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Rows[mid].Seq <= seq) lo = mid;
                else hi = mid - 1;
            }
            return Rows[lo];
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
                    Description = cell.Description,
                    Text = cell.Text,
                    Fresh = cell.Fresh,
                    Age = cell.Fresh ? "právě přišlo"
                                     : $"{cell.Time:HH:mm:ss.fff} · o {ageMs:F0} ms starší",
                });
            }
        }

        /// <summary>Zapne všechny sloupce (25 zaškrtávátek se ručně nezapíná).</summary>
        [RelayCommand]
        private void ShowAllColumns() => SetAllColumns(true);

        /// <summary>Vypne všechny sloupce - rychlejší cesta k „chci vidět jen tyhle tři".</summary>
        [RelayCommand]
        private void HideAllColumns() => SetAllColumns(false);

        private void SetAllColumns(bool visible)
        {
            foreach (var t in ColumnToggles)
                t.IsVisible = visible;
        }

        /// <summary>Zapne všechny typy zpráv (zruší filtr řádků).</summary>
        [RelayCommand]
        private void ShowAllTypes()
        {
            // Hromadna zmena prefiltruje az na konci - jinak by se seznam desetitisic radku
            // prestavoval jednou za kazdy prepnuty typ.
            suppressFilter = true;
            foreach (var t in TypeToggles)
                t.IsVisible = true;
            suppressFilter = false;

            ApplyRowFilter();
            UpdateStatus();
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
            // Zase jen Cancel - probihajici sken si svuj token uklidi sam, az dobehne.
            try { cts?.Cancel(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            cts = null;

            watchTimer?.Stop();
            watchTimer = null;
        }
    }

    /// <summary>
    /// Jeden zaskrtavaci prepinac (sloupec tabulky nebo typ zpravy ve filtru radku). Zmena
    /// hlasi zpet dokumentu callbackem - prepinace jsou dva ruzne seznamy s ruznou reakci,
    /// ale stejnym chovanim, takze si zaslouzi jednu tridu.
    /// </summary>
    /// <summary>Zadost tabulky o vykresleni rad v grafu.</summary>
    /// <param name="Series">Rady vyrobene z prave zaskrtnutych sloupcu (muze byt i prazdne).</param>
    /// <param name="Open">Ma se dokument grafu otevrit/aktivovat? (Jen kdyz zmenu vyvolal uzivatel.)</param>
    public sealed record TelemetryChartRequest(IReadOnlyList<TelemetrySeries> Series, bool Open);

    /// <summary>Co se na prepinaci zmenilo - reakce je u kazdeho jina.</summary>
    public enum TelemetryToggleKind
    {
        /// <summary>Viditelnost (sloupce v tabulce / typu zprav ve filtru radku).</summary>
        Visibility,

        /// <summary>Zarazeni udaje do grafu.</summary>
        Chart,
    }

    public partial class TelemetryToggle : ObservableObject
    {
        private readonly Action<TelemetryToggle, TelemetryToggleKind> onChanged;

        /// <param name="label">Text u zaskrtavatka.</param>
        /// <param name="description">Vysvetleni do tooltipu (u sloupcu z registru; jinak null).</param>
        /// <param name="index">Poradi sloupce v registru (u typu zprav se nepouziva).</param>
        /// <param name="onChanged">Co se ma stat po prepnuti.</param>
        public TelemetryToggle(string label, string description, int index,
                               Action<TelemetryToggle, TelemetryToggleKind> onChanged)
        {
            Label = label;
            Description = description;
            Index = index;
            this.onChanged = onChanged;
        }

        public string Label { get; }
        public string Description { get; }

        /// <summary>Poradi sloupce v registru = poradi datoveho sloupce v tabulce.</summary>
        public int Index { get; }

        /// <summary>Klic pro filtr radku (<c>MsgName</c>); u sloupcu nevyuzity.</summary>
        public string Key { get; init; } = string.Empty;

        [ObservableProperty] private bool isVisible = true;

        /// <summary>Kreslit tenhle udaj v grafu? (Jen u sloupcu; u typu zprav nevyuzito.)</summary>
        [ObservableProperty] private bool inChart;

        partial void OnIsVisibleChanged(bool value)
            => onChanged?.Invoke(this, TelemetryToggleKind.Visibility);

        partial void OnInChartChanged(bool value)
            => onChanged?.Invoke(this, TelemetryToggleKind.Chart);
    }

    /// <summary>Jeden radek panelu detailu: udaj, hodnota a jak je stara.</summary>
    public sealed class TelemetryDetailLine
    {
        public string Header { get; set; }

        /// <summary>Vysvetleni udaje z registru sloupcu - zobrazuje se jako tooltip.</summary>
        public string Description { get; set; }

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

        /// <summary>Vysvetleni udaje (tooltip v detailu i na zahlavi sloupce).</summary>
        public string Description => column.Spec.Description;
    }
}
