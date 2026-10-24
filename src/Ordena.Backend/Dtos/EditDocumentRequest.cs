using Ordena.Backend.Models;

namespace Ordena.Backend.Dtos;

// The new values for an archived document. Its file is renamed or moved to match them.
public sealed record EditDocumentRequest
{
    public required int Id { get; init; }

    // As typed. A new name creates the partner and its folder.
    public required string PartnerName { get; init; }

    public required string DocNumber { get; init; }
    public required DateOnly IssueDate { get; init; }
    public required DocType Type { get; init; }
    public required Direction Direction { get; init; }

    // Paid (received) or collected (issued). It isn't in the file name, so it never moves the file.
    public required bool IsPaid { get; init; }

    // Set after the user confirmed the DuplicateNumber warning: request with { ConfirmDuplicate = true }.
    public bool ConfirmDuplicate { get; init; }
}
