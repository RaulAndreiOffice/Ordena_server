using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Ordena.Backend.Results;
using Ordena.Backend.Services;
using Ordena.Frontend.Messages;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

// The edit dialog: one archived document's fields next to its PDF. Saving renames or moves the
// file to match (the backend does that through the journal).
public sealed partial class EditDocumentViewModel(
    IDocumentService documents,
    IPreviewService preview,
    IDialogService dialogs,
    ILocalizer localizer,
    IMessenger messenger,
    DocumentFormViewModel form) : ObservableObject
{
    private int _documentId;

    // The temporary copy shown by the preview.
    [ObservableProperty]
    private string? _previewPath;

    // "2026/1T/Recibidas/Telefonica-Espana/2026-03-14_Telefonica-Espana_A-2026-0123.pdf"
    [ObservableProperty]
    private string _fileText = "";

    // The file isn't where the document says: saving would fail, so the dialog says why up front.
    [ObservableProperty]
    private bool _isFileMissing;

    public DocumentFormViewModel Form { get; } = form;

    // Raised once the changes are saved; the window then closes.
    public event EventHandler? Completed;

    public async Task LoadAsync(int documentId)
    {
        _documentId = documentId;
        var document = await documents.GetAsync(documentId, CancellationToken.None);
        Form.Load(document);
        FileText = document.RelativePath;

        var path = await documents.GetFullPathAsync(documentId, CancellationToken.None);
        IsFileMissing = path.Error == ErrorCode.FileMissing;
        PreviewPath = path.IsSuccess ? await preview.CreateCopyAsync(path.Value) : null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Form.Validate())
            return;

        var result = await documents.EditAsync(Form.ToEditRequest(_documentId, confirmDuplicate: false), CancellationToken.None);
        if (result.Error == ErrorCode.DuplicateNumber)
        {
            var question = localizer.Format("Import_AskDuplicate", Form.PartnerName.Trim(), Form.DocNumber.Trim());
            if (!await dialogs.ConfirmAsync(question))
                return;
            result = await documents.EditAsync(Form.ToEditRequest(_documentId, confirmDuplicate: true), CancellationToken.None);
        }

        if (!result.IsSuccess)
        {
            await dialogs.ShowErrorAsync(localizer.Error(result.Error.Value)); // stays open to fix it
            return;
        }

        messenger.Send(new DocumentsChangedMessage(DocumentsChange.Edited, _documentId));
        Completed?.Invoke(this, EventArgs.Empty);
    }

    // When the window closes, saved or not.
    public void Release()
    {
        preview.Release(PreviewPath);
        PreviewPath = null;
    }
}
