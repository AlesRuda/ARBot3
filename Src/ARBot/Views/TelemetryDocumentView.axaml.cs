using System;
using ARBot.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace ARBot.Views
{
    /// <summary>
    /// Telemetricka tabulka. Sloupce se NEDAJI napsat v XAML - je jich desitky a jsou datove
    /// rizene registrem (<c>TelemetryColumns</c>), takze se staveji tady podle ViewModelu.
    /// Viz doc/telemetry-view.md.
    /// </summary>
    public partial class TelemetryDocumentView : UserControl
    {
        private bool columnsBuilt;

        public TelemetryDocumentView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => BuildColumns();

            // Dvojklik na radek = skok v prehravani na tento okamzik (tentyz prikaz jako tlacitko
            // v detailu). Tabulka a Replay panel tak spolupracuji misto aby si konkurovaly.
            var grid = this.FindControl<DataGrid>("Grid");
            if (grid != null)
                grid.DoubleTapped += (_, _) =>
                {
                    if (DataContext is TelemetryDocument vm && vm.SeekToSelectedCommand.CanExecute(null))
                        vm.SeekToSelectedCommand.Execute(null);
                };
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>Postavi sloupce: cas, typ zpravy a pak jeden sloupec na kazdy udaj z registru.</summary>
        private void BuildColumns()
        {
            if (columnsBuilt) return;
            if (DataContext is not TelemetryDocument vm) return;
            var grid = this.FindControl<DataGrid>("Grid");
            if (grid == null) return;

            grid.Columns.Clear();
            grid.Columns.Add(TextColumn("Čas", nameof(TelemetryRow.TimeText), 100));
            grid.Columns.Add(TextColumn("Zpráva", nameof(TelemetryRow.MsgName), 130));

            for (int i = 0; i < vm.Columns.Count; i++)
                grid.Columns.Add(CellColumn(vm.Columns[i].Header, i));

            columnsBuilt = true;
        }

        private static DataGridTextColumn TextColumn(string header, string path, double width)
            => new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path),
                Width = new DataGridLength(width),
            };

        /// <summary>
        /// Sloupec jedne telemetricke hodnoty. Je to sablonovy sloupec (ne textovy), protoze
        /// potrebuje <b>tucne u hodnoty, ktera prave prisla</b> - to textovy sloupec neumi.
        /// </summary>
        private static DataGridTemplateColumn CellColumn(string header, int cellIndex)
            => new DataGridTemplateColumn
            {
                Header = header,
                Width = DataGridLength.Auto,
                CellTemplate = new FuncDataTemplate<TelemetryRow>((row, _) =>
                {
                    var text = new TextBlock
                    {
                        Margin = new Thickness(4, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        TextAlignment = TextAlignment.Right,
                    };

                    // Hodnoty se ctou primo (radek uz je hotovy a nemeni se) - zadny binding,
                    // zadne notifikace, jen text. Tabulka ma desetitisice radku.
                    if (row != null && cellIndex < row.Cells.Length)
                    {
                        var cell = row.Cells[cellIndex];
                        text.Text = cell.Text;
                        text.FontWeight = cell.Fresh ? FontWeight.Bold : FontWeight.Normal;
                    }

                    return text;
                }),
            };
    }
}
