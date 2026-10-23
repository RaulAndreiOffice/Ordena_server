using System.Globalization;
using System.Windows.Data;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.Converters;

// DocType and Direction values in the grids, in the app's language ("DocType_Factura" → "Factura").
public sealed class EnumToLocalizedConverter : IValueConverter
{
    private readonly Localizer _localizer = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum item ? _localizer.Get($"{item.GetType().Name}_{item}") : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
