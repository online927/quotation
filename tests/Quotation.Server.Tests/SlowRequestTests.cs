using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Quotation.Contracts;
using Quotation.Server.Services;

namespace Quotation.Server.Tests;

/// <summary>Speed check and the slow-request list (Diagnostics → Slow requests).</summary>
public class SlowRequestTests : QuotationTestBase
{
    [Fact]
    public async Task Every_answer_carries_the_server_time_and_slow_requests_are_listed_with_the_cause()
    {
        // The speed check: the client measures the status call; the server adds its own time to every answer.
        await Api.StatusAsync();
        Assert.NotNull(Api.LastStatusTime);
        using var http = Server.CreateClient();
        var answer = await http.GetAsync("/api/status");
        Assert.True(answer.Headers.Contains(SlowRequestLog.ServerMsHeader));
        Assert.True(answer.Headers.Contains(SlowRequestLog.RequestIdHeader));

        // A request the server answered slowly (e.g. during a Tally sync), then the client's report of the same request.
        var log = Server.Services.GetRequiredService<SlowRequestLog>();
        log.ServerSlow("abc123", DateTime.UtcNow.AddMinutes(-5), "keerthana", "USER-PC", "Search products", 1900, "Tally sync");
        var raw = Server.CreateApi("USER-PC");
        await raw.LoginAsync("admin", "admin123");
        using (var client = Server.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", raw.Token);
            (await client.PostAsJsonAsync("/api/diagnostics/slow", new SlowRequestReport("abc123", "GET /api/products/search", 2100, 1900, "Tally sync"))).EnsureSuccessStatusCode();
            // Network delay: the server answered at once, but the PC waited 3 seconds.
            (await client.PostAsJsonAsync("/api/diagnostics/slow", new SlowRequestReport("def456", "GET /api/customers/12", 3200, 40, null))).EnsureSuccessStatusCode();
            // Quick requests are not listed.
            (await client.PostAsJsonAsync("/api/diagnostics/slow", new SlowRequestReport("ghi789", "GET /api/customers/12", 300, 20, null))).EnsureSuccessStatusCode();
        }

        var list = await Api.SlowRequestsAsync();
        var sync = Assert.Single(list, s => s.Id == "abc123");
        Assert.Equal(2100, sync.TotalMs);
        Assert.Equal(1900, sync.ServerMs);
        Assert.Equal("Tally sync", sync.Busy);
        var network = Assert.Single(list, s => s.Id == "def456");
        Assert.Equal("Customer details", network.What);
        Assert.Equal(40, network.ServerMs);
        Assert.DoesNotContain(list, s => s.Id == "ghi789");
        // Newest first (real slow requests of a busy test machine may be listed too, so only ours are compared).
        Assert.Equal(["def456", "abc123"], list.Where(s => s.Id is "abc123" or "def456").Select(s => s.Id));

        // Users may report but not read the list.
        await Api.CreateUserAsync(new CreateUserRequest("rasik", "Rasik", "first-pass", Quotation.Core.Domain.UserRole.User));
        var user = Server.CreateApi("RASIK-PC");
        await user.LoginAsync("rasik", "first-pass");
        await user.ChangePasswordAsync("first-pass", "rasik-pass");
        await Assert.ThrowsAsync<Quotation.ApiClient.ApiException>(() => user.SlowRequestsAsync());
    }

    [Fact]
    public void Request_names_are_in_words()
    {
        Assert.Equal("Search products", SlowRequestLog.Describe("GET", "/api/products/search"));
        Assert.Equal("Search customers", SlowRequestLog.Describe("GET", "/api/customers/search"));
        Assert.Equal("Customer details", SlowRequestLog.Describe("GET", "/api/customers/15"));
        Assert.Equal("Approve", SlowRequestLog.Describe("POST", "/api/quotations/1b2c/approve"));
        Assert.Equal("Save quotation", SlowRequestLog.Describe("PUT", "/api/quotations/1b2c"));
    }
}
