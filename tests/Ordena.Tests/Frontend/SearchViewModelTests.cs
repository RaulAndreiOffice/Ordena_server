using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// The search page with every service faked: what goes to the database, what comes back on screen,
// and what each row action does.
[Collection(CultureCollection.Name)]
public sealed class SearchViewModelTests : IDisposable
{
    private static readonly DocumentListItem Telefonica = new(1, new DateOnly(2026, 3, 14), "Telefónica España, S.A.U.",
        "A-2026/0123", DocType.Factura, Direction.Recibida, "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf");
    private static readonly DocumentListItem Repsol = new(2, new DateOnly(2026, 4, 2), "Repsol S.A.",
        "T-88812", DocType.FacturaSimplificada, Direction.Recibida, "2026/2T/Recibidas/Repsol/2026-04-02_Repsol_T-88812_Ticket.pdf");

    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeSearchService _search = new() { Items = [Telefonica, Repsol], Years = [2026, 2025] };
    private readonly FakeDocumentService _documents = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeShell _shell = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeNavigation _navigation = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<DocumentsChangedMessage> _changes = [];
    private readonly SearchViewModel _vm;

    public SearchViewModelTests()
    {
        _vm = new SearchViewModel(_search, _documents, _dialogs, _shell, _picker, new Localizer(), _messenger, _navigation)
        {
            SearchDelay = TimeSpan.Zero,
        };
        _messenger.Register<DocumentsChangedMessage>(this, (_, message) => _changes.Add(message));
    }

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public async Task The_first_visit_lists_the_newest_documents_and_the_years()
    {
        await _vm.OnNavigatedToAsync();

        var query = Assert.Single(_search.Queries);
        Assert.Equal(new SearchQuery { Text = "", SortBy = SearchSort.IssueDate, Descending = true }, query);
        Assert.Equal([Telefonica, Repsol], _vm.Results);
        Assert.Equal("2 documentos", _vm.StatusText);
        Assert.Equal(["Todos los años", "2026", "2025"], _vm.Years.Select(y => y.Label));
        Assert.Equal(["Todo el año", "1T · ene–mar", "2T · abr–jun", "3T · jul–sep", "4T · oct–dic"],
            _vm.Quarters.Select(q => q.Label));
        Assert.Equal(["Recibidas y emitidas", "Recibidas", "Emitidas"], _vm.Directions.Select(d => d.Label));
        Assert.Equal("Todos los tipos", _vm.Types[0].Label);
        Assert.False(_vm.IsArchiveEmpty);
        Assert.False(_vm.HasNoMatches);
        Assert.False(_vm.HasCriteria);
        Assert.Equal("Buscar", _vm.Title);
    }

    // The page is a singleton: coming back keeps the query and results, and only puts the cursor back.
    [Fact]
    public async Task Later_visits_keep_the_results_and_focus_the_box()
    {
        await _vm.OnNavigatedToAsync();
        _vm.Text = "telef";
        await _vm.PendingSearch;

        await _vm.OnNavigatedToAsync();

        Assert.Equal(2, _search.Queries.Count);
        Assert.Equal("telef", _vm.Text);
        Assert.Equal(2, _vm.FocusRequest);
    }

    [Fact]
    public async Task Three_fast_keystrokes_give_one_search()
    {
        await _vm.OnNavigatedToAsync();
        _vm.SearchDelay = TimeSpan.FromMilliseconds(50);

        _vm.Text = "t";
        _vm.Text = "te";
        _vm.Text = "tel";
        await _vm.PendingSearch;

        Assert.Equal(["", "tel"], _search.Queries.Select(q => q.Text));
    }

    // "tel" answers after "rep": the grid must keep the newer answer.
    [Fact]
    public async Task A_stale_answer_is_ignored()
    {
        await _vm.OnNavigatedToAsync();
        _search.Hold = true;

        _vm.Text = "tel";
        var older = _vm.PendingSearch;
        _vm.Text = "rep";
        var newer = _vm.PendingSearch;

        _search.Held[1].SetResult(new SearchResult([Repsol], 1));
        await newer;
        _search.Held[0].SetResult(new SearchResult([Telefonica], 1));
        await older;

        Assert.Equal([Repsol], _vm.Results);
    }

    [Fact]
    public async Task The_filters_go_into_the_query_and_clearing_them_searches_once()
    {
        await _vm.OnNavigatedToAsync();

        _vm.SelectedYear = _vm.Years[1];
        _vm.SelectedQuarter = _vm.Quarters[1];
        _vm.SelectedType = _vm.Types.Single(t => t.Value == DocType.Recibo);
        _vm.SelectedDirection = _vm.Directions[2];
        await _vm.PendingSearch;

        var query = _search.Queries[^1];
        Assert.Equal((2026, 1, DocType.Recibo, Direction.Emitida), (query.Year, query.Quarter, query.Type, query.Direction));
        Assert.True(_vm.HasCriteria);

        var before = _search.Queries.Count;
        await _vm.ClearFiltersCommand.ExecuteAsync(null);

        Assert.Equal(before + 1, _search.Queries.Count);
        Assert.Equal(new SearchQuery { Text = "", SortBy = SearchSort.IssueDate, Descending = true }, _search.Queries[^1]);
        Assert.False(_vm.HasCriteria);
    }

    [Fact]
    public async Task The_date_column_flips_its_order_and_another_column_starts_A_to_Z()
    {
        await _vm.OnNavigatedToAsync();

        await _vm.SortCommand.ExecuteAsync("IssueDate");
        Assert.Equal((SearchSort.IssueDate, false), (_search.Queries[^1].SortBy, _search.Queries[^1].Descending));

        await _vm.SortCommand.ExecuteAsync("Partner");
        Assert.Equal((SearchSort.Partner, false), (_search.Queries[^1].SortBy, _search.Queries[^1].Descending));

        await _vm.SortCommand.ExecuteAsync("Partner");
        Assert.Equal((SearchSort.Partner, true), (_search.Queries[^1].SortBy, _search.Queries[^1].Descending));
        Assert.Equal((SearchSort.Partner, true), (_vm.SortBy, _vm.SortDescending));
    }

    [Fact]
    public async Task The_status_line_counts_and_says_when_only_the_first_rows_are_shown()
    {
        _search.Items = [Telefonica];
        await _vm.OnNavigatedToAsync();
        Assert.Equal("1 documento", _vm.StatusText);

        _search.Items = [Telefonica, Repsol];
        _search.TotalCount = 2950;
        _vm.Text = "a";
        await _vm.PendingSearch;
        Assert.Equal("Mostrando 2 de 2.950: afina la búsqueda", _vm.StatusText);
    }

    [Fact]
    public async Task Without_documents_the_page_offers_the_import_and_without_matches_clearing()
    {
        _search.Items = [];
        await _vm.OnNavigatedToAsync();
        Assert.True(_vm.IsArchiveEmpty);
        Assert.False(_vm.HasNoMatches);

        _vm.Text = "zzz";
        await _vm.PendingSearch;
        Assert.False(_vm.IsArchiveEmpty);
        Assert.True(_vm.HasNoMatches);

        await _vm.GoToImportCommand.ExecuteAsync(null);
        Assert.Equal([typeof(ImportViewModel)], _navigation.Visited);
    }

    [Fact]
    public async Task Open_and_show_in_folder_use_the_files_full_path()
    {
        _documents.FullPaths[1] = @"D:\Facturas\2026\1T\a.pdf";
        _documents.FullPaths[2] = @"D:\Facturas\2026\2T\b.pdf";

        await _vm.OpenCommand.ExecuteAsync(Telefonica);
        await _vm.ShowInFolderCommand.ExecuteAsync(Repsol);

        Assert.Equal([@"D:\Facturas\2026\1T\a.pdf"], _shell.Opened);
        Assert.Equal([@"D:\Facturas\2026\2T\b.pdf"], _shell.ShownInFolder);
    }

    // Moved by hand in Explorer: the user finds it, it is linked to the document again, and it opens.
    [Fact]
    public async Task A_missing_file_can_be_found_and_is_then_opened()
    {
        const string Expected = @"D:\Facturas\2026\1T\Recibidas\Telefonica-Espana\2026-03-14_Telefonica-Espana_A-2026-0123.pdf";
        _documents.ExpectedPaths[1] = Expected;
        _picker.PdfChoice = @"C:\Users\Ana\Desktop\factura.pdf";

        await _vm.OpenCommand.ExecuteAsync(Telefonica);

        Assert.Contains(Expected, Assert.Single(_dialogs.Questions));
        Assert.Equal(@"D:\Facturas\2026\1T\Recibidas\Telefonica-Espana", _picker.OpenedAt);
        Assert.Equal([(1, @"C:\Users\Ana\Desktop\factura.pdf")], _documents.Relinked);
        Assert.Equal([@"C:\Users\Ana\Desktop\factura.pdf"], _shell.Opened);
    }

    [Fact]
    public async Task A_missing_file_the_user_does_not_look_for_opens_nothing()
    {
        _dialogs.Confirm = false;

        await _vm.OpenCommand.ExecuteAsync(Telefonica);

        Assert.Empty(_documents.Relinked);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task Another_file_picked_for_a_missing_one_is_refused()
    {
        _picker.PdfChoice = @"C:\Users\Ana\Desktop\otra.pdf";
        _documents.RelinkResult = Result.Fail(ErrorCode.NotSameFile);

        await _vm.OpenCommand.ExecuteAsync(Telefonica);

        Assert.Equal([new Localizer().Error(ErrorCode.NotSameFile)], _dialogs.Errors);
        Assert.Empty(_shell.Opened);
    }

    [Fact]
    public async Task Delete_asks_first_then_moves_it_to_the_trash_and_the_list_refreshes()
    {
        await _vm.OnNavigatedToAsync();

        await _vm.DeleteCommand.ExecuteAsync(Telefonica);
        await _vm.PendingSearch;

        Assert.Contains("«Telefónica España, S.A.U.» nº A-2026/0123", Assert.Single(_dialogs.Questions));
        Assert.Equal([1], _documents.Deleted);
        Assert.Equal([new DocumentsChangedMessage(DocumentsChange.Deleted, 1)], _changes);
        Assert.Equal(2, _search.Queries.Count);
    }

    [Fact]
    public async Task Delete_answered_no_does_nothing()
    {
        _dialogs.Confirm = false;

        await _vm.DeleteCommand.ExecuteAsync(Telefonica);

        Assert.Empty(_documents.Deleted);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task A_delete_that_fails_says_why()
    {
        _documents.DeleteResult = Result.Fail(ErrorCode.FileLocked);

        await _vm.DeleteCommand.ExecuteAsync(Telefonica);

        Assert.Equal([new Localizer().Error(ErrorCode.FileLocked)], _dialogs.Errors);
        Assert.Empty(_changes);
    }

    [Fact]
    public async Task Edit_opens_the_dialog_for_that_document()
    {
        await _vm.EditCommand.ExecuteAsync(Repsol);

        Assert.Equal([2], _dialogs.EditedIds);
    }

    // An import, edit, delete or restore anywhere: the same query again, the selection kept.
    [Fact]
    public async Task A_change_elsewhere_runs_the_query_again_and_keeps_the_selection()
    {
        await _vm.OnNavigatedToAsync();
        _vm.Text = "re";
        await _vm.PendingSearch;
        _vm.SelectedItem = Repsol;

        _messenger.Send(new DocumentsChangedMessage(DocumentsChange.Imported, 9));
        await _vm.PendingSearch;

        Assert.Equal(["", "re", "re"], _search.Queries.Select(q => q.Text));
        Assert.Equal(2, _search.YearLoads);
        Assert.Equal(2, _vm.SelectedItem?.Id);
    }

    [Fact]
    public async Task Nothing_is_searched_before_the_page_is_first_shown()
    {
        _vm.Text = "tel";
        _messenger.Send(new DocumentsChangedMessage(DocumentsChange.Imported, 9));
        await _vm.PendingSearch;

        Assert.Empty(_search.Queries);
    }
}
