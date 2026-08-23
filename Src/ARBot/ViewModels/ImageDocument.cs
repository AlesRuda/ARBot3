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

        /// <summary>DIAGNOSTIKA (self-test): počet zpracovaných snímků a vytvořených <c>WriteableBitmap</c>
        /// napříč všemi instancemi (proces). Ověřuje, zda okno Images reálně churnuje. Jen UI vlákno.</summary>
        public static long DiagFramesIngested;
        public static long DiagBitmapsCreated;

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

        // Vlastni pool kopii surovych snimku (krok 4): CameraFrame nese poolovane capture buffery kamery,
        // ktere UI renderuje az POZDEJI na UI vlakne (Flush). V Post() (na vlakne producenta) si proto
        // porizeme stabilni kopii; po vyrenderovani ji v Flush vratime. Vycerpani = drop (nech stary snimek).
        // Grid se nekopiruje (referenci - je immutable per snimek, viz CameraFramePool).
        private readonly CameraFramePool framePool = new CameraFramePool(6);

        // Grid sjizdnosti jako overlay vrstva "<kamera>/Traversability": rasterizuje se do velikosti
        // depth snimku (per-pixel alfa) a zarovnava se pres ColumnsPerCell (azimut) x RadialEdge.Row
        // (radialne). Rozmer se bere z depth vrstvy stejne kamery (grid rozmer obrazu nenese).
        private readonly Dictionary<string, (PolarTraversabilityGrid grid, DateTime ts)> gridByCam
            = new Dictionary<string, (PolarTraversabilityGrid, DateTime)>();
        private readonly Dictionary<string, (int w, int h)> depthSizeByCam = new Dictionary<string, (int, int)>();
        private readonly Dictionary<string, WriteableBitmap> prerendered = new Dictionary<string, WriteableBitmap>();
        private const string TraversabilitySuffix = "/Traversability";

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

        /// <summary>Pixel pod kurzorem v levem/pravem panelu; prazdne, kdyz je kurzor mimo obraz.</summary>
        [ObservableProperty] private string leftCursorInfo = "";
        [ObservableProperty] private string rightCursorInfo = "";

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

            // CameraFrame nese poolovane capture buffery kamery (krok 4): porizeme stabilni poolovanou
            // kopii, protoze render probiha az pozdeji na UI vlakne. Vycerpani poolu = drop (nech stary).
            Message store = msg;
            if (msg is CameraFrame cf0)
            {
                var copy = framePool.Acquire(cf0);
                if (copy == null) return;   // pool vyschl -> drop
                store = copy;
            }

            // Coalescing klic - jen obrazove zpravy (ostatni ignorujeme, at neplanujeme flush zbytecne).
            // Grid je nyni soucasti CameraFrame (klic "C:") - samostatna grid zprava uz neexistuje.
            string key = store switch
            {
                CameraFrame cf => "C:" + (cf.Name ?? string.Empty),
                ImageMsg m => "B:" + (m.Name ?? string.Empty),
                _ => null
            };
            if (key == null)
            {
                if (store is CameraFrame stray) framePool.Release(stray);
                return;
            }

            lock (pendingGate)
            {
                // Nahrazeny snimek (drop stale) vratime do poolu, at neteceme sloty.
                if (pending.TryGetValue(key, out var old) && old is CameraFrame oldCf)
                    framePool.Release(oldCf);
                pending[key] = store;   // nejnovejsi vyhrava (drop stale)
            }

            // Skryty dokument (neaktivni tab): nejnovejsi si PAMATUJEME (pending vyse, poolovana kopie),
            // ale NErenderujeme (drahe WriteableBitmapy = GC churn - viz devlog: 397 bitmap -> gen2).
            // Render se provede az pri zviditelneni (OnActiveChanged) na zapamatovana data.
            if (!IsActive) return;

            if (updateQueued) return;
            updateQueued = true;
            // Background priorita: vstup a vykreslovani maji prednost pred zpracovanim obrazu,
            // takze UI zustane responzivni i pri zaplave framu.
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        /// <summary>
        /// Pri zviditelneni (dokument se stal aktivnim tabem) vyrenderuj zapamatovanou nejnovejsi zpravu
        /// (pending) hned - okno okamzite ukaze aktualni snimek a pak jede zive. Skryty se nerenderuje.
        /// </summary>
        protected override void OnActiveChanged(bool active)
        {
            if (!active || updateQueued) return;
            updateQueued = true;
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
            {
                Ingest(m);
                // Po vyrenderovani (Ingest zkopiroval data do WriteableBitmap, grid drzime referenci)
                // vratime poolovanou kopii snimku zpet. No-op pro nepoolovane zpravy (ImageMsg).
                if (m is CameraFrame cf) framePool.Release(cf);
            }
        }

        private void Ingest(Message msg)
        {
            DiagFramesIngested++;
            // Grid je soucasti CameraFrame (spolu s depth vrstvou stejneho ramce) - zpracuj ho jako prvni,
            // pak pokracuj bezne rozkladem na obrazove vrstvy (RGB/Probability/Depth).
            if (msg is CameraFrame f && f.Grid != null)
                IngestGrid(f.Name ?? string.Empty, f.Grid, f.TimeStamp);


            foreach (var layer in MessageImageLayers.Extract(msg))
            {
                bool isNew = !registry.ContainsKey(layer.Name);
                registry[layer.Name] = layer;
                if (isNew)
                {
                    Layers.Add(layer.Name);
                    AutoSelect(layer);
                }

                // Zapamatuj rozmer depth vrstvy (pro rasterizaci gridu stejne kamery).
                if (layer.Kind == LayerKind.Depth && layer.Depth != null && layer.Name.EndsWith(DepthSuffix))
                {
                    string cam = layer.Name.Substring(0, layer.Name.Length - DepthSuffix.Length);
                    depthSizeByCam[cam] = (layer.Depth.Width, layer.Depth.Height);
                    if (gridByCam.TryGetValue(cam, out var pending))
                        RegisterGridOverlay(cam, pending.grid, pending.ts);
                }

                if (layer.Name == LeftLayer) RenderSlot(Slot.Left, layer);
                if (layer.Name == RightLayer) RenderSlot(Slot.Right, layer);
                if (layer.Name == LeftOverlayLayer) RenderSlot(Slot.LeftOverlay, layer);
                if (layer.Name == RightOverlayLayer) RenderSlot(Slot.RightOverlay, layer);
            }

            // Detekovane hranice cesty jako vlastni overlay vrstva "<kamera>/Hranice". Slouzi
            // k VIZUALNI kontrole detektoru: statistika nad zaznamem rekla, ze vzdalena cast
            // hranice je vedle, ale ne PROC - to je videt az na obraze. Viz
            // doc/map-correlation-localization.md.
            //
            // AZ TADY, ne pred rozkladem na vrstvy: AssignBaseLayer nize by jinak na prvnim snimku
            // nastavil podklad na "<kamera>/RGB" driv, nez ta vrstva vubec je v Layers - a combo
            // si SelectedItem mimo ItemsSource srazi na null, cimz zhasne i podkladovy panel.
            if (msg is CameraFrame fe && fe.PathEdges != null && fe.PathEdges.Count > 0)
                IngestEdges(fe.Name ?? string.Empty, fe, fe.TimeStamp);

            EnsureDefaultOverlays();
        }

        private const string DepthSuffix = "/Depth";

        private void IngestGrid(string cam, PolarTraversabilityGrid grid, DateTime ts)
        {
            gridByCam[cam] = (grid, ts);
            // Rasterizovat lze az kdyz znam rozmer depth snimku te kamery (jinak pockame na nej).
            if (depthSizeByCam.ContainsKey(cam))
                RegisterGridOverlay(cam, grid, ts);
        }

        private void RegisterGridOverlay(string cam, PolarTraversabilityGrid grid, DateTime ts)
        {
            if (!depthSizeByCam.TryGetValue(cam, out var sz)) return;
            var bmp = RenderGridOverlay(grid, sz.w, sz.h);
            if (bmp == null) return;

            string name = cam + TraversabilitySuffix;
            prerendered[name] = bmp;
            string info = string.Format(CultureInfo.InvariantCulture, "{0}  {1:HH:mm:ss.fff}  Δ{2:F0} ms",
                name, ts, (DateTime.Now - ts).TotalMilliseconds);

            if (!Layers.Contains(name))
            {
                Layers.Add(name);
                // Podklad: depth TEZE kamery, kdyz je jeji panel jeste volny. Overlay uz resi
                // EnsureDefaultOverlays podle kamery panelu - jinak by grid skoncil nad cizi kamerou.
                AssignBaseLayer(cam + DepthSuffix);
            }

            if (name == LeftLayer) SetSlotImage(Slot.Left, bmp, info);
            if (name == RightLayer) SetSlotImage(Slot.Right, bmp, info);
            if (name == LeftOverlayLayer) SetSlotImage(Slot.LeftOverlay, bmp, info);
            if (name == RightOverlayLayer) SetSlotImage(Slot.RightOverlay, bmp, info);
        }

        private const string EdgesSuffix = "/Hranice";

        /// <summary>
        /// Posledni hranice per vrstva - kvuli dorenderovani pri prvnim vyberu vrstvy.
        /// Seznam <see cref="PathEdge"/> je per snimek cerstvy (viz <c>CameraFramePool</c>),
        /// takze drzet na nej referenci je bezpecne.
        /// </summary>
        private readonly Dictionary<string, (List<PathEdge> Edges, int Width, int Height, DateTime Ts)> lastEdges
            = new Dictionary<string, (List<PathEdge>, int, int, DateTime)>();

        /// <summary>
        /// Vyrenderuje detekovane hranice cesty do overlay vrstvy nad BAREVNYM obrazem — sloupce
        /// <see cref="PathEdge.Left"/>/<see cref="PathEdge.Right"/> jsou v souradnicich barevneho
        /// snimku (tamtez je hleda detektor).
        /// </summary>
        private void IngestEdges(string cam, CameraFrame frame, DateTime ts)
        {
            var rgb = frame.ImageRGB;
            if (rgb == null) return;

            string name = cam + EdgesSuffix;
            lastEdges[name] = (frame.PathEdges, rgb.Width, rgb.Height, ts);

            if (!Layers.Contains(name))
            {
                Layers.Add(name);
                AssignBaseLayer(cam + "/RGB");
            }

            // Rendruje se JEN kdyz je vrstva nekde vybrana - jinak by kazdy snimek alokoval
            // bitmapu 640x480x4, tedy ~1 MB pri 30 Hz, jen proto, ze by ji nekdo MOHL chtit videt.
            if (name != LeftLayer && name != RightLayer
                && name != LeftOverlayLayer && name != RightOverlayLayer)
                return;

            var bmp = RenderEdgesOverlay(frame.PathEdges, rgb.Width, rgb.Height, out int marks, out int missing);
            if (bmp == null) return;

            prerendered[name] = bmp;
            string info = string.Format(CultureInfo.InvariantCulture,
                "{0}  {1:HH:mm:ss.fff}  {2} radku, {3} znacek, {4} bez bodu",
                name, ts, frame.PathEdges.Count, marks, missing);

            if (name == LeftLayer) SetSlotImage(Slot.Left, bmp, info);
            if (name == RightLayer) SetSlotImage(Slot.Right, bmp, info);
            if (name == LeftOverlayLayer) SetSlotImage(Slot.LeftOverlay, bmp, info);
            if (name == RightOverlayLayer) SetSlotImage(Slot.RightOverlay, bmp, info);
        }

        /// <summary>
        /// Hranice jako barevne znacky: <b>modra</b> = leva, <b>oranzova</b> = prava,
        /// <b>fialova</b> = sloupec detekovany, ale metricky bod nevznikl (chybi hloubka).
        ///
        /// <para>Vypadky se kresli jako <b>siroka vodorovna cara</b>, ne jako tecka. Duvod: nad
        /// zaznamem jich je ~25 % vsech sloupcu, ale rozstrikane po cele hranici a pri 50%
        /// pruhlednosti overlaye je 3px tecka jine barvy okem nerozeznatelna — vypadalo to, ze
        /// zadne vypadky nejsou. Jejich POCET je proto i v popisce panelu.</para>
        /// </summary>
        private static WriteableBitmap RenderEdgesOverlay(List<PathEdge> edges, int w, int h,
                                                          out int marks, out int missing)
        {
            marks = 0; missing = 0;
            if (edges == null || w <= 0 || h <= 0) return null;

            var buf = new byte[w * h * 4];   // Bgra8888, vynulovano = pruhledne

            int drawn = 0, bad = 0;
            void Mark(int x, int y, byte b, byte g, byte r, int halfWidth)
            {
                if (x >= 0 && x < w && y >= 0 && y < h) drawn++;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -halfWidth; dx <= halfWidth; dx++)
                    {
                        int px = x + dx, py = y + dy;
                        if (px < 0 || py < 0 || px >= w || py >= h) continue;
                        int o = (py * w + px) * 4;
                        buf[o] = b; buf[o + 1] = g; buf[o + 2] = r; buf[o + 3] = 235;
                    }
            }

            const int Dot = 1;       // bezna znacka: 3x3 px
            const int Dash = 5;      // vypadek: 11x3 px, aby byl nepreslechnutelny

            foreach (var e in edges)
            {
                if (e.Y < 0 || e.Y >= h) continue;

                if (e.Left.HasValue)
                {
                    if (e.LeftPoint.A != 0) Mark(e.Left.Value, e.Y, 0xF0, 0xAF, 0x4C, Dot);   // modra
                    else { Mark(e.Left.Value, e.Y, 0xE0, 0x40, 0xC0, Dash); bad++; }          // fialova
                }
                if (e.Right.HasValue)
                {
                    if (e.RightPoint.A != 0) Mark(e.Right.Value, e.Y, 0x4D, 0xB7, 0xFF, Dot); // oranzova
                    else { Mark(e.Right.Value, e.Y, 0xE0, 0x40, 0xC0, Dash); bad++; }
                }
            }

            marks = drawn;
            missing = bad;
            DiagBitmapsCreated++;
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(buf, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
            }
            return bmp;
        }

        /// <summary>Rozumne vychozi prirazeni slotu pri objeveni nove vrstvy.</summary>
        private void AutoSelect(ImageLayer layer)
        {
            if (layer.Kind == LayerKind.Color)
                AssignBaseLayer(layer.Name);
            // Probability se prirazuje az v EnsureDefaultOverlays - musi znat kameru podkladu.
        }

        /// <summary>
        /// Prirad podkladovou vrstvu do leveho/praveho panelu podle JMENA kamery, ne podle poradi
        /// prichodu snimku. Bez toho zalezi na tom, ci snimek dorazi driv, takze levy panel klidne
        /// ukazuje pravou kameru a mezi bezy si strany prohazuji.
        /// Zdroj s jinym jmenem (jedina kamera, backproject) padne do prvniho volneho panelu.
        /// </summary>
        private void AssignBaseLayer(string name)
        {
            string cam = CameraOf(name) ?? name;
            bool left = cam.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0;
            bool right = cam.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0;

            if (left && !right)
            {
                if (string.IsNullOrEmpty(LeftLayer)) LeftLayer = name;
                return;
            }
            if (right && !left)
            {
                if (string.IsNullOrEmpty(RightLayer)) RightLayer = name;
                return;
            }

            if (string.IsNullOrEmpty(LeftLayer)) LeftLayer = name;
            else if (string.IsNullOrEmpty(RightLayer) && name != LeftLayer) RightLayer = name;
        }

        /// <summary>
        /// Doplni vychozi overlay tak, aby patril STEJNE kamere jako podklad na te strane
        /// (nad pravou kamerou tedy right/probability, ne left/probability).
        /// <para>Deje se to az tady, a ne v <see cref="AutoSelect"/> pri objeveni vrstvy: poradi
        /// prichodu vrstev neni zarucene a probability muze dorazit driv nez barva, ktera teprve
        /// urci, ktera kamera je vlevo a ktera vpravo. Volani je levne - jakmile jsou oba overlaye
        /// obsazene, hned se vrati.</para>
        /// </summary>
        private void EnsureDefaultOverlays()
        {
            if (string.IsNullOrEmpty(LeftOverlayLayer))
            {
                string name = FindOverlayFor(LeftLayer);
                if (name != null) LeftOverlayLayer = name;
            }
            if (string.IsNullOrEmpty(RightOverlayLayer))
            {
                string name = FindOverlayFor(RightLayer);
                if (name != null) RightOverlayLayer = name;
            }
        }

        /// <summary>
        /// Najde overlay pro panel s podkladem <paramref name="baseLayer"/>: vrstvu TEHOZ zdroje
        /// (kamery), prednostne probability, jinak grid sjizdnosti.
        /// </summary>
        private string FindOverlayFor(string baseLayer)
        {
            string cam = CameraOf(baseLayer);
            if (cam == null) return null;

            string grid = null;
            foreach (var kv in registry)
            {
                if (!string.Equals(CameraOf(kv.Key), cam, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsProbability(kv.Value)) return kv.Key;
            }
            // Grid sjizdnosti neni v registry (rasterizuje se zvlast) - hledej mezi nazvy vrstev.
            foreach (string n in Layers)
                if (n.EndsWith(TraversabilitySuffix, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(CameraOf(n), cam, StringComparison.OrdinalIgnoreCase))
                { grid = n; break; }
            return grid;
        }

        /// <summary>Nazev zdroje (kamery) z nazvu vrstvy <c>"&lt;kamera&gt;/&lt;vrstva&gt;"</c>; null, kdyz ho nema.</summary>
        private static string CameraOf(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return null;
            int i = layerName.IndexOf('/');
            return i > 0 ? layerName.Substring(0, i) : null;
        }

        private static bool IsProbability(ImageLayer layer)
            => layer.Kind == LayerKind.Probability
               || layer.Name.IndexOf("backproject", StringComparison.OrdinalIgnoreCase) >= 0;

        partial void OnLeftLayerChanged(string value) => RenderFromRegistry(Slot.Left, value);
        partial void OnRightLayerChanged(string value) => RenderFromRegistry(Slot.Right, value);
        partial void OnLeftOverlayLayerChanged(string value) => RenderFromRegistry(Slot.LeftOverlay, value);
        partial void OnRightOverlayLayerChanged(string value) => RenderFromRegistry(Slot.RightOverlay, value);

        // ---------------- pixel pod kurzorem ----------------

        /// <summary>
        /// Ohlasi polohu kurzoru nad panelem v souradnicich PIXELU zdrojoveho obrazu (prepocet z
        /// pozice v ovladacim prvku dela View - zna rozmery a zpusob roztazeni). Vypise hodnotu
        /// pixelu z podkladu i z overlaye, aby slo srovnat treba RGB proti pravdepodobnosti sjizdnosti.
        /// </summary>
        public void UpdateCursor(bool right, int x, int y)
        {
            string info = BuildCursorInfo(right ? RightLayer : LeftLayer,
                                          right ? RightOverlayLayer : LeftOverlayLayer, x, y);
            if (right) RightCursorInfo = info;
            else LeftCursorInfo = info;
        }

        /// <summary>Kurzor opustil panel (nebo je mimo obraz).</summary>
        public void ClearCursor(bool right)
        {
            if (right) RightCursorInfo = "";
            else LeftCursorInfo = "";
        }

        private string BuildCursorInfo(string baseName, string overlayName, int x, int y)
        {
            string b = DescribePixel(baseName, x, y);
            if (b == null) return "";   // mimo obraz nebo vrstva neni k dispozici

            string o = DescribePixel(overlayName, x, y);
            return o == null ? $"[{x},{y}]  {b}" : $"[{x},{y}]  {b}   |   {o}";
        }

        /// <summary>
        /// Hodnota pixelu dane vrstvy jako text; null, kdyz vrstva neexistuje nebo je bod mimo ni.
        /// <para>Cte se ze <see cref="registry"/>, tedy z TEHOZ zdroje, ze ktereho se panel
        /// vykresluje (stejne jako <see cref="RenderFromRegistry"/> pri prepnuti comba). Grid
        /// sjizdnosti se rasterizuje zvlast a v registry neni - u nej se hodnota nehlasi.</para>
        /// </summary>
        private string DescribePixel(string layerName, int x, int y)
        {
            if (string.IsNullOrEmpty(layerName)) return null;
            if (!registry.TryGetValue(layerName, out var layer) || layer == null) return null;
            if (x < 0 || y < 0 || x >= layer.Width || y >= layer.Height) return null;

            try
            {
                switch (layer.Kind)
                {
                    case LayerKind.Color when layer.Color != null:
                        var c = layer.Color[x, y];
                        return $"RGB {c.R},{c.G},{c.B}";

                    case LayerKind.Probability when layer.Gray != null:
                        return $"p {layer.Gray[x, y].Value}";

                    case LayerKind.Depth when layer.Depth != null:
                        int mm = layer.Depth[x, y].Value;
                        return mm > 0 ? $"{mm} mm" : "bez hloubky";
                }
            }
            catch
            {
                // Buffer snimku se mezitim vratil do poolu a prepsal - hodnota proste neni.
                return null;
            }
            return null;
        }

        private enum Slot { Left, Right, LeftOverlay, RightOverlay }

        private void RenderFromRegistry(Slot slot, string name)
        {
            // Hranice se rendruji az kdyz je nekdo chce videt - pri PRVNIM vyberu tedy jeste
            // v prerendered nic neni. Bez tohoto dorenderovani by panel zustal prazdny az do
            // dalsiho snimku, a ve View (pauza) uz zadny dalsi nemusi prijit.
            if (!string.IsNullOrEmpty(name) && !prerendered.ContainsKey(name)
                && name.EndsWith(EdgesSuffix, StringComparison.Ordinal)
                && lastEdges.TryGetValue(name, out var e))
            {
                var edgeBmp = RenderEdgesOverlay(e.Edges, e.Width, e.Height, out _, out _);
                if (edgeBmp != null) prerendered[name] = edgeBmp;
            }

            if (!string.IsNullOrEmpty(name) && prerendered.TryGetValue(name, out var bmp))
                SetSlotImage(slot, bmp, name);
            else if (!string.IsNullOrEmpty(name) && registry.TryGetValue(name, out var layer))
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
            DiagBitmapsCreated++;
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
            DiagBitmapsCreated++;
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
            DiagBitmapsCreated++;
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

        /// <summary>
        /// Rasterizuje grid sjizdnosti do overlaye velikosti depth snimku (per-pixel alfa, prazdno =
        /// pruhledne). Zarovnani: azimut = skupina <see cref="PolarTraversabilityGrid.ColumnsPerCell"/>
        /// sloupcu (trim dopocten z sirky), radialne = pasmo radku <see cref="RadialEdge.Row"/>.
        /// Unknown/prazdne bunky se nevykresluji (depth pak prosvita).
        /// </summary>
        private static WriteableBitmap RenderGridOverlay(PolarTraversabilityGrid g, int w, int h)
        {
            if (g?.Cells == null || g.RadialEdges == null || w <= 0 || h <= 0) return null;
            int N = g.ColumnsPerCell, A = g.AzimuthCount, R = g.RadialCount;
            if (N <= 0 || A <= 0 || R <= 0) return null;

            int trim = Math.Max(0, (w - A * N) / 2);
            var buf = new byte[w * h * 4];   // Bgra8888, vynulovano = pruhledne

            for (int a = 0; a < A; a++)
            {
                int x0 = Math.Max(0, a * N + trim);
                int x1 = Math.Min(w, a * N + N + trim);
                if (x0 >= x1) continue;

                for (int r = 0; r < R; r++)
                {
                    var cell = g.Cells[a * R + r];
                    if (cell.Count <= 0 || cell.Class == TraversabilityClass.Unknown) continue;

                    int rowNear = g.RadialEdges[r].Row;
                    int rowFar = g.RadialEdges[r + 1].Row;
                    if (rowNear < 0 || rowFar < 0) continue;
                    int y0 = Math.Max(0, Math.Min(rowNear, rowFar));
                    int y1 = Math.Min(h, Math.Max(rowNear, rowFar));
                    if (y0 >= y1) continue;

                    byte b, gc, rc;
                    if (cell.Class == TraversabilityClass.Obstacle) { b = 0x35; gc = 0x39; rc = 0xE5; }
                    else { b = 0x50; gc = 0xAF; rc = 0x4C; }
                    byte alpha = (byte)Math.Clamp(60f + 160f * cell.Confidence, 0f, 255f);

                    for (int y = y0; y < y1; y++)
                    {
                        int rowBase = y * w * 4;
                        for (int x = x0; x < x1; x++)
                        {
                            int o = rowBase + x * 4;
                            buf[o] = b; buf[o + 1] = gc; buf[o + 2] = rc; buf[o + 3] = alpha;
                        }
                    }
                }
            }

            DiagBitmapsCreated++;
            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = bmp.Lock())
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(buf, y * w * 4, fb.Address + y * fb.RowBytes, w * 4);
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
