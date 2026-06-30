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
    }
}
