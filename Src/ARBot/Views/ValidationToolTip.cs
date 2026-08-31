using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace ARBot.Views
{
    /// <summary>
    /// Rizeni bubliny s chybou u vstupniho pole. Zapina se pripojenou vlastnosti
    /// <c>ValidationToolTip.Enabled="True"</c> ze Styles/Validation.axaml, takze plati pro celou
    /// aplikaci (viz Views/README.md).
    ///
    /// <para><b>Bublina je videt, kdyz je pole VADNE a zaroven zaostrene NEBO pod mysi.</b>
    /// Je to jeden vyraz nad stavem, ne retez udalosti - prvni verze delala
    /// <c>GotFocus → ukaz</c> / <c>LostFocus → schovej</c> vedle vestavene sluzby bublin, ktera
    /// delala totez pro mys, a ty dva mechanismy si stav navzajem prepisovaly: odjeti mysi zavrelo
    /// bublinu i u pole, ktere zustalo zaostrene. Nahlasil autor 31. 8. 2026.</para>
    ///
    /// <para>Proto se vestavena sluzba na tech polich VYPINA (<c>ToolTip.ServiceEnabled</c>)
    /// a <c>IsOpen</c> se pocita tady - jinak by se o nej dva mechanismy prely.</para>
    /// </summary>
    public static class ValidationToolTip
    {
        /// <summary>Zapina rizeni bubliny s chybou (zaostreni i najeti mysi).</summary>
        public static readonly AttachedProperty<bool> EnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(
                "Enabled", typeof(ValidationToolTip));

        public static void SetEnabled(Control element, bool value)
            => element.SetValue(EnabledProperty, value);

        public static bool GetEnabled(Control element)
            => element.GetValue(EnabledProperty);

        static ValidationToolTip()
        {
            // Vsechny tri vstupy vypoctu se sleduji JEDNOU pro celou tridu (registrace na Changed
            // je globalni, ne per-prvek); handler si sam overi, jestli je rizeni u prvku zapnute.
            InputElement.IsFocusedProperty.Changed.AddClassHandler<Control>(OnStateChanged);
            InputElement.IsPointerOverProperty.Changed.AddClassHandler<Control>(OnStateChanged);
            DataValidationErrors.HasErrorsProperty.Changed.AddClassHandler<Control>(OnStateChanged);

            EnabledProperty.Changed.AddClassHandler<Control>((control, e) =>
            {
                // Vestavena sluzba by si o IsOpen prela s timhle vypoctem.
                ToolTip.SetServiceEnabled(control, e.NewValue is not true);
                Refresh(control);
            });
        }

        private static void OnStateChanged(Control control, AvaloniaPropertyChangedEventArgs e)
        {
            if (GetEnabled(control))
                Refresh(control);
        }

        /// <summary>Srovna stav bubliny s tim, co ma platit: vadne a (zaostrene nebo pod mysi).</summary>
        private static void Refresh(Control control)
        {
            if (control == null) return;

            bool ukazat = DataValidationErrors.GetHasErrors(control)
                          && (control.IsFocused || control.IsPointerOver);

            if (ToolTip.GetIsOpen(control) != ukazat)
                ToolTip.SetIsOpen(control, ukazat);
        }
    }
}
