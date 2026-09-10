using System;
using ARBot.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace ARBot.Views
{
    public partial class ImageDocumentView : UserControl
    {
        public ImageDocumentView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Ohlasi dokumentu bod pod kurzorem ve <b>scenovych</b> souradnicich.
        ///
        /// <para>Obsluha visi na vnitrnim <c>Grid</c>u <c>Viewbox</c>u, ktery ma rozmery scény
        /// (<see cref="ImageDocument.LeftSceneWidth"/>), takze <c>e.GetPosition</c> vraci polohu
        /// v <b>jejich</b> jednotkach — tedy uz scénove pixely, bez ohledu na to, jak Viewbox
        /// panel zvetsil nebo zmensil. Pixel jednotlivych vrstev si dopocita
        /// <see cref="ImageDocument"/> pres <c>ImageLayer.TryPixel</c>; kazda vrstva muze mit jine
        /// rozliseni (pravdepodobnost 128×128 pokryva scénu 640×480).</para>
        ///
        /// <para>⚠️ Drive se tu prepocitavalo meritko a vycentrovani <c>Stretch="Uniform"</c>
        /// z <c>img.Source</c>, tedy z PODKLADU — overlay s jinym pomerem stran tim skoncil
        /// jinde, nez kam ukazoval kurzor. Panel se pozna podle <c>Tag</c> ("R" = pravy).</para>
        /// </summary>
        private void OnImagePointerMoved(object sender, PointerEventArgs e)
        {
            if (DataContext is not ImageDocument vm || sender is not Control panel) return;
            bool right = (panel.Tag as string) == "R";

            double sw = panel.Bounds.Width, sh = panel.Bounds.Height;
            var pos = e.GetPosition(panel);
            int px = (int)Math.Floor(pos.X), py = (int)Math.Floor(pos.Y);
            if (sw <= 0 || sh <= 0 || px < 0 || py < 0 || px >= sw || py >= sh)
            {
                vm.ClearCursor(right);
                return;
            }

            vm.UpdateCursor(right, px, py);
        }

        private void OnImagePointerExited(object sender, PointerEventArgs e)
        {
            if (DataContext is ImageDocument vm && sender is Control panel)
                vm.ClearCursor((panel.Tag as string) == "R");
        }
    }
}
