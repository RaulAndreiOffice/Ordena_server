using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Results;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// Ajustes' archive cards over fake services: moving the folder, finding it, the backups, and what
// "Comprobar carpeta" found. The folders are real, because where a move lands depends on what they hold.
[Collection(CultureCollection.Name)]
public sealed class ArchiveSettingsViewModelTests : IDisposable
{
    private static readonly DocumentDetails Repsol = new(7, "Repsol S.A.", "T-88812", new DateOnly(2026, 3, 20),
        DocType.FacturaSimplificada, Direction.Recibida, "2026/1T/Recibidas/Repsol/2026-03-20_Repsol_T-88812_Ticket.pdf",
        "ticket.pdf", TestData.Now, TestData.Now, null);

    private readonly CultureScope _spanish = new("es-ES");
    private readonly TempFolder _disk = new();
    private readonly string _root;
    private readonly FakeArchiveState _folder;
    private readonly FakeMaintenanceService _maintenance = new();
    private readonly FakeArchiveService _archive = new();
    private readonly FakeSettingsService _settings = new();
    private readonly FakeDocumentService _documents = new();
    private readonly FakeFilePicker _picker = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeNavigation _navigation = new();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly List<object> _messages = [];
    private readonly ArchiveSettingsViewModel _vm;

    public ArchiveSettingsViewModelTests()
    {
        _root = _disk.Path("Facturas");
        Directory.CreateDirectory(_root);
        _folder = new FakeArchiveState { Root = _root };
        _vm = new ArchiveSettingsViewModel(_folder, _maintenance, _archive, _settings, _documents, _picker, _dialogs,
            new Localizer(), _messenger, _navigation);
        _messenger.Register<DocumentsChangedMessage>(this, (_, m) => _messages.Add(m));
        _messenger.Register<FilesDroppedMessage>(this, (_, m) => _messages.Add(m));
    }

    public void Dispose()
    {
        _disk.Dispose();
        _spanish.Dispose();
    }

    // An empty folder made for it is used as it is; the old folder goes once the user agrees.
    [Fact]
    public async Task Moving_into_an_empty_folder_switches_to_it_and_deletes_the_old_copy()
    {
        var usb = _disk.Path("USB");
        Directory.CreateDirectory(usb);
        _picker.Choice = usb;
        _archive.MoveAnswer = (newRoot, progress, _) =>
        {
            progress?.Report(new ArchiveProgress(1, 2, 100, 300));
            progress?.Report(new ArchiveProgress(2, 2, 300, 300));
            return Result.Ok(new ArchiveMoveReport(_root, newRoot, 2, 300));
        };

        await _vm.MoveCommand.ExecuteAsync(null);

        Assert.Equal(_disk.Root, _picker.OpenedAt);
        Assert.Equal([$"move {usb}", $"delete {_root}"], _archive.Calls);
        Assert.Equal(usb, _folder.Root);
        Assert.Equal(["Moviendo la carpeta de facturas…"], _dialogs.ProgressTitles);
        Assert.Equal(2, _dialogs.ProgressReports.Count);
        Assert.StartsWith($"La carpeta de facturas se copiará a:\n{usb}\n", _dialogs.Questions[0]);
        Assert.StartsWith($"La carpeta de facturas está ahora en:\n{usb}\n", _dialogs.Questions[1]);
        Assert.Equal(["Carpeta antigua borrada."], _dialogs.Infos);
    }

    // A folder that already has things in it gets a Facturas folder inside, as Explorer would do.
    [Fact]
    public async Task Moving_into_a_folder_with_files_puts_a_Facturas_folder_in_it_and_can_keep_the_old_one()
    {
        _disk.WriteFile("E/nota.txt", [1]);
        _picker.Choice = _disk.Path("E");
        _dialogs.Confirms.Enqueue(true);
        _dialogs.Confirms.Enqueue(false); // keep the old folder

        await _vm.MoveCommand.ExecuteAsync(null);

        var target = Path.Combine(_disk.Path("E"), "Facturas");
        Assert.Equal([$"move {target}"], _archive.Calls);
        Assert.Equal(target, _folder.Root);
        Assert.Empty(_dialogs.Infos);
    }

    [Fact]
    public async Task Old_files_without_a_copy_are_kept_and_named()
    {
        _picker.Choice = _disk.Path("USB");
        _archive.MoveAnswer = (newRoot, _, _) => Result.Ok(new ArchiveMoveReport(_root, newRoot, 5, 500));
        _archive.DeleteOldCopyResult = Result.Ok(2);

        await _vm.MoveCommand.ExecuteAsync(null);

        Assert.Equal([$"Carpeta antigua borrada, salvo 2 archivos que no tenían copia en la nueva. Siguen en:\n{_root}"], _dialogs.Infos);
    }

    [Fact]
    public async Task A_cancelled_move_changes_nothing_and_says_so()
    {
        _picker.Choice = _disk.Path("USB");
        _archive.MoveAnswer = (_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the move should have been cancelled");
        };
        await _dialogs.CancelProgress.CancelAsync(); // Cancelar, pressed during the copy

        await _vm.MoveCommand.ExecuteAsync(null);

        Assert.Equal(["Traslado cancelado. No ha cambiado nada."], _dialogs.Infos);
        Assert.Equal(_root, _folder.Root);
    }

    [Fact]
    public async Task A_move_that_fails_is_explained_and_changes_nothing()
    {
        var usb = _disk.Path("USB");
        _picker.Choice = usb;
        _archive.MoveAnswer = (_, _, _) => Result.Fail<ArchiveMoveReport>(ErrorCode.TargetExists, usb);

        await _vm.MoveCommand.ExecuteAsync(null);

        Assert.Equal([$"Ya hay archivos en {usb}. Elige otra carpeta."], _dialogs.Errors);
        Assert.Equal(_root, _folder.Root);
        Assert.Single(_archive.Calls);
    }

    // While "Importar carpeta" copies files in, the archive mustn't move or be checked.
    [Fact]
    public async Task Nothing_moves_or_is_checked_while_a_folder_is_being_imported()
    {
        _folder.IsImporting = true;
        _picker.Choice = _disk.Path("USB");

        await _vm.MoveCommand.ExecuteAsync(null);
        await _vm.CheckCommand.ExecuteAsync(null);
        await _vm.ChangeLocationCommand.ExecuteAsync(null);

        Assert.Equal(3, _dialogs.Errors.Count(e => e == "Se está importando una carpeta. Espera a que termine."));
        Assert.Null(_picker.OpenedAt);
        Assert.Empty(_archive.Calls);
        Assert.Empty(_maintenance.Calls);
        Assert.Equal(0, _folder.Locates);
    }

    [Fact]
    public async Task Change_location_uses_the_folder_the_user_moved_it_to()
    {
        _folder.LocateAnswer = @"E:\Facturas";

        await _vm.ChangeLocationCommand.ExecuteAsync(null);

        Assert.Equal(["Ordena usa ahora la carpeta:\nE:\\Facturas"], _dialogs.Infos);
    }

    // Moving, backing up and checking need the folder; finding where it went doesn't.
    [Fact]
    public async Task Without_the_folder_only_finding_it_is_offered()
    {
        await _vm.LoadAsync();
        Assert.Equal("Disponible", _vm.AvailabilityText);
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _folder.IsAvailable = false;

        Assert.Equal("No disponible", _vm.AvailabilityText);
        Assert.Contains(nameof(_vm.AvailabilityText), changed);
        Assert.False(_vm.MoveCommand.CanExecute(null));
        Assert.False(_vm.BackupNowCommand.CanExecute(null));
        Assert.False(_vm.CheckCommand.CanExecute(null));
        Assert.True(_vm.ChangeLocationCommand.CanExecute(null));
    }

    // Leaving Ajustes: the app-wide state no longer reaches the page.
    [Fact]
    public async Task A_page_left_no_longer_follows_the_folder()
    {
        await _vm.LoadAsync();
        _vm.Unload();
        var changed = new List<string?>();
        _vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _folder.IsAvailable = false;

        Assert.Empty(changed);
    }

    [Fact]
    public async Task Back_up_now_shows_the_time_of_the_new_copy()
    {
        await _vm.LoadAsync();
        Assert.Equal("Todavía no hay ninguna copia.", _vm.LastBackupText);
        _maintenance.OnBackup = () => _settings.LastBackupUtc = new DateTime(2026, 10, 24, 12, 21, 0, DateTimeKind.Utc);

        await _vm.BackupNowCommand.ExecuteAsync(null);

        Assert.StartsWith("Última copia: 24/10/2026, ", _vm.LastBackupText);
        Assert.Equal(["backup"], _maintenance.Calls);
    }

    [Fact]
    public async Task The_check_lists_the_documents_without_a_file_and_the_pdfs_Ordena_doesnt_know()
    {
        var scan = Path.Combine(_root, "2026", "escaneo.pdf");
        var copied = Path.Combine(_root, "copiado.pdf");
        _maintenance.CheckAnswer = _ => Result.Ok(new ArchiveCheckReport(3001,
            [new RelinkedFile(1, "2026/a.pdf", "2026/1T/a.pdf"), new RelinkedFile(2, "b.pdf", "2026/1T/b.pdf")],
            [Repsol], [scan, copied]));

        await _vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal(["Comprobando la carpeta de facturas…"], _dialogs.ProgressTitles);
        Assert.True(_vm.HasCheckReport);
        Assert.Equal("Comprobados 3.001 documentos.", _vm.CheckSummary);
        Assert.Equal("Devueltos a su sitio (se habían movido o renombrado a mano): 2", _vm.RelinkedText);
        Assert.Equal("Documentos sin archivo: 1", _vm.MissingTitle);
        Assert.Equal(["Repsol S.A. · T-88812 · 20/3/2026"], _vm.MissingDocuments.Select(d => d.Label)); // as Buscar shows dates
        Assert.Equal("PDF que no están en Ordena: 2", _vm.UnknownTitle);
        Assert.Equal([@"2026\escaneo.pdf", "copiado.pdf"], _vm.UnknownFiles.Select(f => f.Label));
        Assert.True(_vm.RegisterCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_archive_with_nothing_wrong_is_all_in_order()
    {
        _maintenance.CheckAnswer = _ => Result.Ok(new ArchiveCheckReport(12, [], [], []));

        await _vm.CheckCommand.ExecuteAsync(null);

        Assert.Equal("Todo en orden: 12 documentos, cada uno con su archivo.", _vm.CheckSummary);
        Assert.Equal("", _vm.RelinkedText);
        Assert.False(_vm.HasMissingDocuments);
        Assert.False(_vm.HasUnknownFiles);
        Assert.False(_vm.RegisterCommand.CanExecute(null));
    }

    // The file was among the unknown PDFs, renamed beyond recognition: linking it takes it off both lists.
    [Fact]
    public async Task Find_file_links_a_missing_document_again()
    {
        var renamed = Path.Combine(_root, "2026", "factura repsol.pdf");
        _maintenance.CheckAnswer = _ => Result.Ok(new ArchiveCheckReport(1, [], [Repsol], [renamed]));
        await _vm.CheckCommand.ExecuteAsync(null);
        _picker.PdfChoice = renamed;

        await _vm.LocateFileCommand.ExecuteAsync(_vm.MissingDocuments[0]);

        Assert.Equal(Path.Combine(_root, "2026", "1T", "Recibidas", "Repsol"), _picker.OpenedAt);
        Assert.Equal([(7, renamed)], _documents.Relinked);
        Assert.Empty(_vm.MissingDocuments);
        Assert.Empty(_vm.UnknownFiles);
    }

    [Fact]
    public async Task Add_to_Ordena_registers_what_it_can_and_sends_the_rest_to_the_import_queue()
    {
        string[] unknown = [Path.Combine(_root, "a.pdf"), Path.Combine(_root, "b.pdf"), Path.Combine(_root, "escaneo.pdf")];
        _maintenance.CheckAnswer = _ => Result.Ok(new ArchiveCheckReport(5, [], [], unknown));
        _maintenance.RegisterAnswer = _ => Result.Ok(new RegisterReport(2, [unknown[2]], []));
        await _vm.CheckCommand.ExecuteAsync(null);

        await _vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(["check", "register 3"], _maintenance.Calls);
        Assert.Empty(_vm.UnknownFiles);
        Assert.Equal(["Añadidos a Ordena: 2.\nPara la importación manual: 1."], _dialogs.Infos);
        Assert.Equal([typeof(ImportViewModel)], _navigation.Visited);
        Assert.Equal(new DocumentsChangedMessage(DocumentsChange.Registered, 0), _messages[0]);
        var dropped = Assert.IsType<FilesDroppedMessage>(_messages[1]);
        Assert.Equal([unknown[2]], dropped.Paths);
        Assert.False(dropped.KeepSource); // moved to its place once filled in
        Assert.True(dropped.StartsPaid); // already in the archive: settled, like the two registered
    }

    // A file in the trash isn't imported, which would take it out of the trash: the message says it stayed.
    [Fact]
    public async Task What_is_in_the_trash_is_not_sent_to_the_import_queue()
    {
        var trashed = Path.Combine(_root, "_Papelera", "escaneo.pdf");
        _maintenance.CheckAnswer = _ => Result.Ok(new ArchiveCheckReport(5, [], [], [trashed]));
        _maintenance.RegisterAnswer = _ => Result.Ok(new RegisterReport(0, [], [trashed]));
        await _vm.CheckCommand.ExecuteAsync(null);

        await _vm.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(["Añadidos a Ordena: 0.\nPara la importación manual: 0.\nSe quedan en la papelera, sin importar: 1."], _dialogs.Infos);
        Assert.Empty(_navigation.Visited);
        Assert.Empty(_messages);
    }
}
