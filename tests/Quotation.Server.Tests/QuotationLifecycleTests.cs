using System.Net;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

public class QuotationLifecycleTests : QuotationTestBase
{
    [Fact]
    public async Task Tally_values_are_authoritative_over_client_values()
    {
        var request = await BevelRequest();
        request.Buyer.Gstin = "29FAKE0000F1Z0";   // client tries to change the GSTIN
        request.Lines[0].ItemName = "Something else";
        request.Lines[0].Hsn = "1234";
        request.Lines[0].GstRate = 5;
        request.Lines[0].Unit = "BOX";
        var q = await Api.CreateQuotationAsync(request);

        Assert.Equal("27AAACS1234F1Z3", q.Buyer.Gstin);
        Assert.Equal("27", q.Buyer.StateCode);
        Assert.Equal("Sonepar India Private Limited", q.Buyer.Name);
        Assert.Contains("Chakan, Pune", q.Buyer.Address);
        var line = q.Lines[0];
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", line.ItemName);
        Assert.Equal("90172020", line.Hsn);
        Assert.Equal(18m, line.GstRate);
        Assert.Equal("NOS", line.Unit);
        Assert.Equal(15600m, line.TallyRate);
        Assert.Equal(31200m, line.Amount);
        Assert.Equal(31200m, q.GrandTotal);
        Assert.Equal("INR Thirty One Thousand Two Hundred Only", q.AmountInWords);
        Assert.True(q.ConsigneeSameAsBuyer);
        Assert.Equal(q.Buyer.Name, q.Consignee.Name);
    }

    [Fact]
    public async Task Manual_rate_override_and_discount()
    {
        var request = await BevelRequest(qty: 3);
        request.Lines[0].Rate = 15000;
        request.Lines[0].DiscountPercent = 5;
        request.PackingForwarding = 250;
        var q = await Api.CreateQuotationAsync(request);
        Assert.Equal(42750m, q.Lines[0].Amount);
        Assert.Equal(15600m, q.Lines[0].TallyRate);
        Assert.Equal(43000m, q.GrandTotal);
    }

    [Fact]
    public async Task Approval_validates_and_is_kept_even_if_pdf_fails()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);
        // The PDF engine is not part of this phase: approval must still be saved with an error recorded.
        Assert.Equal(QuotationStatus.Approved, approved.Status);
        Assert.Equal("admin", approved.ApprovedBy);
        Assert.NotNull(approved.PdfError);
        Assert.Equal(q.Number, approved.Number);
    }

    [Fact]
    public async Task Invalid_quotation_cannot_be_approved()
    {
        var request = await BevelRequest();
        request.Lines[0].Quantity = 0;
        var q = await Api.CreateQuotationAsync(request); // drafts may be incomplete
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(q.Id));
        Assert.Equal(ApiErrorCodes.Validation, ex.Code);
        Assert.Contains(ex.Details, d => d.Contains("Quantity must be greater than zero"));
        Assert.Equal(QuotationStatus.Draft, (await Api.QuotationAsync(q.Id)).Status);
    }

    [Fact]
    public async Task Product_without_tally_rate_requires_user_rate()
    {
        var customer = await Customer("SONEPAR INDIA PRIVATE LIMITED");
        var product = await Product("CARBIDE END MILL 10MM 4 FLUTE");
        Assert.Null(product.Rate);
        var q = await Api.CreateQuotationAsync(new SaveQuotationRequest
        {
            Date = Today, CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 1, Rate = 0 }],
        });
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(q.Id));
        Assert.Contains(ex.Details, d => d.Contains("Rate must be greater than zero"));
    }

    [Fact]
    public async Task Stale_data_requires_explicit_override_which_is_recorded()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Server.Tally.Online = false;
        await Api.TestTallyAsync(); // server notices Tally is down

        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(q.Id));
        Assert.Equal(ApiErrorCodes.StaleData, ex.Code);

        var approved = await Api.ApproveQuotationAsync(q.Id, overrideStaleData: true);
        Assert.Equal(QuotationStatus.Approved, approved.Status);
        Assert.Contains("Live Tally data could not be retrieved", approved.DataFreshnessWarning);
        var audit = await Api.AuditAsync("Quotation", q.Id.ToString());
        Assert.Contains(audit, a => a.Action == "QuotationApprovedWithStaleData");
    }

    [Fact]
    public async Task Stale_override_can_be_disabled()
    {
        var settings = await Api.SettingsAsync();
        settings.Quotation.AllowStaleDataOverride = false;
        await Api.SaveSettingsAsync(settings);
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Server.Tally.Online = false;
        await Api.TestTallyAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(q.Id, overrideStaleData: true));
        Assert.Equal(ApiErrorCodes.StaleData, ex.Code);
    }

    [Fact]
    public async Task Tally_rate_change_before_approval_is_applied_and_must_be_reviewed()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Server.Tally.ChangeRate("187-901-10-UNIVERSAL BEVEL PROTRACTOR", 16400m, Today.AddDays(-1));

        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(q.Id));
        Assert.Equal(ApiErrorCodes.RatesChanged, ex.Code);
        Assert.Contains(ex.Details, d => d.Contains("15600.00 to 16400.00"));

        var updated = await Api.QuotationAsync(q.Id);
        Assert.Equal(16400m, updated.Lines[0].Rate);
        Assert.Equal(32800m, updated.GrandTotal);
        var approved = await Api.ApproveQuotationAsync(q.Id);
        Assert.Equal(QuotationStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Overridden_rate_is_kept_when_tally_rate_changes()
    {
        var request = await BevelRequest();
        request.Lines[0].Rate = 15000;
        var q = await Api.CreateQuotationAsync(request);
        Server.Tally.ChangeRate("187-901-10-UNIVERSAL BEVEL PROTRACTOR", 16400m, Today.AddDays(-1));
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.ApproveQuotationAsync(q.Id));
        Assert.Contains(ex.Details, d => d.Contains("your rate 15000.00 kept"));
        Assert.Equal(15000m, (await Api.QuotationAsync(q.Id)).Lines[0].Rate);
    }

    [Fact]
    public async Task Concurrent_edits_are_detected()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var pc1 = await Server.LoginAsAdminAsync("PC1");
        var pc2 = await Server.LoginAsAdminAsync("PC2");
        var a = await pc1.QuotationAsync(q.Id);
        var b = await pc2.QuotationAsync(q.Id);

        var ra = ToRequest(a); ra.Remarks = "from PC1";
        await pc1.UpdateQuotationAsync(q.Id, ra);
        var rb = ToRequest(b); rb.Remarks = "from PC2";
        var ex = await Assert.ThrowsAsync<ApiException>(() => pc2.UpdateQuotationAsync(q.Id, rb));
        Assert.Equal(HttpStatusCode.Conflict, ex.Status);
        Assert.Equal(ApiErrorCodes.Concurrency, ex.Code);
        Assert.Equal("from PC1", (await Api.QuotationAsync(q.Id)).Remarks);
    }

    [Fact]
    public async Task Editing_an_approved_quotation_returns_it_to_draft()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        var approved = await Api.ApproveQuotationAsync(q.Id);
        var r = ToRequest(approved);
        r.Lines[0].Quantity = 4;
        var edited = await Api.UpdateQuotationAsync(q.Id, r);
        Assert.Equal(QuotationStatus.Draft, edited.Status);
        Assert.Null(edited.ApprovedBy);
        Assert.Equal(q.Number, edited.Number);
        Assert.Equal(62400m, edited.GrandTotal);
    }

    [Fact]
    public async Task Duplicate_creates_new_number_today_with_current_rates()
    {
        var request = await BevelRequest();
        request.BuyersReference = "PO-778";
        var original = await Api.CreateQuotationAsync(request);
        Server.Tally.ChangeRate("187-901-10-UNIVERSAL BEVEL PROTRACTOR", 16400m, Today.AddDays(-1));
        await Api.StartSyncAsync(SyncKind.Incremental, wait: true);

        var copy = await Api.DuplicateQuotationAsync(original.Id);
        Assert.NotEqual(original.Number, copy.Number);
        Assert.Equal(Today, copy.Date);
        Assert.Equal(original.CustomerId, copy.CustomerId);
        Assert.Equal(original.Lines[0].ProductId, copy.Lines[0].ProductId);
        Assert.Equal("Brand: Mitutoyo\nMOQ: 1", copy.Lines[0].Description);
        Assert.Equal(16400m, copy.Lines[0].Rate);
        Assert.Equal("", copy.BuyersReference);
        Assert.Equal(original.Id, copy.DuplicatedFromId);
    }

    [Fact]
    public async Task Separate_consignee_and_interstate_tax()
    {
        var settings = await Api.SettingsAsync();
        settings.Company.StateCode = "29";
        settings.Quotation.TaxPresentation = TaxPresentation.ComputeTax;
        await Api.SaveSettingsAsync(settings);

        var request = await BevelRequest();
        request.ConsigneeSameAsBuyer = false;
        request.Consignee = new PartyDto { Name = "Sonepar Bangalore Warehouse", Address = "Peenya", StateName = "Karnataka" };
        var q = await Api.CreateQuotationAsync(request);
        Assert.Equal("29", q.Consignee.StateCode);
        Assert.Equal(["CGST", "SGST"], q.Taxes.Select(t => t.Name));        // delivered within Karnataka

        request.ConsigneeSameAsBuyer = true;                                 // delivered to Pune
        var q2 = await Api.CreateQuotationAsync(request);
        Assert.Equal(["IGST"], q2.Taxes.Select(t => t.Name));
        Assert.Equal(31200m + 5616m, q2.GrandTotal);
    }

    [Fact]
    public async Task History_search_by_customer_number_and_item()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        Assert.Contains(await Api.QuotationsAsync("sonepar"), s => s.Id == q.Id);
        Assert.Contains(await Api.QuotationsAsync(q.Number), s => s.Id == q.Id);
        Assert.Contains(await Api.QuotationsAsync("bevel"), s => s.Id == q.Id);
        Assert.Contains(await Api.QuotationsAsync("27AAACS1234F1Z3"), s => s.Id == q.Id);
        Assert.DoesNotContain(await Api.QuotationsAsync("havells"), s => s.Id == q.Id);
        Assert.Contains(await Api.QuotationsAsync(status: QuotationStatus.Draft), s => s.Id == q.Id);
    }

    [Fact]
    public async Task Everything_is_audited()
    {
        var q = await Api.CreateQuotationAsync(await BevelRequest());
        await Api.ApproveQuotationAsync(q.Id);
        await Api.CancelQuotationAsync(q.Id);
        var actions = (await Api.AuditAsync("Quotation", q.Id.ToString())).Select(a => a.Action).ToList();
        Assert.Contains("QuotationCreated", actions);
        Assert.Contains("QuotationApproved", actions);
        Assert.Contains("QuotationCancelled", actions);
    }

    private static SaveQuotationRequest ToRequest(QuotationDto d) => new()
    {
        Revision = d.Revision, Date = d.Date, CustomerId = d.CustomerId, Buyer = d.Buyer, Consignee = d.Consignee,
        ConsigneeSameAsBuyer = d.ConsigneeSameAsBuyer, BuyersReference = d.BuyersReference, Remarks = d.Remarks,
        PackingForwarding = d.PackingForwarding, Lines = d.Lines,
    };
}
