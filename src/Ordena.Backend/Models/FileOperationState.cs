namespace Ordena.Backend.Models;

// Stored as INTEGER. Values are explicit and must never be renumbered.
public enum FileOperationState
{
    Pending = 1,
    Done = 2,
    Failed = 3,
}
