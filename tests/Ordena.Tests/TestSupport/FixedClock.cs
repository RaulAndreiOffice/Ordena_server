using Ordena.Backend.Infrastructure;

namespace Ordena.Tests.TestSupport;

internal sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}
