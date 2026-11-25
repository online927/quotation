using Quotation.Core.Calculation;
using UglyToad.PdfPig;

namespace Quotation.Pdf.Tests;

/// <summary>Round 38: Zoho bold date and tighter lines (ZP-1), Minimal columns wrap (MN-1) and COMPANY column (MN-2).</summary>
public class Round38PdfTests
{
    private static PdfLine Line(int sl, string name, string[] desc, decimal rate) =>
        new(sl, name, desc, "90172020", 18, "", 1, "NOS", rate, 0, QuotationCalculator.LineAmount(1, rate, 0), "");

    [Fact]
    public void Zoho_date_is_bold_and_small_lines_sit_close_together()
    {
        var doc = SampleDocuments.Build([Line(1, "187-901-10-UNIVERSAL BEVEL PROTRACTOR", ["Brand: Mitutoyo", "Range: 0-360°", "Least count 5'"], 15600)]);
        using var pdf = PdfDocument.Open(new ModernPdfRenderer().Render(doc));
        var words = pdf.GetPage(1).GetWords().ToList();
        var date = words.First(w => w.Text.StartsWith("Date", StringComparison.Ordinal));
        Assert.Contains("Bold", date.Letters[0].FontName, StringComparison.OrdinalIgnoreCase);
        var brand = words.First(w => w.Text == "Brand:");
        var range = words.First(w => w.Text == "Range:");
        Assert.InRange(brand.BoundingBox.Bottom - range.BoundingBox.Bottom, 8.5, 10);   // was 12.3 pt
        var sub = words.First(w => w.Text == "Sub");
        var total = words.Last(w => w.Text == "Total");
        Assert.InRange(sub.BoundingBox.Bottom - total.BoundingBox.Bottom, 15, 40);      // P&F between: 2 × 16 pt (was 2 × 25.4)
    }

    [Fact]
    public void Minimal_long_names_wrap_inside_their_column_and_company_ids_have_their_own_column()
    {
        var doc = SampleDocuments.Build([Line(1, "KEI DIGITAL CALIPER 0-300MM", [], 21978)]);
        doc = doc with
        {
            Buyer = doc.Buyer with { Name = "TATA Electronics System Solutions Private Limited", Email = "purchase.department.pune@tataelectronics.example" },
            Consignee = doc.Consignee! with { Name = "Sonepar India Private Limited Chakan Warehouse Unit 2" },
        };
        using var pdf = PdfDocument.Open(new MinimalPdfRenderer().Render(doc));
        var words = pdf.GetPage(1).GetWords().ToList();
        var shipTo = words.First(w => w.Text == "SHIP");
        var reference = words.First(w => w.Text == "REFERENCE");
        // Nothing of the buyer column reaches the ship-to column, nothing of the ship-to column the references.
        var band = words.Where(w => w.BoundingBox.Top < shipTo.BoundingBox.Bottom && w.BoundingBox.Top > shipTo.BoundingBox.Bottom - 140).ToList();
        Assert.All(band.Where(w => w.BoundingBox.Left < shipTo.BoundingBox.Left - 1), w => Assert.True(w.BoundingBox.Right < shipTo.BoundingBox.Left, w.Text));
        Assert.All(band.Where(w => w.BoundingBox.Left >= shipTo.BoundingBox.Left - 1 && w.BoundingBox.Left < reference.BoundingBox.Left - 1),
            w => Assert.True(w.BoundingBox.Right < reference.BoundingBox.Left, w.Text));

        var company = words.First(w => w.Text == "COMPANY");
        var bank = words.First(w => w.Text == "BANK");
        Assert.True(company.BoundingBox.Left > bank.BoundingBox.Left + 50);
        var pan = words.First(w => w.Text == "PAN");
        Assert.True(Math.Abs(pan.BoundingBox.Left - company.BoundingBox.Left) < 1, "PAN sits in the COMPANY column");
    }
}
