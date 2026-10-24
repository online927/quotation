using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Quotation.Pdf.Tests;

/// <summary>Layout details of the TallyPrime quotation and proforma invoice prints.</summary>
public class TallyPrintLayoutTests
{
    private static readonly QuotationPdfRenderer Renderer = new();
    private static readonly PrintStamp Stamp = new(new DateTime(2026, 10, 23, 18, 11, 0), "keerthana");

    private static string Text(Page p) => string.Join(" ", p.GetWords().Select(w => w.Text));

    private static QuotationDocument WithTaxes(QuotationDocument d) => d with { };

    public static QuotationDocument Proforma()
    {
        var line = new PdfLine(1, "Tools (Nos)", ["Hogert Technik Tap And Die Set", "HT3B582 (Set of 32 Pcs)"], "82051000", 18, "",
            1, "NOS", 8356, 0, 8356, PartNumber: "HT3B582");
        var company = SampleDocuments.Company();
        company.Jurisdiction = "SUBJECT TO BANGALORE JURISDICTION";
        var baseDoc = SampleDocuments.Build([line], pf: 500);
        return new QuotationDocument
        {
            Title = "PROFORMA INVOICE",
            Kind = DocumentType.ProformaInvoice,
            Printed = Stamp with { LocalTime = new DateTime(2026, 10, 22, 12, 6, 0) },
            Company = company,
            Number = "TSPI2526-12",
            Date = new DateOnly(2026, 10, 22),
            OtherReferences = "R",
            DeliveryNote = "DN-7",
            DispatchDocNo = "LR 5531",
            DeliveryNoteDate = new DateOnly(2026, 10, 21),
            Buyer = baseDoc.Buyer,
            Consignee = baseDoc.Consignee,
            Lines = [line],
            Subtotal = 8356,
            SummaryRows =
            [
                new PdfSummaryRow("Packing & Forwarding", "", 500), new PdfSummaryRow("SGST", "", 797.04m),
                new PdfSummaryRow("CGST", "", 797.04m), new PdfSummaryRow("Round Off", "", -0.08m, Less: true),
            ],
            GrandTotal = 10450,
            TotalQuantity = 1,
            TotalQuantityUnit = "NOS",
            AmountInWords = IndianFormat.AmountInWords(10450),
            HsnSummary = [new PdfHsnRow("82051000", 8856, 9, 797.04m, 9, 797.04m, 0, 0)],
            TaxAmountInWords = IndianFormat.AmountInWords(1594.08m),
        };
    }

    [Fact]
    public void Quotation_prints_who_printed_it_and_tally_style_titles_and_footer()
    {
        var doc = SampleDocuments.Build(SampleDocuments.ManyLines(30)) with { Printed = Stamp };
        var pdf = Renderer.Render(doc);
        PdfRenderTests.Save("quotation-tally-layout.pdf", pdf);
        using var p = PdfDocument.Open(pdf);
        Assert.True(p.NumberOfPages >= 2);
        var first = Text(p.GetPage(1));
        Assert.Contains("Printed on 23-Oct-26 at 18:11", first);
        Assert.Contains("By: keerthana", first);
        Assert.Contains("continued ...", first);
        Assert.DoesNotContain("Page 1 of", first);
        var second = Text(p.GetPage(2));
        Assert.Contains("QUOTATION(Page 2)", second);
        Assert.DoesNotContain("Printed on", second); // first page only
        var last = Text(p.GetPage(p.NumberOfPages));
        foreach (var e in new[] { "Company's PAN", "Company's MSME No", "Company's IEC", "Company's Bank Details", "Mode/Terms of Payment",
                     "Other References", "Buyer's Ref./Order No.", "PO/4471 dt. 28-Sep-26" })
        {
            Assert.Contains(e, last);
        }
    }

    [Fact]
    public void Round_off_down_is_printed_as_less()
    {
        var doc = SampleDocuments.Build([SampleDocuments.Bevel()]) with
        {
            SummaryRows = [new PdfSummaryRow("Packing & Forwarding", "", 500), new PdfSummaryRow("Round Off", "", -0.4m, Less: true)],
        };
        using var p = PdfDocument.Open(Renderer.Render(doc));
        var text = Text(p.GetPage(1));
        Assert.Contains("Less :", text);
        Assert.Contains("(-)0.40", text);
    }

    [Fact]
    public void Proforma_invoice_matches_the_tally_layout()
    {
        var pdf = Renderer.Render(Proforma());
        PdfRenderTests.Save("proforma-invoice.pdf", pdf);
        using var p = PdfDocument.Open(pdf);
        Assert.Equal(1, p.NumberOfPages);
        var text = Text(p.GetPage(1));
        foreach (var e in new[]
                 {
                     "Printed on 22-Oct-26 at 12:06", "By: keerthana", "PROFORMA INVOICE", "Invoice No.", "TSPI2526-12", "Delivery Note", "DN-7",
                     "Reference No. & Date.", "TSPI2526-12 dt. 22-Oct-26", "Buyer's Order No.", "Dispatch Doc No.", "LR 5531", "Delivery Note Date",
                     "21-Oct-26", "Part No.", "HT3B582", "SGST", "CGST", "Less :", "(-)0.08", "10,450.00", "Taxable", "SGST/UTGST",
                     "Tax Amount", "8,856.00", "797.04", "1,594.08", "Tax Amount (in words)", "One Thousand Five Hundred Ninety Four and Eight paise",
                     "Company's MSME No", "Company's IEC", "This is a Proforma invoice and not a Tax invoice", "Customer's Seal and Signature",
                     "Authorised Signatory", "SUBJECT TO BANGALORE JURISDICTION", "This is a Computer Generated Invoice",
                 })
        {
            Assert.Contains(e, text);
        }
        Assert.DoesNotContain("Due on", text);
        Assert.DoesNotContain("Company's PAN", text); // as in the Tally proforma print
        Assert.DoesNotContain("Prepared by", text);
    }

    [Fact]
    public void Proforma_invoice_has_no_overlapping_words() =>
        new PdfRenderTests().No_overlapping_words(Proforma());
}
