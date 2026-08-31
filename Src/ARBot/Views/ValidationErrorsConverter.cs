using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace ARBot.Views
{
    /// <summary>
    /// Spoji validacni chyby pole do jednoho textu pro bublinu.
    ///
    /// <para><b>Proc konvertor a ne binding rovnou.</b> <c>DataValidationErrors.Errors</c> je
    /// <see cref="IEnumerable"/>, takze v XAML z nej nejde vzit prvek indexem (<c>[0]</c> skonci
    /// chybou prekladu AVLN2000) - a vypsat kolekci pres ItemsControl uvnitr ToolTipu by znamenalo
    /// spolehnout se na to, jak se vaze vizualni strom bubliny. Tohle je deterministicke.</para>
    ///
    /// <para>Pouziva Styles/Validation.axaml; viz Views/README.md, „Chyby ve vstupnich polich".</para>
    /// </summary>
    public sealed class ValidationErrorsConverter : IValueConverter
    {
        /// <summary>Sdilena instance pro pouziti ve stylech (konvertor je bezstavovy).</summary>
        public static readonly ValidationErrorsConverter Instance = new ValidationErrorsConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not IEnumerable errors || value is string)
                return value?.ToString();

            var texty = errors.Cast<object>()
                              .Select(e => e?.ToString())
                              .Where(t => !string.IsNullOrWhiteSpace(t))
                              .ToList();

            // Prazdno vraci null, at bublina vubec nevyskoci (ToolTip.Tip = null ji vypne).
            return texty.Count == 0 ? null : string.Join("\n", texty);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
