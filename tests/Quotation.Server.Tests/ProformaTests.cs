using System.Net;
using Quotation.ApiClient;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;

namespace Quotation.Server.Tests;

public class ProformaTests : QuotationTestBase
{
    private FinancialYear ActiveFy => FinancialYear.For(Today);

    [Fact]
    public async Task Proforma_invoices_have_their_own_number_series()
    {
        var quotation = await Api.CreateQuotationAsync(await BevelRequest());
        var request = await BevelRequest();
        request.DocType = DocumentType.ProformaInvoice;
        var preview = await Api.NextNumberAsync(type: DocumentType.ProformaInvoice);
        var proforma = await Api.CreateQuotationAsync(request);

        Assert.Equal(QuotationNumberFormatter.Format("TSQ{FY}-{SEQ}", ActiveFy, 1), quotation.Number);
        Assert.Equal(QuotationNumberFormatter.Format("TSPI{FY}-{SEQ}", ActiveFy, 1), proforma.Number);
        Assert.Equal(preview.Number, proforma.Number);
        Assert.Equal(DocumentType.ProformaInvoice, proforma.DocType);

        // Continue from the last proforma printed in Tally (TSPI…-12 → next 13); the quotation series is untouched.
        await Api.SetNextSequenceAsync(ActiveFy.StartYear, 13, DocumentType.ProformaInvoice);
        var next = await Api.CreateQuotationAsync(request);
        Assert.EndsWith("-13", next.Number);
        Assert.Equal(2, (await Api.NumberSeriesAsync()).NextSequence);
        Assert.Equal(14, (await Api.NumberSeriesAsync(type: DocumentType.ProformaInvoice)).NextSequence);
    }

    [Fact]
    public async Task Converting_a_quotation_makes_a_proforma_with_the_same_lines()
    {
        var request = await BevelRequest(qty: 3);
        request.BuyersReference = "PO/4471";
        var quotation = await Api.CreateQuotationAsync(request);

        var proforma = await Api.ConvertToProformaAsync(quotation.Id);

        Assert.NotEqual(quotation.Id, proforma.Id);
        Assert.Equal(DocumentType.ProformaInvoice, proforma.DocType);
        Assert.StartsWith("TSPI", proforma.Number);
        Assert.Equal("Our quotation " + quotation.Number, proforma.OtherReferences);
        Assert.Equal("PO/4471", proforma.BuyersReference);
        Assert.Equal(quotation.Lines.Single().Quantity, proforma.Lines.Single().Quantity);
        Assert.Equal(quotation.GrandTotal, proforma.GrandTotal);
        Assert.Equal(QuotationStatus.Draft, proforma.Status);

        // The quotation itself is unchanged, and a proforma cannot be converted again.
        Assert.Equal(DocumentType.Quotation, (await Api.QuotationAsync(quotation.Id)).DocType);
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ConvertToProformaAsync(proforma.Id));
        Assert.Equal(HttpStatusCode.Conflict, ex.Status);
    }

    [Fact]
    public async Task Lists_can_be_filtered_by_document_type()
    {
        var quotation = await Api.CreateQuotationAsync(await BevelRequest());
        var proforma = await Api.ConvertToProformaAsync(quotation.Id);

        var all = await Api.QuotationsAsync();
        Assert.Contains(all, s => s.Id == quotation.Id && s.DocType == DocumentType.Quotation);
        Assert.Contains(all, s => s.Id == proforma.Id && s.DocType == DocumentType.ProformaInvoice);
        Assert.Equal([proforma.Id], (await Api.QuotationsAsync(type: DocumentType.ProformaInvoice)).Select(s => s.Id));
        Assert.Equal([quotation.Id], (await Api.QuotationsAsync(type: DocumentType.Quotation)).Select(s => s.Id));
    }

    [Fact]
    public async Task Proforma_fields_are_saved_and_the_pdf_is_a_proforma_invoice()
    {
        var request = await BevelRequest();
        request.DocType = DocumentType.ProformaInvoice;
        request.DeliveryNote = "DN/118";
        request.DispatchDocNo = "LR 55120";
        request.DeliveryNoteDate = Today;
        var proforma = await Api.CreateQuotationAsync(request);
        Assert.Equal("DN/118", proforma.DeliveryNote);
        Assert.Equal("LR 55120", proforma.DispatchDocNo);
        Assert.Equal(Today, proforma.DeliveryNoteDate);

        await Api.ApproveQuotationAsync(proforma.Id);
        var text = PdfText(await Api.QuotationPdfAsync(proforma.Id));
        Assert.Contains("PROFORMA INVOICE", text);
        Assert.Contains(proforma.Number!, text);
        Assert.Contains("DN/118", text);
        Assert.Contains("Printed on", text);
        Assert.Contains("By: admin", text);
    }

    private static string PdfText(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join("\n", doc.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }
}
