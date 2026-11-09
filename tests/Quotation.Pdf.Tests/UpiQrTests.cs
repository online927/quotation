using Quotation.Core.Domain;
using UglyToad.PdfPig;

namespace Quotation.Pdf.Tests;

/// <summary>"Pay by UPI" QR code: quotations only, Tally layout in the right half of Terms of Delivery, Zoho at the bottom right.</summary>
public class UpiQrTests
{
    private static CompanySettings WithUpi(CompanySettings c, bool on = true)
    {
        c.UpiId = "tsaifuddin@okhdfcbank";
        c.UpiQrOnQuotations = on;
        return c;
    }

    private static QuotationDocument TallyQuotation(bool on = true) =>
        SampleDocuments.Build(SampleDocuments.ManyLines(3)) with { Company = WithUpi(SampleDocuments.Company(), on), TermsOfDelivery = "Within 2 weeks from PO" };

    [Fact]
    public void Link_has_the_upi_id_total_and_quotation_number()
    {
        var doc = TallyQuotation() with { Number = "TSQ2627-5", GrandTotal = 2537m };
        Assert.Equal("upi://pay?pa=tsaifuddin@okhdfcbank&pn=T.SAIFUDDIN%20%26%20CO.&cu=INR&am=2537.00&tn=TSQ2627-5", UpiQr.LinkFor(doc));
        Assert.Null(UpiQr.LinkFor(TallyQuotation(on: false)));
        Assert.Null(UpiQr.LinkFor(doc with { Kind = DocumentType.ProformaInvoice }));
        var noId = TallyQuotation();
        noId.Company.UpiId = " ";
        Assert.Null(UpiQr.LinkFor(noId));
    }

    [Fact]
    public void Tally_layout_prints_the_qr_in_the_right_half_of_terms_of_delivery()
    {
        var bytes = new QuotationPdfRenderer().Render(TallyQuotation());
        PdfRenderTests.Save("upi-qr-tally.pdf", bytes);
        using var pdf = PdfDocument.Open(bytes);
        var page = pdf.GetPage(1);
        var words = page.GetWords().ToList();
        var pay = words.First(w => w.Text == "Pay");
        var terms = words.First(w => w.Text == "Terms" && words.Any(o => o.Text == "Delivery" && Math.Abs(o.BoundingBox.Bottom - w.BoundingBox.Bottom) < 1));
        Assert.True(pay.BoundingBox.Left > terms.BoundingBox.Left + 120); // the right half, beside Terms of Delivery
        Assert.Contains(words, w => w.Text == "tsaifuddin@okhdfcbank");
        Assert.DoesNotContain(words, w => w.Text is "Scan" or "Note"); // only "Pay by UPI" and the UPI ID
        new PdfRenderTests().No_overlapping_words(TallyQuotation());

        var off = PdfDocument.Open(new QuotationPdfRenderer().Render(TallyQuotation(on: false)));
        Assert.DoesNotContain(off.GetPage(1).GetWords(), w => w.Text == "UPI");
        var proforma = PdfDocument.Open(new QuotationPdfRenderer().Render(TallyPrintLayoutTests.Proforma() with
        {
            Company = WithUpi(SampleDocuments.Company()),
        }));
        Assert.DoesNotContain(proforma.GetPage(1).GetWords(), w => w.Text == "UPI");
    }

    [Fact]
    public void Zoho_layout_prints_the_qr_at_the_bottom_right()
    {
        var doc = ModernLayoutTests.Sample() with { Kind = DocumentType.Quotation };
        WithUpi(doc.Company);
        var bytes = new ModernPdfRenderer().Render(doc);
        PdfRenderTests.Save("upi-qr-zoho.pdf", bytes);
        using var pdf = PdfDocument.Open(bytes);
        var page = pdf.GetPage(pdf.NumberOfPages);
        var words = page.GetWords().ToList();
        var pay = words.First(w => w.Text == "Pay");
        Assert.True(pay.BoundingBox.Left > page.Width * 0.7);  // right side
        Assert.True(pay.BoundingBox.Bottom < page.Height * 0.25); // near the bottom
        var conditions = words.First(w => w.Text == "Conditions");
        Assert.True(conditions.BoundingBox.Right < pay.BoundingBox.Left); // terms keep clear of it
        Assert.Contains(words, w => w.Text.Contains("tsaifuddin@okhdfcbank"));
    }
}
