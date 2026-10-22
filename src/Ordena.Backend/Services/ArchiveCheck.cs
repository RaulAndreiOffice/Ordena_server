using Microsoft.Extensions.Logging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// "Comprobar carpeta": compares the PDFs in the archive with the documents in the database.
// A document whose file was moved or renamed by hand gets it back: the file is found among the PDFs
// no document points to, by name first and then by content, and moved back where the row says.
// The row itself never changes, so no journal is needed.
internal sealed class ArchiveCheck(IUnitOfWorkFactory uowFactory, IFileStorage storage, ILogger<ArchiveCheck> logger)
{
    public async Task<ArchiveCheckReport> RunAsync(string archiveRoot, CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        var documents = await uow.Documents.ListAllAsync(ct).ConfigureAwait(false);

        // Only PDFs: that also leaves out the _backup databases and unfinished ".tmp" copies.
        var files = storage.ListFiles(archiveRoot)
            .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => ArchiveNaming.ToRelativePath(archiveRoot, f), StringComparer.OrdinalIgnoreCase);

        var unknown = new Dictionary<string, string>(files, StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
            unknown.Remove(document.RelativePath);

        var hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        List<RelinkedFile> relinked = [];
        List<DocumentDetails> missing = [];

        foreach (var document in documents.Where(d => !files.ContainsKey(d.RelativePath)))
        {
            ct.ThrowIfCancellationRequested();
            var found = FindByName(document) ?? FindByContent(document);
            if (found is null || !await MoveBackAsync(found, document).ConfigureAwait(false))
            {
                missing.Add(document.ToDetails());
                logger.LogWarning("The file of document {Id} is missing: {RelativePath}", document.Id, document.RelativePath);
                continue;
            }
            relinked.Add(new RelinkedFile(document.Id, found, document.RelativePath));
        }

        logger.LogInformation("Checked {Count} documents: {Relinked} moved back, {Missing} missing, {Unknown} unknown PDFs",
            documents.Count, relinked.Count, missing.Count, unknown.Count);
        return new ArchiveCheckReport(
            documents.Count, relinked, missing, unknown.Values.Order(StringComparer.OrdinalIgnoreCase).ToList());

        async Task<bool> MoveBackAsync(string foundAt, Document document)
        {
            var fullPath = unknown[foundAt];
            var moved = await storage.MoveAsync(
                fullPath, ArchiveNaming.ToFullPath(archiveRoot, document.RelativePath), ct).ConfigureAwait(false);
            if (!moved.IsSuccess)
                return false;

            unknown.Remove(foundAt);
            storage.TryDeleteEmptyFolders(Path.GetDirectoryName(fullPath)!, archiveRoot);
            logger.LogInformation("Moved {FoundAt} back to {RelativePath}", foundAt, document.RelativePath);
            return true;
        }

        // Same file name in another folder, and the same content.
        string? FindByName(Document document)
        {
            var name = Path.GetFileName(document.RelativePath);
            return unknown.Keys.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)
                && HashOf(path) == document.Sha256);
        }

        // Renamed: any unknown PDF with the same content. Each file is hashed once, only when needed.
        string? FindByContent(Document document) =>
            unknown.Keys.FirstOrDefault(path => HashOf(path) == document.Sha256);

        string? HashOf(string relativePath)
        {
            if (!hashes.TryGetValue(relativePath, out var hash))
            {
                try
                {
                    hash = Sha256Hasher.ComputeFile(unknown[relativePath]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    hash = null; // open in another program: can't be compared this time
                }
                hashes[relativePath] = hash;
            }
            return hash;
        }
    }
}
