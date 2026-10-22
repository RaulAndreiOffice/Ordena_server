namespace Ordena.Backend.Dtos;

public enum ArchiveStatus
{
    // Before first run picks the folder.
    NotSet,

    // The folder isn't there, for example on an unplugged USB disk: "La carpeta de facturas no está disponible".
    Unavailable,

    Available,
}
