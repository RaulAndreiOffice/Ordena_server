using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ordena.Frontend.Converters;

// true → Visible, false → Collapsed; the other way round with Invert.
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
