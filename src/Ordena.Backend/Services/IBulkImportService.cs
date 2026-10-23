using Ordena.Backend.Dtos;
using Ordena.Backend.Results;

namespace Ordena.Backend.Services;

// "Importar carpeta": a one-time copy of an existing archive whose files are named consistently.
public interface IBulkImportService
{
    // Reads the names of every PDF under the folder (subfolders included) and how the pattern reads them.
    // Only names: no file is opened or changed. Expected failures:
    // - FileMissing: the folder doesn't exist
    // - ArchiveUnavailable: the archive folder doesn't exist
    // - InvalidArchiveFolder: the folder is the archive, a folder inside it, or one that contains it
    // - InvalidNamePattern: a typed pattern that can't be used; Detail is why (see FileNamePattern.Create)
    Task<Result<BulkImportPreview>> AnalyzeAsync(BulkImportOptions options, CancellationToken ct);

    // Copies each item into the archive through the normal import (KeepSource): the folder is never changed.
    // Partners are created first, in one transaction. Cancelling stops between two files, so every file
    // copied so far is complete and in the database. An item with a blank partner or number throws
    // ArgumentException: the wizard only sends complete rows.
    Task<BulkImportReport> RunAsync(BulkImportRun run, IProgress<BulkImportProgress>? progress, CancellationToken ct);
}
