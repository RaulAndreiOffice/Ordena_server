using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ordena.Frontend.Converters;

// Collapsed for null or an empty string, Visible otherwise: optional texts and hints.
// The other way round with Invert: a placeholder shown while a box is empty.
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null or "") == Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
