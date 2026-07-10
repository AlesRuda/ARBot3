using ARBot.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace ARBot.Views
{
    /// <summary>
    /// Pohled panelu senzorů. Dvojklik na řádek senzoru vyvolá otevření odpovídajícího
    /// detailního dokumentu (viz <see cref="SensorStatusTool.ActivateCommand"/>).
    /// </summary>
    public partial class SensorStatusToolView : UserControl
    {
        public SensorStatusToolView()
        {
            InitializeComponent();
        }

        private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
        {
            if ((e.Source as Control)?.DataContext is SensorRow row
                && DataContext is SensorStatusTool vm
                && vm.ActivateCommand.CanExecute(row))
            {
                vm.ActivateCommand.Execute(row);
            }
        }
    }
}
