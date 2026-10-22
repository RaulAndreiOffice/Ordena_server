namespace Ordena.Backend.Models;

// Keys: ArchiveRoot, Language (es / en), LastBackup.
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
