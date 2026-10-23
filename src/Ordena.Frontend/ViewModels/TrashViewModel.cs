using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Dtos;
using Ordena.Backend.Services;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// The deleted documents, the most recently deleted first, each one restorable to its folder.
// Loaded on every visit (a new instance each time).
public sealed partial class TrashViewModel(
    IDocumentService documents, IDialogService dialogs, ILocalizer localizer, IMessenger messenger) : PageViewModel
{
    [ObservableProperty]
    private IReadOnlyList<DocumentDetails> _items = [];

    [ObservableProperty]
    private DocumentDetails? _selectedItem;

    // "3 documentos en la papelera"
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _isEmpty;

    public override string Title => localizer.Get("Page_Trash");

    public override Task OnNavigatedToAsync() => LoadAsync();

    // Back to its standard name and folder (with _2 if that name was taken meanwhile).
    [RelayCommand]
    private async Task RestoreAsync(DocumentDetails? item)
    {
        if ((item ?? SelectedItem) is not { } document)
            return;

        var restored = await documents.RestoreAsync(document.Id, CancellationToken.None);
        if (!restored.IsSuccess)
        {
            await dialogs.ShowErrorAsync(localizer.Error(restored.Error.Value));
            return;
        }

        messenger.Send(new DocumentsChangedMessage(DocumentsChange.Restored, document.Id));
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Items = await documents.ListTrashAsync(CancellationToken.None);
        IsEmpty = Items.Count == 0;
        StatusText = Items.Count == 1 ? localizer.Get("Trash_CountOne") : localizer.Format("Trash_CountMany", Items.Count);
    }
}
