namespace Ordena.Backend.Dtos;

public sealed record UserSettings(string? ArchiveRoot, string Language, DateTime? LastBackupUtc)
{
    public const string Spanish = "es";
    public const string English = "en";
}
