using Quotation.Core.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Quotation.Pdf.Tests;

/// <summary>Round 25: the Zoho layout's foot (company numbers, bank details, QR), order number, GST column and page numbers.</summary>
public class ZohoFooterTests
{
    private static QuotationDocument Doc(bool qr = true)
    {
        var d = ModernLayoutTests.Sample() with
        {
            Kind = DocumentType.Quotation, BuyersReference = "PO/4471", BuyersReferenceDate = new DateOnly(2026, 10, 28),
        };
        var c = d.Company;
        c.Pan = "AAAFT0000A"; c.MsmeNumber = "UDYAM-KR-03-0000000"; c.Iec = "0000000000";
        c.BankAccountHolder = "T.SAIFUDDIN & CO"; c.BankName = "HDFC Bank Ltd"; c.BankAccountNumber = "50200012345678";
        c.BankBranch = "N.R. Road, Bangalore"; c.BankIfsc = "HDFC0000123";
        if (qr) { c.UpiId = "tsaifuddin@okhdfcbank"; c.UpiQrOnQuotations = true; }
        return d;
    }

    private static double Baseline(Page page, string word) =>
        QuotationPdfRenderer.A4Height - page.GetWords().First(w => w.Text == word).Letters[0].StartBaseLine.Y;

    [Fact]
    public void Company_numbers_left_bank_details_middle_qr_bottom_right_on_one_bottom_line()
    {
        var bytes = new ModernPdfRenderer().Render(Doc());
        PdfRenderTests.Save("zoho-footer.pdf", bytes);
        using var pdf = PdfDocument.Open(bytes);
        var page = pdf.GetPage(1);
        var words = page.GetWords().ToList();
        var text = string.Join(" ", words.Select(w => w.Text));
        Assert.DoesNotContain("Regards", text);
        foreach (var s in new[] { "AAAFT0000A", "UDYAM-KR-03-0000000", "50200012345678", "HDFC0000123", "tsaifuddin@okhdfcbank", "Order", "PO/4471" })
            Assert.Contains(s, text);

        var iec = words.First(w => w.Text == "0000000000");
        var ifsc = words.First(w => w.Text == "HDFC0000123");
        var upi = words.First(w => w.Text == "tsaifuddin@okhdfcbank");
        Assert.True(iec.BoundingBox.Right < words.First(w => w.Text == "A/c").BoundingBox.Left); // company numbers left of the bank details
        Assert.True(ifsc.BoundingBox.Right < upi.BoundingBox.Left);                             // bank details clear of the UPI ID
        Assert.True(upi.BoundingBox.Right > 540);                                               // UPI ID at the right
        Assert.Equal(Baseline(page, "0000000000"), Baseline(page, "HDFC0000123"), 0);            // last lines level
        Assert.Equal(Baseline(page, "HDFC0000123"), Baseline(page, "tsaifuddin@okhdfcbank"), 0);
        Assert.Contains("GST", words.Select(w => w.Text));
        Assert.Contains("18%", words.Select(w => w.Text));
        Assert.Contains("O.E", text);
    }

    [Fact]
    public void Pages_are_numbered_page_x_of_y_when_there_are_more_than_one()
    {
        using var one = PdfDocument.Open(new ModernPdfRenderer().Render(Doc(qr: false)));
        Assert.DoesNotContain(one.GetPage(1).GetWords(), w => w.Text == "Page");
        var many = Doc(qr: false) with { Lines = SampleDocuments.ManyLines(24) };
        using var pdf = PdfDocument.Open(new ModernPdfRenderer().Render(many));
        Assert.True(pdf.NumberOfPages > 1);
        var last = string.Join(" ", pdf.GetPage(pdf.NumberOfPages).GetWords().Select(w => w.Text));
        Assert.Contains($"Page {pdf.NumberOfPages} of {pdf.NumberOfPages}", last);
    }
}
