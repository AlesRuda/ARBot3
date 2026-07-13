using System;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.HAL;
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
        private void OpenSensors()
        {
            var tool = _factory.SensorStatus;
            var documentDock = _factory.DocumentDock;
            if (tool == null || documentDock == null || Layout is null)
                return;

            // Nastroj muze byt v ruznych stavech - je nutne je odlisit, jinak by se pri
            // "otevreni" pinnuteho/skryteho panelu vytvoril druhy (duplikat).
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
                    Alignment = Alignment.Left,
                    Proportion = 0.25,
                    VisibleDockables = _factory.CreateList<IDockable>(tool),
                    ActiveDockable = tool
                };
                _factory.SplitToDock(documentDock, toolDock, DockOperation.Left);
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
