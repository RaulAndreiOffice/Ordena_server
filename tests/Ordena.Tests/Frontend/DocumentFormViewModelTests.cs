using Ordena.Backend.Models;
using Ordena.Frontend.Services;
using Ordena.Frontend.ViewModels;
using Ordena.Tests.TestSupport;

namespace Ordena.Tests.Frontend;

// The document fields of the import screen, with partners faked and no typing delay.
[Collection(CultureCollection.Name)]
public sealed class DocumentFormViewModelTests : IDisposable
{
    private const string Telefonica = "Telefónica España, S.A.U.";

    private readonly CultureScope _spanish = new("es-ES");
    private readonly DocumentFormViewModel _form =
        new(new FakePartnerService(Telefonica, "Telepizza", "Iberdrola"), new Localizer()) { SuggestionDelay = TimeSpan.Zero };

    public void Dispose() => _spanish.Dispose();

    [Fact]
    public void It_starts_as_a_received_invoice_with_every_type_listed()
    {
        Assert.True(_form.IsReceived);
        Assert.False(_form.IsIssued);
        Assert.Equal("Proveedor", _form.PartnerLabel);
        Assert.Equal(Enum.GetValues<DocType>(), _form.Types.Select(t => t.Type));
        Assert.Equal("Factura", _form.SelectedType.Label);
    }

    [Fact]
    public void An_issued_document_asks_for_the_customer()
    {
        var changed = new List<string?>();
        _form.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _form.IsIssued = true;

        Assert.False(_form.IsReceived);
        Assert.Equal("Cliente", _form.PartnerLabel);
        Assert.Contains(nameof(_form.IsIssued), changed);
        Assert.Contains(nameof(_form.PartnerLabel), changed);
    }

    [Fact]
    public void Missing_fields_fail_validation_each_with_its_message()
    {
        _form.DocNumber = "A-1";

        Assert.False(_form.Validate());

        Assert.Equal(["Obligatorio"], _form.GetErrors(nameof(_form.PartnerName)).Select(e => e.ErrorMessage));
        Assert.Equal(["Obligatorio"], _form.GetErrors(nameof(_form.IssueDate)).Select(e => e.ErrorMessage));
        Assert.Empty(_form.GetErrors(nameof(_form.DocNumber)));
    }

    // A name or number of spaces only is as good as empty.
    [Fact]
    public void Blank_fields_fail_validation_too()
    {
        Fill();
        _form.PartnerName = "   ";

        Assert.False(_form.Validate());
        Assert.NotEmpty(_form.GetErrors(nameof(_form.PartnerName)));
    }

    [Fact]
    public void A_filled_form_validates_and_becomes_the_import_request()
    {
        Fill();
        _form.IsIssued = true;
        _form.SelectedType = _form.Types.Single(t => t.Type == DocType.Proforma);

        Assert.True(_form.Validate());
        var request = _form.ToImportRequest(@"C:\Descargas\scan.pdf", confirmDuplicate: true);

        Assert.Equal(@"C:\Descargas\scan.pdf", request.SourcePath);
        Assert.Equal("Iberdrola", request.PartnerName);
        Assert.Equal("F-77", request.DocNumber);
        Assert.Equal(new DateOnly(2026, 3, 14), request.IssueDate);
        Assert.Equal(DocType.Proforma, request.Type);
        Assert.Equal(Direction.Emitida, request.Direction);
        Assert.True(request.ConfirmDuplicate);
    }

    [Fact]
    public void Typing_a_partner_lists_the_existing_ones_that_match()
    {
        _form.PartnerName = "tele";

        Assert.Equal([Telefonica, "Telepizza"], _form.Suggestions);
        Assert.Null(_form.SimilarHint);
    }

    // The backend files it under the existing partner anyway; the hint says so before saving.
    [Fact]
    public void An_existing_partner_written_another_way_shows_where_it_will_be_filed()
    {
        _form.PartnerName = "telefonica espana sau";

        Assert.Equal($"Se guardará como «{Telefonica}»", _form.SimilarHint);

        _form.PartnerName = Telefonica;
        Assert.Null(_form.SimilarHint);
    }

    // Consecutive files usually come from the same kind of batch: type and direction stay.
    [Fact]
    public void Reset_clears_the_document_but_keeps_type_and_direction()
    {
        Fill();
        _form.IsIssued = true;
        _form.SelectedType = _form.Types.Single(t => t.Type == DocType.Recibo);
        _form.Validate();
        _form.PartnerName = "";
        Assert.True(_form.HasErrors);
        var focus = _form.FocusRequest;

        _form.Reset();

        Assert.Equal("", _form.PartnerName);
        Assert.Equal("", _form.DocNumber);
        Assert.Null(_form.IssueDate);
        Assert.Empty(_form.Suggestions);
        Assert.Null(_form.SimilarHint);
        Assert.False(_form.HasErrors);
        Assert.True(_form.IsIssued);
        Assert.Equal(DocType.Recibo, _form.SelectedType.Type);
        Assert.Equal(focus + 1, _form.FocusRequest);
    }

    // The edit dialog starts from the archived document's values, with nothing marked as missing.
    [Fact]
    public void An_archived_document_loads_into_the_form_and_back_into_an_edit_request()
    {
        var imported = new DateTime(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc);
        _form.Validate();

        _form.Load(new Ordena.Backend.Dtos.DocumentDetails(7, Telefonica, "A-2026/0123", new DateOnly(2026, 3, 14),
            DocType.FacturaRectificativa, Direction.Emitida, "x.pdf", "scan.pdf", imported, imported, null));

        Assert.False(_form.HasErrors);
        Assert.True(_form.IsIssued);
        Assert.Equal("Cliente", _form.PartnerLabel);
        Assert.True(_form.Validate());
        Assert.Equal(new Ordena.Backend.Dtos.EditDocumentRequest
        {
            Id = 7,
            PartnerName = Telefonica,
            DocNumber = "A-2026/0123",
            IssueDate = new DateOnly(2026, 3, 14),
            Type = DocType.FacturaRectificativa,
            Direction = Direction.Emitida,
            ConfirmDuplicate = true,
        }, _form.ToEditRequest(7, confirmDuplicate: true));
    }

    [Fact]
    public void Screen_readers_hear_each_type_by_its_name()
    {
        Assert.Equal("Factura rectificativa", _form.Types.Single(t => t.Type == DocType.FacturaRectificativa).ToString());
    }

    private void Fill()
    {
        _form.PartnerName = "Iberdrola";
        _form.DocNumber = "F-77";
        _form.IssueDate = new DateTime(2026, 3, 14);
    }
}
