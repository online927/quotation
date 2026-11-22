using Quotation.Core.Calculation;
using Quotation.Core.Domain;
namespace Quotation.Pdf.Tests;
/// <summary>Round 27: when the items go on to a second page, the first page is filled (not left a quarter empty).</summary>
public class PageFillTests
{
    public static QuotationDocument Doc()
    {
        var items = new (string Name, string Desc, decimal Qty, decimal Rate)[]
        {
            ("Tools (Nos)", "Kristeel SPG", 1, 477), ("500-197-30-DIGIMATIC CALIPER", "Mitutoyo", 1, 8800), ("Tools (Nos)", "Round Die 5/8\"", 1, 1700),
            ("TAP (NOS)", "Tap 5/8 BSP", 1, 6188), ("103-137-OUTSIDE MICROMETER", "Analog 0-25mm|Mitutoyo", 1, 2350), ("Screws New", "Allen Bolt 12x100", 12, 74.15m),
            ("Screws New", "Allen Bolt 12x75", 12, 47.45m), ("Screws New", "Allen Bolt 10x100", 12, 64.65m), ("Screws New", "Allen Bolt 10x75", 12, 31.35m),
            ("1501-Degree Protractor (D Head)", "Kristeel", 1, 375), ("GS File", "Black Decker Needle File", 1, 600), ("Tools (Nos)", "Panther Back Plate 1650-2", 1, 2850),
            ("Tools (Nos)", "200 Back Plate", 1, 2850), ("Tools (Nos)", "200 MTSJ3-1", 1, 18915), ("Jaws (New)", "Unnath 160 SJ3 Forward Jaw", 1, 2750),
            ("Jaws (New)", "Unnath 160 SJ3 Reverse Jaw", 1, 2750), ("Jaws (New)", "Unnath 160 SJ3 Master Jaw", 1, 2805),
        };
        var lines = items.Select((x, i) => new PdfLine(i + 1, x.Name, x.Desc.Split('|'), "82051000", 18, "", x.Qty, "NOS", x.Rate, 0,
            QuotationCalculator.LineAmount(x.Qty, x.Rate, 0))).ToList();
        return SampleDocuments.Build(lines, pf: 0);
    }

    [Fact]
    public void The_first_page_is_filled_and_only_the_items_that_do_not_fit_go_on()
    {
        var pdf = new QuotationPdfRenderer().Render(Doc());
        PdfRenderTests.Save("tally-page-fill.pdf", pdf);
        using var p = UglyToad.PdfPig.PdfDocument.Open(pdf);
        Assert.Equal(2, p.NumberOfPages);
        var first = string.Join(" ", p.GetPage(1).GetWords().Select(w => w.Text));
        var second = string.Join(" ", p.GetPage(2).GetWords().Select(w => w.Text));
        Assert.Contains("Reverse Jaw", first); // item 16 of 17 on page 1 (before: only 11)
        Assert.Contains("Master Jaw", second);
        Assert.DoesNotContain("Reverse Jaw", second);

        var zoho = new ModernPdfRenderer().Render(Doc());
        using var z = UglyToad.PdfPig.PdfDocument.Open(zoho);
        var zFirst = string.Join(" ", z.GetPage(1).GetWords().Select(w => w.Text));
        var zLast = string.Join(" ", z.GetPage(z.NumberOfPages).GetWords().Select(w => w.Text));
        Assert.Contains("Allen Bolt 10x75", zFirst); // item 9 (the Dispatch / Destination / Delivery rows of Round 35 take some room)
        Assert.Contains("Master Jaw", zLast);
    }
}
