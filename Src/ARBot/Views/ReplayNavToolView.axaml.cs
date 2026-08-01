using Avalonia.Controls;

namespace ARBot.Views
{
    public partial class ReplayNavToolView : UserControl
    {
        public ReplayNavToolView()
        {
            InitializeComponent();

            // Vybrany radek drz viditelny - behem Play sleduje pozici, po kliknuti zustane v obraze.
            EntriesList.SelectionChanged += (_, _) =>
            {
                int i = EntriesList.SelectedIndex;
                if (i >= 0)
                    EntriesList.ScrollIntoView(i);
            };
        }
    }
}
