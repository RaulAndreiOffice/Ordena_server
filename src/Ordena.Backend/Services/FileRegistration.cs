using Microsoft.Extensions.Logging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Infrastructure;
using Ordena.Backend.Repositories;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

// "Añadir a Ordena" for the PDFs "Comprobar carpeta" found in the archive with no document: copied in by
// hand, or left behind by a restore. A PDF whose path follows the app's naming is registered where it is,
// as "Reconstruir índice" does for a whole archive, so the file doesn't move. Everything else is left for
// the manual import queue, which says why: other names, no number in the name, content already archived,
// a number the partner already has, and files outside the archive.
internal sealed class FileRegistration(
    IUnitOfWorkFactory uowFactory, IFileStorage storage, IClock clock, ILogger<FileRegistration> logger)
{
    public async Task<RegisterReport> RunAsync(string archiveRoot, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var now = clock.UtcNow;
        List<string> remaining = [];
        var registered = 0;

        await using var uow = uowFactory.Create();
        var partners = (await uow.Partners.ListAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(p => p.FolderName, StringComparer.OrdinalIgnoreCase);

        // Two copies of one file, or two files with one number, in this same batch: the first one wins.
        HashSet<string> hashes = [];
        HashSet<(string Folder, string Number)> numbers = [];

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            var parsed = ArchiveNaming.IsInArchive(archiveRoot, path) && storage.FileExists(path)
                ? ArchiveNaming.TryParse(ArchiveNaming.ToRelativePath(archiveRoot, path))
                : null;
            var sha256 = parsed is { DocNumber.Length: > 0 } ? IndexRebuild.TryHash(path) : null;
            if (parsed is null || sha256 is null
                || !hashes.Add(sha256)
                || await uow.Documents.GetByHashAsync(sha256, ct).ConfigureAwait(false) is not null)
            {
                remaining.Add(path);
                continue;
            }

            var numberNorm = TextNormalizer.NormalizeNumber(parsed.DocNumber);
            partners.TryGetValue(parsed.PartnerFolder, out var partner);
            var numberTaken = !parsed.InTrash
                && (!numbers.Add((parsed.PartnerFolder.ToUpperInvariant(), numberNorm))
                    || (partner is { Id: > 0 }
                        && await uow.Documents.NumberExistsAsync(partner.Id, numberNorm, null, ct).ConfigureAwait(false)));
            if (numberTaken)
            {
                remaining.Add(path);
                continue;
            }

            if (partner is null)
            {
                partner = IndexRebuild.NewPartner(parsed.PartnerFolder, now);
                partners[parsed.PartnerFolder] = partner;
                uow.Partners.Add(partner);
            }

            var relativePath = ArchiveNaming.ToRelativePath(archiveRoot, path);
            var document = IndexRebuild.NewDocument(parsed, relativePath, storage.GetLength(path), sha256, now);
            if (partner.Id > 0)
                document.PartnerId = partner.Id;
            else
                document.Partner = partner; // created above, saved with the documents
            uow.Documents.Add(document);
            registered++;
            logger.LogInformation("Registered {RelativePath} where it is", relativePath);
        }

        await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return new RegisterReport(registered, remaining);
    }
}
