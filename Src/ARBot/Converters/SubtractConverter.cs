using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ARBot.Converters
{
    /// <summary>
    /// Odečte konstantu (<c>ConverterParameter</c>) od navázané číselné hodnoty; výsledek ořízne na &gt;= 0.
    /// Použití: navázat <c>MaxHeight</c> na výšku předka a nechat místo na chrome (např. přepínač + okraje),
    /// aby vnořený <c>ScrollViewer</c> dostal správný strop a naskočil scrollbar, když se obsah nevejde.
    /// </summary>
    public sealed class SubtractConverter : IValueConverter
    {
        public static readonly SubtractConverter Instance = new SubtractConverter();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double v = value is double d ? d : 0;
            double p = 0;
            if (parameter is string s)
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out p);
            else if (parameter is double pd) p = pd;

            double r = v - p;
            return r < 0 ? 0 : r;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
