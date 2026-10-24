using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

[Collection(CultureCollection.Name)]
public sealed class BulkImportViewModelTests : IDisposable
{
    private readonly CultureScope _spanish = new("es-ES");
    private readonly FakeBulkImportService _bulk = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeNavigation _navigation = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly FakeArchiveState _archive = new();
    private readonly BulkImportViewModel _vm;
    private readonly List<DocumentsChangedMessage> _docChanges = [];
    private readonly List<FilesDroppedMessage> _droppedMessages = [];

    public BulkImportViewModelTests()
    {
        var localizer = new Localizer();
        _vm = new BulkImportViewModel(_bulk, _picker, _dialogs, localizer, _messenger, _navigation, _archive)
        {
            PatternDelay = TimeSpan.Zero,
        };
        _messenger.Register<DocumentsChangedMessage>(this, (_, m) => _docChanges.Add(m));
        _messenger.Register<FilesDroppedMessage>(this, (_, m) => _droppedMessages.Add(m));
    }

    public void Dispose() => _spanish.Dispose();

    // Progress<T> reports on the thread pool when a test has no UI thread: wait for it, briefly.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var waited = 0; !condition(); waited += 10)
        {
            Assert.True(waited < 5000, "timed out");
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Initial_state_is_step_Folder_with_Factura_default()
    {
        Assert.Equal(BulkStep.Folder, _vm.Step);
        Assert.Equal(1, _vm.StepNumber);
        Assert.Equal(DocType.Factura, _vm.SelectedType.Type);
        Assert.True(_vm.IsReceived);
        Assert.False(_vm.IsIssued);
    }

    [Fact]
    public async Task Analyze_validates_path_and_shows_preview_and_patterns()
    {
        _vm.Folder = "not-a-full-path";
        await _vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(BulkStep.Folder, _vm.Step);
        Assert.Single(_dialogs.Errors);

        _dialogs.Errors.Clear();
        _vm.Folder = @"C:\OldArchive";

        _bulk.AnalyzeAnswer = options => Result.Ok(new BulkImportPreview(
            options.Folder,
            new NamePattern("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd", 2, 2),
            [
                new NamePattern("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd", 2, 2),
                new NamePattern("{proveedor}_{numero}", null, 1, 2),
            ],
            [
                new BulkImportRow(@"C:\OldArchive\2026-03-14_Telefonica_A-1.pdf", "2026-03-14_Telefonica_A-1.pdf",
                    BulkRowStatus.Ok, new DateOnly(2026, 3, 14), "Telefonica", "A-1"),
                new BulkImportRow(@"C:\OldArchive\2026-03-15_Repsol_B-2.pdf", "2026-03-15_Repsol_B-2.pdf",
                    BulkRowStatus.CheckDate, new DateOnly(2026, 3, 15), "Repsol", "B-2"),
            ],
            [
                new BulkPartner("Telefonica", 1, "Telefónica España, S.A.U."),
                new BulkPartner("Repsol", 1, null),
            ],
            [
                new BulkPartnerGroup(["Telefonica", "Telefonica Moviles"], "Telefónica España, S.A.U."),
            ]));

        await _vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(BulkStep.Pattern, _vm.Step);
        Assert.Equal(2, _vm.StepNumber);
        Assert.Equal(2, _vm.Patterns.Count);
        Assert.Equal(_vm.Patterns[0], _vm.SelectedPattern);
        Assert.Equal(2, _vm.Rows.Count);
        Assert.Single(_vm.Groups);
        Assert.Equal(2, _vm.Partners.Count);
    }

    [Fact]
    public async Task Switching_pattern_card_or_custom_pattern_reanalyzes()
    {
        _vm.Folder = @"C:\OldArchive";
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(
            @"C:\OldArchive",
            new NamePattern("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd", 1, 1),
            [
                new NamePattern("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd", 1, 1),
                new NamePattern("{proveedor}_{numero}", null, 1, 1),
            ],
            [], [], []));

        await _vm.AnalyzeCommand.ExecuteAsync(null);

        // Selecting second card
        _vm.SelectedPattern = _vm.Patterns[1];
        await _vm.PendingAnalysis;

        // Custom pattern
        _vm.UseCustomPattern = true;
        _vm.CustomPattern = "{numero}_{proveedor}";
        await _vm.PendingAnalysis;

        Assert.Null(_vm.PatternError);

        // Invalid custom pattern
        _bulk.AnalyzeAnswer = _ => Result.Fail<BulkImportPreview>(ErrorCode.InvalidNamePattern, "MissingNumber");
        _vm.CustomPattern = "{fecha}_{proveedor}";
        await _vm.PendingAnalysis;

        Assert.NotNull(_vm.PatternError);
    }

    [Fact]
    public async Task A_re_analysis_reads_the_folder_with_the_chosen_card_or_the_typed_pattern()
    {
        _vm.Folder = @"C:\OldArchive";
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(@"C:\OldArchive", null,
            [new NamePattern("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd", 1, 1), new NamePattern("{proveedor} - {numero}", null, 1, 1)],
            [], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);

        _vm.SelectedPattern = _vm.Patterns[1];
        await _vm.PendingAnalysis;
        _vm.UseCustomPattern = true;
        await _vm.PendingAnalysis;
        _vm.CustomDateFormat = "ddMMyyyy";
        await _vm.PendingAnalysis;

        Assert.Equal(
            [(null, null), ("{proveedor} - {numero}", null), ("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd"), ("{fecha}_{proveedor}_{numero}", "ddMMyyyy")],
            _bulk.Analyzed.Select(o => (o.Pattern, o.DateFormat)));
    }

    // Back on step 1 with a typed pattern on: analyzing another folder reads it once, with the detected
    // pattern, and nothing of the previous folder's pattern lands on it.
    [Fact]
    public async Task Analyzing_another_folder_reads_it_once_with_the_detected_pattern()
    {
        _vm.Folder = @"C:\OldArchive";
        _bulk.AnalyzeAnswer = o => Result.Ok(new BulkImportPreview(o.Folder, null,
            [new NamePattern("{fecha}_{proveedor}_{numero}", "yyyy-MM-dd", 1, 1)], [], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);
        _vm.UseCustomPattern = true;
        await _vm.PendingAnalysis;
        _vm.BackCommand.Execute(null);
        _bulk.Analyzed.Clear();

        _vm.Folder = @"D:\Otra";
        await _vm.AnalyzeCommand.ExecuteAsync(null);

        var analysis = Assert.Single(_bulk.Analyzed);
        Assert.Equal((@"D:\Otra", null), (analysis.Folder, analysis.Pattern));
        Assert.False(_vm.UseCustomPattern);
        Assert.Equal(BulkStep.Pattern, _vm.Step);
    }

    [Fact]
    public async Task Using_the_files_dates_is_offered_only_when_some_names_had_no_date()
    {
        _vm.Folder = @"C:\OldArchive";
        var dated = new BulkImportRow(@"C:\OldArchive\a.pdf", "a.pdf", BulkRowStatus.Ok, new DateOnly(2026, 3, 14), "Repsol", "1");
        var undated = new BulkImportRow(@"C:\OldArchive\b.pdf", "b.pdf", BulkRowStatus.CheckDate, new DateOnly(2025, 11, 20), "Repsol", "2");

        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(@"C:\OldArchive", null, [], [dated], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.False(_vm.HasCheckDateRows);

        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(@"C:\OldArchive", null, [], [dated, undated], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.True(_vm.HasCheckDateRows);
        Assert.Equal("Se importarán 1 de 2", _vm.ImportCountText);

        _vm.IncludeFileDatesCommand.Execute(null);
        Assert.Equal("Se importarán 2 de 2", _vm.ImportCountText);
    }

    // The run reports each file as it goes; Cancel stops it between two files.
    [Fact]
    public async Task The_run_shows_its_progress_and_can_be_cancelled()
    {
        _vm.Folder = @"C:\OldArchive";
        static BulkImportRow Row(int n) =>
            new($@"C:\OldArchive\{n}.pdf", $"{n}.pdf", BulkRowStatus.Ok, new DateOnly(2026, 3, n), "Repsol", $"{n}");
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(@"C:\OldArchive", null, [], [Row(1), Row(2), Row(3)], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);
        var cancelled = new TaskCompletionSource();
        _bulk.RunAsyncAnswer = async (run, progress, ct) =>
        {
            progress?.Report(new BulkImportProgress(1, run.Items.Count, "1.pdf"));
            using (ct.Register(() => cancelled.TrySetResult()))
                await cancelled.Task;
            return new BulkImportReport(1, [], [], [], Cancelled: true, TimeSpan.FromSeconds(2));
        };

        var running = _vm.ImportCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _vm.Done == 1);

        Assert.Equal(BulkStep.Running, _vm.Step);
        Assert.True(_archive.IsImporting); // Ajustes won't move or check the archive meanwhile
        Assert.Equal(3, _vm.Total);
        Assert.Equal("1 de 3 · 1.pdf", _vm.ProgressText);

        _vm.CancelCommand.Execute(null);
        await running;
        await _vm.PendingRun;

        Assert.Equal(BulkStep.Report, _vm.Step);
        Assert.False(_archive.IsImporting);
        Assert.Equal("Importación cancelada", _vm.ReportTitle);
        Assert.StartsWith("Cancelada. Importados 1", _vm.ReportText);
    }

    [Fact]
    public async Task Step_navigation_works_forward_and_backward()
    {
        _vm.Folder = @"C:\OldArchive";
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(@"C:\OldArchive", null, [], [], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);

        Assert.Equal(BulkStep.Pattern, _vm.Step);

        _vm.NextCommand.Execute(null);
        Assert.Equal(BulkStep.Preview, _vm.Step);

        _vm.NextCommand.Execute(null);
        Assert.Equal(BulkStep.Partners, _vm.Step);

        _vm.BackCommand.Execute(null);
        Assert.Equal(BulkStep.Preview, _vm.Step);

        _vm.BackCommand.Execute(null);
        Assert.Equal(BulkStep.Pattern, _vm.Step);

        _vm.BackCommand.Execute(null);
        Assert.Equal(BulkStep.Folder, _vm.Step);
    }

    [Fact]
    public async Task Editing_a_row_updates_status_and_import_count()
    {
        _vm.Folder = @"C:\OldArchive";
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(
            @"C:\OldArchive", null, [],
            [
                new BulkImportRow(@"C:\OldArchive\bad.pdf", "bad.pdf", BulkRowStatus.NotRecognized, null, null, null),
                new BulkImportRow(@"C:\OldArchive\check.pdf", "check.pdf", BulkRowStatus.CheckDate, new DateOnly(2026, 1, 1), "Partner", "N-1"),
            ],
            [], []));

        await _vm.AnalyzeCommand.ExecuteAsync(null);
        _vm.NextCommand.Execute(null); // Go to Preview

        Assert.Equal(BulkStep.Preview, _vm.Step);
        Assert.False(_vm.Rows[0].WillImport);

        // Include file dates command
        _vm.IncludeFileDatesCommand.Execute(null);
        Assert.True(_vm.Rows[1].Include);
        Assert.True(_vm.Rows[1].WillImport);

        // Edit first row to make it valid
        _vm.Rows[0].DateText = "14/03/2026";
        _vm.Rows[0].Partner = "Endesa";
        _vm.Rows[0].Number = "E-99";

        Assert.Equal(BulkRowStatus.Ok, _vm.Rows[0].Status);
        Assert.True(_vm.Rows[0].Include);
        Assert.True(_vm.Rows[0].WillImport);
    }

    [Fact]
    public async Task PartnerMap_respects_accepted_groups_and_renames()
    {
        _vm.Folder = @"C:\OldArchive";
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(
            @"C:\OldArchive", null, [],
            [],
            [
                new BulkPartner("Telefonica", 10, null),
                new BulkPartner("Iberdrola", 5, null),
            ],
            [
                new BulkPartnerGroup(["Telefonica", "Telefonica SA"], "Telefónica España"),
            ]));

        await _vm.AnalyzeCommand.ExecuteAsync(null);
        _vm.NextCommand.Execute(null);
        _vm.NextCommand.Execute(null); // Go to Partners

        Assert.Equal(BulkStep.Partners, _vm.Step);

        // Rename Iberdrola
        _vm.Partners[1].Rename = "Iberdrola Clientes";

        var map = _vm.PartnerMap();
        Assert.Equal("Telefónica España", map["Telefonica"]);
        Assert.Equal("Telefónica España", map["Telefonica SA"]);
        Assert.Equal("Iberdrola Clientes", map["Iberdrola"]);

        // If group is not accepted
        _vm.Groups[0].Accept = false;
        map = _vm.PartnerMap();
        Assert.False(map.ContainsKey("Telefonica"));
    }

    [Fact]
    public async Task Run_executes_bulk_import_and_reports_results()
    {
        _vm.Folder = @"C:\OldArchive";
        var row = new BulkImportRow(@"C:\OldArchive\doc.pdf", "doc.pdf", BulkRowStatus.Ok,
            new DateOnly(2026, 3, 14), "Telefonica", "123");

        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(
            @"C:\OldArchive", null, [], [row], [new BulkPartner("Telefonica", 1, null)], []));

        await _vm.AnalyzeCommand.ExecuteAsync(null);

        _bulk.RunAnswer = run =>
        {
            Assert.Single(run.Items);
            Assert.True(run.MarkPaid); // an old archive is normally settled: on unless unticked
            return new BulkImportReport(1, [], [], [], false, TimeSpan.FromSeconds(5));
        };

        await _vm.ImportCommand.ExecuteAsync(null);
        await _vm.PendingRun;

        Assert.Equal(BulkStep.Report, _vm.Step);
        Assert.Equal("Importación terminada", _vm.ReportTitle);
        Assert.Contains("Importados 1", _vm.ReportText);
        Assert.Single(_docChanges);
        Assert.Equal(DocumentsChange.BulkImported, _docChanges[0].Kind);
    }

    [Fact]
    public async Task SendToManual_navigates_and_sends_left_out_in_copy_mode()
    {
        _vm.Folder = @"C:\OldArchive";
        var unrec = new BulkImportRow(@"C:\OldArchive\unrec.pdf", "unrec.pdf", BulkRowStatus.NotRecognized, null, null, null);

        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(
            @"C:\OldArchive", null, [], [unrec], [], []));

        await _vm.AnalyzeCommand.ExecuteAsync(null);

        _bulk.RunAnswer = _ => new BulkImportReport(0, [], [], [], false, TimeSpan.FromSeconds(1));

        await _vm.ImportCommand.ExecuteAsync(null);
        await _vm.PendingRun;

        Assert.Equal(BulkStep.Report, _vm.Step);
        Assert.Equal([@"C:\OldArchive\unrec.pdf"], _vm.LeftOut);

        await _vm.SendToManualCommand.ExecuteAsync(null);

        Assert.Equal(typeof(ImportViewModel), _navigation.Visited.Last());
        var dropped = Assert.Single(_droppedMessages);
        Assert.True(dropped.KeepSource);
        Assert.True(dropped.StartsPaid);
        Assert.Equal([@"C:\OldArchive\unrec.pdf"], dropped.Paths);
    }

    [Fact]
    public async Task Unticking_mark_paid_imports_everything_as_pending()
    {
        _vm.Folder = @"C:\OldArchive";
        var row = new BulkImportRow(@"C:\OldArchive\doc.pdf", "doc.pdf", BulkRowStatus.Ok, new DateOnly(2026, 3, 14), "Repsol", "1");
        _bulk.AnalyzeAnswer = _ => Result.Ok(new BulkImportPreview(@"C:\OldArchive", null, [], [row], [], []));
        await _vm.AnalyzeCommand.ExecuteAsync(null);
        BulkImportRun? run = null;
        _bulk.RunAnswer = r =>
        {
            run = r;
            return new BulkImportReport(1, [], [], [], false, TimeSpan.Zero);
        };

        _vm.MarkPaid = false;
        await _vm.ImportCommand.ExecuteAsync(null);
        await _vm.PendingRun;

        Assert.False(run!.MarkPaid);
    }
}
