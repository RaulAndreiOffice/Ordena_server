using System.Diagnostics;
using Ordena.Backend.Dtos;
using Ordena.Backend.Files;
using Ordena.Backend.Models;
using Ordena.Backend.Repositories;
using Ordena.Backend.Results;
using Ordena.Backend.Rules;

namespace Ordena.Backend.Services;

internal sealed class BulkImportService(
    IUnitOfWorkFactory uowFactory,
    IFileStorage storage,
    PartnerResolver partnerResolver,
    IImportService import) : IBulkImportService
{
    // Shorter normalized names aren't grouped by "one inside the other": "sa" is in too many names.
    private const int MinContainedLength = 4;

    public Task<Result<BulkImportPreview>> AnalyzeAsync(BulkImportOptions options, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(options.Folder))
            throw new ArgumentException($"The folder must be an absolute path: '{options.Folder}'.", nameof(options));

        return Task.Run(() => AnalyzeCoreAsync(options, ct), ct);
    }

    public Task<BulkImportReport> RunAsync(BulkImportRun run, IProgress<BulkImportProgress>? progress, CancellationToken ct)
    {
        if (run.Items.Any(i => string.IsNullOrWhiteSpace(i.PartnerName) || string.IsNullOrWhiteSpace(i.DocNumber)))
            throw new ArgumentException("Every item needs a partner and a number.", nameof(run));

        return Task.Run(() => RunCoreAsync(run, progress, ct), CancellationToken.None);
    }

    private async Task<Result<BulkImportPreview>> AnalyzeCoreAsync(BulkImportOptions options, CancellationToken ct)
    {
        await using var uow = uowFactory.Create();
        var root = await ArchiveRoot.GetAsync(uow, storage, ct).ConfigureAwait(false);
        if (!root.IsSuccess)
            return root.FailAs<BulkImportPreview>();

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.Folder));
        if (!storage.DirectoryExists(folder))
            return Result.Fail<BulkImportPreview>(ErrorCode.FileMissing, folder);
        if (ArchiveNaming.AreNested(folder, root.Value))
            return Result.Fail<BulkImportPreview>(ErrorCode.InvalidArchiveFolder, folder);

        var files = storage.ListFiles(folder)
            .Where(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var names = files.Select(Path.GetFileNameWithoutExtension).ToList();
        var detected = PatternDetector.Rank(names!, options.PartnerFromFolder);

        var pattern = ChoosePattern(options, detected);
        if (!pattern.IsSuccess)
            return pattern.FailAs<BulkImportPreview>();

        var existingNumbers = (await uow.Documents.GetNumberKeysAsync(ct).ConfigureAwait(false)).ToHashSet();
        var partners = await uow.Partners.ListAllAsync(ct).ConfigureAwait(false);

        var seenNumbers = new HashSet<(string, string)>();
        var rows = files.Select(file => ReadRow(file, folder, pattern.Value, options.PartnerFromFolder, existingNumbers, seenNumbers)).ToList();
        var used = pattern.Value is { } chosen
            ? new NamePattern(chosen.Pattern, chosen.DateFormat, rows.Count(r => r.Status != BulkRowStatus.NotRecognized && r.Status != BulkRowStatus.InvalidDate), rows.Count)
            : null;

        var counts = rows
            .Where(r => r.PartnerName is not null)
            .GroupBy(r => r.PartnerName!, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(p => p.Count).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var existingByNorm = partners.GroupBy(p => p.NameNorm).ToDictionary(g => g.Key, g => g.First().Name);
        var summary = counts
            .Select(p => new BulkPartner(p.Name, p.Count, existingByNorm.GetValueOrDefault(TextNormalizer.NormalizeName(p.Name))))
            .ToList();

        return Result.Ok(new BulkImportPreview(folder, used, detected, rows, summary, GroupSimilar(counts, partners)));
    }

    // The typed pattern, or the detected one that reads the most names; null when nothing reads any name.
    private static Result<FileNamePattern?> ChoosePattern(BulkImportOptions options, IReadOnlyList<NamePattern> detected)
    {
        if (options.Pattern is not null)
        {
            var typed = FileNamePattern.Create(options.Pattern, options.DateFormat, options.PartnerFromFolder);
            return typed.IsSuccess
                ? Result.Ok<FileNamePattern?>(typed.Value)
                : Result.Fail<FileNamePattern?>(typed.Error.Value, typed.Detail);
        }

        return Result.Ok(detected.Count == 0
            ? null
            : FileNamePattern.Create(detected[0].Pattern, detected[0].DateFormat, options.PartnerFromFolder).Value);
    }

    private BulkImportRow ReadRow(
        string file,
        string folder,
        FileNamePattern? pattern,
        bool partnerFromFolder,
        HashSet<(string, string)> existingNumbers,
        HashSet<(string, string)> seenNumbers)
    {
        var relative = Path.GetRelativePath(folder, file);
        var parsed = pattern?.Match(Path.GetFileNameWithoutExtension(file));
        if (parsed is null || parsed.Status == NameMatch.NotRecognized)
            return new BulkImportRow(file, relative, BulkRowStatus.NotRecognized, null, null, null);

        var partner = partnerFromFolder
            ? FileNamePattern.CleanPartner(Path.GetFileName(Path.GetDirectoryName(file)) ?? "")
            : parsed.Partner!;
        if (parsed.Status == NameMatch.InvalidDate)
            return new BulkImportRow(file, relative, BulkRowStatus.InvalidDate, null, partner, parsed.Number);

        // A number the partner already has, in the archive or earlier in this folder.
        var key = (TextNormalizer.NormalizeName(partner), TextNormalizer.NormalizeNumber(parsed.Number!));
        var repeated = !seenNumbers.Add(key) || existingNumbers.Contains(key);

        var status = parsed.Date is null ? BulkRowStatus.CheckDate
            : repeated ? BulkRowStatus.NumberExists
            : BulkRowStatus.Ok;
        var date = parsed.Date ?? DateOnly.FromDateTime(storage.GetLastWriteTime(file));
        return new BulkImportRow(file, relative, status, date, partner, parsed.Number);
    }

    // Names that are probably one partner: the same once normalized, or one's words inside the other's
    // ("telefonica" in "telefonica espana"). The archive's partners take part, so a short name in the folder
    // is offered the full name already in use.
    private static IReadOnlyList<BulkPartnerGroup> GroupSimilar(
        IReadOnlyList<(string Name, int Count)> names, IReadOnlyList<Partner> existing)
    {
        var nodes = names.Select(n => (n.Name, Norm: TextNormalizer.NormalizeName(n.Name), n.Count, Existing: false))
            .Concat(existing.Select(p => (p.Name, Norm: p.NameNorm, Count: 0, Existing: true)))
            .Where(n => n.Norm.Length > 0)
            .ToList();

        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                if (nodes[i].Existing && nodes[j].Existing)
                    continue; // the archive's own partners are never merged here
                if (Similar(nodes[i].Norm, nodes[j].Norm))
                    parent[Find(i)] = Find(j);
            }
        }

        var groups = new List<BulkPartnerGroup>();
        foreach (var component in Enumerable.Range(0, nodes.Count).GroupBy(Find))
        {
            var members = component.Select(i => nodes[i]).ToList();
            // The spelling used most; on a tie the longer one, usually the more complete name.
            var inFolder = members.Where(m => !m.Existing)
                .OrderByDescending(m => m.Count).ThenByDescending(m => m.Name.Length)
                .ToList();
            var archive = members.Where(m => m.Existing).ToList();
            if (inFolder.Count == 0)
                continue;

            // One name in the folder is worth a suggestion only when it would join a partner of the archive
            // that it doesn't already match (the same normalized name joins it anyway).
            var suggested = archive.Count > 0 ? archive[0].Name : inFolder[0].Name;
            var differs = inFolder.Count > 1 || archive.Any(a => a.Norm != inFolder[0].Norm);
            if (differs)
                groups.Add(new BulkPartnerGroup(inFolder.Select(m => m.Name).ToList(), suggested));
        }
        return groups;
    }

    private static bool Similar(string a, string b)
    {
        if (a == b)
            return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= MinContainedLength && $" {longer} ".Contains($" {shorter} ", StringComparison.Ordinal);
    }

    private async Task<BulkImportReport> RunCoreAsync(BulkImportRun run, IProgress<BulkImportProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        string FinalName(string name) => run.PartnerMap.TryGetValue(name, out var mapped) ? mapped : name;
        var invalidPartners = await CreatePartnersAsync(run.Items.Select(i => FinalName(i.PartnerName))).ConfigureAwait(false);

        var imported = 0;
        var duplicates = new List<string>();
        var numberExists = new List<string>();
        var failed = new List<BulkImportFailure>();
        var cancelled = false;
        foreach (var (i, item) in run.Items.Index())
        {
            // Only between files: each import is whole, so a cancelled run leaves a consistent archive.
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var partner = FinalName(item.PartnerName);
            if (invalidPartners.Contains(partner))
            {
                failed.Add(new BulkImportFailure(item.Path, ErrorCode.InvalidPartnerName));
            }
            else
            {
                var result = await import.ImportAsync(new ImportRequest
                {
                    SourcePath = item.Path,
                    PartnerName = partner,
                    DocNumber = item.DocNumber,
                    IssueDate = item.IssueDate,
                    Type = item.Type,
                    Direction = run.Direction,
                    IsPaid = run.MarkPaid,
                    ConfirmDuplicate = item.IncludeDuplicateNumber,
                    KeepSource = true,
                }, CancellationToken.None).ConfigureAwait(false);

                if (result.IsSuccess)
                    imported++;
                else if (result.Error is ErrorCode.AlreadyArchived or ErrorCode.AlreadyInTrash)
                    duplicates.Add(item.Path);
                else if (result.Error == ErrorCode.DuplicateNumber)
                    numberExists.Add(item.Path);
                else
                    failed.Add(new BulkImportFailure(item.Path, result.Error.Value));
            }

            progress?.Report(new BulkImportProgress(i + 1, run.Items.Count, Path.GetFileName(item.Path)));
        }

        return new BulkImportReport(imported, duplicates, numberExists, failed, cancelled, watch.Elapsed);
    }

    // Partners first, in one transaction: each new one gets its folder name once, whatever the order.
    // Returns the names that can't be a partner's.
    private async Task<HashSet<string>> CreatePartnersAsync(IEnumerable<string> names)
    {
        var invalid = new HashSet<string>(StringComparer.Ordinal);
        await using var uow = uowFactory.Create();
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            if (!(await partnerResolver.GetOrCreateAsync(uow, name, CancellationToken.None).ConfigureAwait(false)).IsSuccess)
                invalid.Add(name);
        }
        await uow.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return invalid;
    }
}
