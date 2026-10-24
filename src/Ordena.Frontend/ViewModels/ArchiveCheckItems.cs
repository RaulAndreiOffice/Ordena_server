using Ordena.Backend.Dtos;

namespace Ordena.Frontend.ViewModels;

// A document "Comprobar carpeta" found without its file: "Repsol S.A. · T-88812 · 20/03/2026", and where
// the file should be. "Buscar archivo" links it again.
public sealed record MissingDocumentItem(DocumentDetails Document, string Label);

// A PDF in the archive that no document points to. Label is its path inside the archive.
public sealed record UnknownFileItem(string Path, string Label);
