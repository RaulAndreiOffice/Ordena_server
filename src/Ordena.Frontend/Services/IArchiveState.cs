using System.ComponentModel;

namespace Ordena.Frontend.Services;

// The archive as the screens see it: where it is, whether it is there, and whether a bulk import is
// copying into it. One for the app: the shell's banner and footer and Ajustes follow its changes.
public interface IArchiveState : INotifyPropertyChanged
{
    // Null before first run picks a folder.
    string? Root { get; }

    // False while the folder isn't there ("La carpeta de facturas no está disponible"): search still
    // works, opening and importing wait.
    bool IsAvailable { get; }

    // Set by "Importar carpeta" while its run copies files in: the archive mustn't move or be checked meanwhile.
    bool IsImporting { get; set; }

    // Reads the setting and looks for the folder again ("Reintentar", and on every page change). When the
    // folder is back, the moves a crash interrupted while it was away are settled.
    Task RefreshAsync();

    // "Buscar…" and "Cambiar ubicación…": the folder the archive is in now, picked by the user and checked
    // to hold its files. False when they cancel or it isn't the archive (after saying so).
    Task<bool> LocateAsync();

    // After "Mover carpeta de facturas" switched to newRoot.
    void SetRoot(string newRoot);
}
