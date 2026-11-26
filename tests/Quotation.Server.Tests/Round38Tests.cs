using System.Text;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 38: product links (PL-1), delivery never remembered (DM-1), a create sent twice makes one quotation (Q10),
/// Zoho bank rows (ZP-1).</summary>
public class Round38Tests : QuotationTestBase
{
    private static string PdfWords(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private static bool HasWebLink(byte[] pdf, string url) => Encoding.Latin1.GetString(pdf).Contains(url, StringComparison.Ordinal);

    [Fact]
    public async Task Product_links_load_from_rows_show_on_items_and_print_as_a_clickable_line()
    {
        const string url = "https://www.example.com/products/bevel-protractor";
        var result = await Api.LoadProductLinksAsync(
        [
            new ProductLinkRowDto("187-901-10", url),
            new ProductLinkRowDto("NO-SUCH-CODE-77", "https://www.example.com/x"),
            new ProductLinkRowDto("112-201", "ftp://example.com/not-a-web-page"),
        ]);
        Assert.Equal(3, result.Rows);
        Assert.Equal(1, result.Linked);
        Assert.Equal(["NO-SUCH-CODE-77"], result.NotFound);
        Assert.Equal(["112-201"], result.BadLinks);

        // The item carries its link in searches and on the quotation's lines.
        Assert.Equal(url, (await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR")).Link);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        Assert.True(q.ShowProductLinks);
        Assert.Equal(url, q.Lines[0].Link);

        // Every layout prints "View product" with a real web link.
        foreach (var layout in new[] { PdfLayouts.Tally, PdfLayouts.Zoho, PdfLayouts.Minimal })
        {
            var pdf = await Api.QuotationPdfAsync(q.Id, layout: layout);
            Assert.Contains("View product", PdfWords(pdf));
            Assert.True(HasWebLink(pdf, url), layout);
        }

        // Round 39 (GR-2): the maker's own switch decides (the quotation's tick and the Settings switch are gone).
        Api.UpdateCurrentUser(await Api.SetMyPreferencesAsync(new MyPreferencesDto(ProductLinksOnPdf: false)));
        Assert.DoesNotContain("View product", PdfWords(await Api.QuotationPdfAsync(q.Id, layout: PdfLayouts.Zoho)));
        Api.UpdateCurrentUser(await Api.SetMyPreferencesAsync(new MyPreferencesDto(ProductLinksOnPdf: true)));
        var all = await Api.SettingsAsync();
        all.Quotation.ProductLinksOnPdf = false; // ignored now
        await Api.SaveSettingsAsync(all);
        Assert.Contains("View product", PdfWords(await Api.QuotationPdfAsync(q.Id, layout: PdfLayouts.Tally)));
    }

    [Fact]
    public async Task A_delivery_line_typed_in_the_description_is_not_remembered()
    {
        var r = await BevelRequest();
        r.Lines[0].Description = "Brand: Mitutoyo\nDelivery: 2-3 weeks\nEx-stock";
        await Api.CreateQuotationAsync(r);
        var offered = await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        Assert.Equal("Brand: Mitutoyo", offered.Description);
    }

    [Fact]
    public async Task A_new_quotation_sent_again_with_the_same_key_is_made_once()
    {
        var r = await BevelRequest();
        r.ClientKey = Guid.NewGuid();
        var first = await Api.CreateQuotationAsync(r);
        var again = await Api.CreateQuotationAsync(r);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.Number, again.Number);

        r.ClientKey = Guid.NewGuid();
        Assert.NotEqual(first.Id, (await Api.CreateQuotationAsync(r)).Id);
    }

    [Fact]
    public async Task Zoho_pdf_leaves_out_an_empty_bank_row()
    {
        var all = await Api.SettingsAsync();
        all.Company.BankName = "Example Bank Ltd";
        all.Company.BankAccountNumber = "000000000000";
        all.Company.BankAccountHolder = "";
        await Api.SaveSettingsAsync(all);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        var words = PdfWords(await Api.QuotationPdfAsync(q.Id, layout: PdfLayouts.Zoho));
        Assert.Contains("Example", words);
        Assert.DoesNotContain("Holder's", words);
    }
}
