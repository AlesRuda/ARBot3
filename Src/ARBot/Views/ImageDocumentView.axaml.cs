using System;
using ARBot.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace ARBot.Views
{
    public partial class ImageDocumentView : UserControl
    {
        public ImageDocumentView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Prepocte polohu kurzoru na PIXEL zdrojoveho obrazu a ohlasi ji dokumentu.
        /// <para>Prepocet patri sem, protoze zavisi na <c>Stretch="Uniform"</c>: obraz je v panelu
        /// vycentrovany a olemovany prazdnem, takze se nesmi skalovat pres celou plochu prvku.
        /// Panel se pozna podle <c>Tag</c> ("R" = pravy).</para>
        /// </summary>
        private void OnImagePointerMoved(object sender, PointerEventArgs e)
        {
            if (DataContext is not ImageDocument vm || sender is not Image img) return;
            bool right = (img.Tag as string) == "R";

            if (img.Source is not Bitmap bmp) { vm.ClearCursor(right); return; }

            var size = bmp.PixelSize;
            double cw = img.Bounds.Width, ch = img.Bounds.Height;
            if (cw <= 0 || ch <= 0 || size.Width <= 0 || size.Height <= 0)
            {
                vm.ClearCursor(right);
                return;
            }

            // Uniform: jednotne meritko a vycentrovani (prazdno nahore/dole nebo vlevo/vpravo).
            double scale = Math.Min(cw / size.Width, ch / size.Height);
            double dw = size.Width * scale, dh = size.Height * scale;
            var pos = e.GetPosition(img);
            int px = (int)Math.Floor((pos.X - (cw - dw) / 2) / scale);
            int py = (int)Math.Floor((pos.Y - (ch - dh) / 2) / scale);

            vm.UpdateCursor(right, px, py);
        }

        private void OnImagePointerExited(object sender, PointerEventArgs e)
        {
            if (DataContext is ImageDocument vm && sender is Image img)
                vm.ClearCursor((img.Tag as string) == "R");
        }
    }
}
