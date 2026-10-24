using Ordena.Backend.Models;

namespace Ordena.Backend.Dtos;

// One PDF to archive, with what the user typed for it.
public sealed record ImportRequest
{
    // Absolute path of the dropped file. It is deleted once the copy in the archive is committed.
    public required string SourcePath { get; init; }

    // As typed. A new name creates the partner and its folder.
    public required string PartnerName { get; init; }

    public required string DocNumber { get; init; }
    public required DateOnly IssueDate { get; init; }
    public required DocType Type { get; init; }
    public required Direction Direction { get; init; }

    // Already paid (received) or collected (issued). A new document is pending unless the user says so.
    public bool IsPaid { get; init; }

    // Set after the user confirmed the DuplicateNumber warning: request with { ConfirmDuplicate = true }.
    public bool ConfirmDuplicate { get; init; }

    // Copy mode: the original stays where it is (bulk import of an old archive, which is its backup).
    public bool KeepSource { get; init; }
}
