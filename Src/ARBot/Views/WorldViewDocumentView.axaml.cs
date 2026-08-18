using System;
using System.Linq;
using ARBot.ViewModels;
using Avalonia.Controls;
using Mapsui.Extensions;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ARBot.Views
{
    /// <summary>
    /// View svetoveho (world) pohledu. Mapsui <c>MapControl</c> se vytvari a pripojuje v code-behind
    /// (mimo design-time) - vyhne se to xmlns 3rd-party controlu i padu navrhare. Ovladaci panel a info
    /// jsou v XAML a binduji na <see cref="WorldViewDocument"/>.
    /// </summary>
    public partial class WorldViewDocumentView : UserControl
    {
        private Mapsui.UI.Avalonia.MapControl? mapControl;

        public WorldViewDocumentView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => AttachMap();
            AttachMap();
        }

        /// <summary>Vytvori/priradi MapControl podle aktualniho DataContextu (idempotentni, mimo design-time).</summary>
        private void AttachMap()
        {
            if (Design.IsDesignMode) return;
            if (DataContext is not WorldViewDocument vm) return;

            var host = this.FindControl<Panel>("MapHost");
            if (host == null) return;

            if (mapControl == null)
            {
                mapControl = new Mapsui.UI.Avalonia.MapControl();
                // Ctrl + klik = zadani cile lokalniho planovace. Ctrl proto, aby se to nepletlo
                // s beznym pan/zoom (viz doc/occupancy-and-local-planning.md).
                mapControl.PointerPressed += OnMapPointerPressed;
                mapControl.PointerMoved += OnMapPointerMoved;
                host.Children.Insert(0, mapControl);
            }

            if (!ReferenceEquals(mapControl.Map, vm.Map))
                mapControl.Map = vm.Map;
        }

        /// <summary>
        /// Klik do mapy s modifikatorem: <b>Ctrl</b> = cil lokalniho planovace, <b>Shift</b> =
        /// presun simulovaneho robotu (pixel -&gt; Web Mercator -&gt; lokalni ENU). Bez modifikatoru
        /// se nic nedeje - klik patri beznemu pan/zoom.
        /// </summary>
        private void OnMapPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (mapControl == null || DataContext is not WorldViewDocument vm) return;
            // Shift + klik = presun SIMULOVANEHO robotu na to misto (vyvojarska pomucka, viz
            // doc/virtual-hw.md). Testuje se PRED Ctrl, aby se obe modifikatory nepletly.
            if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Shift) != 0)
            {
                try
                {
                    var sp = e.GetPosition(mapControl);
                    var sw = mapControl.Map.Navigator.Viewport.ScreenToWorld(sp.X, sp.Y);
                    if (vm.RequestTeleportFromMercator(sw.X, sw.Y))
                        e.Handled = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
                return;
            }

            if ((e.KeyModifiers & Avalonia.Input.KeyModifiers.Control) == 0) return;

            try
            {
                var p = e.GetPosition(mapControl);
                var world = mapControl.Map.Navigator.Viewport.ScreenToWorld(p.X, p.Y);
                if (vm.RequestGoalFromMercator(world.X, world.Y))
                    e.Handled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// Tooltip nad znackami (start / cil / mrkev). Znacky se od sebe lisi jen barvou, takze bez
        /// popisu jsou to tri puntiky bez vysvetleni. Hit-test delame nad vlastnimi daty ViewModelu
        /// (pozice znacek si stejne stavime sami), tolerance se prepocita z rozliseni viewportu,
        /// aby byla konstantni v pixelech nezavisle na zoomu.
        /// </summary>
        private void OnMapPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            if (mapControl == null || DataContext is not WorldViewDocument vm) return;

            try
            {
                var p = e.GetPosition(mapControl);
                var viewport = mapControl.Map.Navigator.Viewport;
                var world = viewport.ScreenToWorld(p.X, p.Y);

                const double hitRadiusPx = 12.0;
                string? tip = vm.FindMarkerTip(world.X, world.Y, hitRadiusPx * viewport.Resolution);

                ToolTip.SetTip(mapControl, tip);
                ToolTip.SetIsOpen(mapControl, tip != null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>Vybere .mbtiles soubor pro offline podklad a ulozi cestu do ViewModelu.</summary>
        private async void OnBrowseMbTiles(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WorldViewDocument vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            try
            {
                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Vyber MBTiles podklad",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("MBTiles") { Patterns = new[] { "*.mbtiles" } },
                        new FilePickerFileType("Vše") { Patterns = new[] { "*.*" } },
                    },
                });

                var file = files?.FirstOrDefault();
                var path = file?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    vm.MbTilesPath = path;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        /// <summary>Vyzada cilovy .mbtiles soubor a spusti export aktualniho vyrezu mapy.</summary>
        private async void OnLoadOsmMap(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WorldViewDocument vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            try
            {
                var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Vyber OSM mapu (.osm)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("OSM XML") { Patterns = new[] { "*.osm", "*.xml" } },
                        new FilePickerFileType("Vše") { Patterns = new[] { "*.*" } },
                    },
                });

                var path = files?.FirstOrDefault()?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    await vm.LoadOsmMapAsync(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private async void OnExportMbTiles(object sender, RoutedEventArgs e)
        {
            if (DataContext is not WorldViewDocument vm) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            try
            {
                var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Ulož výřez jako MBTiles",
                    SuggestedFileName = "vyrez.mbtiles",
                    DefaultExtension = "mbtiles",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("MBTiles") { Patterns = new[] { "*.mbtiles" } },
                    },
                });

                var path = file?.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    await vm.ExportCurrentViewToMbTilesAsync(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
    }
}
