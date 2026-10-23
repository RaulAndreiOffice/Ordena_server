using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Ordena.Frontend.Converters;

// A resource key from a ViewModel ("Icon.Bold.Search") → the resource itself, so ViewModels can
// name icons without referencing WPF types.
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
