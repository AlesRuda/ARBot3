using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci dokument pro zobrazeni obrazovych zprav (<see cref="Blob"/> i
    /// <see cref="ARBot.HAL.CameraFrame"/>). Zpravy prijima jako <see cref="IMessageSink"/>,
    /// rozklada je na pojmenovane vrstvy (<see cref="MessageImageLayers"/>) a nabizi je v
    /// combech pro sloty: levy zaklad, pravy zaklad a polopruhledny (sedivy) overlay.
    ///
    /// Zdroj zprav (zive kamery / prehravany zaznam) se pripoji zvenci pres
    /// <see cref="AttachFeed"/>; dokument feed pri zavreni zastavi.
    /// </summary>
    public partial class ImageDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.ImageDocumentView);

        /// <summary>Rozsah hloubky pro normalizaci Gray16 do grayscale [mm].</summary>
        private const double DepthMaxMm = 6000.0;

        private readonly Dictionary<string, ImageLayer> registry = new Dictionary<string, ImageLayer>();
        private readonly List<IDisposable> feeds = new List<IDisposable>();

        // Backpressure: nejnovejsi nezpracovana zprava na kazdy zdroj (klic). Starsi framy se
        // zahazuji, aby pri zaplave z kamer/backprojectu nerostla dispatcher fronta a UI
        // zustalo responzivni (viz Post/Flush).
        private readonly object pendingGate = new object();
        private readonly Dictionary<string, Message> pending = new Dictionary<string, Message>();
        private volatile bool updateQueued;

        /// <summary>Dostupne pojmenovane vrstvy (pro comba).</summary>
        public ObservableCollection<string> Layers { get; } = new ObservableCollection<string>();

        [ObservableProperty] private string leftLayer;
        [ObservableProperty] private string rightLayer;
        [ObservableProperty] private string leftOverlayLayer;
        [ObservableProperty] private string rightOverlayLayer;
        [ObservableProperty] private double overlayOpacity = 0.5;   // spolecna pruhlednost obou overlayu

        [ObservableProperty] private WriteableBitmap leftImage;
        [ObservableProperty] private WriteableBitmap rightImage;
        [ObservableProperty] private WriteableBitmap leftOverlayImage;
        [ObservableProperty] private WriteableBitmap rightOverlayImage;

        [ObservableProperty] private string leftInfo = "-";
        [ObservableProperty] private string rightInfo = "-";
        [ObservableProperty] private string leftOverlayInfo = "-";
        [ObservableProperty] private string rightOverlayInfo = "-";

        /// <summary>Konstruktor pro design-time / navrhar.</summary>
        public ImageDocument()
        {
            Id = "Images";
            Title = "Obrázky";
        }

        /// <summary>Pripoji zdroj/e zprav; dokument je pri zavreni zastavi (Dispose).</summary>
        public void AttachFeed(params IDisposable[] disposables)
        {
            if (disposables != null)
                feeds.AddRange(disposables);
        }

        // --- IMessageSink ---
        // Bezi na vlakne producenta (RelaySource fan-out) - musi byt neblokujici. Proto zde
        // jen ulozime nejnovejsi obrazovou zpravu na zdroj (starsi zahodime) a koalescovane
        // naplanujeme jednu UI aktualizaci. Tim se ze zpracovani vyrizne backlog: UI vzdy
        // renderuje jen posledni frame kazdeho zdroje, takze se nehromadi a nezaostava cas.
        public void Post(Message msg)
        {
            if (msg == null) return;

            // Coalescing klic - jen obrazove zpravy (ostatni ignorujeme, at neplanujeme flush zbytecne).
            string key = msg switch
            {
                CameraFrame cf => "C:" + (cf.Name ?? string.Empty),
                ImageMsg m => "B:" + (m.Name ?? string.Empty),
                _ => null
            };
            if (key == null) return;

            lock (pendingGate)
                pending[key] = msg;   // nejnovejsi vyhrava (drop stale)

            if (updateQueued) return;
            updateQueued = true;
            // Background priorita: vstup a vykreslovani maji prednost pred zpracovanim obrazu,
            // takze UI zustane responzivni i pri zaplave framu.
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        /// <summary>Zpracuje nejnovejsi nasbirane framy (nejvyse jeden na zdroj) na UI vlakne.</summary>
        private void Flush()
        {
            updateQueued = false;

            List<Message> batch;
            lock (pendingGate)
            {
                if (pending.Count == 0) return;
                batch = new List<Message>(pending.Values);
                pending.Clear();
            }

            foreach (var m in batch)
                Ingest(m);
        }

        private void Ingest(Message msg)
        {
            foreach (var layer in MessageImageLayers.Extract(msg))
            {
                bool isNew = !registry.ContainsKey(layer.Name);
                registry[layer.Name] = layer;
                if (isNew)
                {
                    Layers.Add(layer.Name);
                    AutoSelect(layer);
                }

                if (layer.Name == LeftLayer) RenderSlot(Slot.Left, layer);
                if (layer.Name == RightLayer) RenderSlot(Slot.Right, layer);
                if (layer.Name == LeftOverlayLayer) RenderSlot(Slot.LeftOverlay, layer);
                if (layer.Name == RightOverlayLayer) RenderSlot(Slot.RightOverlay, layer);
            }
        }

        /// <summary>Rozumne vychozi prirazeni slotu pri objeveni nove vrstvy.</summary>
        private void AutoSelect(ImageLayer layer)
        {
            bool isProbability = layer.Kind == LayerKind.Probability
                                 || layer.Name.IndexOf("backproject", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isProbability)
            {
                // Vychozi: stejny probability overlay na obou stranach (uzivatel muze zmenit zvlast).
                if (string.IsNullOrEmpty(LeftOverlayLayer)) LeftOverlayLayer = layer.Name;
                if (string.IsNullOrEmpty(RightOverlayLayer)) RightOverlayLayer = layer.Name;
            }
            else if (layer.Kind == LayerKind.Color)
            {
                if (string.IsNullOrEmpty(LeftLayer)) LeftLayer = layer.Name;
                else if (string.IsNullOrEmpty(RightLayer) && layer.Name != LeftLayer) RightLayer = layer.Name;
            }
        }

        partial void OnLeftLayerChanged(string value) => RenderFromRegistry(Slot.Left, value);
        partial void OnRightLayerChanged(string value) => RenderFromRegistry(Slot.Right, value);
        partial void OnLeftOverlayLayerChanged(string value) => RenderFromRegistry(Slot.LeftOverlay, value);
        partial void OnRightOverlayLayerChanged(string value) => RenderFromRegistry(Slot.RightOverlay, value);

        private enum Slot { Left, Right, LeftOverlay, RightOverlay }

        private void RenderFromRegistry(Slot slot, string name)
        {
            if (!string.IsNullOrEmpty(name) && registry.TryGetValue(name, out var layer))
                RenderSlot(slot, layer);
            else
                SetSlotImage(slot, null, "-");
        }

        private void RenderSlot(Slot slot, ImageLayer layer)
        {
            var bmp = Render(layer);
            string info = string.Format(CultureInfo.InvariantCulture, "{0}  {1}×{2}  {3:HH:mm:ss.fff}",
                layer.Name, layer.Width, layer.Height, layer.TimeStamp);
            SetSlotImage(slot, bmp, info);
        }

        private void SetSlotImage(Slot slot, WriteableBitmap bmp, string info)
        {
            switch (slot)
            {
                case Slot.Left: LeftImage = bmp; LeftInfo = info; break;
                case Slot.Right: RightImage = bmp; RightInfo = info; break;
                case Slot.LeftOverlay: LeftOverlayImage = bmp; LeftOverlayInfo = info; break;
                case Slot.RightOverlay: RightOverlayImage = bmp; RightOverlayInfo = info; break;
            }
        }

        // --- render Image<T> -> WriteableBitmap (Bgra8888) ---

        private WriteableBitmap Render(ImageLayer layer)
        {
            switch (layer.Kind)
            {
                case LayerKind.Color: return RenderColor(layer.Color);
                case LayerKind.Probability: return RenderGray(layer.Gray);
                case LayerKind.Depth: return RenderDepth(layer.Depth);
                default: return null;
            }
        }

        private static WriteableBitmap RenderColor(Image<BGR32> rgb)
        {
            if (rgb == null || rgb.Width <= 0 || rgb.Height <= 0) return null;
            int w = rgb.Width, h = rgb.Height;
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                int rowBytes = w * 4;
                var data = rgb.Data;
                for (int y = 0; y < h; y++)
                    Marshal.Copy(data, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
            }
            return bmp;
        }

        private static WriteableBitmap RenderGray(Image<Gray> gray)
        {
            if (gray == null || gray.Width <= 0 || gray.Height <= 0) return null;
            int w = gray.Width, h = gray.Height;
            var src = gray.Data;   // 1 bajt/pixel
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            byte[] row = new byte[w * 4];
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        byte g = src[srcRow + x];
                        int o = x * 4;
                        row[o] = g; row[o + 1] = g; row[o + 2] = g; row[o + 3] = 255;
                    }
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w * 4);
                }
            }
            return bmp;
        }

        private static WriteableBitmap RenderDepth(Image<Gray16> depth)
        {
            if (depth == null || depth.Width <= 0 || depth.Height <= 0) return null;
            int w = depth.Width, h = depth.Height;
            var src = depth.Data;   // 2 bajty/pixel, little-endian
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            byte[] row = new byte[w * 4];
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < h; y++)
                {
                    int srcRow = y * w * 2;
                    for (int x = 0; x < w; x++)
                    {
                        int v = src[srcRow + x * 2] + (src[srcRow + x * 2 + 1] << 8);
                        byte g;
                        if (v <= 0) g = 0;
                        else { double t = v / DepthMaxMm; if (t > 1.0) t = 1.0; g = (byte)(255.0 * (1.0 - t)); }
                        int o = x * 4;
                        row[o] = g; row[o + 1] = g; row[o + 2] = g; row[o + 3] = 255;
                    }
                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, w * 4);
                }
            }
            return bmp;
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            foreach (var d in feeds)
            {
                try { d.Dispose(); } catch { }
            }
            feeds.Clear();
        }
    }
}
