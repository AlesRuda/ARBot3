using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.HAL;
using ARBot.HAL.Devices.Camera;
using ARBot.Common.Vision;
using ARBot.Robot;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public string Greeting { get; } = "Welcome to Avalonia!";

        private readonly DockFactory _factory = new();

        /// <summary>
        /// Rozlozeni dokovacich panelu.
        /// </summary>
        public IRootDock? Layout { get; }

        public MainWindowViewModel()
        {
            Layout = _factory.CreateLayout();
            if (Layout is not null)
                _factory.InitLayout(Layout);

            // Dvojklik na senzor v panelu Sensors otevře jeho detailní dokument.
            if (_factory.SensorStatus is not null)
                _factory.SensorStatus.SensorActivated += OpenSensorDocument;

            // Panel senzorů je po startu SBALENÝ do auto-hide proužku na levé hraně - dokumenty
            // tak dostanou celou šířku. Rozbalí se kliknutím na záložku nebo z menu Tools →
            // Sensors overview (ReopenTool si s odepnutým i připnutým stavem poradí).
            // Až po InitLayout: pinování pracuje se ŽIVÝM stromem layoutu (PinnedDockables rootu).
            if (Layout is not null && _factory.SensorStatus is not null)
            {
                // Sirka vysunuteho prouzku: pinnuty panel se neridi proporci doku, ale vlastnimi
                // "pinned bounds" (x, y, sirka, vyska); 0 u vysky = neomezeno.
                _factory.SensorStatus.SetPinnedBounds(0, 0, DockFactory.SensorPanelPinnedWidth, 0);
                _factory.PinDockable(_factory.SensorStatus);
            }

            // Bezobslužný self-test (parametr selftest=1) - reprodukovatelné měření výkonu bez obsluhy.
            StartSelfTestIfRequested();

            // Bezobslužný screenshot World pohledu do deníčku (parametr worldshot=true).
            StartWorldShotIfRequested();

            // Totéž pro telemetrickou tabulku a graf (parametr telemetryshot=true).
            StartTelemetryShotIfRequested();
        }

        /// <summary>
        /// Otevře (nebo aktivuje, je-li už otevřený) detailní dokument pro daný senzor.
        /// Mapování typu senzoru na dokument je v <see cref="CreateSensorDocument"/> —
        /// sem se budou přidávat další typy (kamery, GPS, motory, ...).
        /// </summary>
        private void OpenSensorDocument(ISensor sensor)
        {
            var dock = _factory.DocumentDock;
            if (dock == null || Layout == null || sensor == null)
                return;

            var doc = CreateSensorDocument(sensor);
            if (doc == null)
                return;   // typ senzoru zatím nemá vlastní dokument

            // Už otevřený dokument pro tentýž senzor jen aktivovat (podle Id).
            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == doc.Id);
            if (existing != null)
            {
                (doc as IDisposable)?.Dispose();
                _factory.SetActiveDockable(existing);
                _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            _factory.SetFocusedDockable(Layout, doc);
        }

        /// <summary>Vytvoří detailní dokument podle typu senzoru (rozšiřitelné).</summary>
        private static Document? CreateSensorDocument(ISensor sensor) => sensor switch
        {
            IIMU imu => new IMUDocument(imu),
            IGPS gps => new GpsDocument(gps),
            IMotorControl motors => new MotorControlDocument(motors),
            // POŘADÍ: VirtualCamera musí být PŘED obecnou ICamera — u switch expression vyhrává
            // první odpovídající vzor, takže obráceně by se speciální dokument nikdy nevytvořil.
            VirtualCamera vcam => new VirtualCameraDocument(vcam),
            ICamera cam => new CameraDocument(cam),
            _ => null
        };

        /// <summary>
        /// Otevre dokument pro zobrazeni obrazku (Blob i CameraFrame) a pripoji ho na
        /// <see cref="ARBotRuntime.Stream"/> (jediny verejny fan-out proud). Vize
        /// (<see cref="BackProjectProcessor"/>) je v Run soucasti grafu runtime, takze
        /// dokument uz zadny vlastni feed / BackProject nedrzí - jen zobrazuje, co teče
        /// na Streamu (surove CameraFrame i odvozene Blob vrstvy). Ve View totez ze zaznamu.
        /// </summary>
        [RelayCommand]
        private void OpenImages()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            // Uz otevreny dokument jen aktivovat.
            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "Images");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null) _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var doc = new ImageDocument();

            // Pripoj dokument na verejny Stream runtime; odpojeni pri zavreni resi AttachFeed/Dispose.
            try
            {
                doc.AttachFeed(ARBotRuntime.Current.Stream.Connect(doc));
            }
            catch { /* runtime nedostupne (napr. design-time) */ }

            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }

        /// <summary>
        /// Otevre (nebo aktivuje) robot-centricky pohled (grid sjizdnosti a vyhledove dalsi
        /// robot-centricke vrstvy) a pripoji ho na <see cref="ARBotRuntime.Stream"/>. V Run se grid
        /// pocita synchronne na vlakne kamery (<see cref="ARBot.Common.Vision.CameraFrameProcessor"/>) a je
        /// soucasti <see cref="ARBot.Common.Devices.CameraFrame"/>; ve View se prehrava zaznamenany -
        /// dokument v obou pripadech jen zobrazuje, co tece na Streamu.
        /// </summary>
        [RelayCommand]
        private void OpenRobotCentric()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "RobotCentric");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null) _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var doc = new RobotCentricDocument();
            try
            {
                doc.AttachFeed(ARBotRuntime.Current.Stream.Connect(doc));
            }
            catch { /* runtime nedostupne (napr. design-time) */ }

            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }

        /// <summary>
        /// Otevre (nebo aktivuje) telemetricky pohled - tabulka udaju v case nad prehravanym
        /// zaznamem (stav robotu, ridici zasahy, plan, globalni navigace). Dokument si data
        /// nacte sam jednim skenem zaznamu; ve Run zustane prazdny s vysvetlenim.
        /// Viz doc/telemetry-view.md.
        /// </summary>
        [RelayCommand]
        private void OpenTelemetry()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "Telemetry");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null) _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var doc = new TelemetryDocument();

            // Tabulka je misto, kde se vybira co kreslit; dokument grafu zaklada a aktivuje
            // ale az tohle - tabulka o docich nic nevi (viz doc/telemetry-view.md).
            doc.ChartSeriesChanged += (_, request) => ShowTelemetryChart(doc, request);

            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }

        /// <summary>
        /// Predá rady do dokumentu grafu; kdyz jeste neni a zadost o to stoji
        /// (<see cref="TelemetryChartRequest.Open"/>), zalozi ho. Zadost bez otevirani se pouzije
        /// pri preskenovani zaznamu - aktualizuje uz otevreny graf, ale zadny novy neotvira.
        /// </summary>
        private void ShowTelemetryChart(TelemetryDocument telemetry, TelemetryChartRequest request)
        {
            var dock = _factory.DocumentDock;
            if (dock == null || request == null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "TelemetryChart")
                           as TelemetryChartDocument;

            if (existing == null)
            {
                if (!request.Open) return;

                existing = new TelemetryChartDocument();

                // Prepinac konvence uhlu v grafu meni stav TABULKY - ta data vlastni a posle
                // zpatky prepoctene rady. Jinak by kazdy dokument mohl ukazovat jinou konvenci.
                existing.WorldAnglesRequested += (_, world) => telemetry.WorldAngles = world;
                _factory.AddDockable(dock, existing);
            }

            existing.SetSeries(request.Series, request.WorldAngles);

            if (request.Open)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null)
                    _factory.SetFocusedDockable(Layout, existing);
            }
        }

        /// <summary>
        /// Otevre (nebo aktivuje) svetovy (world) pohled - mapa s prepinatelnym podkladem a vrstvami dat
        /// ze <see cref="ARBotRuntime.Stream"/> (poloha/kurz, trajektorie, trasa/graf, znacky). Base vrstvu
        /// lze vypnout (na OrangePI = zadne pokusy o internet). Viz <see cref="WorldViewDocument"/>.
        /// </summary>
        [RelayCommand]
        private void OpenWorldView()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "WorldView");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null) _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var doc = new WorldViewDocument();
            try
            {
                doc.AttachFeed(ARBotRuntime.Current.Stream.Connect(doc));
                // Ctrl + klik v mape = cil. Kdyz bezi globalni navigace (je nactena mapa), jde cil
                // TAM jako LLA a ona uz krmi lokalni vrstvu mrkvi po trase; jinak zustava puvodni
                // chovani (cil primo lokalnimu planovaci). Viz doc/global-navigation-runtime.md.
                doc.GoalRequested = (x, y) =>
                {
                    var rt = ARBotRuntime.Current;
                    if (rt.GlobalNavigator != null && rt.MapOrigin != null)
                        rt.GlobalNavigator.SetGoal(rt.MapOrigin.ToLLA(x, y));
                    else
                        rt.Navigator?.SetGoal(x, y);
                };

                // Shift + klik = presun simulovaneho robotu (vyvojarska pomucka). Pohled o runtime
                // nic nevi, jen se zepta; runtime rozhodne, jestli to ma smysl (viz doc/virtual-hw.md).
                doc.TeleportRequested = (x, y) => ARBotRuntime.Current.TeleportSimulatedRobot(x, y);

                // Mapa se na Stream publikuje jednou pri startu behu - pohled otevreny az potom
                // by ji neuvidel, proto si ji vyzvedne z runtime.
                var map = ARBotRuntime.Current.MapMessage;
                if (map != null)
                    doc.Post(map);
            }
            catch { /* runtime nedostupne (napr. design-time) */ }

            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }

        [RelayCommand]
        private void Open()
        {
            // TODO: implementovat otevreni
        }

        [RelayCommand]
        private void Save()
        {
            // TODO: implementovat ulozeni
        }

        // ---------------- Rezimy runtime (Run / View) ----------------

        // Stavovy automat prikazu: Run/View jen kdyz NEbezi, Stop jen kdyz bezi. Po kazde
        // zmene stavu (Start/Stop) je nutne prekreslit CanExecute vsech prikazu (RefreshRuntimeCommands).
        private bool CanStart => !ARBotRuntime.Current.IsRunning;
        private bool CanStop => ARBotRuntime.Current.IsRunning;

        private void RefreshRuntimeCommands()
        {
            RunModeCommand.NotifyCanExecuteChanged();
            RunAndLogCommand.NotifyCanExecuteChanged();
            ViewModeCommand.NotifyCanExecuteChanged();
            StopRuntimeCommand.NotifyCanExecuteChanged();
            UseNoHwCommand.NotifyCanExecuteChanged();
            UseRealHwCommand.NotifyCanExecuteChanged();
            UseVirtualHwCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsNoHwSelected));
            OnPropertyChanged(nameof(IsRealHwSelected));
            OnPropertyChanged(nameof(IsVirtualHwSelected));
        }

        // ---------------- volba hardwaru ----------------
        // Menu jen nastavuje POZADOVANY rezim; skutecne se HW zaklada az v ARBotRuntime.Start,
        // protoze virtualni potrebuje fuzi (zdroj pozy) a mapu. Prepinat lze jen kdyz runtime
        // stoji - za behu by se pod grafem vymenily senzory. Viz doc/virtual-hw.md.

        /// <summary>Je vybrany rezim „bez hardwaru"? (Zaskrtnuti v menu.)</summary>
        public bool IsNoHwSelected => ARBotRuntime.Current.RequestedHwMode == HwMode.None;
        /// <summary>Je vybrany realny hardware?</summary>
        public bool IsRealHwSelected => ARBotRuntime.Current.RequestedHwMode == HwMode.Real;
        /// <summary>Je vybrany virtualni (simulovany) hardware?</summary>
        public bool IsVirtualHwSelected => ARBotRuntime.Current.RequestedHwMode == HwMode.Virtual;

        private void SetHwMode(HwMode mode)
        {
            ARBotRuntime.Current.RequestedHwMode = mode;

            // Zadny HW jde uvolnit hned; realny i virtualni se zakladaji az pri Startu
            // (u virtualniho to ani driv nejde - chybi fuze a mapa).
            if (mode == HwMode.None)
                try { ARBotHW.Current.SetNoHW(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            RefreshRuntimeCommands();
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void UseNoHw() => SetHwMode(HwMode.None);

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void UseRealHw() => SetHwMode(HwMode.Real);

        [RelayCommand(CanExecute = nameof(CanStart))]
        private void UseVirtualHw() => SetHwMode(HwMode.Virtual);

        /// <summary>Spusti runtime v rezimu Run BEZ zaznamu (realne senzory + rizeni).</summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private void RunMode()
        {
            try { ARBotRuntime.Current.Start(ARBot.Robot.Mode.Run); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            RefreshRuntimeCommands();
        }

        /// <summary>
        /// Spusti runtime v rezimu Run SE zaznamem. Vystupni soubor se pojmenuje automaticky
        /// <c>yyyyMMdd-HHmmss.rec</c> ve slozce <c>records</c> v korenu repa (sidecar index
        /// <c>.rec.idx</c> resi runtime). Slozka se pripadne vytvori.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private void RunAndLog()
        {
            try
            {
                string dir = System.IO.Path.Combine(RepoRootOrBase(), "records");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".rec");

                ARBotRuntime.Current.Start(ARBot.Robot.Mode.Run, file);
                System.Diagnostics.Debug.WriteLine("Run + zaznam do: " + file);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            RefreshRuntimeCommands();
        }

        /// <summary>
        /// Koren git repa (slozka obsahujici <c>.git</c>) hledany smerem nahoru od build outputu;
        /// fallback na <see cref="AppContext.BaseDirectory"/> (nasazeni bez repa, napr. na Pi).
        /// </summary>
        private static string RepoRootOrBase()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string git = System.IO.Path.Combine(dir.FullName, ".git");
                if (System.IO.Directory.Exists(git) || System.IO.File.Exists(git))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return AppContext.BaseDirectory;
        }

        /// <summary>Zastavi runtime (Run i View).</summary>
        [RelayCommand(CanExecute = nameof(CanStop))]
        private void StopRuntime()
        {
            try { ARBotRuntime.Current.Stop(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            RefreshRuntimeCommands();
        }

        /// <summary>
        /// Spusti runtime v rezimu View nad zvolenym zaznamem. Cestu vybere uzivatel pres
        /// souborovy dialog (StorageProvider hlavniho okna); pri nedostupnem dialogu se
        /// pouzije zadana <paramref name="file"/> (jinak se nic nestane).
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStart))]
        private async System.Threading.Tasks.Task ViewMode()
        {
            string file = null;
            try
            {
                var top = App.MainTopLevel;
                if (top?.StorageProvider is { } sp)
                {
                    var picks = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title = "Otevrit zaznam (View)",
                        AllowMultiple = false,
                        // Vedle zaznamu lezi sidecar index *.rec.idx (a v adresari byvaji i jine
                        // soubory) - bez filtru se v tom hleda spatne. "Vse" zustava pro pripad
                        // zaznamu s jinou priponou.
                        FileTypeFilter = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("Záznam ARBot")
                            {
                                Patterns = new[] { "*.rec" },
                            },
                            new Avalonia.Platform.Storage.FilePickerFileType("Vše")
                            {
                                Patterns = new[] { "*.*" },
                            },
                        },
                    });
                    if (picks != null && picks.Count > 0)
                        file = picks[0].Path?.LocalPath;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            if (string.IsNullOrEmpty(file))
                return;

            try { ARBotRuntime.Current.Start(ARBot.Robot.Mode.View, file); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            RefreshRuntimeCommands();

            // Po startu View otevri navigacni nastroj (krok 9), obrazovy dokument i robot-centricky pohled.
            OpenReplayNav();
            OpenImages();
            OpenRobotCentric();
        }

        /// <summary>Prave otevreny navigacni nastroj replay (nebo <c>null</c>). Drzime referenci,
        /// protoze panel nezije v <see cref="DockFactory.DocumentDock"/>, ale ve spodnim doku vedle
        /// Debug outputu - a ten se muze sbalit/odpojit, takze ho v layoutu nelze spolehlive najit.</summary>
        private ReplayNavTool _replayNav;

        /// <summary>
        /// Otevre (nebo aktivuje) navigacni nastroj pro replay (View). Panel se dokuje do
        /// TEHOZ doku jako Debug output (spodni panel), ne mezi dokumenty: je to nastroj, ktery
        /// ma byt videt SOUCASNE s obrazovymi dokumenty, ne misto nich.
        /// </summary>
        private void OpenReplayNav()
        {
            var src = ARBotRuntime.Current.FileSource;
            if (src == null || Layout is null)
                return;

            // Nastroj z predchoziho zaznamu je navazany na jiny (uz zavreny) zdroj - zahodit.
            if (_replayNav != null && !ReferenceEquals(_replayNav.Source, src))
            {
                if (_replayNav.Owner is IDock owner && owner.VisibleDockables != null
                    && owner.VisibleDockables.Contains(_replayNav))
                    _factory.RemoveDockable(_replayNav, true);
                _replayNav = null;
            }

            if (_replayNav == null)
            {
                _replayNav = new ReplayNavTool(src);

                // Preferovane umisteni: jako dalsi zalozka v doku, kde sedi Debug output.
                var host = HostDockOfDebugOutput();
                if (host != null)
                {
                    _factory.AddDockable(host, _replayNav);
                    _factory.SetActiveDockable(_replayNav);
                    _factory.SetFocusedDockable(Layout, _replayNav);
                    return;
                }
            }

            // Debug output neni v hlavnim okne (zavren/pinnut/vytazen) nebo uz nastroj existuje -
            // spolecna cesta: nadokovat/aktivovat dole stejne jako Debug output.
            ReopenTool(_replayNav, Alignment.Bottom, DockOperation.Bottom);
        }

        /// <summary>Dok, ve kterem prave sedi Debug output - jen kdyz je normalne viditelny
        /// v hlavnim okne (ne pinnuty do auto-hide prouzku, ne v plovoucim okne).</summary>
        private IDock HostDockOfDebugOutput()
        {
            var debug = _factory.DebugOutput;
            if (debug == null || Layout is null)
                return null;
            if (_factory.IsDockablePinned(debug, Layout))
                return null;
            if (!ContainsVisible(Layout, debug))
                return null;
            return debug.Owner as IDock;
        }

        /// <summary>
        /// Otevre novy dokument s RGB streamem z kamery D435.
        /// </summary>
        [RelayCommand]
        private void TestD435()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            var doc = new D435TestDocument();
            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }

        /// <summary>
        /// Otevre (nebo aktivuje, pokud uz je otevreny) panel s prehledem senzoru.
        /// Po zavreni se levy dok sbali - pri zavreni posledniho nastroje Dock navic
        /// rozpusti i obalujici proporcionalni dok, takze ulozene reference na nej uz
        /// nejsou v layoutu. Proto panel dokujeme vzdy vuci zivemu <see cref="DockFactory.DocumentDock"/>
        /// pres <see cref="Dock.Model.Core.IFactory.SplitToDock"/> (ten se nikdy nesbali).
        /// </summary>
        [RelayCommand]
        private void OpenSensors() => ReopenTool(_factory.SensorStatus, Alignment.Left, DockOperation.Left);

        /// <summary>Otevre (nebo aktivuje) panel s Debug/Trace vystupem.</summary>
        [RelayCommand]
        private void OpenDebugOutput() => ReopenTool(_factory.DebugOutput, Alignment.Bottom, DockOperation.Bottom);

        /// <summary>
        /// Otevre (nebo aktivuje, pokud uz je otevreny) dokovaci nastroj. Osetruje vsechny stavy,
        /// v nichz se nastroj muze po zavreni nachazet, jinak by se vytvoril duplikat:
        /// pinnuty (auto-hide prouzek), skryty (HideToolsOnClose) i uplne odpojeny (zavren se
        /// sbalenim doku nebo vytazeny do plovouciho okna). V poslednim pripade ho nadokuje zpet
        /// do hlavniho layoutu vuci stabilnimu <see cref="DockFactory.DocumentDock"/> (ten se
        /// nikdy nesbaluje), s danym <paramref name="alignment"/>/<paramref name="operation"/>.
        /// </summary>
        private void ReopenTool(IDockable tool, Alignment alignment, DockOperation operation)
        {
            var documentDock = _factory.DocumentDock;
            if (tool == null || documentDock == null || Layout is null)
                return;

            if (_factory.IsDockablePinned(tool, Layout))
            {
                // Pinnuty (auto-hide prouzek) -> vrat do normalniho (odepnuteho) stavu.
                _factory.UnpinDockable(tool);
            }
            else if (Layout.HiddenDockables != null && Layout.HiddenDockables.Contains(tool))
            {
                // Skryty (zavren s HideToolsOnClose) -> obnov na puvodni misto.
                _factory.RestoreDockable(tool);
            }
            else if (!ContainsVisible(Layout, tool))
            {
                // Neni ve viditelnem strome hlavniho okna - bud uplne mimo layout (zavren
                // se sbalenim doku), nebo vytazeny do plovouciho okna. Odpoj ho z aktualniho
                // umisteni (collapse zavre i pripadne prazdne plovouci okno) a nadokuj zpet
                // do hlavniho layoutu. SplitToDock s IDock parametrem pouzije nas dok primo
                // (spravne vykresleni vcetne zalozky) a vyresi i orientaci.
                if (tool.Owner is IDock current && current.VisibleDockables != null
                    && current.VisibleDockables.Contains(tool))
                    _factory.RemoveDockable(tool, true);

                var toolDock = new ToolDock
                {
                    Id = "ToolDock",
                    Title = "ToolDock",
                    Alignment = alignment,
                    // Tataz proporce jako ve vychozim layoutu - jinak by mel panel po znovuotevreni
                    // jinou sirku nez po startu.
                    Proportion = DockFactory.SensorPanelProportion,
                    VisibleDockables = _factory.CreateList<IDockable>(tool),
                    ActiveDockable = tool
                };
                _factory.SplitToDock(documentDock, toolDock, operation);
            }
            // else: uz je viditelny v hlavnim okne -> jen aktivovat nize.

            _factory.SetActiveDockable(tool);
            _factory.SetFocusedDockable(Layout, tool);
        }

        /// <summary>
        /// Rekurzivne hleda dockable ve viditelnem strome (VisibleDockables) daneho doku.
        /// Zamerne NEprochazi plovouci okna (RootDock.Windows) - slouzi k rozliseni, zda je
        /// nastroj v hlavnim okne, nebo vytazeny do plovouciho okna (pripadne uplne mimo).
        /// </summary>
        private static bool ContainsVisible(IDock dock, IDockable target)
        {
            if (dock.VisibleDockables == null)
                return false;

            foreach (var d in dock.VisibleDockables)
            {
                if (ReferenceEquals(d, target))
                    return true;
                if (d is IDock child && ContainsVisible(child, target))
                    return true;
            }
            return false;
        }
    }
}
