namespace Ordena.Backend.Infrastructure;

// Lets tests fix "now".
internal interface IClock
{
    DateTime UtcNow { get; }
}
