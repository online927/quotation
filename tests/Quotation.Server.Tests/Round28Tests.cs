using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 28: the layout chosen on the Formater page is kept with the quotation; the XO game switch.</summary>
public class Round28Tests : QuotationTestBase
{
    public Round28Tests() => Server.AdministratorsMakeDocuments = false;

    private static SaveQuotationRequest Formater(string? layout) => new()
    {
        Date = DateOnly.FromDateTime(DateTime.Today),
        Buyer = new PartyDto { Name = "BuyersHub", Address = "Plot 27, Karmanghat\nHyderabad Telangana 500079", Gstin = "36AAZFB2304B1ZQ" },
        FromFormater = true,
        PdfLayout = layout,
        Lines = [new QuotationLineDto { ItemName = "Digital Caliper 150mm Special", GstRate = 18, Quantity = 2, Unit = "Nos", Rate = 3450 }],
    };

    [Fact]
    public async Task A_formater_quotation_prints_in_the_layout_chosen_on_the_page()
    {
        var ravi = await Server.LoginAsStandardAsync(); // ravi's own layout: Tally
        // Tally chosen: the preview, the approved PDF and the quotation all keep it.
        Assert.DoesNotContain("Quotation/Proforma", GstAndEditingRulesTests.PdfText(await ravi.PreviewUnsavedPdfAsync(Formater(PdfLayouts.Tally))));
        var tally = await ravi.CreateQuotationAsync(Formater(PdfLayouts.Tally));
        Assert.Equal(PdfLayouts.Tally, tally.PdfLayout);
        await ravi.ApproveQuotationAsync(tally.Id);
        Assert.DoesNotContain("Quotation/Proforma", GstAndEditingRulesTests.PdfText(await ravi.QuotationPdfAsync(tally.Id)));

        // Zoho chosen.
        var zoho = await ravi.CreateQuotationAsync(Formater(PdfLayouts.Zoho));
        await ravi.ApproveQuotationAsync(zoho.Id);
        Assert.Contains("Quotation/Proforma", GstAndEditingRulesTests.PdfText(await ravi.QuotationPdfAsync(zoho.Id)));

        // Made before the choice existed (no layout): Zoho, as before.
        var old = await ravi.CreateQuotationAsync(Formater(null));
        Assert.Null(old.PdfLayout);
        Assert.Contains("Quotation/Proforma", GstAndEditingRulesTests.PdfText(await ravi.PreviewPdfAsync(old.Id)));
    }

    [Fact]
    public async Task The_xo_game_is_an_administrator_switch_off_at_first()
    {
        var all = await Api.SettingsAsync();
        Assert.False(all.Quotation.XoGame);
        all.Quotation.XoGame = true;
        await Api.SaveSettingsAsync(all);
        var ravi = await Server.LoginAsStandardAsync();
        Assert.True((await ravi.SettingsAsync()).Quotation.XoGame);
    }
}
