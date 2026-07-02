using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;

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
    }
}
