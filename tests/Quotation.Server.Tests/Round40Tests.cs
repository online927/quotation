using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 40: the Zoho seal's own size and place (SL-1); GST always computed and no default P&amp;F (ST-1).</summary>
public class Round40Tests : QuotationTestBase
{
    private static double SealWidth(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return doc.GetPage(doc.NumberOfPages).GetImages().Single().Bounds.Width;
    }

    private static string Words(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public async Task The_zoho_seal_has_its_own_size_and_place()
    {
        var ravi = await Api.CreateUserAsync(new CreateUserRequest("ravi", "Ravi", "ravi-pass", UserRole.User));
        await Api.UploadUserSealAsync(ravi.Id, Convert.FromBase64String(UsersDnuSealTests.Png));

        // At first the Zoho seal is where the Tally one is
        var company = (await Api.SettingsAsync()).Company;
        Assert.Null(company.ZohoSealWidth);
        Assert.Equal((company.SealWidth, 0d, 0d), company.ZohoSeal()); // the Tally width at the Zoho place (Round 41)

        // Moving the Tally seal keeps the Zoho seal where it was
        var before = company.SealWidth;
        await Api.SaveSealPositionAsync(new SealPositionDto(180, 10, -40));
        company = (await Api.SettingsAsync()).Company;
        Assert.Equal((180d, 10d, -40d), (company.SealWidth, company.SealOffsetX, company.SealOffsetY));
        Assert.Equal(before, company.ZohoSealWidth);

        // The Zoho one is saved on its own
        await Api.SaveSealPositionAsync(new SealPositionDto(90, 250, -20, PdfLayouts.Zoho));
        company = (await Api.SettingsAsync()).Company;
        Assert.Equal((90d, 250d, -20d), company.ZohoSeal());
        Assert.Equal(180, company.SealWidth);
        Assert.Contains(await Api.AuditAsync(), a => a.Action == "SealPositionChanged" && a.Details.StartsWith("Zoho", StringComparison.Ordinal));

        // Previews: a Zoho page with the Zoho numbers, a Tally page with the Tally ones
        var zoho = await Api.SealPreviewAsync(layout: PdfLayouts.Zoho);
        Assert.Equal(90, SealWidth(zoho), 0);
        Assert.Contains("Quotation/Proforma", Words(zoho));
        Assert.Equal(60, SealWidth(await Api.SealPreviewAsync(width: 60, layout: PdfLayouts.Zoho)), 0); // unsaved numbers
        Assert.Equal(180, SealWidth(await Api.SealPreviewAsync()), 0);

        // Settings → Save from a PC that does not send the Zoho numbers does not wipe them
        var all = await Api.SettingsAsync();
        all.Company.ZohoSealWidth = null;
        all.Company.ZohoSealOffsetX = null;
        all.Company.ZohoSealOffsetY = null;
        await Api.SaveSettingsAsync(all);
        Assert.Equal((90d, 250d, -20d), (await Api.SettingsAsync()).Company.ZohoSeal());
    }

    [Fact]
    public async Task Gst_is_always_computed_and_there_is_no_default_packing_and_forwarding()
    {
        var all = await Api.SettingsAsync();
        all.Quotation.TaxPresentation = TaxPresentation.RateOnly;
        all.Quotation.DefaultPackingForwardingAmount = 250;
        await Api.SaveSettingsAsync(all);
        var saved = (await Api.SettingsAsync()).Quotation;
        Assert.Equal(TaxPresentation.ComputeTax, saved.TaxPresentation);
        Assert.Equal(0, saved.DefaultPackingForwardingAmount);
    }
}
