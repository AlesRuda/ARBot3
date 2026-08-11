using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.HAL;
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

            // Bezobslužný self-test (parametr selftest=1) - reprodukovatelné měření výkonu bez obsluhy.
            StartSelfTestIfRequested();

            // Bezobslužný screenshot World pohledu do deníčku (parametr worldshot=true).
            StartWorldShotIfRequested();
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
                // Ctrl + klik v mape = cil lokalniho planovace (v Run; ve View navigace nebezi).
                doc.GoalRequested = (x, y) => ARBotRuntime.Current.Navigator?.SetGoal(x, y);
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
        }

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
                        AllowMultiple = false
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

        /// <summary>Otevre (nebo aktivuje) navigacni nastroj pro replay (View).</summary>
        private void OpenReplayNav()
        {
            var src = ARBotRuntime.Current.FileSource;
            if (src == null)
                return;

            var dock = _factory.DocumentDock;
            if (dock == null || Layout is null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "ReplayNav");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var tool = new ReplayNavTool(src);
            _factory.AddDockable(dock, tool);
            _factory.SetActiveDockable(tool);
            _factory.SetFocusedDockable(Layout, tool);
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
                    Proportion = 0.25,
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
