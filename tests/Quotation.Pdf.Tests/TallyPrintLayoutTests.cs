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
    public void Tax_lines_follow_the_items_and_the_subtotal_only_with_several_items()
    {
        // One item (TSPI2526-12): P&F, SGST, CGST and Round Off directly under the item, no subtotal.
        using (var one = PdfDocument.Open(Renderer.Render(Proforma())))
        {
            var words = one.GetPage(1).GetWords().ToList();
            var itemY = words.First(w => w.Text == "Pcs)").BoundingBox.Bottom; // last description line of the item
            var pfY = words.First(w => w.Text == "Packing").BoundingBox.Bottom;
            var totalY = words.First(w => w.Text == "Total").BoundingBox.Bottom;
            Assert.InRange(itemY - pfY, 5, 40);   // one blank line after the item (PDF y grows upwards)
            Assert.True(pfY - totalY > 200);       // the empty space is below the tax lines, not above
            Assert.DoesNotContain(words, w => w.Text == "8,356.00" && w.BoundingBox.Bottom < itemY - 2); // no subtotal
        }

        // Several items: the subtotal (with a short rule) comes first.
        var lines = SampleDocuments.ManyLines(3);
        var doc = SampleDocuments.Build(lines, pf: 700) with
        {
            SummaryRows = [new PdfSummaryRow("Packing & Forwarding", "", 700), new PdfSummaryRow("SGST", "", 100), new PdfSummaryRow("CGST", "", 100)],
        };
        using var many = PdfDocument.Open(Renderer.Render(doc));
        var w2 = many.GetPage(1).GetWords().ToList();
        var packing = w2.First(w => w.Text == "Packing").BoundingBox.Bottom;
        var subtotal = w2.Where(w => w.Text == IndianFormat.Amount(doc.Subtotal)).Select(w => w.BoundingBox.Bottom).ToList();
        Assert.Contains(subtotal, y => y > packing && y - packing < 20); // the subtotal line right above P&F
    }

    [Fact]
    public void Seal_is_stamped_over_the_authorised_signatory_on_the_last_page()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAJUlEQVR4nGM4ICDwn5aYYdSCUQtGLRi1YNSCUQtGLRi1YGhYAAC4lDX9H0TkSwAAAABJRU5ErkJggg==");
        var doc = SampleDocuments.Build(SampleDocuments.ManyLines(30)) with { Seal = png };
        using var p = PdfDocument.Open(Renderer.Render(doc));
        Assert.Empty(p.GetPage(1).GetImages());
        var last = p.GetPage(p.NumberOfPages);
        var image = Assert.Single(last.GetImages());
        var signatory = last.GetWords().First(w => w.Text == "Signatory").BoundingBox;
        Assert.True(image.Bounds.Right > signatory.Left && image.Bounds.Left < signatory.Right);   // over the signatory box
        Assert.True(image.Bounds.Bottom < signatory.Top && image.Bounds.Top > signatory.Bottom);
    }

    private static QuotationDocument WithBuyer(PdfParty buyer, PdfParty? consignee) =>
        SampleDocuments.Build([SampleDocuments.Bevel()]) with { Buyer = buyer, Consignee = consignee };

    private static readonly PdfParty Gutslane = new("GUTSLANE PRECITECH",
        ["Plot No.238~240, Road No.10,", "Averahalli, KIADB, Dobbaspet 4th",
         "Phase, Sompura Hobli, Nelamangala Taluk, Bangalore - 562111, Karnataka, India"],
        "29AZGPG3058H3Z8", "Karnataka", "29", "", "9731966554", "girishbabu@gutslane.com");

    [Fact]
    public void Party_labels_and_colons_line_up_like_Tally()
    {
        using var p = PdfDocument.Open(Renderer.Render(WithBuyer(Gutslane, null)));
        var all = p.GetPage(1).GetWords().ToList();
        var buyer = all.First(w => w.Text == "Buyer").BoundingBox.Bottom;
        var words = all.Where(w => w.BoundingBox.Bottom < buyer).ToList(); // the buyer block, not the company's lines
        var labels = new[] { "GSTIN/UIN", "State", "Contact", "E-Mail" }.Select(l => words.First(w => w.Text == l)).ToList();
        Assert.All(labels, w => Assert.Equal(labels[0].BoundingBox.Left, w.BoundingBox.Left, 1));
        // The colon after each label, and the value after it, start at the same place on every line.
        var colons = labels.Select(l => words.Where(w => w.Text.StartsWith(':') && Math.Abs(w.BoundingBox.Bottom - l.BoundingBox.Bottom) < 1)
            .MinBy(w => w.BoundingBox.Left)!).ToList();
        Assert.All(colons, c => Assert.Equal(colons[0].BoundingBox.Left, c.BoundingBox.Left, 1));
        var values = labels.Zip(colons, (l, c) => words.Where(w => Math.Abs(w.BoundingBox.Bottom - l.BoundingBox.Bottom) < 1
            && w.BoundingBox.Left > c.BoundingBox.Right - 0.1 && !w.Text.StartsWith(':')).MinBy(w => w.BoundingBox.Left)!).ToList();
        Assert.Equal(["29AZGPG3058H3Z8", "Karnataka,", "9731966554", "girishbabu@gutslane.com"], values.Select(v => v.Text));
        Assert.All(values, v => Assert.Equal(values[0].BoundingBox.Left, v.BoundingBox.Left, 1));
        Assert.True(values[0].BoundingBox.Left > colons[0].BoundingBox.Left);
    }

    [Fact]
    public void Long_address_line_prints_smaller_on_one_line_and_consignee_can_be_left_out()
    {
        using var p = PdfDocument.Open(Renderer.Render(WithBuyer(Gutslane, null)));
        var page = p.GetPage(1);
        var words = page.GetWords().ToList();
        var phase = words.First(w => w.Text == "Phase,");
        var india = words.First(w => w.Text == "India");
        Assert.Equal(phase.BoundingBox.Bottom, india.BoundingBox.Bottom, 1); // one line, not wrapped
        var plot = words.First(w => w.Text == "Plot");
        Assert.True(phase.BoundingBox.Height < plot.BoundingBox.Height - 0.5, $"{phase.BoundingBox.Height} vs {plot.BoundingBox.Height}");
        Assert.DoesNotContain("Consignee", Text(page));
        Assert.Contains("Buyer (Bill to)", Text(page));
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
