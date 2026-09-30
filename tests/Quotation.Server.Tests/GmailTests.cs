using System.Net;
using System.Text.Json;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Gmail;

namespace Quotation.Server.Tests;

public class GmailTests : QuotationTestBase
{
    private static int FirstId(JsonElement results, string field) => results.EnumerateArray().First().GetProperty(field).GetInt32();

    public int AiRuns { get; private set; }
    public bool AiFails { get; set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Server.Gmail = new FakeGmail();
        Server.AiProvider = new ScriptedProvider(async ai =>
        {
            AiRuns++;
            if (AiFails) throw new Quotation.AI.AiUnavailableException("Claude is temporarily unavailable.");
            var text = ai.Request.UserMessage;
            if (text.Contains("newsletter", StringComparison.OrdinalIgnoreCase))
            {
                await ai.Call("create_quotation_draft", new
                {
                    is_quotation_request = false, customer_query = (string?)null, customer_id = (int?)null, reference = (string?)null,
                    summary = "Newsletter", notes = (string?)null, confidence = 0.9, lines = Array.Empty<object>(),
                });
                return;
            }
            var p = await ai.Call("search_product", new { query = "187-901-10" });
            await ai.Call("create_quotation_draft", ScriptedProvider.Draft("Sonepar India", null,
                ScriptedProvider.Line("universal bevel protractor", "187-901-10", FirstId(p, "product_id"), 1)) );
        });
    }

    private void AddSoneparRequest() => Server.Gmail!.Add("purchase.pune@sonepar.example", "Rahul Deshmukh", "Quotation Required",
        "Dear Sir,\n\nPlease quote for 1 No. 187-901-10 Universal Bevel Protractor.\n\nRegards,\nSonepar India");

    [Fact]
    public async Task New_request_email_becomes_a_review_draft_and_newsletters_are_ignored()
    {
        AddSoneparRequest();
        Server.Gmail!.Add("news@vendor.example", "Vendor", "Monthly newsletter", "Our newsletter for September");

        var result = await Api.GmailPollAsync();
        Assert.Equal(2, result.New);
        Assert.Equal(2, result.Processed);

        var messages = await Api.GmailMessagesAsync();
        var quote = messages.Single(m => m.Subject == "Quotation Required");
        Assert.Equal(EmailProcessingStatus.DraftCreated, quote.Status);
        Assert.NotNull(quote.QuotationId);
        Assert.Equal(EmailProcessingStatus.Ignored, messages.Single(m => m.Subject == "Monthly newsletter").Status);

        var q = await Api.QuotationAsync(quote.QuotationId!.Value);
        Assert.Equal(QuotationSource.Gmail, q.Source);
        Assert.Equal(QuotationStatus.PendingReview, q.Status);
        Assert.Equal("SONEPAR INDIA PRIVATE LIMITED", (await Api.AiRequestAsync(quote.AiRequestId!.Value)).Analysis!.Customer.CustomerName);
        Assert.Contains("Quotation Required", q.OtherReferences);
        Assert.Null(q.Number);
    }

    [Fact]
    public async Task Same_email_is_never_processed_twice()
    {
        AddSoneparRequest();
        await Api.GmailPollAsync();
        await Api.GmailPollAsync();
        var again = await Api.GmailPollAsync();
        Assert.Equal(0, again.New);
        Assert.Equal(1, AiRuns);
        Assert.Single(await Api.GmailMessagesAsync());
        Assert.Single(await Api.QuotationsAsync(source: QuotationSource.Gmail));
    }

    [Fact]
    public async Task Interrupted_processing_resumes_without_duplicates()
    {
        AddSoneparRequest();
        await Api.GmailPollAsync();
        // Simulate a crash in the middle of processing: row left in PROCESSING state long ago.
        Server.Db(db =>
        {
            var e = db.EmailMessages.Single();
            e.Status = EmailProcessingStatus.Processing;
            e.UpdatedUtc = DateTime.UtcNow.AddHours(-1);
            return db.SaveChanges();
        });
        await Api.GmailPollAsync();
        Assert.Equal(1, AiRuns);                                 // the existing analysis was reused
        Assert.Single(await Api.QuotationsAsync(source: QuotationSource.Gmail));
        Assert.Equal(EmailProcessingStatus.DraftCreated, (await Api.GmailMessagesAsync()).Single().Status);
    }

    [Fact]
    public async Task Ai_outage_is_retried_a_limited_number_of_times()
    {
        AddSoneparRequest();
        AiFails = true;
        for (var i = 0; i < 5; i++) await Api.GmailPollAsync();
        var m = (await Api.GmailMessagesAsync()).Single();
        Assert.Equal(EmailProcessingStatus.Failed, m.Status);
        Assert.Equal(3, m.Attempts);
        Assert.Contains("temporarily unavailable", m.LastError);
        Assert.Equal(3, AiRuns);
    }

    [Fact]
    public async Task Recovers_after_ai_comes_back()
    {
        AddSoneparRequest();
        AiFails = true;
        await Api.GmailPollAsync();
        AiFails = false;
        await Api.GmailPollAsync();
        Assert.Equal(EmailProcessingStatus.DraftCreated, (await Api.GmailMessagesAsync()).Single().Status);
    }

    [Fact]
    public async Task Emails_wait_while_ai_is_not_configured()
    {
        var provider = Server.AiProvider;
        Server.AiProvider = null;
        AddSoneparRequest();
        await Api.GmailPollAsync();
        var m = (await Api.GmailMessagesAsync()).Single();
        Assert.Equal(EmailProcessingStatus.Unprocessed, m.Status);
        Assert.Contains("not configured", m.ProcessingResult);

        Server.AiProvider = provider;
        await Api.GmailPollAsync();
        Assert.Equal(EmailProcessingStatus.DraftCreated, (await Api.GmailMessagesAsync()).Single().Status);
    }

    [Fact]
    public async Task Gmail_outage_is_reported()
    {
        Server.Gmail!.Offline = true;
        var r = await Api.GmailPollAsync();
        Assert.Contains("Gmail unavailable", r.Error);
        Assert.Contains("Gmail unavailable", (await Api.GmailStatusAsync()).LastError);
    }

    [Fact]
    public async Task Not_connected_is_reported()
    {
        Server.Gmail = null;
        var r = await Api.GmailPollAsync();
        Assert.Equal("Gmail is not connected.", r.Error);
        Assert.False((await Api.GmailStatusAsync()).Connected);
    }

    [Fact]
    public async Task Connect_flow_validates_client_redirect_and_state()
    {
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.GmailConnectStartAsync("http://127.0.0.1:53111/"));
        Assert.Contains("OAuth client JSON", ex.Message);

        var s = await Api.SettingsAsync();
        s.Gmail.ClientSecretJson = """{"installed":{"client_id":"123.apps.googleusercontent.com","client_secret":"shh","auth_uri":"https://accounts.google.com/o/oauth2/auth","token_uri":"https://oauth2.googleapis.com/token","redirect_uris":["http://localhost"]}}""";
        await Api.SaveSettingsAsync(s);
        Assert.True((await Api.GmailStatusAsync()).ClientConfigured);

        var remote = await Assert.ThrowsAsync<ApiException>(() => Api.GmailConnectStartAsync("https://evil.example/cb"));
        Assert.Equal(HttpStatusCode.BadRequest, remote.Status);

        var start = await Api.GmailConnectStartAsync("http://127.0.0.1:53111/");
        Assert.StartsWith("https://accounts.google.com/", start.AuthorizationUrl);
        Assert.Contains("gmail.readonly", Uri.UnescapeDataString(start.AuthorizationUrl));
        Assert.Contains("state=" + start.State, start.AuthorizationUrl);
        Assert.DoesNotContain("shh", start.AuthorizationUrl); // the client secret never goes to the browser

        var bad = await Assert.ThrowsAsync<ApiException>(() =>
            Api.GmailConnectCompleteAsync(new GmailConnectCompleteRequest("code", "WRONGSTATE", "http://127.0.0.1:53111/")));
        Assert.Contains("expired or is invalid", bad.Message);
    }

    [Fact]
    public async Task Invalid_oauth_json_is_rejected()
    {
        var s = await Api.SettingsAsync();
        s.Gmail.ClientSecretJson = "{not json";
        var ex = await Assert.ThrowsAsync<ApiException>(() => Api.SaveSettingsAsync(s));
        Assert.Contains(ex.Details, d => d.Contains("OAuth client JSON"));
    }

    [Theory]
    [InlineData("Rahul Deshmukh <purchase@sonepar.example>", "purchase@sonepar.example", "Rahul Deshmukh")]
    [InlineData("\"Sales, Team\" <sales@x.in>", "sales@x.in", "Sales, Team")]
    [InlineData("plain@x.in", "plain@x.in", "")]
    public void Parses_from_header(string header, string address, string name) =>
        Assert.Equal((address, name), GmailParsing.ParseFrom(header));

    [Fact]
    public void Html_body_to_text()
    {
        var text = GmailParsing.HtmlToText("<html><style>p{}</style><body><p>Dear Sir,</p><p>Please quote <b>10 nos</b> &amp; oblige.<br>Regards</p></body></html>");
        Assert.Equal("Dear Sir,\nPlease quote 10 nos & oblige.\nRegards", text.Replace("\n\n", "\n"));
    }
}
