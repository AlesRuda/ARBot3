using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ARBot.ViewModels;

namespace ARBot.Views
{
    public partial class DebugOutputToolView : UserControl
    {
        private INotifyCollectionChanged subscribed;

        public DebugOutputToolView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, EventArgs e)
        {
            if (subscribed != null)
                subscribed.CollectionChanged -= OnLinesChanged;

            subscribed = (DataContext as DebugOutputTool)?.Lines;
            if (subscribed != null)
                subscribed.CollectionChanged += OnLinesChanged;
        }

        /// <summary>
        /// Auto-scroll na konec pri prichodu novych radku - ale jen kdyz uz je uzivatel
        /// u spodku (aby ho scrollovani nevytrhavalo pri cteni historie).
        /// </summary>
        private void OnLinesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add)
                return;

            if (LinesList.ItemCount == 0)
                return;

            if (LinesList.Scroll is ScrollViewer sv)
            {
                // "u spodku" = konec viewportu je blizko konce obsahu (tolerance ~2 radky).
                double tolerance = 40;
                bool atBottom = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - tolerance;
                if (!atBottom)
                    return;
            }

            LinesList.ScrollIntoView(LinesList.ItemCount - 1);
        }
    }
}
