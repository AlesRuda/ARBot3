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
        /// <summary>
        /// Korenovy layout, aby se pri zmene aktivniho tabu dal projit CELY strom (dokument muze
        /// zit ve vlastni dokovaci skupine, ne jen v <see cref="DocumentDock"/>) — viz
        /// <see cref="OnActiveDockableChanged"/>.
        /// </summary>
        private IDockable rootLayout;

        public override void InitLayout(IDockable layout)
        {
            rootLayout = layout;
            DefaultHostWindowLocator = () => new HostWindow();
            // Sleduj zmenu aktivniho tabu -> nastav IsActive nasim dokumentum (viditelny = aktivni tab
            // DocumentDock). Umoznuje dokumentum gatovat drahy render, kdyz nejsou videt (viz ImageDocument).
            ActiveDockableChanged += OnActiveDockableChanged;
            base.InitLayout(layout);
        }

        /// <summary>
        /// Přepočte <see cref="DocumentBase.IsActive"/> <b>všem dokumentům v celém layoutu</b>:
        /// dokument je aktivní, když je <c>ActiveDockable</c> svého vlastního doku.
        ///
        /// <para><b>Prochází se celý strom, ne jen <see cref="DocumentDock"/>.</b> Uživatel si může
        /// dokument vytáhnout do <b>vlastní dokovací skupiny</b> (nebo plovoucího okna) — a pak už
        /// v <c>DocumentDock.VisibleDockables</c> není. Původní verze mu proto po přetažení
        /// přestala <c>IsActive</c> aktualizovat a <b>zamrzl na poslední hodnotě</b>: když byl
        /// v tu chvíli aktivní jiný tab, zůstalo mu <c>false</c> natrvalo. U dokumentů, které na
        /// tom gatují render (náhled kamery v misi Robotour, <c>ImageDocument</c>), to znamená
        /// <b>trvale prázdný panel</b> — a vypadá to jako vada té vizualizace, ne doku.
        /// Nahlásil autor 27. 8. 2026 („mise Robotour přestala ukazovat kameru").</para>
        /// </summary>
        private void OnActiveDockableChanged(object sender, ActiveDockableChangedEventArgs e)
        {
            var root = rootLayout ?? DocumentDock;
            if (root != null) RefreshActive(root);
        }

        /// <summary>Rekurzivně projde dokovací strom a nastaví <c>IsActive</c> nalezeným dokumentům.</summary>
        private static void RefreshActive(IDockable node)
        {
            if (node is not IDock dock || dock.VisibleDockables == null) return;

            foreach (var child in dock.VisibleDockables)
            {
                if (child is DocumentBase doc)
                    doc.SetActive(ReferenceEquals(dock.ActiveDockable, doc));

                RefreshActive(child);
            }
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
