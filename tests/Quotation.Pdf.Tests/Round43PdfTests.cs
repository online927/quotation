using UglyToad.PdfPig;

namespace Quotation.Pdf.Tests;

/// <summary>Round 43 (SG-1): the Zoho layout's "for COMPANY … Authorised Signatory" at the bottom right of the last page,
/// with the bank details in the middle between the company's numbers and the signatory.</summary>
public class Round43PdfTests
{
    [Fact]
    public void Zoho_last_page_has_the_signatory_bottom_right_and_the_bank_details_in_the_middle()
    {
        var doc = SampleDocuments.Build([SampleDocuments.Bevel()]);
        using var pdf = PdfDocument.Open(new ModernPdfRenderer().Render(doc));
        var words = pdf.GetPage(pdf.NumberOfPages).GetWords().ToList();
        var signatory = words.Last(w => w.Text == "Signatory");
        var authorised = words.Last(w => w.Text == "Authorised");
        var forWord = words.Last(w => w.Text == "for");
        Assert.True(signatory.BoundingBox.Right > 545);                                  // right-aligned to the amount column
        Assert.True(Math.Abs(signatory.BoundingBox.Bottom - authorised.BoundingBox.Bottom) < 1);
        Assert.True(forWord.BoundingBox.Bottom > signatory.BoundingBox.Bottom + 25);      // the company line above, room for the seal
        Assert.Contains("Bold", words[words.IndexOf(forWord) + 1].Letters[0].FontName, StringComparison.OrdinalIgnoreCase);
        var bank = words.Where(w => w.Text == "Bank").ToList();
        if (bank.Count > 0) Assert.True(bank.Max(b => b.BoundingBox.Right) < authorised.BoundingBox.Left);
        new PdfRenderTests().No_overlapping_words(doc);
    }
}
