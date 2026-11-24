using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Round 37: Duplicate keeps rates; USD stays USD (copy, proforma); USD note choice; one catalogue once.</summary>
public class Round37Tests : QuotationTestBase
{
    private static string PdfWords(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", doc.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    private static int Pages(byte[] pdf)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return doc.NumberOfPages;
    }

    private async Task<QuotationDto> UsdAsync(decimal dollars = 195m)
    {
        await Api.SetMyPreferencesAsync(new MyPreferencesDto(UsdNext: true, UsdRate: 84.2m));
        var r = await BevelRequest();
        r.Currency = Currencies.Usd;
        r.ExchangeRate = 84.2m;
        r.Lines[0].Rate = dollars;
        return await Api.CreateQuotationAsync(r);
    }

    [Fact]
    public async Task Duplicate_copies_every_rate_as_it_is()
    {
        var r = await BevelRequest();
        r.Lines[0].Rate = 14200m;                                   // my own price (Tally 15,600)
        var q = await Api.CreateQuotationAsync(r);
        var copy = await Api.DuplicateQuotationAsync(q.Id);
        Assert.Equal(14200m, copy.Lines[0].Rate);
        Assert.Equal(q.GrandTotal, copy.GrandTotal);
    }

    [Fact]
    public async Task A_usd_quotation_forgets_the_rate_and_its_copy_and_proforma_stay_in_dollars()
    {
        var q = await UsdAsync();
        var me = await Api.MeAsync();
        Assert.False(me.UsdNext);
        Assert.Equal(0m, me.UsdRate);                                // not remembered

        var copy = await Api.DuplicateQuotationAsync(q.Id);
        Assert.True(copy.IsUsd);
        Assert.True(copy.WithoutGst);
        Assert.Equal(84.2m, copy.ExchangeRate);
        Assert.Equal(195m, copy.Lines[0].Rate);
        Assert.Equal(390m, copy.GrandTotal);

        var all = await Api.SettingsAsync();
        all.Quotation.ProformaEnabled = true;
        await Api.SaveSettingsAsync(all);
        await Api.ApproveQuotationAsync(q.Id);
        var pf = await Api.ConvertToProformaAsync(q.Id);
        Assert.Equal(DocumentType.ProformaInvoice, pf.DocType);
        Assert.True(pf.IsUsd);
        Assert.Equal(195m, pf.Lines[0].Rate);
        Assert.Equal(390m, pf.GrandTotal);
        Assert.Equal("USD Three Hundred Ninety Only", pf.AmountInWords);
        Assert.Empty(pf.Taxes);
    }

    [Fact]
    public async Task The_usd_note_on_the_pdf_follows_settings()
    {
        var q = await UsdAsync();
        await Api.ApproveQuotationAsync(q.Id);
        Assert.Contains("Amounts in US dollars (USD), without GST. 1 USD =", PdfWords(await Api.QuotationPdfAsync(q.Id)));

        var all = await Api.SettingsAsync();
        all.Quotation.UsdPdfNote = UsdPdfNotes.DollarsOnly;
        await Api.SaveSettingsAsync(all);
        var words = PdfWords(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("Amounts in US dollars (USD).", words);
        Assert.DoesNotContain("1 USD =", words);

        all.Quotation.UsdPdfNote = UsdPdfNotes.None;
        await Api.SaveSettingsAsync(all);
        words = PdfWords(await Api.QuotationPdfAsync(q.Id));
        Assert.DoesNotContain("Amounts in US dollars", words);
        Assert.Contains("$ 390.00", words);
    }

    [Fact]
    public async Task One_catalogue_on_several_items_is_added_once()
    {
        var r = await BevelRequest();
        var draft = await Api.CreateQuotationAsync(r);
        var catalogue = await Api.PreviewPdfAsync(draft.Id);           // a one-page PDF
        var bevel = r.Lines[0].ProductId!.Value;
        var other = (await Api.SearchProductsAsync("caliper")).First(h => h.Product.Id != bevel).Product;
        await Api.UploadDatasheetAsync(bevel, "Mitutoyo-catalogue.pdf", catalogue);
        await Api.UploadDatasheetAsync(other.Id, "same-catalogue-other-name.pdf", catalogue);

        r.Lines.Add(new QuotationLineDto { ProductId = other.Id, Quantity = 1, Rate = 1000m });
        var q = await Api.CreateQuotationAsync(r);
        await Api.ApproveQuotationAsync(q.Id);
        Assert.Equal(2, Pages(await Api.QuotationPdfAsync(q.Id)));    // the quotation + the catalogue once (not twice)
    }
}
