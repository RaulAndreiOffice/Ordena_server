using Microsoft.Extensions.Logging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// "Reconstruir índice": a new database from what the archive's folders and file names say, when there is
// no backup to restore. Each name gives the date, direction, partner folder, number and type; the content
// gives the SHA-256. What names can't hold comes from the folder instead: the partner's name is its folder
// ("Telefonica Espana"), and the number is the sanitized one. Search works the same on both.
internal sealed class IndexRebuild(
    IUnitOfWorkFactory uowFactory, IFileStorage storage, IClock clock, ILogger<IndexRebuild> logger)
{
    private const int BatchSize = 500;

    public async Task<RebuildReport> RunAsync(string archiveRoot, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        await using (var uow = uowFactory.Create())
        {
            if (await uow.Documents.GetLastUpdateAsync(ct).ConfigureAwait(false) is not null
                || await uow.Partners.AnyAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException("The index can only be rebuilt into an empty database.");
        }

        // Sorted, so when two files have the same content the same one is kept every time.
        List<string> unrecognized = [];
        List<(string Path, ArchivePath Parsed, long Length)> candidates = [];
        foreach (var file in storage.ListFiles(archiveRoot)
                     .Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var parsed = ArchiveNaming.TryParse(ArchiveNaming.ToRelativePath(archiveRoot, file));
            if (parsed is null)
                unrecognized.Add(file);
            else
                candidates.Add((file, parsed, storage.GetLength(file)));
        }

        // Hashing reads every file, so it comes before any write: a cancel leaves the database empty.
        var total = candidates.Sum(c => c.Length);
        var done = 0L;
        HashSet<string> seen = [];
        List<string> duplicates = [];
        List<(string Path, ArchivePath Parsed, long Length, string Sha256)> indexed = [];
        foreach (var (i, candidate) in candidates.Index())
        {
            ct.ThrowIfCancellationRequested();
            var sha256 = TryHash(candidate.Path);
            if (sha256 is null)
                unrecognized.Add(candidate.Path);
            else if (!seen.Add(sha256))
                duplicates.Add(candidate.Path);
            else
                indexed.Add((candidate.Path, candidate.Parsed, candidate.Length, sha256));

            done += candidate.Length;
            progress?.Report(new ArchiveProgress(i + 1, candidates.Count, done, total));
        }

        var now = clock.UtcNow;
        var partners = new Dictionary<string, Partner>(StringComparer.OrdinalIgnoreCase);
        await using (var uow = uowFactory.Create())
        {
            foreach (var folder in indexed.Select(f => f.Parsed.PartnerFolder).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var name = folder.Replace('-', ' ');
                var partner = new Partner
                {
                    Name = name,
                    NameNorm = TextNormalizer.NormalizeName(name),
                    FolderName = folder,
                    CreatedAt = now,
                };
                partners[folder] = partner;
                uow.Partners.Add(partner);
            }
            await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        foreach (var batch in indexed.Chunk(BatchSize))
        {
            await using var uow = uowFactory.Create();
            foreach (var (path, parsed, length, sha256) in batch)
            {
                uow.Documents.Add(new Document
                {
                    Sha256 = sha256,
                    PartnerId = partners[parsed.PartnerFolder].Id,
                    DocNumber = parsed.DocNumber,
                    DocNumberNorm = TextNormalizer.NormalizeNumber(parsed.DocNumber),
                    IssueDate = parsed.IssueDate,
                    Type = parsed.Type,
                    Direction = parsed.Direction,
                    RelativePath = ArchiveNaming.ToRelativePath(archiveRoot, path),
                    OriginalName = Path.GetFileName(path),
                    FileSizeBytes = length,
                    ImportedAt = now,
                    UpdatedAt = now,
                    DeletedAt = parsed.InTrash ? now : null,
                });
            }
            await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Rebuilt the index from {Root}: {Documents} documents, {Partners} partners, {Unrecognized} unrecognized, {Duplicates} duplicates",
            archiveRoot, indexed.Count, partners.Count, unrecognized.Count, duplicates.Count);
        return new RebuildReport(indexed.Count, partners.Count, unrecognized, duplicates);
    }

    // Null for a file another program holds open; it is reported, not indexed.
    private static string? TryHash(string path)
    {
        try
        {
            return Sha256Hasher.ComputeFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
