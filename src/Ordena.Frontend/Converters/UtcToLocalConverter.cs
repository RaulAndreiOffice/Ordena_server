using System.Globalization;
using System.Windows.Data;

namespace Ordena.Frontend.Converters;

// The database keeps times in UTC (SQLite hands them back without a kind); the screen shows local time.
public sealed class UtcToLocalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime() : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
