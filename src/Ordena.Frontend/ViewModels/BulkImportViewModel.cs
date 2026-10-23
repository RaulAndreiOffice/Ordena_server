using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

public enum BulkStep
{
    Folder,
    Pattern,
    Preview,
    Partners,
    Running,
    Report,
}

// "Importar carpeta": a one-time copy of an existing, consistently named archive. The folder is analyzed
// by name only, the user checks the pattern, the rows and the partner names, and the run copies each
// file through the normal import; the old folder is never changed. A singleton, so a run in progress
// survives switching pages.
public sealed partial class BulkImportViewModel : PageViewModel
{
    private readonly IBulkImportService _bulk;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly IMessenger _messenger;
    private readonly INavigationService _navigation;

    // The options the current preview was analyzed with; re-analysis changes only the pattern.
    private BulkImportOptions? _options;
    private CancellationTokenSource? _pendingPattern;
    private CancellationTokenSource? _run;

    // Set while the page itself picks a pattern card, so that doesn't analyze again.
    private bool _settingPattern;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepNumber))]
    private BulkStep _step;

    // Step 1
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeCommand))]
    private string _folder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIssued))]
    private bool _isReceived = true;

    [ObservableProperty]
    private TypeOption _selectedType;

    [ObservableProperty]
    private bool _partnerFromFolder;

    // Step 2
    [ObservableProperty]
    private IReadOnlyList<PatternOption> _patterns = [];

    [ObservableProperty]
    private PatternOption? _selectedPattern;

    [ObservableProperty]
    private bool _useCustomPattern;

    [ObservableProperty]
    private string _customPattern = "{fecha}_{proveedor}_{numero}";

    [ObservableProperty]
    private string _customDateFormat = "yyyy-MM-dd";

    // Why the typed pattern can't be used, or null.
    [ObservableProperty]
    private string? _patternError;

    // "2.871 de 3.000 coinciden" for what is used now, and three of its names as read.
    [ObservableProperty]
    private string _matchText = "";

    [ObservableProperty]
    private IReadOnlyList<string> _examples = [];

    // Step 3
    [ObservableProperty]
    private IReadOnlyList<BulkRowViewModel> _rows = [];

    [ObservableProperty]
    private IReadOnlyList<StatusFilterOption> _statusFilters = [];

    [ObservableProperty]
    private StatusFilterOption? _selectedStatusFilter;

    [ObservableProperty]
    private IReadOnlyList<BulkRowViewModel> _filteredRows = [];

    // "2.871 se importarán"
    [ObservableProperty]
    private string _importCountText = "";

    // Step 4
    [ObservableProperty]
    private IReadOnlyList<PartnerGroupViewModel> _groups = [];

    [ObservableProperty]
    private IReadOnlyList<PartnerNameViewModel> _partners = [];

    // Step 5
    [ObservableProperty]
    private int _done;

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private string _progressText = "";

    // The report
    [ObservableProperty]
    private string _reportText = "";

    // What wasn't imported and needs a person: not recognized, left out, failed, or a number already taken.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendToManualCommand))]
    private IReadOnlyList<string> _leftOut = [];

    [ObservableProperty]
    private string _leftOutText = "";

    public BulkImportViewModel(
        IBulkImportService bulk,
        IFilePickerService picker,
        IDialogService dialogs,
        ILocalizer localizer,
        IMessenger messenger,
        INavigationService navigation)
    {
        _bulk = bulk;
        _picker = picker;
        _dialogs = dialogs;
        _localizer = localizer;
        _messenger = messenger;
        _navigation = navigation;
        Types = Enum.GetValues<DocType>().Select(t => new TypeOption(t, localizer.Enum(t))).ToList();
        _selectedType = Types[0];
        DateFormats = NamePatterns.DateFormats;
    }

    public override string Title => _localizer.Get("Page_BulkImport");

    public IReadOnlyList<TypeOption> Types { get; }
    public IReadOnlyList<string> DateFormats { get; }

    public bool IsIssued
    {
        get => !IsReceived;
        set => IsReceived = !value;
    }

    // 1 to 5 for the step header; the report is still step 5.
    public int StepNumber => Math.Min((int)Step + 1, 5);

    // How long typing a pattern must pause before the folder is read again. Zero in tests.
    public TimeSpan PatternDelay { get; set; } = TimeSpan.FromMilliseconds(400);

    // The latest re-analysis and the run, for tests to await.
    internal Task PendingAnalysis { get; private set; } = Task.CompletedTask;
    internal Task PendingRun { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private void Browse()
    {
        if (_picker.PickFolder(_localizer.Get("Bulk_PickFolderTitle"), Folder.Length > 0 ? Folder : null) is { } folder)
            Folder = folder;
    }

    // Step 1 → 2: read the folder's names with the pattern that reads the most of them.
    [RelayCommand(CanExecute = nameof(HasFolder))]
    private async Task AnalyzeAsync()
    {
        var options = new BulkImportOptions
        {
            Folder = Folder.Trim(),
            Direction = IsReceived ? Direction.Recibida : Direction.Emitida,
            Type = SelectedType.Type,
            PartnerFromFolder = PartnerFromFolder,
        };
        if (!System.IO.Path.IsPathFullyQualified(options.Folder))
        {
            await _dialogs.ShowErrorAsync(_localizer.Get("Bulk_FolderMissing"));
            return;
        }

        var preview = await _bulk.AnalyzeAsync(options, CancellationToken.None);
        if (!preview.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(FolderError(preview.Error.Value));
            return;
        }

        _options = options;
        UseCustomPattern = false;
        PatternError = null;
        Show(preview.Value);
        _settingPattern = true;
        Patterns = preview.Value.Detected.Select(ToOption).ToList();
        SelectedPattern = Patterns.FirstOrDefault();
        _settingPattern = false;
        Step = BulkStep.Pattern;
    }

    private bool HasFolder() => !string.IsNullOrWhiteSpace(Folder);

    // Another detected pattern, or a typed one (after a pause): the folder is read again with it.
    // Picking a card while a typed pattern is on goes back to the cards (which re-analyzes with it).
    partial void OnSelectedPatternChanged(PatternOption? value)
    {
        if (_settingPattern || value is null)
            return;
        if (UseCustomPattern)
            UseCustomPattern = false;
        else
            PendingAnalysis = ReanalyzeAsync(value.Pattern, value.DateFormat, TimeSpan.Zero);
    }

    partial void OnUseCustomPatternChanged(bool value)
    {
        if (_options is null)
            return;
        PendingAnalysis = value
            ? ReanalyzeAsync(CustomPattern, CustomDateFormat, TimeSpan.Zero)
            : SelectedPattern is { } pattern ? ReanalyzeAsync(pattern.Pattern, pattern.DateFormat, TimeSpan.Zero) : Task.CompletedTask;
    }

    partial void OnCustomPatternChanged(string value)
    {
        if (UseCustomPattern)
            PendingAnalysis = ReanalyzeAsync(value, CustomDateFormat, PatternDelay);
    }

    partial void OnCustomDateFormatChanged(string value)
    {
        if (UseCustomPattern)
            PendingAnalysis = ReanalyzeAsync(CustomPattern, value, TimeSpan.Zero);
    }

    private async Task ReanalyzeAsync(string pattern, string? dateFormat, TimeSpan delay)
    {
        if (_options is null)
            return;

        _pendingPattern?.Cancel();
        var pending = _pendingPattern = new CancellationTokenSource();
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, pending.Token);

            var preview = await _bulk.AnalyzeAsync(_options with { Pattern = pattern, DateFormat = dateFormat }, pending.Token);
            if (pending.IsCancellationRequested)
                return;

            if (preview.Error == ErrorCode.InvalidNamePattern)
            {
                PatternError = _localizer.Get($"Bulk_PatternError_{preview.Detail}");
                return;
            }
            if (!preview.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(FolderError(preview.Error.Value));
                return;
            }

            PatternError = null;
            Show(preview.Value);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            // A newer pattern took over.
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (Step == BulkStep.Pattern && PatternError is null)
            Step = BulkStep.Preview;
        else if (Step == BulkStep.Preview)
            Step = BulkStep.Partners;
    }

    [RelayCommand]
    private void Back()
    {
        Step = Step switch
        {
            BulkStep.Pattern => BulkStep.Folder,
            BulkStep.Preview => BulkStep.Pattern,
            BulkStep.Partners => BulkStep.Preview,
            _ => Step,
        };
    }

    partial void OnSelectedStatusFilterChanged(StatusFilterOption? value) => Filter();

    // "Usar la fecha del archivo": every row dated by its file is imported with that date.
    [RelayCommand]
    private void IncludeFileDates()
    {
        foreach (var row in Rows.Where(r => r.Status == BulkRowStatus.CheckDate))
            row.Include = true;
        Recount();
    }

    // Step 4 → 5: copy every row that will be imported, with the partner names as decided.
    [RelayCommand]
    private Task ImportAsync() => PendingRun = RunAsync();

    private async Task RunAsync()
    {
        var items = Rows.Where(r => r.WillImport)
            .Select(r => new BulkImportItem(r.Path, r.Date!.Value, r.Partner.Trim(), r.Number.Trim(), r.Type.Type,
                IncludeDuplicateNumber: r.Status == BulkRowStatus.NumberExists))
            .ToList();
        var run = new BulkImportRun(_options!.Direction, items, PartnerMap());

        _run = new CancellationTokenSource();
        Done = 0;
        Total = items.Count;
        ProgressText = "";
        Step = BulkStep.Running;
        var progress = new Progress<BulkImportProgress>(p =>
        {
            Done = p.Done;
            ProgressText = _localizer.Format("Bulk_Progress", p.Done, p.Total, p.CurrentFile);
        });

        var report = await _bulk.RunAsync(run, progress, _run.Token);

        ShowReport(report, items);
        _messenger.Send(new DocumentsChangedMessage(DocumentsChange.BulkImported, 0));
        Step = BulkStep.Report;
    }

    // Stops between two files; everything copied so far stays, each file complete.
    [RelayCommand]
    private void Cancel() => _run?.Cancel();

    // The files that need a person go to the Import page, copied rather than moved like here.
    [RelayCommand(CanExecute = nameof(HasLeftOut))]
    private async Task SendToManualAsync()
    {
        await _navigation.NavigateToAsync<ImportViewModel>();
        _messenger.Send(new FilesDroppedMessage(LeftOut, KeepSource: true));
    }

    private bool HasLeftOut() => LeftOut.Count > 0;

    [RelayCommand]
    private Task GoToSearchAsync() => _navigation.NavigateToAsync<SearchViewModel>();

    // "Importar otra carpeta"
    [RelayCommand]
    private void StartOver()
    {
        _options = null;
        Rows = [];
        FilteredRows = [];
        Patterns = [];
        Groups = [];
        Partners = [];
        LeftOut = [];
        Folder = "";
        Step = BulkStep.Folder;
    }

    // The partner names as the import will use them: an accepted group's names all become its target;
    // any other name its rename, when changed.
    internal IReadOnlyDictionary<string, string> PartnerMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var partner in Partners.Where(p => !string.IsNullOrWhiteSpace(p.Rename) && p.Rename.Trim() != p.Name))
            map[partner.Name] = partner.Rename.Trim();
        foreach (var group in Groups.Where(g => g.Accept && !string.IsNullOrWhiteSpace(g.Target)))
        {
            foreach (var name in group.Names)
                map[name] = group.Target.Trim();
        }
        return map;
    }

    private void Show(BulkImportPreview preview)
    {
        foreach (var row in Rows)
        {
            row.StatusChanged -= OnRowStatusChanged;
            row.IncludeChanged -= OnRowIncludeChanged;
        }
        var type = SelectedType;
        Rows = preview.Rows.Select(r => new BulkRowViewModel(r, type)).ToList();
        foreach (var row in Rows)
        {
            row.StatusChanged += OnRowStatusChanged;
            row.IncludeChanged += OnRowIncludeChanged;
        }

        MatchText = preview.Pattern is { } used
            ? _localizer.Format("Bulk_Matches", used.Matches, used.Total)
            : _localizer.Get("Bulk_NoPattern");
        Examples = preview.Rows.Where(r => r.Status == BulkRowStatus.Ok).Take(3)
            .Select(r => _localizer.Format("Bulk_Example", System.IO.Path.GetFileName(r.Path),
                r.IssueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), r.PartnerName, r.DocNumber))
            .ToList();

        Groups = preview.Groups
            .Select(g => new PartnerGroupViewModel(g.Names, NamesText(g.Names, preview.Partners), g.SuggestedName))
            .ToList();
        Partners = preview.Partners.Select(p => new PartnerNameViewModel(p.Name, p.Count, p.ExistingName)).ToList();
        Recount();
    }

    private void OnRowStatusChanged(object? sender, EventArgs e) => Recount();
    private void OnRowIncludeChanged(object? sender, EventArgs e) => CountImports();

    // The filter's counts and what will be imported, after any change to the rows.
    private void Recount()
    {
        var current = SelectedStatusFilter?.Status;
        StatusFilters =
        [
            new StatusFilterOption(null, _localizer.Format("Bulk_FilterAll", Rows.Count)),
            .. Enum.GetValues<BulkRowStatus>()
                .Select(s => (Status: s, Count: Rows.Count(r => r.Status == s)))
                .Where(s => s.Count > 0)
                .Select(s => new StatusFilterOption(s.Status, _localizer.Format($"Bulk_Filter_{s.Status}", s.Count))),
        ];
        SelectedStatusFilter = StatusFilters.FirstOrDefault(f => f.Status == current) ?? StatusFilters[0];
        Filter();
        CountImports();
    }

    private void CountImports() =>
        ImportCountText = _localizer.Format("Bulk_WillImport", Rows.Count(r => r.WillImport), Rows.Count);

    private void Filter() =>
        FilteredRows = SelectedStatusFilter?.Status is { } status ? Rows.Where(r => r.Status == status).ToList() : Rows;

    private void ShowReport(BulkImportReport report, IReadOnlyList<BulkImportItem> items)
    {
        // Rows the user left out, and what the run couldn't import. Files a cancelled run didn't reach aren't
        // here: running the wizard again imports them (the ones already copied are skipped as duplicates).
        var sent = items.Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notSent = Rows.Where(r => !sent.Contains(r.Path)).Select(r => r.Path);
        LeftOut = notSent
            .Concat(report.NumberExists)
            .Concat(report.Failed.Select(f => f.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReportText = _localizer.Format(report.Cancelled ? "Bulk_ReportCancelled" : "Bulk_Report",
            report.Imported, report.Duplicates.Count, report.NumberExists.Count, report.Failed.Count,
            report.Elapsed.TotalSeconds < 60 ? $"{report.Elapsed.TotalSeconds:0} s" : $"{report.Elapsed:m\\:ss} min");
        LeftOutText = _localizer.Format("Bulk_LeftOut", LeftOut.Count);
    }

    private string FolderError(ErrorCode error) => error switch
    {
        ErrorCode.FileMissing => _localizer.Get("Bulk_FolderMissing"),
        ErrorCode.InvalidArchiveFolder => _localizer.Get("Bulk_FolderIsArchive"),
        _ => _localizer.Error(error),
    };

    private PatternOption ToOption(NamePattern pattern) => new(
        pattern.Pattern,
        pattern.DateFormat,
        pattern.DateFormat is null ? pattern.Pattern : $"{pattern.Pattern} · {pattern.DateFormat}",
        _localizer.Format("Bulk_Matches", pattern.Matches, pattern.Total));

    // "Telefonica ×120 · Telefonica Espana ×3"
    private static string NamesText(IReadOnlyList<string> names, IReadOnlyList<BulkPartner> partners) =>
        string.Join(" · ", names.Select(n => $"{n} ×{partners.FirstOrDefault(p => p.Name == n)?.Count ?? 0}"));
}
