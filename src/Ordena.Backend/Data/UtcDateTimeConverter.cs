using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ordena.Backend.Data;

// SQLite stores DateTime as TEXT and loses its Kind. Every timestamp is written as UTC,
// so mark it as UTC on read; otherwise ToLocalTime() would treat it as local time.
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    public static readonly UtcDateTimeConverter Instance = new();
}
