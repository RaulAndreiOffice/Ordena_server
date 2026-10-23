using System.Globalization;
using System.IO;
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

// The search page: what is typed and the filters go to the database as one query, and its first 500
// rows replace the grid in one assignment. A singleton, so the query and results survive switching pages.
public sealed partial class SearchViewModel : PageViewModel, IRecipient<DocumentsChangedMessage>
{
    private readonly ISearchService _search;
    private readonly IDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly IFilePickerService _picker;
    private readonly ILocalizer _localizer;
    private readonly IMessenger _messenger;
    private readonly INavigationService _navigation;

    // The search waiting or running; a newer one cancels it, so its answer is never shown.
    private CancellationTokenSource? _pending;

    // Set by the first visit, which runs the first search. Clearing the filters sets several at once.
    private bool _loaded;
    private bool _batching;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    private string _text = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    private FilterOption<int>? _selectedYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    private FilterOption<int>? _selectedQuarter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    private FilterOption<DocType>? _selectedType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCriteria))]
    private FilterOption<Direction>? _selectedDirection;

    // Changed only by Sort, which searches itself.
    [ObservableProperty]
    private SearchSort _sortBy = SearchSort.IssueDate;

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private IReadOnlyList<FilterOption<int>> _years;

    [ObservableProperty]
    private IReadOnlyList<DocumentListItem> _results = [];

    [ObservableProperty]
    private DocumentListItem? _selectedItem;

    // "37 documentos", or "Mostrando 500 de 2.950: afina la búsqueda".
    [ObservableProperty]
    private string _statusText = "";

    // Nothing archived yet: the page offers the import instead of an empty grid.
    [ObservableProperty]
    private bool _isArchiveEmpty;

    // Documents exist, but none matches: the page offers to clear the filters.
    [ObservableProperty]
    private bool _hasNoMatches;

    // Bumped to put the cursor in the search box (on arrival, and Ctrl+F).
    [ObservableProperty]
    private int _focusRequest;

    public SearchViewModel(
        ISearchService search,
        IDocumentService documents,
        IDialogService dialogs,
        IShellService shell,
        IFilePickerService picker,
        ILocalizer localizer,
        IMessenger messenger,
        INavigationService navigation)
    {
        _search = search;
        _documents = documents;
        _dialogs = dialogs;
        _shell = shell;
        _picker = picker;
        _localizer = localizer;
        _messenger = messenger;
        _navigation = navigation;

        _years = [new FilterOption<int>(null, localizer.Get("Filter_AllYears"))];
        Quarters =
        [
            new(null, localizer.Get("Filter_AllQuarters")),
            .. Enumerable.Range(1, 4).Select(q => new FilterOption<int>(q, localizer.Get($"Quarter_{q}"))),
        ];
        Types =
        [
            new(null, localizer.Get("Filter_AllTypes")),
            .. Enum.GetValues<DocType>().Select(t => new FilterOption<DocType>(t, localizer.Enum(t))),
        ];
        Directions =
        [
            new(null, localizer.Get("Filter_AllDirections")),
            new(Direction.Recibida, localizer.Get("Filter_Received")),
            new(Direction.Emitida, localizer.Get("Filter_Issued")),
        ];
        _selectedYear = _years[0];
        _selectedQuarter = Quarters[0];
        _selectedType = Types[0];
        _selectedDirection = Directions[0];

        messenger.Register<DocumentsChangedMessage>(this);
    }

    public override string Title => _localizer.Get("Page_Search");

    public IReadOnlyList<FilterOption<int>> Quarters { get; }
    public IReadOnlyList<FilterOption<DocType>> Types { get; }
    public IReadOnlyList<FilterOption<Direction>> Directions { get; }

    // Something is typed or filtered: "Limpiar filtros" is offered.
    public bool HasCriteria =>
        !string.IsNullOrWhiteSpace(Text)
        || SelectedYear?.Value is not null
        || SelectedQuarter?.Value is not null
        || SelectedType?.Value is not null
        || SelectedDirection?.Value is not null;

    // How long typing must pause before the search runs. Zero in tests.
    public TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    // The latest search, for tests to await.
    internal Task PendingSearch { get; private set; } = Task.CompletedTask;

    // The first visit loads the newest documents; later ones keep the query and results as they were.
    public override async Task OnNavigatedToAsync()
    {
        FocusRequest++;
        if (_loaded)
            return;

        _loaded = true;
        await (PendingSearch = RefreshAsync());
    }

    // After an import, edit, delete or restore: the same query again, and the years it may have changed.
    // async void like an event handler, so an unexpected error reaches App's handler.
    public async void Receive(DocumentsChangedMessage message)
    {
        if (_loaded)
            await (PendingSearch = RefreshAsync());
    }

    partial void OnTextChanged(string value) => Schedule(SearchDelay);
    partial void OnSelectedYearChanged(FilterOption<int>? value) => Schedule(TimeSpan.Zero);
    partial void OnSelectedQuarterChanged(FilterOption<int>? value) => Schedule(TimeSpan.Zero);
    partial void OnSelectedTypeChanged(FilterOption<DocType>? value) => Schedule(TimeSpan.Zero);
    partial void OnSelectedDirectionChanged(FilterOption<Direction>? value) => Schedule(TimeSpan.Zero);

    // The same column again flips the order; another column starts A→Z, except the date, newest first.
    [RelayCommand]
    private Task SortAsync(string column)
    {
        var sort = Enum.Parse<SearchSort>(column);
        SortDescending = sort == SortBy ? !SortDescending : sort == SearchSort.IssueDate;
        SortBy = sort;
        return SearchNowAsync();
    }

    [RelayCommand]
    private Task ClearFiltersAsync()
    {
        _batching = true;
        Text = "";
        SelectedYear = Years[0];
        SelectedQuarter = Quarters[0];
        SelectedType = Types[0];
        SelectedDirection = Directions[0];
        _batching = false;
        FocusRequest++;
        return SearchNowAsync();
    }

    [RelayCommand]
    private void FocusSearch() => FocusRequest++;

    [RelayCommand]
    private Task GoToImportAsync() => _navigation.NavigateToAsync<ImportViewModel>();

    // Double-click, Enter, or "Abrir": the PDF in the default viewer.
    [RelayCommand]
    private async Task OpenAsync(DocumentListItem? item)
    {
        if (await ResolvePathAsync(item ?? SelectedItem) is { } path)
            _shell.OpenFile(path);
    }

    [RelayCommand]
    private async Task ShowInFolderAsync(DocumentListItem? item)
    {
        if (await ResolvePathAsync(item ?? SelectedItem) is { } path)
            _shell.ShowInFolder(path);
    }

    // The edit dialog sends DocumentsChangedMessage when it saves, which refreshes the grid.
    [RelayCommand]
    private async Task EditAsync(DocumentListItem? item)
    {
        if ((item ?? SelectedItem) is { } document)
            await _dialogs.ShowEditDocumentAsync(document.Id);
    }

    // To the trash, after asking: the file moves to _Papelera and can be restored from there.
    [RelayCommand]
    private async Task DeleteAsync(DocumentListItem? item)
    {
        if ((item ?? SelectedItem) is not { } document
            || !await _dialogs.ConfirmAsync(_localizer.Format("Search_ConfirmDelete", document.PartnerName, document.DocNumber)))
            return;

        var deleted = await _documents.DeleteAsync(document.Id, CancellationToken.None);
        if (!deleted.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(_localizer.Error(deleted.Error.Value));
            return;
        }

        _messenger.Send(new DocumentsChangedMessage(DocumentsChange.Deleted, document.Id));
    }

    private void Schedule(TimeSpan delay)
    {
        if (_loaded && !_batching)
            PendingSearch = SearchAsync(delay);
    }

    private Task SearchNowAsync() => PendingSearch = SearchAsync(TimeSpan.Zero);

    private async Task RefreshAsync()
    {
        await LoadYearsAsync();
        await SearchAsync(TimeSpan.Zero);
    }

    private async Task SearchAsync(TimeSpan delay)
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, pending.Token);

            var result = await _search.SearchAsync(BuildQuery(), pending.Token);
            if (!pending.IsCancellationRequested)
                Show(result);
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested)
        {
            // A newer search took over.
        }
    }

    private SearchQuery BuildQuery() => new()
    {
        Text = Text,
        Year = SelectedYear?.Value,
        Quarter = SelectedQuarter?.Value,
        Type = SelectedType?.Value,
        Direction = SelectedDirection?.Value,
        SortBy = SortBy,
        Descending = SortDescending,
    };

    private void Show(SearchResult result)
    {
        var selectedId = SelectedItem?.Id;
        Results = result.Items;
        SelectedItem = selectedId is { } id ? result.Items.FirstOrDefault(i => i.Id == id) : null;

        StatusText = result.IsTruncated
            ? _localizer.Format("Search_Truncated", result.Items.Count, result.TotalCount)
            : result.TotalCount == 1
                ? _localizer.Get("Search_ResultOne")
                : _localizer.Format("Search_ResultMany", result.TotalCount);
        IsArchiveEmpty = result.TotalCount == 0 && !HasCriteria;
        HasNoMatches = result.TotalCount == 0 && HasCriteria;
    }

    // The year filter offers the years that have documents. A year that just lost its last document
    // stays chosen until the filters are cleared, so the grid doesn't change under the user.
    private async Task LoadYearsAsync()
    {
        var years = await _search.GetYearsAsync(CancellationToken.None);
        var options = years.Select(y => new FilterOption<int>(y, y.ToString(CultureInfo.InvariantCulture))).ToList();
        if (SelectedYear?.Value is { } chosen && !years.Contains(chosen))
            options.Add(SelectedYear);

        _batching = true;
        Years = [Years[0], .. options];
        _batching = false;
    }

    // The file's full path. A missing file can be found and re-linked first ("Buscar archivo"):
    // moved or renamed by hand in Explorer, it is the same file elsewhere.
    private async Task<string?> ResolvePathAsync(DocumentListItem? item)
    {
        if (item is null)
            return null;

        var path = await _documents.GetFullPathAsync(item.Id, CancellationToken.None);
        if (path.Error == ErrorCode.FileMissing)
        {
            var expected = path.Detail;
            if (!await _dialogs.ConfirmAsync(_localizer.Format("Search_FileMissingAsk", expected)))
                return null;

            var chosen = _picker.PickPdfFile(
                _localizer.Get("Search_PickFileTitle"), _localizer.Get("Import_PdfFilter"), Path.GetDirectoryName(expected));
            if (chosen is null)
                return null;

            var relinked = await _documents.RelinkAsync(item.Id, chosen, CancellationToken.None);
            if (!relinked.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(_localizer.Error(relinked.Error.Value));
                return null;
            }

            path = await _documents.GetFullPathAsync(item.Id, CancellationToken.None);
        }

        if (path.IsSuccess)
            return path.Value;

        await _dialogs.ShowErrorAsync(_localizer.Error(path.Error.Value));
        return null;
    }
}
