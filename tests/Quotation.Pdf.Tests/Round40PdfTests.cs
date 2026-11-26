using UglyToad.PdfPig;

namespace Quotation.Pdf.Tests;

/// <summary>Round 40: the Zoho layout's date under the Ref No (QR-3), the terms rows under the ref lines (QR-2) and the
/// Zoho seal's own size and place (SL-1).</summary>
public class Round40PdfTests
{
    [Fact]
    public void Zoho_date_is_its_own_bold_line_under_the_ref_no_and_the_terms_rows_follow_the_ref_lines()
    {
        var doc = SampleDocuments.Build([SampleDocuments.Bevel()]);
        using var pdf = PdfDocument.Open(new ModernPdfRenderer().Render(doc));
        var words = pdf.GetPage(1).GetWords().ToList();
        var refNo = words.First(w => w.Text == "TSQ2526-3247");
        var date = words.First(w => w.Text == "Date:");
        var order = words.First(w => w.Text == "Order");
        var other = words.First(w => w.Text == "Other");
        Assert.Contains("Bold", date.Letters[0].FontName, StringComparison.OrdinalIgnoreCase);
        Assert.True(refNo.BoundingBox.Bottom > date.BoundingBox.Bottom && date.BoundingBox.Bottom > order.BoundingBox.Bottom); // Ref No, Date, Order No
        Assert.DoesNotContain(words, w => w.Text == "Date" || w.Text == "Date :");                                            // no "Date :" row any more
        var terms = words.First(w => w.Text == "Terms");
        var buyer = words.First(w => w.Text == "Buyer");
        Assert.True(terms.BoundingBox.Top < other.BoundingBox.Bottom);   // under the ref lines
        Assert.True(terms.BoundingBox.Bottom > buyer.BoundingBox.Bottom - 30); // beside the buyer, not far below it
        new PdfRenderTests().No_overlapping_words(doc);
    }

    [Fact]
    public void Zoho_seal_has_its_own_size_and_place_and_starts_from_the_tally_numbers()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAABgAAAAYCAYAAADgdz34AAAAJUlEQVR4nGM4ICDwn5aYYdSCUQtGLRi1YNSCUQtGLRi1YGhYAAC4lDX9H0TkSwAAAABJRU5ErkJggg==");
        var doc = SampleDocuments.Build([SampleDocuments.Bevel()]) with { Seal = png };
        doc.Company.SealWidth = 120;
        doc.Company.SealOffsetX = 10;
        Assert.Equal((120d, 0d, 0d), doc.Company.ZohoSeal()); // nothing of its own yet: the Tally width at the Zoho place
        var before = SealBox(new ModernPdfRenderer().Render(doc));
        Assert.Equal(120, before.Width, 0);
        Assert.True(before.Left > 595 / 2.0); // Round 41 (SL-2): at the bottom right, where the QR used to be

        doc.Company.ZohoSealWidth = 80;
        doc.Company.ZohoSealOffsetX = -300;
        doc.Company.ZohoSealOffsetY = -20;
        var after = SealBox(new ModernPdfRenderer().Render(doc));
        Assert.Equal(80, after.Width, 0);
        Assert.True(after.Left < before.Left - 250);    // moved far left
        Assert.True(after.Bottom > before.Bottom + 10); // and up
        // The Tally layout keeps the Tally numbers
        using var tally = PdfDocument.Open(new QuotationPdfRenderer().Render(doc));
        var image = tally.GetPage(tally.NumberOfPages).GetImages().Single();
        Assert.Equal(120, image.Bounds.Width, 0);
    }

    private static UglyToad.PdfPig.Core.PdfRectangle SealBox(byte[] bytes)
    {
        using var pdf = PdfDocument.Open(bytes);
        return pdf.GetPage(pdf.NumberOfPages).GetImages().Single(i => i.Bounds.Bottom < 200).Bounds;
    }
}
