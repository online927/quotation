using Quotation.Core.Domain;
using Quotation.Data.Entities;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>Settings → Party Details: the buyer and consignee printed as Tally's "Party Details" print options.</summary>
public class PartyPrintTests : QuotationTestBase
{
    private static readonly PartySnapshot Typed = new()
    {
        Name = "Sonepar India Private Limited", Address = "Plot No. 12, MIDC Industrial Area\nChakan, Pune\nPIN: 410501",
        Gstin = "27AAACS1234F1Z3", StateName = "Maharashtra", StateCode = "27",
        ContactPerson = "Mr. Rahul Deshmukh", Phone = "9822012345", Email = "purchase.pune@sonepar.example",
    };

    private static readonly PartyMaster Ledger = new("Sonepar India Pvt Ltd", "SONEPAR-PUNE",
        ["Plot 12, MIDC", "Chakan"], "410501", "AAACS1234F");

    [Fact]
    public void Defaults_match_the_office_Tally_settings()
    {
        var p = PdfQuotationRenderer.ToParty(Typed, new PartyPrintSettings(), Ledger);
        Assert.Equal("Sonepar India Private Limited", p.Name); // as typed, Name Only
        Assert.Equal(["Plot No. 12, MIDC Industrial Area", "Chakan, Pune - 410501"], p.AddressLines); // PIN joined, as Tally
        Assert.Equal("", p.ContactPerson);
        Assert.Equal("9822012345", p.Phone);
        Assert.Equal("purchase.pune@sonepar.example", p.Email);
        Assert.Equal("27AAACS1234F1Z3", p.Gstin);
        Assert.Equal("Maharashtra", p.StateName);
        Assert.Equal("", p.Pan);
    }

    [Fact]
    public void Each_option_changes_the_party_block()
    {
        var p = PdfQuotationRenderer.ToParty(Typed, new PartyPrintSettings
        {
            ShowContactPerson = true, ShowPan = true, AddressInContinuousLine = true, DisplayName = PartyDisplayName.NameAndAlias,
        }, Ledger);
        Assert.Equal("Mr. Rahul Deshmukh", p.ContactPerson);
        Assert.Equal("AAACS1234F", p.Pan);
        Assert.Equal(["Plot No. 12, MIDC Industrial Area, Chakan, Pune - 410501"], p.AddressLines);
        Assert.Equal("Sonepar India Private Limited (SONEPAR-PUNE)", p.Name);

        p = PdfQuotationRenderer.ToParty(Typed, new PartyPrintSettings
        {
            ShowContactDetails = false, ShowContactPerson = true, ShowGstin = false, ShowStateNameAndCode = false, ShowPinCode = false,
            DisplayName = PartyDisplayName.AliasOnly,
        }, Ledger);
        Assert.Equal(("", "", ""), (p.ContactPerson, p.Phone, p.Email));
        Assert.Equal(("", "", ""), (p.Gstin, p.StateName, p.StateCode));
        Assert.Equal(["Plot No. 12, MIDC Industrial Area", "Chakan, Pune"], p.AddressLines);
        Assert.Equal("SONEPAR-PUNE", p.Name);

        p = PdfQuotationRenderer.ToParty(Typed, new PartyPrintSettings { NameAndAddressFromMaster = true, DisplayName = PartyDisplayName.AliasAndName }, Ledger);
        Assert.Equal("SONEPAR-PUNE (Sonepar India Pvt Ltd)", p.Name);
        Assert.Equal(["Plot 12, MIDC", "Chakan - 410501"], p.AddressLines);
    }

    [Fact]
    public void Pin_typed_in_the_address_is_not_repeated_and_can_be_left_out()
    {
        var typed = new PartySnapshot { Name = "GUTSLANE PRECITECH", Address = "Plot No.238~240, Road No.10,\nNelamangala Taluk, Bangalore - 562111" };
        var ledger = Ledger with { Pincode = "562111" };
        Assert.Equal("Nelamangala Taluk, Bangalore - 562111",
            PdfQuotationRenderer.ToParty(typed, new PartyPrintSettings(), ledger).AddressLines[^1]);
        Assert.Equal("Nelamangala Taluk, Bangalore",
            PdfQuotationRenderer.ToParty(typed, new PartyPrintSettings { ShowPinCode = false }, ledger).AddressLines[^1]);

        // A changed address does not get the ledger's PIN; the Tally address unchanged does.
        var other = new PartySnapshot { Name = "X", Address = "Some other place" };
        Assert.Equal(["Some other place"], PdfQuotationRenderer.ToParty(other, new PartyPrintSettings(), Ledger).AddressLines);
        var asTally = new PartySnapshot { Name = "X", Address = "Plot 12, MIDC\nChakan" };
        Assert.Equal(["Plot 12, MIDC", "Chakan - 410501"], PdfQuotationRenderer.ToParty(asTally, new PartyPrintSettings(), Ledger).AddressLines);
    }

    [Fact]
    public async Task Z_prints_the_modern_layout_with_discount_hsn_and_the_sign_off()
    {
        var settings = await Api.SettingsAsync();
        settings.Quotation.ModernHeading = "Quotation/Proforma";
        settings.Quotation.ModernSignOff = ["Regards,", "{name}", "Tsaifuddin and Company"];
        await Api.SaveSettingsAsync(settings);

        var request = await BevelRequest();
        request.Lines[0].DiscountPercent = 10;
        request.PreparedBy = "Keerthana";
        var q = await Api.CreateQuotationAsync(request);
        await Api.ApproveQuotationAsync(q.Id, overrideStaleData: true);

        var z = GstAndEditingRulesTests.PdfText(await Api.QuotationPdfAsync(q.Id, modern: true));
        Assert.Contains("Quotation/Proforma", z);
        Assert.Contains($"Ref No. {q.Number}", z);
        Assert.Contains("Disc %", z);
        Assert.Contains("10%", z);
        Assert.Contains("90172020", z);                 // HSN/SAC
        Assert.Contains("Part No. 187-901-10", z);      // the Part No. column is on by default
        Assert.Contains("Buyer/Consignee", z);
        Assert.Contains("Sub Total", z);
        Assert.Matches(@"CGST9 \(9%\)|IGST18 \(18%\)", z); // tax lines by rate
        Assert.Contains("Keerthana", z);                // {name} = prepared by
        Assert.DoesNotContain("Amount Chargeable", z);  // not the Tally style

        var tally = GstAndEditingRulesTests.PdfText(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("Amount Chargeable", tally);    // the normal button is unchanged
        Assert.DoesNotContain("Quotation/Proforma", tally);
    }

    [Fact]
    public async Task Pdf_headings_are_set_in_settings()
    {
        var settings = await Api.SettingsAsync();
        settings.Quotation.QuotationHeading = "PRICE OFFER";
        settings.Quotation.ProformaHeading = "  ";
        await Api.SaveSettingsAsync(settings);

        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id, overrideStaleData: true);
        Assert.Contains("PRICE OFFER", GstAndEditingRulesTests.PdfText(await Api.QuotationPdfAsync(q.Id)));
        var pi = await Api.ConvertToProformaAsync(q.Id);
        await Api.ApproveQuotationAsync(pi.Id, overrideStaleData: true);
        var text = GstAndEditingRulesTests.PdfText(await Api.QuotationPdfAsync(pi.Id));
        Assert.Contains("PROFORMA INVOICE", text); // empty = the standard heading
        Assert.DoesNotContain("PRICE OFFER", text);

        var preview = GstAndEditingRulesTests.PdfText(await Api.PrintPreviewAsync(new QuotationSettings { QuotationHeading = "Quotation Cum Offer" }));
        Assert.Contains("Quotation Cum Offer", preview); // as typed, before saving
    }

    [Fact]
    public void Half_width_address_is_passed_to_the_pdf()
    {
        Assert.False(PdfQuotationRenderer.ToParty(Typed, new PartyPrintSettings(), Ledger).AddressHalfWidth);
        Assert.True(PdfQuotationRenderer.ToParty(Typed, new PartyPrintSettings { AddressHalfWidth = true }, Ledger).AddressHalfWidth);
    }

    [Theory]
    [InlineData(ConsigneePrint.Yes, true, true, "Buyer (Bill to)")]
    [InlineData(ConsigneePrint.No, true, false, "Buyer (Bill to)")]
    [InlineData(ConsigneePrint.OnlyWhenDifferent, true, false, "Buyer (Bill to)")]
    [InlineData(ConsigneePrint.CombinedWhenSame, true, false, "Buyer/Consignee")]
    [InlineData(ConsigneePrint.Yes, false, true, "Buyer (Bill to)")]
    [InlineData(ConsigneePrint.No, false, false, "Buyer (Bill to)")]
    [InlineData(ConsigneePrint.OnlyWhenDifferent, false, true, "Buyer (Bill to)")]
    [InlineData(ConsigneePrint.CombinedWhenSame, false, true, "Buyer (Bill to)")]
    public void Consignee_block_follows_the_setting(ConsigneePrint choice, bool same, bool consigneeBlock, string buyerCaption)
    {
        var q = new QuotationHeader
        {
            Number = "X", Buyer = Typed, ConsigneeSameAsBuyer = same,
            Consignee = same ? new PartySnapshot() : new PartySnapshot { Name = "Sonepar Warehouse", Address = "Peenya, Bangalore" },
        };
        var settings = new QuotationSettings { PartyPrint = new PartyPrintSettings { Consignee = choice } };
        var doc = new PdfQuotationRenderer(Microsoft.Extensions.Logging.Abstractions.NullLogger<PdfQuotationRenderer>.Instance)
            .ToDocument(q, new CompanySettings(), settings, watermark: null);
        Assert.Equal(consigneeBlock, doc.Consignee is not null);
        Assert.Equal(buyerCaption, doc.BuyerCaption);
        Assert.True(doc.ShrinkToFitOnePage); // on by default
    }

    [Fact]
    public void A_consignee_typed_like_the_buyer_counts_as_the_same_and_old_no_setting_is_kept()
    {
        var q = new QuotationHeader
        {
            Buyer = Typed, ConsigneeSameAsBuyer = false,
            Consignee = new PartySnapshot { Name = "SONEPAR INDIA PRIVATE LIMITED", Address = "Plot No. 12, MIDC Industrial Area,\nChakan, Pune\nPIN: 410501" },
        };
        Assert.True(PdfQuotationRenderer.SameParty(q));
        Assert.False(PdfQuotationRenderer.ShowsConsignee(q, new PartyPrintSettings()));
        // Settings saved by the previous version with "Show Consignee details" off.
        Assert.Equal(ConsigneePrint.No, new PartyPrintSettings { ShowConsignee = false, Consignee = ConsigneePrint.Yes }.EffectiveConsignee);
    }

    [Fact]
    public async Task Saved_options_are_used_on_the_pdf_and_the_preview()
    {
        var settings = await Api.SettingsAsync();
        settings.Quotation.PartyPrint.ShowPan = true;
        settings.Quotation.PartyPrint.Consignee = ConsigneePrint.No;
        await Api.SaveSettingsAsync(settings);

        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id, overrideStaleData: true);
        var text = GstAndEditingRulesTests.PdfText(await Api.QuotationPdfAsync(q.Id));
        Assert.Contains("Chakan, Pune - 410501", text); // the ledger's PIN: the address is as in Tally
        Assert.Contains("PAN/IT No : AAACS1234F", text); // PAN from the Tally ledger
        Assert.Contains("9822012345", text);
        Assert.DoesNotContain("Rahul", text);
        Assert.DoesNotContain("Consignee", text);

        // The preview uses the options on screen, before they are saved.
        var preview = GstAndEditingRulesTests.PdfText(await Api.PrintPreviewAsync(new QuotationSettings { PartyPrint = new PartyPrintSettings { ShowContactPerson = true } }));
        Assert.Contains("Rahul", preview);
        Assert.Contains("Consignee", preview);
        Assert.DoesNotContain("PAN/IT No", preview);
    }
}
