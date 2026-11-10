using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;

namespace Quotation.Server.Tests;

/// <summary>Pay by UPI settings; the QR is on quotation PDFs only.</summary>
public class Round17Tests : QuotationTestBase
{
    [Fact]
    public async Task Upi_id_is_checked_and_the_qr_is_printed_on_quotations()
    {
        await TestServer.EnableProformasAsync(Api);
        var settings = await Api.SettingsAsync();
        settings.Company.UpiQrOnQuotations = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() => Api.SaveSettingsAsync(settings))).Status);
        settings.Company.UpiId = "not a upi id";
        Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() => Api.SaveSettingsAsync(settings))).Status);
        settings.Company.UpiId = "tsaifuddin@okhdfcbank";
        await Api.SaveSettingsAsync(settings);

        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(await Api.QuotationPdfAsync(q.Id)))
            Assert.Contains(pdf.GetPage(1).GetWords(), w => w.Text == "tsaifuddin@okhdfcbank");
        var proforma = await Api.ConvertToProformaAsync(q.Id);
        await Api.ApproveQuotationAsync(proforma.Id);
        using (var pdf = UglyToad.PdfPig.PdfDocument.Open(await Api.QuotationPdfAsync(proforma.Id)))
            Assert.DoesNotContain(pdf.GetPage(1).GetWords(), w => w.Text == "tsaifuddin@okhdfcbank");
    }
}
