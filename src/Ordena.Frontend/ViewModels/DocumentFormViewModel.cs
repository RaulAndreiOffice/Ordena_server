using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Ordena.Backend.Dtos;
using Ordena.Backend.Models;
using Ordena.Backend.Services;
using Ordena.Frontend.Resources;
using Ordena.Frontend.Services;

namespace Ordena.Frontend.ViewModels;

public sealed record TypeOption(DocType Type, string Label)
{
    // What screen readers announce for a dropdown entry, instead of the record's members.
    public override string ToString() => Label;
}

// The fields of one document: partner (with suggestions), number, date, type, Recibida/Emitida.
// Shared by the import screen and, later, the edit dialog.
public sealed partial class DocumentFormViewModel : ObservableValidator
{
    private readonly IPartnerService _partners;
    private readonly ILocalizer _localizer;
    private CancellationTokenSource? _partnerLookup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIssued), nameof(PartnerLabel))]
    private bool _isReceived = true;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.Validation_Required))]
    private string _partnerName = "";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.Validation_Required))]
    private string _docNumber = "";

    // DateTime for the DatePicker; only the date part is used.
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.Validation_Required))]
    private DateTime? _issueDate;

    [ObservableProperty]
    private TypeOption _selectedType;

    // Existing partners matching what is typed, for the autocomplete list.
    [ObservableProperty]
    private IReadOnlyList<string> _suggestions = [];

    // "Se guardará como «Telefónica España, S.A.U.»" when the typed name is an existing partner's
    // written another way: the backend files it under that partner.
    [ObservableProperty]
    private string? _similarHint;

    // Bumped to put the cursor back in the partner field (FocusBehavior).
    [ObservableProperty]
    private int _focusRequest;

    public DocumentFormViewModel(IPartnerService partners, ILocalizer localizer)
    {
        _partners = partners;
        _localizer = localizer;
        Types = Enum.GetValues<DocType>().Select(t => new TypeOption(t, localizer.Enum(t))).ToList();
        _selectedType = Types[0];
    }

    public IReadOnlyList<TypeOption> Types { get; }

    // How long typing must pause before partners are looked up. Zero in tests.
    public TimeSpan SuggestionDelay { get; set; } = TimeSpan.FromMilliseconds(150);

    public bool IsIssued
    {
        get => !IsReceived;
        set => IsReceived = !value;
    }

    // The partner of a received document is its supplier; of an issued one, its customer.
    public string PartnerLabel => _localizer.Get(IsReceived ? "Form_Supplier" : "Form_Customer");

    // Shows every missing field; true when the form can be saved.
    public bool Validate()
    {
        ValidateAllProperties();
        return !HasErrors;
    }

    public ImportRequest ToImportRequest(string sourcePath, bool confirmDuplicate, bool keepSource = false) => new()
    {
        SourcePath = sourcePath,
        PartnerName = PartnerName.Trim(),
        DocNumber = DocNumber.Trim(),
        IssueDate = DateOnly.FromDateTime(IssueDate ?? throw new InvalidOperationException("Validate the form first.")),
        Type = SelectedType.Type,
        Direction = IsReceived ? Direction.Recibida : Direction.Emitida,
        ConfirmDuplicate = confirmDuplicate,
        KeepSource = keepSource,
    };

    // An archived document's values, for the edit dialog.
    public void Load(DocumentDetails document)
    {
        IsReceived = document.Direction == Direction.Recibida;
        PartnerName = document.PartnerName;
        DocNumber = document.DocNumber;
        IssueDate = document.IssueDate.ToDateTime(TimeOnly.MinValue);
        SelectedType = Types.Single(t => t.Type == document.Type);
        ClearErrors();
    }

    public EditDocumentRequest ToEditRequest(int documentId, bool confirmDuplicate) => new()
    {
        Id = documentId,
        PartnerName = PartnerName.Trim(),
        DocNumber = DocNumber.Trim(),
        IssueDate = DateOnly.FromDateTime(IssueDate ?? throw new InvalidOperationException("Validate the form first.")),
        Type = SelectedType.Type,
        Direction = IsReceived ? Direction.Recibida : Direction.Emitida,
        ConfirmDuplicate = confirmDuplicate,
    };

    // Ready for the next file. Type and Recibida/Emitida stay: consecutive files are usually alike.
    public void Reset()
    {
        _partnerLookup?.Cancel();
        PartnerName = "";
        DocNumber = "";
        IssueDate = null;
        Suggestions = [];
        SimilarHint = null;
        ClearErrors();
        FocusRequest++;
    }

    partial void OnPartnerNameChanged(string value) => _ = LookUpPartnerAsync(value);

    // After a pause in typing: the suggestions, and whether the name is an existing partner's.
    // A newer keystroke cancels an older lookup, so a slow answer never overwrites a newer one.
    private async Task LookUpPartnerAsync(string text)
    {
        _partnerLookup?.Cancel();
        var lookup = _partnerLookup = new CancellationTokenSource();
        try
        {
            if (SuggestionDelay > TimeSpan.Zero)
                await Task.Delay(SuggestionDelay, lookup.Token);

            var suggestions = await _partners.SuggestAsync(text, 10, lookup.Token);
            var similar = await _partners.FindSimilarAsync(text, lookup.Token);
            if (lookup.IsCancellationRequested)
                return;

            Suggestions = suggestions.Select(p => p.Name).ToList();
            SimilarHint = similar is not null && similar.Name != text.Trim()
                ? _localizer.Format("Form_SavedAs", similar.Name)
                : null;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
