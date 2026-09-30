using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Quotation.Pdf.Tests;

public class PdfRenderTests
{
    private static readonly QuotationPdfRenderer Renderer = new();

    private static PdfDocument Open(byte[] bytes) => PdfDocument.Open(bytes);

    private static string Text(Page p) => string.Join(" ", p.GetWords().Select(w => w.Text));

    internal static void Save(string name, byte[] pdf)
    {
        var dir = Environment.GetEnvironmentVariable("PDF_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), pdf);
        }
    }

    [Fact]
    public void Single_product_quotation_contains_all_reference_fields()
    {
        var pdf = Renderer.Render(SampleDocuments.Build([SampleDocuments.Bevel()]));
        Save("single.pdf", pdf);
        using var doc = Open(pdf);
        Assert.Equal(1, doc.NumberOfPages);
        var text = Text(doc.GetPage(1));
        string[] expected =
        [
            "QUOTATION", "T.SAIFUDDIN", "N.R.Road,", "GSTIN/UIN:", "29AAAFT0000A1Z0", "Karnataka,", "Consignee", "(Ship", "Buyer", "(Bill",
            "Quotation", "TSQ2526-3247", "Dated", "30-Sep-26", "Buyer's", "PO/4471", "Dispatched", "Destination", "Pune", "Payment",
            "Other", "References", "Delivery", "Sl", "Description", "Goods", "HSN/SAC", "GST", "Due", "Quantity", "Rate", "per", "Disc.",
            "Amount", "187-901-10-UNIVERSAL", "BEVEL", "PROTRACTOR", "Brand:", "Mitutoyo", "MOQ:", "90172020", "18", "15,600.00",
            "31,200.00", "Packing", "Forwarding", "500.00", "Total", "31,700.00", "Chargeable", "INR", "Thirty", "Seven", "Only", "E.",
            "O.E", "PAN", "AAAFT0000A", "MSME", "IEC", "Declaration", "valid", "Bank", "IFS", "EXMP0000001", "Prepared", "Verified",
            "Authorised", "Signatory", "Computer", "Generated",
        ];
        foreach (var e in expected) Assert.Contains(e, text);
    }

    public static IEnumerable<object[]> OverlapCases()
    {
        yield return [SampleDocuments.Build(SampleDocuments.ManyLines(12))];
        var longDesc = SampleDocuments.Bevel() with { DescriptionLines = Enumerable.Range(1, 120).Select(i => $"Spec {i}").ToArray() };
        yield return [SampleDocuments.Build([longDesc])];
        yield return [SampleDocuments.Build([SampleDocuments.Bevel()], buyerAddress: [new string('W', 20) + " " + string.Join(" ", Enumerable.Repeat("Longword", 30))])];
    }

    [Theory]
    [MemberData(nameof(OverlapCases))]
    public void No_overlapping_words(QuotationDocument document)
    {
        var pdf = Renderer.Render(document);
        using var doc = Open(pdf);
        foreach (var page in doc.GetPages())
        {
            var words = page.GetWords().Where(w => w.Text.Trim().Length > 0).ToList();
            for (var i = 0; i < words.Count; i++)
            for (var j = i + 1; j < words.Count; j++)
            {
                var a = words[i].BoundingBox;
                var b = words[j].BoundingBox;
                var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                var overlapY = Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom);
                Assert.False(overlapX > 0.8 && overlapY > 0.8,
                    $"Page {page.Number}: '{words[i].Text}' overlaps '{words[j].Text}'");
            }
        }
    }

    [Fact]
    public void All_text_is_inside_the_page()
    {
        var pdf = Renderer.Render(SampleDocuments.Build(SampleDocuments.ManyLines(30)));
        using var doc = Open(pdf);
        foreach (var page in doc.GetPages())
        foreach (var w in page.GetWords())
        {
            Assert.InRange(w.BoundingBox.Left, 0, page.Width);
            Assert.InRange(w.BoundingBox.Right, 0, page.Width - 20);
            Assert.InRange(w.BoundingBox.Bottom, 10, page.Height);
        }
    }

    [Fact]
    public void Multi_page_quotation_repeats_header_and_keeps_totals_on_last_page()
    {
        var lines = SampleDocuments.ManyLines(45);
        var pdf = Renderer.Render(SampleDocuments.Build(lines));
        Save("multipage.pdf", pdf);
        using var doc = Open(pdf);
        Assert.True(doc.NumberOfPages >= 3, $"expected several pages, got {doc.NumberOfPages}");

        var pages = doc.GetPages().ToList();
        for (var i = 0; i < pages.Count; i++)
        {
            var text = Text(pages[i]);
            Assert.Contains("TSQ2526-3247", text);          // header on every page
            Assert.Contains("Description", text);            // column headings on every page
            Assert.Contains($"Page {i + 1} of {pages.Count}", text);
            if (i < pages.Count - 1)
            {
                Assert.Contains($"continued to page number {i + 2}", text);
                Assert.DoesNotContain("Chargeable", text);
                Assert.DoesNotContain("Authorised", text);
            }
        }
        var last = Text(pages[^1]);
        Assert.Contains("Chargeable", last);
        Assert.Contains("Authorised", last);
        Assert.Contains("Total", last);

        // Every item appears exactly once, in order, never split across pages.
        var all = string.Join(" ", pages.Select(Text));
        var positions = lines.Select(l => all.IndexOf($"ITEM NUMBER {l.SlNo} WITH", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
        foreach (var l in lines)
        {
            var onPages = pages.Count(p => Text(p).Contains($"ITEM NUMBER {l.SlNo} WITH", StringComparison.Ordinal));
            Assert.Equal(1, onPages);
        }
    }

    [Fact]
    public void Row_is_never_split_between_pages()
    {
        var lines = SampleDocuments.ManyLines(45);
        using var measure = PdfSharp.Drawing.XGraphics.CreateMeasureContext(new PdfSharp.Drawing.XSize(595, 842),
            PdfSharp.Drawing.XGraphicsUnit.Point, PdfSharp.Drawing.XPageDirection.Downwards);
        var layout = Renderer.Layout(measure, SampleDocuments.Build(lines));
        var placed = layout.Pages.SelectMany(p => p).ToList();
        Assert.Equal(lines.Count, placed.Count);
        Assert.All(placed, r => Assert.False(r.IsContinuation));
    }

    [Fact]
    public void Very_long_description_is_continued_without_losing_text()
    {
        var desc = Enumerable.Range(1, 120).Select(i => $"Specification line {i}: tolerance ±0.01 mm").ToArray();
        var line = SampleDocuments.Bevel() with { DescriptionLines = desc };
        var pdf = Renderer.Render(SampleDocuments.Build([line]));
        Save("longdesc.pdf", pdf);
        using var doc = Open(pdf);
        var all = string.Join(" ", doc.GetPages().Select(Text));
        for (var i = 1; i <= 120; i++) Assert.Contains($"line {i}:", all);
        Assert.True(doc.NumberOfPages >= 2);
    }

    [Fact]
    public void Long_customer_address_wraps_inside_the_buyer_block()
    {
        var address = new[]
        {
            "Unit No. 1203-1207, 12th Floor, Tower B, World Trade Centre Complex, Brigade Gateway Campus, Dr. Rajkumar Road",
            "Malleshwaram West, Near Orion Mall, Bangalore North Taluk, Bangalore Urban District, Karnataka",
            "PIN: 560055",
        };
        var pdf = Renderer.Render(SampleDocuments.Build([SampleDocuments.Bevel()], buyerAddress: address));
        Save("longaddress.pdf", pdf);
        using var doc = Open(pdf);
        var page = doc.GetPage(1);
        var words = page.GetWords().ToList();
        var mid = page.Width / 2;
        foreach (var token in new[] { "Malleshwaram", "Rajkumar", "Taluk", "560055" })
        {
            var w = words.First(x => x.Text.Contains(token));
            Assert.True(w.BoundingBox.Right < mid, $"'{token}' spills into the right column");
        }
    }

    [Fact]
    public void Draft_watermark_is_printed_for_previews()
    {
        var pdf = Renderer.Render(SampleDocuments.Build([SampleDocuments.Bevel()], watermark: "DRAFT"));
        using var doc = Open(pdf);
        var letters = string.Concat(doc.GetPage(1).Letters.Select(l => l.Value));
        Assert.Contains("DRAFT", letters);
    }

    [Fact]
    public void Missing_optional_fields_are_simply_omitted()
    {
        var d = SampleDocuments.Build([SampleDocuments.Bevel()], pf: 0);
        var company = SampleDocuments.Company();
        company.MsmeNumber = "";
        company.Iec = "";
        company.BankName = "";
        company.BankAccountNumber = "";
        var doc2 = new QuotationDocument
        {
            Company = company, Number = d.Number, Date = d.Date, Buyer = d.Buyer, Consignee = d.Consignee, Lines = d.Lines,
            Subtotal = d.Subtotal, GrandTotal = d.Subtotal, TotalQuantity = 2, TotalQuantityUnit = "NOS", AmountInWords = "INR Thirty One Thousand Two Hundred Only",
        };
        using var doc = Open(Renderer.Render(doc2));
        var text = Text(doc.GetPage(1));
        Assert.DoesNotContain("MSME", text);
        Assert.DoesNotContain("Bank", text);
        Assert.DoesNotContain("Packing", text);
        Assert.Contains("31,200.00", text);
    }

    [Fact]
    public void Rendering_is_fast_enough()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 5; i++) Renderer.Render(SampleDocuments.Build(SampleDocuments.ManyLines(10)));
        Assert.True(sw.ElapsedMilliseconds / 5 < 1500, $"{sw.ElapsedMilliseconds / 5} ms per PDF");
    }
}
