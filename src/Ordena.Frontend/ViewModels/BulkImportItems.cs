using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ordena.Backend.Dtos;

namespace Ordena.Frontend.ViewModels;

// One detected (or typed) name pattern, as a card: "{fecha}_{proveedor}_{numero} · dd-MM-yyyy",
// "2.871 de 3.000 coinciden".
public sealed record PatternOption(string Pattern, string? DateFormat, string Label, string MatchText)
{
    public override string ToString() => Label;
}

// A choice of the preview's status filter: "Fecha inválida (12)". A null Status is every row.
public sealed record StatusFilterOption(BulkRowStatus? Status, string Label)
{
    public override string ToString() => Label;
}

// One file of the folder in the preview. Date, partner and number can be fixed by hand: an edited row
// with all three is ready to import (Ok). Include says whether it is imported: on for Ok rows, off for
// the others until fixed or chosen ("Incluir aunque el nº exista", "Usar la fecha del archivo").
public sealed partial class BulkRowViewModel : ObservableObject
{
    private static readonly string[] DateFormats = ["d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInclude))]
    private BulkRowStatus _status;

    [ObservableProperty]
    private string _dateText;

    [ObservableProperty]
    private string _partner;

    [ObservableProperty]
    private string _number;

    [ObservableProperty]
    private TypeOption _type;

    [ObservableProperty]
    private bool _include;

    public BulkRowViewModel(BulkImportRow row, TypeOption type)
    {
        Path = row.Path;
        RelativePath = row.RelativePath;
        _status = row.Status;
        _dateText = row.IssueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
        _partner = row.PartnerName ?? "";
        _number = row.DocNumber ?? "";
        _type = type;
        _include = row.Status == BulkRowStatus.Ok;
    }

    public string Path { get; }
    public string RelativePath { get; }

    // Raised after a hand edit changed the row's status, so the page recounts its filter.
    public event EventHandler? StatusChanged;

    // Raised when the row is ticked or unticked, so the page updates "2.871 se importarán".
    public event EventHandler? IncludeChanged;

    partial void OnIncludeChanged(bool value) => IncludeChanged?.Invoke(this, EventArgs.Empty);

    // Rows that can't be imported as they are can't be ticked until fixed.
    public bool CanInclude => Status is BulkRowStatus.Ok or BulkRowStatus.NumberExists or BulkRowStatus.CheckDate;

    public DateOnly? Date => DateOnly.TryParseExact(DateText.Trim(), DateFormats, CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var date) ? date : null;

    // Imported with these values: ticked, and all three fields readable.
    public bool WillImport => Include && CanInclude && Date is not null
        && !string.IsNullOrWhiteSpace(Partner) && !string.IsNullOrWhiteSpace(Number);

    partial void OnDateTextChanged(string value) => Recheck();
    partial void OnPartnerChanged(string value) => Recheck();
    partial void OnNumberChanged(string value) => Recheck();

    // A hand-fixed row with a readable date, a partner and a number is ready (a number still taken shows up
    // in the run's report); one missing any of them isn't.
    private void Recheck()
    {
        var status = Date is not null && !string.IsNullOrWhiteSpace(Partner) && !string.IsNullOrWhiteSpace(Number)
            ? BulkRowStatus.Ok
            : Date is null && DateText.Trim().Length > 0 ? BulkRowStatus.InvalidDate : BulkRowStatus.NotRecognized;
        if (status == Status)
            return;

        Status = status;
        Include = status == BulkRowStatus.Ok;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

// Names that are probably one partner, merged into Target when Accept is ticked:
// "Telefonica ×120 · Telefonica Espana ×3 → Telefónica España, S.A.U.".
public sealed partial class PartnerGroupViewModel(IReadOnlyList<string> names, string namesText, string target) : ObservableObject
{
    [ObservableProperty]
    private string _target = target;

    [ObservableProperty]
    private bool _accept = true;

    public IReadOnlyList<string> Names { get; } = names;
    public string NamesText { get; } = namesText;
}

// One partner name of the folder, with how many files use it; Rename changes it for the import.
// ExistingName: the archive's partner it will join.
public sealed partial class PartnerNameViewModel(string name, int count, string? existingName) : ObservableObject
{
    [ObservableProperty]
    private string _rename = name;

    public string Name { get; } = name;
    public int Count { get; } = count;
    public string? ExistingName { get; } = existingName;
}
