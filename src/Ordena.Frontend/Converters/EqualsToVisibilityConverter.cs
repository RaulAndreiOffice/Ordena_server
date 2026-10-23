using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ordena.Frontend.Converters;

// Visible when the value, as text, equals the parameter: one panel per step of a wizard
// (Visibility="{Binding Step, Converter=..., ConverterParameter=Preview}").
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
