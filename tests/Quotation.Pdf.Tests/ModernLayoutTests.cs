using Quotation.Core.Calculation;
using Quotation.Core.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Quotation.Pdf.Tests;

/// <summary>The "Modern" (Z) layout against docs/templates/modern-z (SPEC.md and the reference PDF).</summary>
public class ModernLayoutTests
{
    private static readonly ModernPdfRenderer Renderer = new();

    private static string Text(Page p) => string.Join(" ", p.GetWords().Select(w => w.Text));

    /// <summary>The data of sample_data.json (reference Quotation-100186).</summary>
    public static QuotationDocument Sample() => new()
    {
        Company = new CompanySettings
        {
            CompanyName = "T.SAIFUDDIN & CO",
            AddressLines = ["No.72 N.R.Road", "Bangalore", "Karnataka 560002"],
            Mobile = "+917019740620",
            Phone = "+919739394657",
            Gstin = "29AADFT8885H1ZF",
        },
        ModernTitle = "Quotation/Proforma",
        Number = "100186",
        OtherReferences = "R",
        Date = new DateOnly(2026, 11, 2),
        BuyerCaption = "Buyer/Consignee",
        Buyer = new PdfParty("BuyersHub", ["Plot 27 , PRT Colony , Karmanghat", "Hyderabad Telangana 500079", "India"],
            "36AAZFB2304B1ZQ", "", "", "", "+919182706754", "sales@buyershub.org.in"),
        Lines = [new PdfLine(1, "Vernier Hook Depth Gauge", ["Make - Insize", "Model : 1234-2001"], "", 18, "", 5, "Nos", 12011, 0, 60055)],
        Subtotal = 60055,
        TaxLines = [new PdfTaxLine("IGST", 18, 10809.90m)],
        GrandTotal = 70864.90m,
        AmountInWords = "",
        TermsAndConditions = ["Payment: 100% Against Proforma", "Packing and forwarding charges extra"],
    };

    [Fact]
    public void Sample_data_prints_like_the_reference()
    {
        var bytes = Renderer.Render(Sample());
        PdfRenderTests.Save("modern-sample.pdf", bytes);
        using var p = PdfDocument.Open(bytes);
        Assert.Equal(1, p.NumberOfPages);
        var page = p.GetPage(1);
        var text = Text(page);
        foreach (var s in new[] { "Quotation/Proforma", "Ref No. 100186", "Other Ref: R", "Buyer/Consignee", "BuyersHub", "GSTIN 36AAZFB2304B1ZQ",
                     "02 Nov 2026", "Vernier Hook Depth Gauge", "Make - Insize", "5.00", "Nos", "12,011.00", "60,055.00", "Sub Total",
                     "IGST18 (18%)", "10,809.90", "Total", "₹70,864.90", "Terms & Conditions", "1. Payment: 100% Against Proforma",
                     "2. Packing and forwarding charges extra", "HSN/SAC", "GST", "Disc %", "E. & O.E",
                     "This is a Computer Generated Document" })
        {
            Assert.Contains(s, text);
        }

        // Positions from SPEC.md §5 (PdfPig measures from the bottom of the page).
        double Baseline(string word) => QuotationPdfRenderer.A4Height - page.GetWords().First(w => w.Text == word).Letters[0].StartBaseLine.Y;
        double Left(string word) => page.GetWords().First(w => w.Text == word).BoundingBox.Left;
        Assert.Equal(145.3, Baseline("T.SAIFUDDIN"), 0); // seller name
        Assert.Equal(46.4, Left("T.SAIFUDDIN"), 0);
        Assert.Equal(246.8, Baseline("BuyersHub"), 0);
        Assert.Equal(369.2, Baseline("Vernier"), 0);
        Assert.Equal(86.3, Left("Vernier"), 0);
        Assert.Equal(467.3, Baseline("₹70,864.90"), 0);
        Assert.Equal(553.1, page.GetWords().First(w => w.Text == "₹70,864.90").BoundingBox.Right, 0);
        Assert.DoesNotContain("Regards", text);
    }

    [Fact]
    public void Discount_hsn_part_no_and_many_items_continue_on_more_pages()
    {
        var lines = Enumerable.Range(1, 14).Select(i => new PdfLine(i, $"Item {i} with a long name that has to wrap inside the item column",
            ["Make - Mitutoyo", "Model : 187-901"], "90172020", 18, "", 2, "NOS", 1000, i == 1 ? 10 : 0,
            QuotationCalculator.LineAmount(2, 1000, i == 1 ? 10 : 0), PartNumber: $"P-{i}")).ToList();
        var doc = Sample() with
        {
            Lines = lines, ShowPartNumber = true, Subtotal = lines.Sum(l => l.Amount),
            SummaryRows = [new PdfSummaryRow("Packing & Forwarding", "", 500), new PdfSummaryRow("Round Off", "", -0.4m, Less: true)],
            TaxLines = [new PdfTaxLine("CGST", 9, 2500), new PdfTaxLine("SGST", 9, 2500)],
            GrandTotal = 33299.6m,
            Remarks = "Delivery in two weeks",
            Printed = new PrintStamp(new DateTime(2026, 11, 3, 10, 5, 0), "keerthana"),
        };
        var bytes = Renderer.Render(doc);
        PdfRenderTests.Save("modern-many.pdf", bytes);
        using var p = PdfDocument.Open(bytes);
        Assert.True(p.NumberOfPages >= 2);
        var first = Text(p.GetPage(1));
        var last = Text(p.GetPage(p.NumberOfPages));
        Assert.Contains("continued ...", first);
        Assert.DoesNotContain("Sub Total", first);
        Assert.Contains("10%", first);           // the discount
        Assert.Contains("90172020", first);      // HSN/SAC
        Assert.Contains("Part No. P-1", first);
        Assert.Contains("CGST9 (9%)", last);
        Assert.Contains("SGST9 (9%)", last);
        Assert.Contains("Packing & Forwarding", last);
        Assert.Contains("(-)0.40", last);
        Assert.Contains("Remarks: Delivery in two weeks", last);
        Assert.Contains("Printed on 03-Nov-26 at 10:05", last);
        var all = Enumerable.Range(1, p.NumberOfPages).Select(i => Text(p.GetPage(i))).ToList();
        for (var i = 1; i <= 14; i++) Assert.Single(all, t => t.Contains($"Part No. P-{i} ", StringComparison.Ordinal) || t.EndsWith($"Part No. P-{i}", StringComparison.Ordinal));
    }
}

/// <summary>Buyer and consignee addresses are printed in at most three lines (Tally and Zoho layouts), never cut.</summary>
public class AddressLinesTests
{
    private static readonly PdfParty LongAddress = new("Instatenders Solutions Private Limited",
        ["No. 149/1, Gandhi Street,", "Kundrathur,", "Sriperumbudur Main Road,", "Kavanur,", "Kundrathur, Chennai - 600069",
         "Near the big water tank opposite the old bus stand, Tamil Nadu, India"],
        "33AAFCI2552K1ZM", "Tamil Nadu", "33", "", "9150081257");

    private static int AddressLineCount(UglyToad.PdfPig.Content.Page page, string from, string until)
    {
        var words = page.GetWords().ToList();
        var top = words.First(w => w.Text == from).BoundingBox.Bottom;
        var bottom = words.First(w => w.Text == until && w.BoundingBox.Bottom < top).BoundingBox.Bottom;
        return words.Where(w => w.BoundingBox.Bottom < top - 1 && w.BoundingBox.Bottom > bottom + 1 && w.BoundingBox.Left < page.Width / 2)
            .Select(w => Math.Round(w.BoundingBox.Bottom)).Distinct().Count();
    }

    [Fact]
    public void Tally_layout_prints_a_long_address_in_three_lines()
    {
        var doc = SampleDocuments.Build([SampleDocuments.Bevel()]) with { Buyer = LongAddress, Consignee = null };
        using var p = PdfDocument.Open(new QuotationPdfRenderer().Render(doc));
        var page = p.GetPage(1);
        Assert.Equal(3, AddressLineCount(page, "Instatenders", "GSTIN/UIN"));
        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
        foreach (var word in new[] { "149/1,", "Sriperumbudur", "Kavanur,", "600069,", "opposite", "India" }) Assert.Contains(word, text); // nothing cut
    }

    [Fact]
    public void Zoho_layout_prints_a_long_address_in_three_lines()
    {
        var doc = ModernLayoutTests.Sample() with { Buyer = LongAddress };
        var bytes = new ModernPdfRenderer().Render(doc);
        PdfRenderTests.Save("modern-long-address.pdf", bytes);
        using var p = PdfDocument.Open(bytes);
        var page = p.GetPage(1);
        Assert.InRange(AddressLineCount(page, "Instatenders", "Code"), 1, 3); // wider box: it may need fewer
        var text = string.Join(" ", page.GetWords().Select(w => w.Text));
        foreach (var word in new[] { "149/1,", "Sriperumbudur", "Kavanur,", "600069,", "opposite", "India" }) Assert.Contains(word, text);
    }

    [Fact]
    public void A_short_address_is_printed_as_typed()
    {
        var doc = SampleDocuments.Build([SampleDocuments.Bevel()]) with { Consignee = null };
        using var p = PdfDocument.Open(new QuotationPdfRenderer().Render(doc));
        var page = p.GetPage(1);
        Assert.Equal(3, AddressLineCount(page, "Sonepar", "GSTIN/UIN")); // Plot No. 12 … / Chakan, Pune / PIN: 410501
        var words = page.GetWords().ToList();
        Assert.Equal(words.First(w => w.Text == "Plot").BoundingBox.Left, words.First(w => w.Text == "Chakan,").BoundingBox.Left, 1); // own lines
    }
}
