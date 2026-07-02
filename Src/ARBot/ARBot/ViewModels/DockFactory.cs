using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Vychozi rozlozeni dokovacich panelu.
    /// </summary>
    public class DockFactory : Factory
    {
        /// <summary>Dok pro dokumenty (sem se pridavaji nove dokumenty, napr. z menu).</summary>
        public IDocumentDock DocumentDock { get; private set; }

        public override IRootDock CreateLayout()
        {
            var document = new Document { Id = "Document1", Title = "Document" };

            var tool = new Tool { Id = "Tool1", Title = "Tool" };

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
                Proportion = 0.25,
                VisibleDockables = CreateList<IDockable>(tool),
                ActiveDockable = tool
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

            var root = CreateRootDock();
            root.Id = "Root";
            root.Title = "Root";
            root.VisibleDockables = CreateList<IDockable>(mainLayout);
            root.ActiveDockable = mainLayout;
            root.DefaultDockable = mainLayout;

            return root;
        }
    }
}
