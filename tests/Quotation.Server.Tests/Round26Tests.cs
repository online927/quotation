using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 26: quotations from the Formater page may have a typed customer and typed items (not in Tally).</summary>
public class Round26Tests : QuotationTestBase
{
    public Round26Tests() => Server.AdministratorsMakeDocuments = false;

    private static SaveQuotationRequest Typed(bool fromFormater) => new()
    {
        Date = DateOnly.FromDateTime(DateTime.Today),
        Buyer = new PartyDto { Name = "BuyersHub", Address = "Plot 27, Karmanghat\nHyderabad Telangana 500079", Gstin = "36AAZFB2304B1ZQ" },
        FromFormater = fromFormater,
        Lines = [new QuotationLineDto { ItemName = "Digital Caliper 150mm Special", Description = "Make - Insize", GstRate = 18, Quantity = 2, Unit = "Nos", Rate = 3450 }],
    };

    [Fact]
    public async Task Formater_quotations_with_a_typed_customer_and_item_are_approved_and_printed_in_the_zoho_layout()
    {
        var all = await Api.SettingsAsync();
        all.Company.StateName = "Karnataka";
        all.Company.StateCode = "29";
        await Api.SaveSettingsAsync(all);
        var ravi = await Server.LoginAsStandardAsync();
        var q = await ravi.CreateQuotationAsync(Typed(fromFormater: true));
        Assert.Equal(QuotationSource.Formater, q.Source);
        Assert.Equal(QuotationStatus.Draft, q.Status);
        Assert.Equal("36", q.Buyer.StateCode); // from the GSTIN

        // Preview of the unsaved text: no number used.
        var preview = await ravi.PreviewUnsavedPdfAsync(Typed(fromFormater: true));
        Assert.Contains("Quotation/Proforma", GstAndEditingRulesTests.PdfText(preview)); // the Zoho layout

        var approved = await ravi.ApproveQuotationAsync(q.Id);
        Assert.Equal(QuotationStatus.Generated, approved.Status);
        Assert.Contains("IGST", string.Join(" ", approved.Taxes.Select(t => t.Name))); // Telangana: another state
        var pdf = GstAndEditingRulesTests.PdfText(await ravi.QuotationPdfAsync(q.Id));
        Assert.Contains("Quotation/Proforma", pdf);
        Assert.Contains("BuyersHub", pdf);
        Assert.Contains("Digital Caliper 150mm Special", pdf);
    }

    [Fact]
    public async Task Ordinary_quotations_still_need_a_tally_customer_and_items()
    {
        var ravi = await Server.LoginAsStandardAsync();
        var q = await ravi.CreateQuotationAsync(Typed(fromFormater: false));
        var refused = await Assert.ThrowsAsync<ApiException>(() => ravi.ApproveQuotationAsync(q.Id));
        Assert.Contains("Tally", refused.Message + string.Join(" ", refused.Details));
    }
}
