using System;
using ARBot.Common.Telemetry;
using ARBot.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls.Shapes;
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
        private TelemetryDocument boundVm;

        /// <summary>Datove sloupce v poradi registru - podle nich se skryva/ukazuje.</summary>
        private DataGridColumn[] dataColumns = Array.Empty<DataGridColumn>();

        public TelemetryDocumentView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => { BuildColumns(); Rebind(); };

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

        /// <summary>
        /// Odber udalosti "radek vybralo prehravani". Scrolluje se JEN v tomto pripade - kdyby se
        /// scrollovalo pri kazde zmene vyberu, tabulka by uzivateli skakala pod rukama.
        /// </summary>
        private void Rebind()
        {
            if (boundVm != null)
            {
                boundVm.PlaybackRowChanged -= OnPlaybackRowChanged;
                boundVm.ColumnVisibilityChanged -= OnColumnVisibilityChanged;
            }

            boundVm = DataContext as TelemetryDocument;

            if (boundVm != null)
            {
                boundVm.PlaybackRowChanged += OnPlaybackRowChanged;
                boundVm.ColumnVisibilityChanged += OnColumnVisibilityChanged;
            }
        }

        /// <summary>
        /// Skryje/ukaze datovy sloupec. Sloupce <c>DataGrid</c>u nejsou ve visual tree a nemaji
        /// DataContext, takze na <c>IsVisible</c> nejde bindovat - jde to jen takhle z ruky.
        /// </summary>
        private void OnColumnVisibilityChanged(object sender, TelemetryToggle toggle)
        {
            if (toggle == null) return;
            if (toggle.Index < 0 || toggle.Index >= dataColumns.Length) return;

            dataColumns[toggle.Index].IsVisible = toggle.IsVisible;
        }

        private void OnPlaybackRowChanged(object sender, TelemetryRow row)
        {
            var grid = this.FindControl<DataGrid>("Grid");
            if (grid == null || row == null) return;
            try { grid.ScrollIntoView(row, null); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
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
            // Sirky: cas je "HH:mm:ss.fff" VCETNE milisekund (ty jsou u telemetrie to podstatne),
            // takze sloupec musi byt tak siroky, aby se neorezaly.
            grid.Columns.Add(TextColumn("Čas", nameof(TelemetryRow.TimeText), 130,
                                        "Čas řádku: T_in (čas pořízení) zakládající zprávy, a když ho "
                                        + "nemá, tak T_out (čas příchodu na Stream)."));
            grid.Columns.Add(TextColumn("Zpráva", nameof(TelemetryRow.MsgName), 155,
                                        "Typ zprávy, která tento řádek založila. Ostatní sloupce drží "
                                        + "hodnotu z minula (tučně = hodnota právě přišla)."));

            dataColumns = new DataGridColumn[vm.Columns.Count];
            for (int i = 0; i < vm.Columns.Count; i++)
            {
                var toggle = i < vm.ColumnToggles.Count ? vm.ColumnToggles[i] : null;
                dataColumns[i] = CellColumn(vm.Columns[i], i, toggle);
                grid.Columns.Add(dataColumns[i]);
            }

            // Prepinace uz mohly byt prestavene drive nez vznikly sloupce (jiny zaznam, znovu
            // vytvorene view) - srovnat vychozi stav podle nich.
            for (int i = 0; i < vm.ColumnToggles.Count && i < dataColumns.Length; i++)
                dataColumns[i].IsVisible = vm.ColumnToggles[i].IsVisible;

            columnsBuilt = true;
        }

        private static DataGridTextColumn TextColumn(string header, string path, double width,
                                                     string description)
            => new DataGridTextColumn
            {
                Header = HeaderBlock(header, description),
                Binding = new Binding(path),
                Width = new DataGridLength(width),
            };

        /// <summary>
        /// Zahlavi sloupce jako TextBlock s tooltipem: zahlavi musi byt zkratka (sirka sloupce),
        /// takze vyznam udaje se dozvi az najeti mysi. Popis je v registru sloupcu.
        /// </summary>
        private static Control HeaderBlock(string header, string description)
        {
            var block = new TextBlock
            {
                Text = header,
                FontSize = 14,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            if (!string.IsNullOrEmpty(description))
            {
                ToolTip.SetTip(block, description);
                ToolTip.SetShowDelay(block, 300);
            }

            return block;
        }

        /// <summary>
        /// Zahlavi datoveho sloupce: nazev + <b>prepinac „kreslit v grafu"</b> primo v hlavicce.
        /// Pres flyout to jde taky, ale tady je to na miste, kde se uzivatel na ten udaj zrovna
        /// diva - a nemusi hledat, ktery radek seznamu mu odpovida.
        /// </summary>
        private static Control DataHeader(ColumnSpec spec, TelemetryToggle toggle)
        {
            var panel = new DockPanel { LastChildFill = true };

            var chart = new ToggleButton
            {
                // Ikona jako geometrie, ne znak: symbol grafu (∿, 📈) nemusi byt v pouzitem fontu
                // a vysypal by se prazdny obdelnik. Lomena cara je proste nakreslena.
                Content = new Path
                {
                    Data = Geometry.Parse("M0,7 L3,3 L6,6 L10,0"),
                    Stroke = Brushes.Silver,
                    StrokeThickness = 1.5,
                    Width = 11,
                    Height = 8,
                },
                Padding = new Thickness(4, 2),
                MinHeight = 0,
                MinWidth = 0,
                Opacity = 0.75,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            ToolTip.SetTip(chart, "Kreslit tento údaj v grafu");
            ToolTip.SetShowDelay(chart, 300);

            // Obousmerne na tentyz prepinac, jaky ma flyout „Sloupce ▾" - jinak by se oba ovladace
            // rozesly (zapnuto v hlavicce, vypnuto v seznamu).
            chart.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(TelemetryToggle.InChart))
            {
                Source = toggle,
                Mode = BindingMode.TwoWay,
            });

            // Plny nazev: "Dock" je i namespace dokovaci knihovny, takze samotne Dock.Right nejde.
            DockPanel.SetDock(chart, Avalonia.Controls.Dock.Right);
            panel.Children.Add(chart);
            panel.Children.Add(HeaderBlock(spec.Header, spec.Description));
            return panel;
        }

        /// <summary>
        /// Sloupec jedne telemetricke hodnoty. Je to sablonovy sloupec (ne textovy), protoze
        /// potrebuje <b>tucne u hodnoty, ktera prave prisla</b> - to textovy sloupec neumi.
        /// </summary>
        private static DataGridTemplateColumn CellColumn(ColumnSpec spec, int cellIndex,
                                                        TelemetryToggle toggle)
            => new DataGridTemplateColumn
            {
                Header = toggle != null ? DataHeader(spec, toggle)
                                        : HeaderBlock(spec.Header, spec.Description),
                Width = DataGridLength.Auto,
                CellTemplate = new FuncDataTemplate<TelemetryRow>((row, _) =>
                {
                    var text = new TextBlock
                    {
                        Margin = new Thickness(4, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        // Svisle na stred jako sloupec casu - jinak hodnoty "plavou" nahore
                        // a radek se necte jako jeden celek.
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
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
