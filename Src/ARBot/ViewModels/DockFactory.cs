using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Vychozi rozlozeni dokovacich panelu.
    /// </summary>
    public class DockFactory : Factory
    {
        /// <summary>
        /// Proporce leveho doku se senzory, kdyz je panel rozbaleny (odepnuty).
        /// <para>NENI to primo podil sirky okna - Dock proporce normalizuje mezi sourozenci
        /// v <see cref="Dock.Model.Mvvm.Controls.ProportionalDock"/>. Zmerene na okne 1424 px:
        /// 0,35 → 370 px, 0,50 → 475 px (tedy zhruba <c>125 + 700·p</c>). Hodnota je zkalibrovana
        /// tak, aby rozbaleny panel vysel stejne siroky jako vysunuty prouzek
        /// (<see cref="SensorPanelPinnedWidth"/>) - jinak by pri odepnuti poskocil.</para>
        /// </summary>
        public const double SensorPanelProportion = 0.25;

        /// <summary>Sirka vysunuteho panelu senzoru [px], kdyz je sbaleny do auto-hide prouzku.
        /// Pinnuty panel se nerozmeruje podle <see cref="SensorPanelProportion"/>, ale podle
        /// vlastnich "pinned bounds" - viz <c>MainWindowViewModel</c>.</summary>
        public const double SensorPanelPinnedWidth = 300;

        /// <summary>Dok pro dokumenty (sem se pridavaji nove dokumenty, napr. z menu).
        /// Nikdy se nesbaluje (IsCollapsable=false), takze slouzi i jako stabilni kotva
        /// pro (znovu)otevirani nastroju vuci zivemu stromu layoutu.</summary>
        public IDocumentDock DocumentDock { get; private set; }

        /// <summary>Nastroj s prehledem senzoru (otviratelny z menu).</summary>
        public SensorStatusTool SensorStatus { get; private set; }

        /// <summary>Panel s Debug/Trace vystupem (otviratelny/znovuotviratelny z menu).</summary>
        public DebugOutputTool DebugOutput { get; private set; }

        /// <summary>
        /// Inicializace layoutu. Nastavuje tvurce hostitelskeho okna, aby fungovala
        /// plovouci okna - bez <see cref="DefaultHostWindowLocator"/> by se dockable po
        /// vytazeni mimo hlavni okno jen "ztratil" (Dock nema z ceho okno vytvorit).
        /// </summary>
        public override void InitLayout(IDockable layout)
        {
            DefaultHostWindowLocator = () => new HostWindow();
            // Sleduj zmenu aktivniho tabu -> nastav IsActive nasim dokumentum (viditelny = aktivni tab
            // DocumentDock). Umoznuje dokumentum gatovat drahy render, kdyz nejsou videt (viz ImageDocument).
            ActiveDockableChanged += OnActiveDockableChanged;
            base.InitLayout(layout);
        }

        private void OnActiveDockableChanged(object sender, ActiveDockableChangedEventArgs e)
        {
            var dock = DocumentDock;
            var active = dock?.ActiveDockable;
            var docs = dock?.VisibleDockables;
            if (docs == null) return;
            foreach (var d in docs)
                if (d is DocumentBase doc)
                    doc.SetActive(ReferenceEquals(doc, active));
        }

        public override IRootDock CreateLayout()
        {
            var document = new Document { Id = "Document1", Title = "Document" };

            // Levy panel: prehled senzoru z ARBotHW.Current (jmeno + chybovy stav).
            var sensorStatus = new SensorStatusTool();
            SensorStatus = sensorStatus;

            var documentDock = new DocumentDock
            {
                Id = "Documents",
                Title = "Documents",
                IsCollapsable = false,
                VisibleDockables = CreateList<IDockable>(document),
                ActiveDockable = document,
                CanCreateDocument = true
            };
            DocumentDock = documentDock;

            var toolDock = new ToolDock
            {
                Id = "ToolDock",
                Title = "ToolDock",
                Alignment = Alignment.Left,
                // Sirsi nez vychozich 0,25 - do uzkeho panelu se nevesly delsi nazvy senzoru.
                Proportion = SensorPanelProportion,
                // Sbalitelny (default): po zavreni posledniho nastroje se dok odstrani z layoutu
                // (uvolni misto). Menu Tools -> Sensors overview ho pak zase vlozi (viz OpenSensors).
                VisibleDockables = CreateList<IDockable>(sensorStatus),
                ActiveDockable = sensorStatus
            };

            var mainLayout = new ProportionalDock
            {
                Id = "MainLayout",
                Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(
                    toolDock,
                    new ProportionalDockSplitter(),
                    documentDock)
            };

            // Spodni panel s vystupem Debug/Trace (zalozka "Debug output").
            var debugOutput = new DebugOutputTool();
            DebugOutput = debugOutput;

            var bottomDock = new ToolDock
            {
                Id = "BottomDock",
                Title = "BottomDock",
                Alignment = Alignment.Bottom,
                Proportion = 0.25,
                VisibleDockables = CreateList<IDockable>(debugOutput),
                ActiveDockable = debugOutput
            };

            // Vertikalni rozlozeni: nahore hlavni layout, dole panel s debug vystupem.
            var verticalLayout = new ProportionalDock
            {
                Id = "VerticalLayout",
                Orientation = Orientation.Vertical,
                VisibleDockables = CreateList<IDockable>(
                    mainLayout,
                    new ProportionalDockSplitter(),
                    bottomDock)
            };

            var root = CreateRootDock();
            root.Id = "Root";
            root.Title = "Root";
            root.VisibleDockables = CreateList<IDockable>(verticalLayout);
            root.ActiveDockable = verticalLayout;
            root.DefaultDockable = verticalLayout;

            return root;
        }
    }
}
