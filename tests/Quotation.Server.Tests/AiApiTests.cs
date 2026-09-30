using System.Net;
using System.Text.Json;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

public class AiApiTests : QuotationTestBase
{
    private static int FirstId(JsonElement results, string field) => results.EnumerateArray().First().GetProperty(field).GetInt32();

    [Fact]
    public async Task Ai_not_configured_is_reported()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.AnalyzeAsync("quote 2 bevel protractors"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.Status);
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public async Task Clear_request_creates_pending_review_draft_with_tally_values_and_no_number()
    {
        Server.AiProvider = new ScriptedProvider(async ai =>
        {
            var c = await ai.Call("search_customer", new { query = "Bharat Precision" });
            var p = await ai.Call("search_product", new { query = "universal bevel protractor 187-901-10" });
            await ai.Call("get_current_rate", new { product_id = FirstId(p, "product_id") });
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Bharat Precision", FirstId(c, "customer_id"),
                ScriptedProvider.Line("universal bevel protractors", "universal bevel protractor 187-901-10", FirstId(p, "product_id"), 2)));
        });

        var r = await Api.AnalyzeAsync("Create quotation for Bharat Precision for 2 universal bevel protractors 187-901-10");
        Assert.Equal(AiRequestStatus.DraftCreated, r.Status);
        Assert.NotNull(r.QuotationId);
        Assert.Null(r.QuotationNumber);

        var q = await Api.QuotationAsync(r.QuotationId!.Value);
        Assert.Equal(QuotationStatus.PendingReview, q.Status);
        Assert.Equal(QuotationSource.Ai, q.Source);
        Assert.Null(q.Number);                            // AI never consumes a number
        Assert.Equal("29ABCPB1234K1Z2", q.Buyer.Gstin);   // from Tally
        Assert.Equal("90172020", q.Lines[0].Hsn);
        Assert.Equal(18m, q.Lines[0].GstRate);
        Assert.Equal(15600m, q.Lines[0].Rate);
        Assert.Equal(31200m, q.GrandTotal);

        // The human reviews and saves → the quotation gets its number; approval requires the human.
        var saved = await Api.UpdateQuotationAsync(q.Id, new SaveQuotationRequest
        {
            Revision = q.Revision, Date = q.Date, CustomerId = q.CustomerId, Buyer = q.Buyer, ConsigneeSameAsBuyer = true, Lines = q.Lines,
        });
        Assert.NotNull(saved.Number);
        var approved = await Api.ApproveQuotationAsync(q.Id);
        Assert.Equal(QuotationStatus.Generated, approved.Status);
        Assert.Equal("admin", approved.ApprovedBy);
    }

    [Fact]
    public async Task Ambiguous_request_waits_for_the_user_then_creates_draft_from_selection()
    {
        Server.AiProvider = new ScriptedProvider(async ai =>
        {
            var p = await ai.Call("search_product", new { query = "3 core 2.5 sqmm cable" });
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Bharat Precision", null,
                ScriptedProvider.Line("3 core 2.5 sqmm cable", "3 core 2.5 sqmm cable", FirstId(p, "product_id"), 10)));
        });

        var r = await Api.AnalyzeAsync("Please quote 10 mtr 3 core 2.5 sqmm cable for Bharat Precision");
        Assert.Equal(AiRequestStatus.NeedsClarification, r.Status);
        Assert.Null(r.QuotationId);
        var line = r.Analysis!.Lines.Single();
        Assert.Equal(ResolutionStatus.NeedsSelection, line.Status);
        Assert.True(line.Candidates.Count >= 3);

        var dashboard = await Api.DashboardAsync();
        Assert.Equal(1, dashboard.InboxNeedsAttention);

        var fr = line.Candidates.First(c => c.Name == "POLYCAB FR 3 CORE 2.5 SQMM FLEXIBLE CABLE");
        var created = await Api.CreateDraftFromAiAsync(r.Id, new CreateDraftFromAiRequest
        {
            CustomerId = r.Analysis.Customer.CustomerId!.Value,
            Lines = [new AiDraftLine { ProductId = fr.Id, Quantity = 10 }],
        });
        Assert.Equal(AiRequestStatus.DraftCreated, created.Status);
        var q = await Api.QuotationAsync(created.QuotationId!.Value);
        Assert.Equal("POLYCAB FR 3 CORE 2.5 SQMM FLEXIBLE CABLE", q.Lines[0].ItemName);
        Assert.Equal(126m, q.Lines[0].Rate);
        Assert.Equal(QuotationStatus.PendingReview, q.Status);

        await Assert.ThrowsAsync<ApiException>(() => Api.CreateDraftFromAiAsync(r.Id, new CreateDraftFromAiRequest
        {
            CustomerId = r.Analysis.Customer.CustomerId!.Value, Lines = [new AiDraftLine { ProductId = fr.Id, Quantity = 10 }],
        }));
    }

    [Fact]
    public async Task Draft_creation_rejects_invalid_decisions()
    {
        Server.AiProvider = new ScriptedProvider(async ai =>
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Nobody", null, ScriptedProvider.Line("x", "x", null, null))));
        var r = await Api.AnalyzeAsync("??");
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.CreateDraftFromAiAsync(r.Id, new CreateDraftFromAiRequest
        {
            CustomerId = 999999, Lines = [new AiDraftLine { ProductId = 1, Quantity = 1 }],
        }));
        Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
        var bharat = (await Customer("BHARAT PRECISION ENGINEERING")).Id;
        var ex2 = await Assert.ThrowsAsync<ApiException>(() => Api.CreateDraftFromAiAsync(r.Id, new CreateDraftFromAiRequest
        {
            CustomerId = bharat, Lines = [new AiDraftLine { ProductId = 1, Quantity = 0 }],
        }));
        Assert.Equal(HttpStatusCode.BadRequest, ex2.Status);
    }

    [Fact]
    public async Task Provider_failure_is_recorded_not_lost()
    {
        Server.AiProvider = new ScriptedProvider(_ => throw new Quotation.AI.AiUnavailableException("Claude is temporarily unavailable."));
        var r = await Api.AnalyzeAsync("quote something");
        Assert.Equal(AiRequestStatus.Failed, r.Status);
        Assert.Contains("temporarily unavailable", r.Error);
        Assert.Contains(await Api.AiRequestsAsync(AiRequestStatus.Failed), x => x.Id == r.Id);
    }

    [Fact]
    public async Task Dismiss_and_history_tool()
    {
        await Api.CreateQuotationAsync(await BevelRequest());
        string? history = null;
        Server.AiProvider = new ScriptedProvider(async ai =>
        {
            var c = await ai.Call("search_customer", new { query = "SONEPAR INDIA PRIVATE LIMITED" });
            var h = await ai.Call("get_quotation_history", new { customer_id = FirstId(c, "customer_id"), query = (string?)null });
            history = h.GetRawText();
        });
        var r = await Api.AnalyzeAsync("same as last time for Sonepar");
        Assert.Contains("UNIVERSAL BEVEL PROTRACTOR", history);
        var dismissed = await Api.DismissAiRequestAsync(r.Id);
        Assert.Equal(AiRequestStatus.Dismissed, dismissed.Status);
    }
}
