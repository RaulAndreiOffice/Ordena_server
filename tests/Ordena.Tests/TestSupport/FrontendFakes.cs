using System.Text;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;

namespace Ordena.Tests.TestSupport;

// Settings kept in memory, recording what was saved and in which order.
internal sealed class FakeSettingsService : ISettingsService
{
    public string Language { get; set; } = UserSettings.Spanish;
    public string? ArchiveRoot { get; set; }
    public string Suggestion { get; set; } = @"C:\Users\Ana\Documents\Facturas";

    // What SetArchiveRootAsync answers; Ok saves the folder.
    public Result ArchiveRootResult { get; set; } = Result.Ok();

    public List<string> Saved { get; } = [];

    public Task<UserSettings> GetAsync(CancellationToken ct) =>
        Task.FromResult(new UserSettings(ArchiveRoot, Language, null));

    public Task<Result> SetArchiveRootAsync(string path, CancellationToken ct)
    {
        Saved.Add($"root {path}");
        if (ArchiveRootResult.IsSuccess)
            ArchiveRoot = path;
        return Task.FromResult(ArchiveRootResult);
    }

    public Task SetLanguageAsync(string language, CancellationToken ct)
    {
        Saved.Add($"language {language}");
        Language = language;
        return Task.CompletedTask;
    }

    public string SuggestArchiveRoot() => Suggestion;
}

// Answers with Choice / Files / PdfChoice (null or empty = the user cancelled) and remembers where
// it was opened.
internal sealed class FakeFilePicker : IFilePickerService
{
    public string? Choice { get; set; }
    public string? OpenedAt { get; private set; }
    public IReadOnlyList<string> Files { get; set; } = [];
    public string? PdfChoice { get; set; }

    public string? PickFolder(string title, string? initialFolder)
    {
        OpenedAt = initialFolder;
        return Choice;
    }

    public IReadOnlyList<string> PickPdfFiles(string title, string filterName) => Files;

    public string? PickPdfFile(string title, string filterName, string? initialFolder)
    {
        OpenedAt = initialFolder;
        return PdfChoice;
    }
}

// Records every message instead of showing it; ConfirmAsync answers Confirm, and the edit dialog
// "saves" when EditSaves.
internal sealed class FakeDialogs : IDialogService
{
    public List<string> Errors { get; } = [];
    public List<string> Questions { get; } = [];
    public List<int> EditedIds { get; } = [];
    public bool Confirm { get; set; } = true;
    public bool EditSaves { get; set; } = true;

    public Task<bool> ShowEditDocumentAsync(int documentId)
    {
        EditedIds.Add(documentId);
        return Task.FromResult(EditSaves);
    }

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string message) => Task.CompletedTask;

    public Task<bool> ConfirmAsync(string message)
    {
        Questions.Add(message);
        return Task.FromResult(Confirm);
    }
}

// "Copies" a file by naming it preview:<path>; files in Unreadable give null, as a locked file would.
internal sealed class FakePreview : IPreviewService
{
    public HashSet<string> Unreadable { get; } = [];
    public List<string?> Released { get; } = [];

    public Task<string?> CreateCopyAsync(string path) =>
        Task.FromResult(Unreadable.Contains(path) ? null : $"preview:{path}");

    public void Release(string? copy) => Released.Add(copy);
}

// Records where pages asked to go.
internal sealed class FakeNavigation : INavigationService
{
    public List<Type> Visited { get; } = [];

    public PageViewModel? CurrentPage => null;

    public event EventHandler? CurrentPageChanged { add { } remove { } }

    public Task NavigateToAsync(Type pageType)
    {
        Visited.Add(pageType);
        return Task.CompletedTask;
    }
}

// Counts restarts instead of ending the test run.
internal sealed class FakeAppLifetime : IAppLifetime
{
    public int Restarts { get; private set; }

    public void Restart() => Restarts++;
}

internal sealed class FakeShell : IShellService
{
    public List<string> Opened { get; } = [];
    public List<string> ShownInFolder { get; } = [];

    public void OpenFile(string path) => Opened.Add(path);

    public void ShowInFolder(string path) => ShownInFolder.Add(path);
}

// Records each query and answers with Items (TotalCount when the result should look truncated).
// With Hold, every answer waits in Held until the test completes it: to answer out of order.
internal sealed class FakeSearchService : ISearchService
{
    public List<SearchQuery> Queries { get; } = [];
    public IReadOnlyList<DocumentListItem> Items { get; set; } = [];
    public int? TotalCount { get; set; }
    public IReadOnlyList<int> Years { get; set; } = [];
    public int YearLoads { get; private set; }
    public bool Hold { get; set; }
    public List<TaskCompletionSource<SearchResult>> Held { get; } = [];

    public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        if (!Hold)
            return Task.FromResult(new SearchResult(Items, TotalCount ?? Items.Count));

        var answer = new TaskCompletionSource<SearchResult>();
        Held.Add(answer);
        return answer.Task;
    }

    public Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken ct)
    {
        YearLoads++;
        return Task.FromResult(Years);
    }
}

// Checks answer from Checks (Ok when the path isn't there); imports from Answer, which by default
// archives the file as document 1, 2, 3… Every request is recorded.
internal sealed class FakeImportService : IImportService
{
    public FakeImportService() => Answer = request => Archived(request, Requests.Count);

    public Dictionary<string, Result> Checks { get; } = [];
    public List<string> Checked { get; } = [];
    public List<ImportRequest> Requests { get; } = [];
    public Func<ImportRequest, Result<ImportResult>> Answer { get; set; }

    public Task<Result> CheckAsync(string sourcePath, CancellationToken ct)
    {
        Checked.Add(sourcePath);
        return Task.FromResult(Checks.GetValueOrDefault(sourcePath) ?? Result.Ok());
    }

    public Task<Result<ImportResult>> ImportAsync(ImportRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(Answer(request));
    }

    // Filed as 2026/1T/Recibidas/<partner>/<file name>.
    public static Result<ImportResult> Archived(ImportRequest request, int id, bool sourceDeleted = true) =>
        Result.Ok(new ImportResult(
            new DocumentListItem(id, request.IssueDate, request.PartnerName, request.DocNumber, request.Type,
                request.Direction, $"2026/1T/Recibidas/{request.PartnerName}/{Path.GetFileName(request.SourcePath)}"),
            sourceDeleted));
}

// Documents in memory. A document without a FullPaths entry has a missing file, expected at
// ExpectedPaths[id]. Every edit, delete, restore and re-link is recorded; the *Result properties
// say how they answer. A restored document leaves Trash.
internal sealed class FakeDocumentService : IDocumentService
{
    public Dictionary<int, DocumentDetails> Details { get; } = [];
    public Dictionary<int, string> FullPaths { get; } = [];
    public Dictionary<int, string> ExpectedPaths { get; } = [];
    public List<DocumentDetails> Trash { get; } = [];

    public List<EditDocumentRequest> Edits { get; } = [];
    public List<int> Deleted { get; } = [];
    public List<int> Restored { get; } = [];
    public List<(int Id, string Path)> Relinked { get; } = [];

    public Func<EditDocumentRequest, Result<DocumentListItem>> EditAnswer { get; set; } = request =>
        Result.Ok(new DocumentListItem(request.Id, request.IssueDate, request.PartnerName, request.DocNumber,
            request.Type, request.Direction, "edited.pdf"));
    public Result DeleteResult { get; set; } = Result.Ok();
    public Result RestoreResult { get; set; } = Result.Ok();
    public Result RelinkResult { get; set; } = Result.Ok();

    public Task<DocumentDetails> GetAsync(int id, CancellationToken ct) => Task.FromResult(Details[id]);

    public Task<Result<string>> GetFullPathAsync(int id, CancellationToken ct) =>
        Task.FromResult(FullPaths.TryGetValue(id, out var path)
            ? Result.Ok(path)
            : Result.Fail<string>(ErrorCode.FileMissing, ExpectedPaths.GetValueOrDefault(id)));

    public Task<Result<DocumentListItem>> EditAsync(EditDocumentRequest request, CancellationToken ct)
    {
        Edits.Add(request);
        return Task.FromResult(EditAnswer(request));
    }

    public Task<Result> DeleteAsync(int id, CancellationToken ct)
    {
        Deleted.Add(id);
        return Task.FromResult(DeleteResult);
    }

    public Task<Result> RestoreAsync(int id, CancellationToken ct)
    {
        Restored.Add(id);
        if (RestoreResult.IsSuccess)
            Trash.RemoveAll(d => d.Id == id);
        return Task.FromResult(RestoreResult);
    }

    public Task<IReadOnlyList<DocumentDetails>> ListTrashAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DocumentDetails>>(Trash.ToList());

    public Task<Result> RelinkAsync(int id, string path, CancellationToken ct)
    {
        Relinked.Add((id, path));
        if (RelinkResult.IsSuccess)
            FullPaths[id] = path;
        return Task.FromResult(RelinkResult);
    }
}

// Partners by name. Suggestions contain the text; a similar one has the same letters and digits once
// accents, case and punctuation are ignored (simpler than the real normalization, enough for the form).
internal sealed class FakePartnerService(params string[] names) : IPartnerService
{
    private readonly List<Partner> _partners = names.Select((n, i) => new Partner { Id = i + 1, Name = n }).ToList();

    public Task<IReadOnlyList<Partner>> SuggestAsync(string text, int max, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Partner>>(text.Trim().Length == 0
            ? []
            : _partners.Where(p => Key(p.Name).Contains(Key(text))).Take(max).ToList());

    public Task<Partner?> FindSimilarAsync(string name, CancellationToken ct) =>
        Task.FromResult(_partners.FirstOrDefault(p => Key(name).Length > 0 && Key(p.Name) == Key(name)));

    private static string Key(string text) => new(text.Normalize(NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());
}

internal sealed class FakeBulkImportService : IBulkImportService
{
    public Func<BulkImportOptions, Result<BulkImportPreview>> AnalyzeAnswer { get; set; } = _ =>
        Result.Ok(new BulkImportPreview("", null, [], [], [], []));

    public Func<BulkImportRun, BulkImportReport> RunAnswer { get; set; } = _ =>
        new BulkImportReport(0, [], [], [], false, TimeSpan.Zero);

    public Task<Result<BulkImportPreview>> AnalyzeAsync(BulkImportOptions options, CancellationToken ct) =>
        Task.FromResult(AnalyzeAnswer(options));

    public Task<BulkImportReport> RunAsync(BulkImportRun run, IProgress<BulkImportProgress>? progress, CancellationToken ct) =>
        Task.FromResult(RunAnswer(run));
}

