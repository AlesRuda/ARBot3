using Avalonia.Controls;

namespace ARBot.Views
{
    /// <summary>
    /// View panelu „Konfigurace" - tabulka parametru s popisem, hodnotou a jejim puvodem.
    /// Logika je v <see cref="ARBot.ViewModels.ConfigurationDocument"/>.
    /// </summary>
    public partial class ConfigurationDocumentView : UserControl
    {
        public ConfigurationDocumentView()
        {
            InitializeComponent();
        }
    }
}
