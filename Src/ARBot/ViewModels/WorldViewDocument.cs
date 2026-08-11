using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Osm;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using ARBot.Common.Occupancy;
using NetTopologySuite.Geometries;
using BruTile.MbTiles;
using SkiaSharp;
using Color = Mapsui.Styles.Color;   // rozlisit od ARBot.Common.Common.Color

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci dokument se <b>svetovym (world) pohledem</b>: mapa (Mapsui) s prepinatelnym podkladem
    /// a vrstvami dat ze <see cref="ARBot.Robot.ARBotRuntime.Stream"/>. Analogie
    /// <see cref="RobotCentricDocument"/>, ale v geografickem ramci (WGS84 / Web Mercator).
    ///
    /// Vrstvy (kazda samostatne vypinatelna, vcetne podkladove):
    /// <list type="bullet">
    /// <item><b>Podklad</b> - OSM (online) / offline MBTiles / zadny. Kdyz je <see cref="ShowBaseMap"/> vypnuty
    ///   nebo je zdroj <see cref="BaseMap.None"/>, zadna dlazdicova vrstva se nevytvori =&gt; na OrangePI
    ///   <b>zadne pokusy o komunikaci po internetu</b>.</item>
    /// <item><b>Poloha + kurz</b> - z <see cref="GPSState"/> (poloha) a <see cref="RobotStateMsg"/> (kurz).</item>
    /// <item><b>Trajektorie</b> - stopa ujete drahy z GPS fixu.</item>
    /// <item><b>Trasa / graf</b> - hrany z <see cref="GraphNavigationMsg"/> (OsmNav; zatim se na Stream neemituje).</item>
    /// <item><b>Znacky</b> - start / cil / vysledek z <see cref="GraphNavigationMsg"/>.</item>
    /// </list>
    ///
    /// Zpravy prijima jako <see cref="IMessageSink"/> ze Streamu (Run i View), backpressure "latest-wins +
    /// Background flush" (viz Views/README.md). Mapsui <see cref="Mapsui.Map"/> vlastni tento ViewModel;
    /// View mu ho v code-behind priradi do <c>MapControl.Map</c> (mimo design-time).
    /// </summary>
    public partial class WorldViewDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.WorldViewDocumentView);

        /// <summary>Zdroj mapoveho podkladu.</summary>
        public enum BaseMap { None, OpenStreetMap, OfflineMbTiles }

        /// <summary>Polozka comboboxu podkladu (ToString =&gt; cesky popisek).</summary>
        public sealed class BaseMapChoice
        {
            public BaseMap Value { get; }
            public string Label { get; }
            public BaseMapChoice(BaseMap value, string label) { Value = value; Label = label; }
            public override string ToString() => Label;
        }

        // Web Mercator (EPSG:3857) rozliseni [m/px] na zoom urovni z: 156543.03392 / 2^z.
        private const double Merc0 = 156543.03392;
        private const double InitialResolution = Merc0 / (1 << 16);   // ~zoom 16

        // Export MBTiles: rozsah zoomu, tvrdy strop poctu dlazdic (ochrana pred obrim stahovanim
        // a proti OSM tile usage policy) a setrne tempo mezi requesty.
        private const int ExportMinZoom = 13;
        private const int ExportMaxZoom = 19;
        private const long MaxExportTiles = 5000;
        private const int ExportThrottleMs = 100;

        // Sdileny HttpClient s korektnim User-Agent (OSM tile usage policy).
        private static readonly HttpClient Http = CreateHttp();
        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("ARBot3/1.0 (autonomni robot; offline map export; +https://openstreetmap.org)");
            return h;
        }

        private CancellationTokenSource? exportCts;

        // sqlite-net (z BruTile.MbTiles) vyzaduje jednorazovou inicializaci nativniho SQLite provideru,
        // jinak pri prvnim SQLiteConnection padne "You need to call SQLitePCL.raw.SetProvider()".
        // Bundle balicek se transitivne nepritahl -> nastavime provider explicitne (jednou).
        private static int sqliteInitialized;
        private static void EnsureSqliteProvider()
        {
            if (Interlocked.Exchange(ref sqliteInitialized, 1) != 0) return;
            try { SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3()); } catch { }
        }

        private readonly bool designMode;
        private readonly List<IDisposable> feeds = new List<IDisposable>();

        // --- Backpressure: nejnovejsi zpravy per typ, koalescovany flush na UI (viz Views/README.md) ---
        private readonly object gate = new object();
        private GPSState? pendingGps;
        private RobotStateMsg? pendingRobot;
        private GraphNavigationMsg? pendingGraph;
        private MapMsg? pendingMap;
        private OccupancyGridMsg? pendingOccupancy;
        private LocalPlanMsg? pendingPlan;
        private volatile bool updateQueued;

        // Aktualni (posledni zpracovane) zpravy pouzite napric flushi.
        private GPSState? lastGps;
        private RobotStateMsg? lastRobot;
        private GraphNavigationMsg? lastGraph;
        private MapMsg? lastMap;
        private OccupancyGridMsg? lastOccupancy;
        private LocalPlanMsg? lastPlan;

        // Trajektorie (stopa) v Web Mercator metrech; capovana delka.
        private const int MaxTrackPoints = 5000;
        private const double MinTrackStepMeters = 0.5;
        private readonly List<MPoint> track = new List<MPoint>();

        private bool initialCentered;

        // --- Mapsui model + vrstvy (vlastni tento ViewModel) ---
        /// <summary>Mapsui mapa - View ji priradi do MapControl.Map (mimo design-time).</summary>
        public Map Map { get; } = new Map();

        private readonly MemoryLayer robotLayer = new MemoryLayer("Poloha");
        private readonly MemoryLayer trajectoryLayer = new MemoryLayer("Trajektorie");
        private readonly MemoryLayer routeLayer = new MemoryLayer("Trasa");
        private readonly MemoryLayer markerLayer = new MemoryLayer("Znacky");
        private readonly MemoryLayer mapLayer = new MemoryLayer("Mapa");   // sit z OsmNav (MapMsg)
        private readonly MemoryLayer occupancyLayer = new MemoryLayer("Occupancy");   // lokalni mapa (rastr)
        private readonly MemoryLayer planLayer = new MemoryLayer("Plan");             // lokalni plan + cil

        private ILayer? osmLayer;            // cache OSM dlazdicove vrstvy (aby se pri toggle neztracela cache)
        private ILayer? offlineLayer;       // cache offline (MBTiles) vrstvy pro aktualni cestu
        private string? offlineLayerPath;   // cesta, pro kterou je offlineLayer postaveny

        // --- Prepinace vrstev (bindovane z View) ---
        [ObservableProperty] private bool showBaseMap = true;
        [ObservableProperty] private bool showRobot = true;
        [ObservableProperty] private bool showTrajectory = true;
        [ObservableProperty] private bool showRoute = true;
        [ObservableProperty] private bool showMarkers = true;

        /// <summary>Vrstva: lokalni mapa (occupancy grid) - akumulovana sjizdnost okolo robotu.</summary>
        [ObservableProperty] private bool showOccupancy = true;

        /// <summary>Vrstva: lokalni plan (draha + cil).</summary>
        [ObservableProperty] private bool showPlan = true;

        /// <summary>Vrstva: silnicni sit nactena z OsmNav (MapMsg).</summary>
        [ObservableProperty] private bool showMap = true;

        /// <summary>Stav nacitani OSM mapy (progress / vysledek / chyba).</summary>
        [ObservableProperty] private string mapStatus = string.Empty;

        /// <summary>Vychozi sirka cesty [m] pri nacitani OSM (pouzije se, kdyz nema tag <c>width</c>).</summary>
        [ObservableProperty] private decimal defaultRoadWidthMeters = 2m;

        /// <summary>Sledovat robota (centrovat mapu na jeho polohu pri kazde aktualizaci).</summary>
        [ObservableProperty] private bool follow = true;

        /// <summary>Rozbaleny ovladaci panel; sbaleny zabira jen prepinaci tlacitko a nebrani mape.</summary>
        [ObservableProperty] private bool panelExpanded = true;

        /// <summary>Probiha export MBTiles (tlacitko je po dobu exportu zakazane).</summary>
        [ObservableProperty] private bool exporting;

        /// <summary>Stav exportu MBTiles (progress / vysledek / chyba).</summary>
        [ObservableProperty] private string exportStatus = string.Empty;

        /// <summary>Nabidka podkladu pro combobox.</summary>
        public IReadOnlyList<BaseMapChoice> BaseMapChoices { get; }

        /// <summary>Vybrany podklad.</summary>
        [ObservableProperty] private BaseMapChoice selectedBaseMap;

        /// <summary>Cesta k offline MBTiles souboru (pro <see cref="BaseMap.OfflineMbTiles"/>).</summary>
        [ObservableProperty] private string mbTilesPath = string.Empty;

        /// <summary>Stavovy radek (poloha, fix, stari zpravy).</summary>
        [ObservableProperty] private string info = "Cekam na data…";

        /// <summary>Konstruktor pro design-time i runtime (bez sitovych vedlejsich efektu).</summary>
        public WorldViewDocument()
        {
            Id = "WorldView";
            Title = "World";

            designMode = Design.IsDesignMode;

            BaseMapChoices = new List<BaseMapChoice>
            {
                new BaseMapChoice(BaseMap.None, "Bez podkladu"),
                new BaseMapChoice(BaseMap.OpenStreetMap, "OpenStreetMap (online)"),
                new BaseMapChoice(BaseMap.OfflineMbTiles, "Offline (MBTiles)"),
            };

            // Vychozi podklad: na ARM (OrangePI) zadny (offline-first), jinak OSM. V design-time vzdy zadny.
#if IsARM64
            var defaultBase = BaseMap.None;
#else
            var defaultBase = designMode ? BaseMap.None : BaseMap.OpenStreetMap;
#endif
            selectedBaseMap = FindChoice(defaultBase);

            // Prazdne datove vrstvy (naplni je Flush). Podklad + poradi resi RebuildLayers.
            RebuildLayers();
        }

        private BaseMapChoice FindChoice(BaseMap b)
        {
            foreach (var c in BaseMapChoices) if (c.Value == b) return c;
            return BaseMapChoices[0];
        }

        /// <summary>Pripoji zdroj/e zprav; dokument je pri zavreni zastavi (Dispose).</summary>
        public void AttachFeed(params IDisposable[] disposables)
        {
            if (disposables != null)
                feeds.AddRange(disposables);
        }

        /// <summary>
        /// Zadani cile lokalniho planovace [m, world ENU]. Nastavuje ten, kdo dokument zaklada
        /// (viz <c>MainWindowViewModel</c>) - dokument sam runtime nezna.
        /// </summary>
        public Action<double, double>? GoalRequested { get; set; }

        /// <summary>
        /// Prevede kliknuti do mapy (Web Mercator) na cil lokalniho planovace. Prevod jde pres
        /// stejny <see cref="GeoReference"/>, kterym se kresli ostatni lokalni vrstvy - bez GPS fixu
        /// a pozy tedy cil zadat nelze (vraci false).
        /// </summary>
        public bool RequestGoalFromMercator(double mercX, double mercY)
        {
            var geoRef = BuildGeoReference();
            if (geoRef == null || GoalRequested == null) return false;

            var (lon, lat) = SphericalMercator.ToLonLat(mercX, mercY);
            var local = geoRef.ToLocal(LLA.FromDegrees(lat, lon));

            GoalRequested(local.X, local.Y);
            return true;
        }

        // ============================ IMessageSink (vlakno producenta) ============================
        public void Post(Message msg)
        {
            switch (msg)
            {
                case GPSState g: lock (gate) pendingGps = g; break;
                case RobotStateMsg r: lock (gate) pendingRobot = r; break;
                case GraphNavigationMsg gn: lock (gate) pendingGraph = gn; break;
                case MapMsg m: lock (gate) pendingMap = m; break;
                case OccupancyGridMsg og: lock (gate) pendingOccupancy = og; break;
                case LocalPlanMsg lp: lock (gate) pendingPlan = lp; break;
                default: return;   // ostatni zpravy nas nezajimaji
            }

            if (updateQueued) return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        // ============================ Flush + render (UI vlakno) ============================
        private void Flush()
        {
            updateQueued = false;

            GPSState? gps; RobotStateMsg? robot; GraphNavigationMsg? graph; MapMsg? map;
            OccupancyGridMsg? occupancy; LocalPlanMsg? plan;
            lock (gate)
            {
                gps = pendingGps; pendingGps = null;
                robot = pendingRobot; pendingRobot = null;
                graph = pendingGraph; pendingGraph = null;
                map = pendingMap; pendingMap = null;
                occupancy = pendingOccupancy; pendingOccupancy = null;
                plan = pendingPlan; pendingPlan = null;
            }

            if (gps != null) lastGps = gps;
            if (robot != null) lastRobot = robot;
            if (graph != null) lastGraph = graph;
            if (occupancy != null) lastOccupancy = occupancy;
            if (plan != null) lastPlan = plan;

            // Mapa (sit) je staticka a muze byt velka - featury prestavuj JEN kdyz prisla nova mapa.
            if (map != null)
            {
                lastMap = map;
                UpdateMapFeature(map);
                mapLayer.DataHasChanged();
                CenterOnMapIfNeeded(map);
            }

            MPoint? robotMerc = null;

            // --- Poloha + kurz (GPS poloha, kurz z fuze nebo z GPS) ---
            if (lastGps != null && IsValidFix(lastGps))
            {
                var (mx, my) = SphericalMercator.FromLonLat(lastGps.Longitude, lastGps.Latitude);
                robotMerc = new MPoint(mx, my);

                double? headingRad = lastRobot?.Theta ?? lastGps.DynamicOrientation ?? lastGps.Orientation;
                UpdateRobotFeature(robotMerc, lastGps.Latitude, headingRad);

                AppendTrack(robotMerc);
            }

            UpdateTrajectoryFeature();

            // --- Trasa/graf + znacky (lokalni ENU -> LLA pres GeoReference zarovnany na GPS) ---
            var geoRef = BuildGeoReference();
            UpdateRouteAndMarkers(lastGraph, geoRef);

            // --- Lokalni mapa + plan (take v lokalnim ENU -> stejny GeoReference) ---
            // Prestavuj JEN kdyz prisla nova zprava - occupancy je rastr 256x256 (prekodovani do PNG).
            if (occupancy != null) UpdateOccupancyFeature(occupancy, geoRef);
            if (plan != null || occupancy != null) UpdatePlanFeature(lastPlan, geoRef);

            // Prekresli data v aktualne pripojenych vrstvach.
            robotLayer.DataHasChanged();
            trajectoryLayer.DataHasChanged();
            routeLayer.DataHasChanged();
            markerLayer.DataHasChanged();
            occupancyLayer.DataHasChanged();
            planLayer.DataHasChanged();

            // Prvni fix: vycentruj a zoomni; dale (kdyz Follow) drz robota v centru.
            if (robotMerc != null)
            {
                try
                {
                    if (!initialCentered)
                    {
                        Map.Navigator.CenterOnAndZoomTo(robotMerc, InitialResolution);
                        initialCentered = true;
                    }
                    else if (Follow)
                    {
                        Map.Navigator.CenterOn(robotMerc);
                    }
                }
                catch { /* Navigator jeste nema viewport (control nepripojen) - zkusi se priste */ }
            }

            Info = BuildInfo();
        }

        private static bool IsValidFix(GPSState g)
            => g.Quality != GPSState.FixQuality.Invalid
               && !double.IsNaN(g.Latitude) && !double.IsNaN(g.Longitude)
               && (g.Latitude != 0 || g.Longitude != 0);

        private void UpdateRobotFeature(MPoint merc, double latDeg, double? headingRad)
        {
            // Robota kreslime jako jeho SKUTECNY pudorys (sdileny RobotGlyph.OutlineMeters) = metricky
            // polygon, ktery se skaluje se zoomem (realna velikost robota). Lokalni obrys (lx vpravo,
            // ly vpred) se orotuje o kurz (matematicky uhel theta) do sveta ENU a prevede do Mercatoru.
            // Web Mercator zkresluje meritko o 1/cos(lat) -> pro realnou velikost nasobime timto faktorem.
            double theta = headingRad ?? Math.PI / 2;   // bez kurzu miri robot "na sever"
            double s = Math.Sin(theta), c = Math.Cos(theta);
            double k = 1.0 / Math.Cos(latDeg * Math.PI / 180.0);

            var outline = ARBot.Views.Controls.RobotGlyph.OutlineMeters;
            var ring = new Coordinate[outline.Count + 1];
            for (int i = 0; i < outline.Count; i++)
            {
                double lx = outline[i].lx, ly = outline[i].ly;
                double east = lx * s + ly * c;     // lokalni (vpravo/vpred) -> svet ENU (shodne s RobotGlyph)
                double north = -lx * c + ly * s;
                ring[i] = new Coordinate(merc.X + east * k, merc.Y + north * k);
            }
            ring[outline.Count] = ring[0];   // uzavrit prstenec

            var gf = new GeometryFeature { Geometry = new Polygon(new LinearRing(ring)) };
            gf.Styles.Add(new VectorStyle
            {
                Fill = new Brush(new Color(0xFF, 0xEB, 0x3B, 0xD0)),   // zluta, mirne pruhledna
                Outline = new Pen(new Color(0x21, 0x21, 0x21), 1),
                Line = null,
            });
            robotLayer.Features = new IFeature[] { gf };
        }

        private void AppendTrack(MPoint merc)
        {
            if (track.Count > 0)
            {
                var last = track[track.Count - 1];
                double dx = merc.X - last.X, dy = merc.Y - last.Y;
                if (dx * dx + dy * dy < MinTrackStepMeters * MinTrackStepMeters)
                    return;   // pohyb pod prahem - neukladat (setri body a prekresleni)
            }
            track.Add(merc);
            if (track.Count > MaxTrackPoints)
                track.RemoveRange(0, track.Count - MaxTrackPoints);
        }

        private void UpdateTrajectoryFeature()
        {
            if (track.Count < 2) { trajectoryLayer.Features = Array.Empty<IFeature>(); return; }

            var coords = new Coordinate[track.Count];
            for (int i = 0; i < track.Count; i++)
                coords[i] = new Coordinate(track[i].X, track[i].Y);

            var gf = new GeometryFeature { Geometry = new LineString(coords) };
            gf.Styles.Add(new VectorStyle { Line = new Pen(new Color(0xFF, 0x98, 0x00), 3) });   // oranzova
            trajectoryLayer.Features = new IFeature[] { gf };
        }

        /// <summary>
        /// Zarovna lokalni ENU ramec (metry) na GPS: origin lokalni roviny se posune tak, aby aktualni
        /// lokalni poloha robotu (<see cref="RobotStateMsg.X"/>/<see cref="RobotStateMsg.Y"/>) odpovidala
        /// jeho GPS poloze. Pouziva se pro geo-umisteni trasy/grafu a znacek (ktere jsou v lokalnich
        /// metrech). Zarovnani je <b>aproximativni</b> (drifuje s GPS sumem) - pro vizualizaci staci.
        /// Vraci null, kdyz neni k dispozici GPS fix i lokalni poza.
        /// </summary>
        private GeoReference? BuildGeoReference()
        {
            if (lastGps == null || lastRobot == null || !IsValidFix(lastGps))
                return null;
            var gpsLLA = LLA.FromDegrees(lastGps.Latitude, lastGps.Longitude);
            var originLLA = new GeoReference(gpsLLA).ToLLA(-lastRobot.X, -lastRobot.Y);   // posun o -(X,Y)
            return new GeoReference(originLLA);
        }

        /// <summary>
        /// Vrstva lokalni mapy (occupancy grid). Grid je <b>osove srovnany se svetem</b> (ENU), takze
        /// se da vykreslit jako obycejny RASTR v obdelniku - v tomto pohledu se tedy NEROTUJE
        /// (na rozdil od robot-centrickeho pohledu, kde by se s kurzem tocil, coz je pro akumulovanou
        /// mapu matouci; proto je tato vrstva tady a ne tam).
        ///
        /// <para>256 x 256 bunek nelze delat jako featury - koduje se do PNG a vklada jako
        /// <see cref="MRaster"/>. Web Mercator je konformni, takze mala oblast (12,8 m) se do
        /// osove srovnaneho obdelniku mapuje bez znatelneho zkresleni.</para>
        /// </summary>
        private void UpdateOccupancyFeature(OccupancyGridMsg og, GeoReference? geoRef)
        {
            if (geoRef == null || og.Occ == null || og.Size <= 0)
            {
                occupancyLayer.Features = Array.Empty<IFeature>();
                return;
            }

            // Rohy gridu v lokalnim ENU -> Web Mercator.
            double x0 = og.OriginX * og.Resolution, y0 = og.OriginY * og.Resolution;
            double x1 = (og.OriginX + og.Size) * og.Resolution, y1 = (og.OriginY + og.Size) * og.Resolution;

            MPoint ToMerc(double lx, double ly)
            {
                var lla = geoRef.ToLLA(lx, ly);
                var (mx, my) = SphericalMercator.FromLonLat(
                    Conversions.Rad2Deg(lla.Longitude), Conversions.Rad2Deg(lla.Latitude));
                return new MPoint(mx, my);
            }

            var min = ToMerc(x0, y0);
            var max = ToMerc(x1, y1);
            var rect = new MRect(Math.Min(min.X, max.X), Math.Min(min.Y, max.Y),
                                 Math.Max(min.X, max.X), Math.Max(min.Y, max.Y));

            var png = EncodeOccupancyPng(og);
            if (png == null)
            {
                occupancyLayer.Features = Array.Empty<IFeature>();
                return;
            }

            occupancyLayer.Features = new IFeature[] { new RasterFeature(new MRaster(png, rect)) };
        }

        /// <summary>
        /// Zakoduje occupancy grid do PNG (BGRA, premultiplied): neprujezdne cervene, potvrzene volne
        /// zelene, nezname pruhledne. Radek 0 obrazu je SEVER (nejvyssi j) - rastr se kresli shora dolu.
        /// </summary>
        private static byte[]? EncodeOccupancyPng(OccupancyGridMsg og)
        {
            int n = og.Size;
            try
            {
                using var bmp = new SKBitmap(new SKImageInfo(n, n, SKColorType.Bgra8888, SKAlphaType.Premul));
                var pixels = new uint[n * n];
                for (int j = 0; j < n; j++)
                {
                    int row = (n - 1 - j) * n;   // otoceni: sever nahoru
                    for (int i = 0; i < n; i++)
                    {
                        pixels[row + i] = og.State(i, j) switch
                        {
                            CellState.Blocked => OccBlockedBgra,
                            CellState.Free => OccFreeBgra,
                            _ => 0u,             // Unknown = pruhledne
                        };
                    }
                }

                var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                    pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { bmp.InstallPixels(bmp.Info, handle.AddrOfPinnedObject(), bmp.Info.RowBytes); }
                finally { /* pixely se hned zakoduji, pak uz je nikdo nedrzi */ }

                using var image = SKImage.FromBitmap(bmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                handle.Free();
                return data.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WorldView: kodovani occupancy selhalo: {ex.Message}");
                return null;
            }
        }

        // Barvy occupancy rastru (BGRA premultiplied, jako u SKColorType.Bgra8888).
        private static readonly uint OccBlockedBgra = PremulBgra(0xE5, 0x39, 0x35, 0xB0);
        private static readonly uint OccFreeBgra = PremulBgra(0x4C, 0xAF, 0x50, 0x50);

        private static uint PremulBgra(byte r, byte g, byte b, byte a)
        {
            uint rr = (uint)(r * a / 255), gg = (uint)(g * a / 255), bb = (uint)(b * a / 255);
            return ((uint)a << 24) | (rr << 16) | (gg << 8) | bb;
        }

        /// <summary>Vrstva lokalniho planu: draha jako cara + cil jako bod.</summary>
        private void UpdatePlanFeature(LocalPlanMsg? plan, GeoReference? geoRef)
        {
            if (plan == null || geoRef == null)
            {
                planLayer.Features = Array.Empty<IFeature>();
                return;
            }

            Coordinate ToMerc(double lx, double ly)
            {
                var lla = geoRef.ToLLA(lx, ly);
                var (mx, my) = SphericalMercator.FromLonLat(
                    Conversions.Rad2Deg(lla.Longitude), Conversions.Rad2Deg(lla.Latitude));
                return new Coordinate(mx, my);
            }

            var features = new List<IFeature>();

            if (plan.WayPoints != null && plan.WayPoints.Length >= 2)
            {
                var coords = new Coordinate[plan.WayPoints.Length];
                for (int i = 0; i < plan.WayPoints.Length; i++)
                    coords[i] = ToMerc(plan.WayPoints[i].X, plan.WayPoints[i].Y);

                var line = new GeometryFeature { Geometry = new LineString(coords) };
                line.Styles.Add(new VectorStyle { Line = new Pen(new Color(0x42, 0xA5, 0xF5), 3) });
                features.Add(line);
            }

            // Cil (pozadovany, tedy i kdyz plan skoncil oriznuty na hranici gridu).
            var goal = ToMerc(plan.RequestedGoalX, plan.RequestedGoalY);
            var goalFeature = new GeometryFeature
            {
                Geometry = new NetTopologySuite.Geometries.Point(goal),
            };
            goalFeature.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.6,
                Fill = new Brush(new Color(0xFF, 0xD5, 0x4F)),
                Outline = new Pen(new Color(0x21, 0x21, 0x21), 1),
            });
            features.Add(goalFeature);

            planLayer.Features = features;
        }

        private void UpdateRouteAndMarkers(GraphNavigationMsg? gn, GeoReference? geoRef)
        {
            if (gn == null || geoRef == null)
            {
                routeLayer.Features = Array.Empty<IFeature>();
                markerLayer.Features = Array.Empty<IFeature>();
                return;
            }

            MPoint LocalToMerc(double localX, double localY)
            {
                var lla = geoRef.ToLLA(localX, localY);
                var (mx, my) = SphericalMercator.FromLonLat(
                    Conversions.Rad2Deg(lla.Longitude), Conversions.Rad2Deg(lla.Latitude));
                return new MPoint(mx, my);
            }

            // Hrany grafu / trasy - z vrcholu podle indexu From/To (Line2D je nekonecna primka bez koncu).
            var edges = new List<IFeature>();
            if (gn.Edges != null && gn.Vertexes != null)
            {
                int vc = gn.Vertexes.Count;
                foreach (var e in gn.Edges)
                {
                    if (e.From < 0 || e.To < 0 || e.From >= vc || e.To >= vc) continue;
                    var v1 = gn.Vertexes[e.From];
                    var v2 = gn.Vertexes[e.To];
                    var a = LocalToMerc(v1.X, v1.Y);
                    var b = LocalToMerc(v2.X, v2.Y);
                    var gf = new GeometryFeature
                    {
                        Geometry = new LineString(new[]
                        {
                            new Coordinate(a.X, a.Y),
                            new Coordinate(b.X, b.Y),
                        })
                    };
                    // Zvyraznena cesta jinak nez zbytek grafu.
                    var color = e.HightLight ? new Color(0x4C, 0xAF, 0x50)       // zelena - vybrana cesta
                              : e.Path ? new Color(0x90, 0xCA, 0xF9)             // svetle modra - trasa
                              : new Color(0x9E, 0x9E, 0x9E);                     // seda - graf
                    gf.Styles.Add(new VectorStyle { Line = new Pen(color, e.HightLight ? 4 : 2) });
                    edges.Add(gf);
                }
            }
            routeLayer.Features = edges;

            // Znacky: start / cil / vysledek.
            var markers = new List<IFeature>
            {
                MakeMarker(LocalToMerc(gn.StartX, gn.StartY), new Color(0x4C, 0xAF, 0x50)),   // start - zelena
                MakeMarker(LocalToMerc(gn.TargetX, gn.TargetY), new Color(0xE5, 0x39, 0x35)), // cil - cervena
            };
            if (gn.ResultX.HasValue && gn.ResultY.HasValue)
                markers.Add(MakeMarker(LocalToMerc(gn.ResultX.Value, gn.ResultY.Value), new Color(0x21, 0x96, 0xF3)));
            markerLayer.Features = markers;
        }

        /// <summary>
        /// Prestavi vrstvu mapy (sit z OsmNav) z <see cref="MapMsg"/>. Cesty se kresli jako <b>vyplnene pasy
        /// promenne sirky</b> (metricka sirka z uzlu): kazda hrana = lichobeznik (polovicni sirka na zacatku
        /// a konci dle sirky uzlu), kazdy uzel = kotouc (hladke napojeni v krizovatce). Vse se <b>sjednoti</b>
        /// do jednoho tvaru (uniformni pruhlednost + cisty vnejsi obrys). Souradnice jsou geograficke ->
        /// primo do Web Mercatoru; sirku [m] nasobime 1/cos(lat) (zkresleni Mercatoru). Cela vrstva je v JEDNE
        /// feature - efektivni i pro velkou sit (prestavuje se jen pri nove mape).
        /// </summary>
        private void UpdateMapFeature(MapMsg map)
        {
            if (map.Edges == null || map.Edges.Count == 0 || map.Nodes == null || map.Nodes.Count == 0)
            {
                mapLayer.Features = Array.Empty<IFeature>();
                return;
            }

            // Uzly -> Mercator + polovicni sirka v Mercator metrech (1/cos(lat) korekce).
            var pts = new MPoint[map.Nodes.Count];
            var halfW = new double[map.Nodes.Count];
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                var n = map.Nodes[i];
                var (mx, my) = SphericalMercator.FromLonLat(n.LonDeg, n.LatDeg);
                pts[i] = new MPoint(mx, my);
                double k = 1.0 / Math.Cos(n.LatDeg * Math.PI / 180.0);
                halfW[i] = 0.5 * n.WidthMeters * k;
            }

            var polys = new List<Polygon>(map.Edges.Count + map.Nodes.Count);

            // Hrany -> lichobezniky (promenna sirka po delce).
            foreach (var e in map.Edges)
            {
                if (e.From < 0 || e.To < 0 || e.From >= pts.Length || e.To >= pts.Length) continue;
                var a = pts[e.From]; var b = pts[e.To];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) continue;
                double px = -dy / len, py = dx / len;          // jednotkova kolmice
                double hA = halfW[e.From], hB = halfW[e.To];
                if (hA <= 0 && hB <= 0) continue;
                var ring = new[]
                {
                    new Coordinate(a.X + px * hA, a.Y + py * hA),
                    new Coordinate(b.X + px * hB, b.Y + py * hB),
                    new Coordinate(b.X - px * hB, b.Y - py * hB),
                    new Coordinate(a.X - px * hA, a.Y - py * hA),
                    new Coordinate(a.X + px * hA, a.Y + py * hA),
                };
                polys.Add(new Polygon(new LinearRing(ring)));
            }

            // Uzly -> kotouce (zaobli konce a vyplni klin v krizovatce = hladke napojeni).
            for (int i = 0; i < pts.Length; i++)
                AddDisc(polys, pts[i].X, pts[i].Y, halfW[i]);

            if (polys.Count == 0) { mapLayer.Features = Array.Empty<IFeature>(); return; }

            // Sjednoceni prekryvu -> uniformni vypln + jeden vnejsi obrys (jinak by se prekryvy scitaly v alfe).
            NetTopologySuite.Geometries.Geometry geom;
            try { geom = new MultiPolygon(polys.ToArray()).Union(); }
            catch { geom = new MultiPolygon(polys.ToArray()); }

            var gf = new GeometryFeature { Geometry = geom };
            gf.Styles.Add(new VectorStyle
            {
                Fill = new Brush(new Color(0x7E, 0x57, 0xC2, 0xA0)),   // fialova, polopruhledna
                Outline = new Pen(new Color(0x4A, 0x2F, 0x8F), 1),     // tmavsi obrys site
                Line = null,
            });
            mapLayer.Features = new IFeature[] { gf };
        }

        // Kotouc (n-uhelnik) jako Polygon - pro zaobleni uzlu/konce cesty.
        private static void AddDisc(List<Polygon> polys, double cx, double cy, double r)
        {
            if (r <= 0) return;
            const int n = 12;
            var ring = new Coordinate[n + 1];
            for (int i = 0; i < n; i++)
            {
                double ang = 2.0 * Math.PI * i / n;
                ring[i] = new Coordinate(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
            }
            ring[n] = ring[0];
            polys.Add(new Polygon(new LinearRing(ring)));
        }

        /// <summary>Pri prvnim nacteni mapy (a bez GPS fixu) vycentruje/zoomne na rozsah site.</summary>
        private void CenterOnMapIfNeeded(MapMsg map)
        {
            if (initialCentered || map.Nodes == null || map.Nodes.Count == 0) return;

            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            foreach (var n in map.Nodes)
            {
                var (mx, my) = SphericalMercator.FromLonLat(n.LonDeg, n.LatDeg);
                if (mx < minx) minx = mx; if (mx > maxx) maxx = mx;
                if (my < miny) miny = my; if (my > maxy) maxy = my;
            }
            if (maxx < minx || maxy < miny) return;

            try
            {
                double padX = Math.Max(1.0, (maxx - minx) * 0.05);
                double padY = Math.Max(1.0, (maxy - miny) * 0.05);
                Map.Navigator.ZoomToBox(new MRect(minx - padX, miny - padY, maxx + padX, maxy + padY));
                initialCentered = true;
            }
            catch { /* viewport jeste nepripraven - zkusi se pri dalsi mape */ }
        }

        /// <summary>
        /// Nacte OSM mapu ze souboru <c>.osm</c>, sestavi <see cref="ARBot.Common.Maps.OsmNav.Graph.RoadNetwork"/>
        /// (pesi profil), zkonvertuje na <see cref="MapMsg"/> a posle do dokumentu (stejnou cestou jako ze
        /// streamu). Parsovani + stavba grafu bezi na pozadi. Vola se z View (po vyberu souboru).
        /// </summary>
        public async Task LoadOsmMapAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            MapStatus = "Načítám mapu…";
            double defaultWidth = (double)DefaultRoadWidthMeters;   // ctem na UI vlakne
            try
            {
                var msg = await Task.Run(() =>
                {
                    using var fs = File.OpenRead(path);
                    var data = OsmXmlReader.Read(fs);
                    var net = GraphBuilder.BuildNetwork(data, TravelProfile.Pedestrian(), defaultWidth);
                    return net.ToLogMessage(Path.GetFileName(path));
                });

                ShowMap = true;    // at je vrstva videt
                Post(msg);         // projde Flushem -> vykresli + vycentruje (kdyz jeste neni fix)
                MapStatus = string.Format(CultureInfo.InvariantCulture,
                    "Mapa: {0} uzlů, {1} hran ({2})", msg.Nodes.Count, msg.Edges.Count, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MapStatus = "Chyba načtení mapy: " + ex.Message;
            }
        }

        private static IFeature MakeMarker(MPoint merc, Color fill)
        {
            var f = new PointFeature(merc);
            f.Styles.Add(new SymbolStyle
            {
                SymbolType = SymbolType.Ellipse,
                SymbolScale = 0.7,
                Fill = new Brush(fill),
                Outline = new Pen(Color.White, 2),
            });
            return f;
        }

        private string BuildInfo()
        {
            var sb = new System.Text.StringBuilder();
            if (lastGps != null)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "GPS: {0:F6}, {1:F6}  fix={2}  sat={3}",
                    lastGps.Latitude, lastGps.Longitude, lastGps.Quality, lastGps.NumberOfSatellites);
            }
            else sb.Append("GPS: —");

            if (lastRobot != null)
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "\nPoza: X={0:F2} Y={1:F2}  θ={2:F1}°  v={3:F2} m/s",
                    lastRobot.X, lastRobot.Y, Conversions.Rad2Deg(lastRobot.Theta), lastRobot.V);

            if (track.Count >= 2)
                sb.AppendFormat(CultureInfo.InvariantCulture, "\nStopa: {0} b.", track.Count);

            return sb.ToString();
        }

        // ============================ Export MBTiles z aktualniho vyrezu ============================

        /// <summary>
        /// Ulozi aktualni viditelny vyrez mapy jako <b>rastrovy MBTiles</b> (z13..z19) stazenim dlazdic
        /// z OpenStreetMap. Ma <b>tvrdy strop</b> poctu dlazdic (<see cref="MaxExportTiles"/>) a setrne tempo
        /// (User-Agent + prodleva) kvuli OSM tile usage policy. Po dokonceni nastavi <see cref="MbTilesPath"/>
        /// na vysledny soubor. Vola se z View (ktere si vyzada cilovou cestu pres Save dialog).
        /// </summary>
        public async Task ExportCurrentViewToMbTilesAsync(string filePath)
        {
            if (Exporting || string.IsNullOrWhiteSpace(filePath)) return;

            // Vyrez ctem na UI vlakne (Navigator drzi aktualni viewport z MapControlu).
            var vp = Map.Navigator.Viewport;
            double res = vp.Resolution, w = vp.Width, h = vp.Height, cx = vp.CenterX, cy = vp.CenterY;
            if (res <= 0 || w <= 0 || h <= 0) { ExportStatus = "Mapa není připravená (nejprve ji zobraz)."; return; }

            double minx = cx - w * res / 2, maxx = cx + w * res / 2;
            double miny = cy - h * res / 2, maxy = cy + h * res / 2;
            var (lonMin, latMin) = SphericalMercator.ToLonLat(minx, miny);
            var (lonMax, latMax) = SphericalMercator.ToLonLat(maxx, maxy);
            if (lonMin > lonMax) { var t = lonMin; lonMin = lonMax; lonMax = t; }
            if (latMin > latMax) { var t = latMin; latMin = latMax; latMax = t; }

            long total = CountTiles(lonMin, latMin, lonMax, latMax);
            if (total <= 0) { ExportStatus = "Prázdný výřez."; return; }
            if (total > MaxExportTiles)
            {
                ExportStatus = string.Format(CultureInfo.InvariantCulture,
                    "Příliš mnoho dlaždic ({0}). Přibliž mapu / zmenši výřez (max {1}).", total, MaxExportTiles);
                return;
            }

            Exporting = true;
            ExportStatus = string.Format(CultureInfo.InvariantCulture, "Export… 0/{0}", total);
            var cts = exportCts = new CancellationTokenSource();
            var ct = cts.Token;

            try
            {
                int saved = await Task.Run(
                    () => DownloadToMbTiles(filePath, lonMin, latMin, lonMax, latMax, total, ct), ct);
                MbTilesPath = filePath;
                ExportStatus = string.Format(CultureInfo.InvariantCulture,
                    "Hotovo: {0} dlaždic → {1}", saved, Path.GetFileName(filePath));
            }
            catch (OperationCanceledException) { ExportStatus = "Export zrušen."; }
            catch (Exception ex) { ExportStatus = "Chyba exportu: " + ex.Message; }
            finally { Exporting = false; exportCts = null; }
        }

        private static long CountTiles(double lonMin, double latMin, double lonMax, double latMax)
        {
            long total = 0;
            for (int z = ExportMinZoom; z <= ExportMaxZoom; z++)
            {
                int n = 1 << z;
                int xMin = ClampTile(LonToTileX(lonMin, z), n), xMax = ClampTile(LonToTileX(lonMax, z), n);
                int yMin = ClampTile(LatToTileY(latMax, z), n), yMax = ClampTile(LatToTileY(latMin, z), n);
                total += (long)(xMax - xMin + 1) * (yMax - yMin + 1);
            }
            return total;
        }

        // Bezi na threadpoolu (Task.Run). Stahne dlazdice a zapise MBTiles (SQLite). Vraci pocet ulozenych.
        private int DownloadToMbTiles(string filePath, double lonMin, double latMin, double lonMax, double latMax,
                                      long total, CancellationToken ct)
        {
            EnsureSqliteProvider();

            if (File.Exists(filePath)) File.Delete(filePath);

            using var conn = new SQLite.SQLiteConnection(filePath);
            conn.Execute("CREATE TABLE metadata (name TEXT, value TEXT);");
            conn.Execute("CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, tile_row INTEGER, tile_data BLOB);");
            conn.Execute("CREATE UNIQUE INDEX tile_index ON tiles (zoom_level, tile_column, tile_row);");

            void Meta(string n, string v) => conn.Execute("INSERT INTO metadata (name, value) VALUES (?, ?)", n, v);
            var ci = CultureInfo.InvariantCulture;
            Meta("name", Path.GetFileNameWithoutExtension(filePath));
            Meta("format", "png");
            Meta("type", "baselayer");
            Meta("version", "1.0");
            Meta("minzoom", ExportMinZoom.ToString(ci));
            Meta("maxzoom", ExportMaxZoom.ToString(ci));
            Meta("bounds", string.Format(ci, "{0},{1},{2},{3}", lonMin, latMin, lonMax, latMax));
            Meta("description", "ARBot export výřezu z OpenStreetMap");

            int done = 0, saved = 0;
            for (int z = ExportMinZoom; z <= ExportMaxZoom; z++)
            {
                int n = 1 << z;
                int xMin = ClampTile(LonToTileX(lonMin, z), n), xMax = ClampTile(LonToTileX(lonMax, z), n);
                int yMin = ClampTile(LatToTileY(latMax, z), n), yMax = ClampTile(LatToTileY(latMin, z), n);

                conn.BeginTransaction();
                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        ct.ThrowIfCancellationRequested();

                        byte[]? data = null;
                        try
                        {
                            var url = string.Format(ci, "https://tile.openstreetmap.org/{0}/{1}/{2}.png", z, x, y);
                            data = Http.GetByteArrayAsync(url, ct).GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException) { conn.Commit(); throw; }
                        catch { data = null; }   // chybejici/chybna dlazdice - preskoc

                        if (data != null && data.Length > 0)
                        {
                            int tmsRow = n - 1 - y;   // MBTiles pouziva TMS (prevraceny Y)
                            conn.Execute(
                                "INSERT OR REPLACE INTO tiles (zoom_level, tile_column, tile_row, tile_data) VALUES (?, ?, ?, ?)",
                                z, x, tmsRow, data);
                            saved++;
                        }

                        done++;
                        if (done % 25 == 0)
                        {
                            int d = done, s = saved;
                            Dispatcher.UIThread.Post(() => ExportStatus = string.Format(
                                CultureInfo.InvariantCulture, "Export… {0}/{1} (uloženo {2})", d, total, s));
                        }

                        Thread.Sleep(ExportThrottleMs);   // setrne tempo k OSM
                    }
                }
                conn.Commit();
            }
            return saved;
        }

        private static int LonToTileX(double lonDeg, int z)
            => (int)Math.Floor((lonDeg + 180.0) / 360.0 * (1 << z));

        private static int LatToTileY(double latDeg, int z)
        {
            double lat = Math.Max(Math.Min(latDeg, 85.05112878), -85.05112878) * Math.PI / 180.0;
            return (int)Math.Floor((1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * (1 << z));
        }

        private static int ClampTile(int v, int n) => v < 0 ? 0 : (v > n - 1 ? n - 1 : v);

        // ============================ Sprava vrstev / podkladu (UI vlakno) ============================
        partial void OnShowBaseMapChanged(bool value) => RebuildLayers();
        partial void OnShowRobotChanged(bool value) => RebuildLayers();
        partial void OnShowTrajectoryChanged(bool value) => RebuildLayers();
        partial void OnShowRouteChanged(bool value) => RebuildLayers();
        partial void OnShowMarkersChanged(bool value) => RebuildLayers();
        partial void OnShowOccupancyChanged(bool value) => RebuildLayers();
        partial void OnShowPlanChanged(bool value) => RebuildLayers();
        partial void OnShowMapChanged(bool value) => RebuildLayers();
        partial void OnSelectedBaseMapChanged(BaseMapChoice value) => RebuildLayers();
        partial void OnMbTilesPathChanged(string value) => RebuildLayers();

        /// <summary>
        /// Postavi <c>Map.Layers</c> od zakladu podle prepinacu a zvoleneho podkladu. Podklad je vespod,
        /// nad nim datove vrstvy (robot navrchu). Kdyz je podklad vypnuty/None, <b>zadna dlazdicova vrstva
        /// se nevytvori</b> -&gt; zadne pokusy o sit.
        /// </summary>
        private void RebuildLayers()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RebuildLayers);
                return;
            }

            Map.Layers.Clear();

            if (ShowBaseMap && !designMode)
            {
                var b = GetBaseLayer(SelectedBaseMap?.Value ?? BaseMap.None);
                if (b != null) Map.Layers.Add(b);
            }

            if (ShowMap) Map.Layers.Add(mapLayer);   // sit z OsmNav nad podkladem, pod ostatnimi daty
            if (ShowOccupancy) Map.Layers.Add(occupancyLayer);   // lokalni mapa nad siti, pod vektory
            if (ShowTrajectory) Map.Layers.Add(trajectoryLayer);
            if (ShowPlan) Map.Layers.Add(planLayer);
            if (ShowRoute) Map.Layers.Add(routeLayer);
            if (ShowMarkers) Map.Layers.Add(markerLayer);
            if (ShowRobot) Map.Layers.Add(robotLayer);   // robot navrchu

            ApplyZoomBounds();   // udrz hlubsi zoom i po zmene vrstev (limity by se jinak prepocetly z vrstev)
        }

        /// <summary>
        /// Povoli <b>hlubsi priblizeni</b> (cca 10x nad ramec zdroje dlazdic). Robot ma ~0,5 m a pri beznem
        /// mapovem zoomu je subpixelovy; hluboky zoom umozni videt jeho metricky tvar. Nad maximem dlazdic
        /// se podklad jen zvetsi (overzoom), vrstvy dat se kresli dal ostre. Min. rozliseni ~ zoom 23.
        /// </summary>
        private void ApplyZoomBounds()
        {
            try { Map.Navigator.OverrideZoomBounds = new MMinMax(Merc0 / (1 << 23), Merc0); }
            catch { /* Navigator jeste nepripraven - RebuildLayers to zavola znovu */ }
        }

        private ILayer? GetBaseLayer(BaseMap source)
        {
            switch (source)
            {
                case BaseMap.OpenStreetMap:
                    // User-Agent dle OSM tile usage policy.
                    return osmLayer ??= OpenStreetMap.CreateTileLayer("ARBot3 (autonomni robot; +https://openstreetmap.org)");

                case BaseMap.OfflineMbTiles:
                    if (string.IsNullOrWhiteSpace(MbTilesPath) || !File.Exists(MbTilesPath))
                        return null;
                    if (offlineLayer != null && offlineLayerPath == MbTilesPath)
                        return offlineLayer;
                    try
                    {
                        EnsureSqliteProvider();
                        var src = new MbTilesTileSource(new SQLite.SQLiteConnectionString(MbTilesPath, false));
                        offlineLayer = new Mapsui.Tiling.Layers.TileLayer(src) { Name = "Offline" };
                        offlineLayerPath = MbTilesPath;
                        return offlineLayer;
                    }
                    catch { return null; }

                default:
                    return null;
            }
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            try { exportCts?.Cancel(); } catch { }

            foreach (var d in feeds)
            {
                try { d.Dispose(); } catch { }
            }
            feeds.Clear();
        }
    }
}
