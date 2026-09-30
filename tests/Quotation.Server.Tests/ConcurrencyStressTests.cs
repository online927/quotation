using System.Collections.Concurrent;
using System.Diagnostics;
using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;
using Quotation.Core.Numbering;
using Xunit.Abstractions;

namespace Quotation.Server.Tests;

/// <summary>
/// Several client PCs working at once while a full 20,000-product sync writes to the database:
/// every request must succeed, numbers must be unique and gap-free, and the sync must complete.
/// </summary>
public class ConcurrencyStressTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Six_pcs_create_search_and_approve_during_a_full_sync()
    {
        using var server = new TestServer(products: 20000, customers: 3000, today: DateOnly.FromDateTime(DateTime.Today));
        var admin = await server.LoginAsAdminAsync("SERVER-ADMIN");
        await admin.StartSyncAsync(SyncKind.Full, wait: true);

        var customer = (await admin.SearchCustomersAsync("SONEPAR INDIA PRIVATE")).First().Customer;
        var product = (await admin.SearchProductsAsync("universal bevel")).First().Product;
        SaveQuotationRequest Request() => new()
        {
            Date = DateOnly.FromDateTime(DateTime.Today),
            CustomerId = customer.Id,
            Lines = [new QuotationLineDto { ProductId = product.Id, Quantity = 2, Rate = 15600, TallyRate = product.Rate }],
        };

        var clients = new List<QuotationApiClient>();
        for (var pc = 1; pc <= 6; pc++) clients.Add(await server.LoginAsAdminAsync($"PC-{pc}"));

        var errors = new ConcurrentBag<string>();
        var numbers = new ConcurrentBag<string>();
        var sw = Stopwatch.StartNew();

        var sync = admin.StartSyncAsync(SyncKind.Full, wait: true);
        var work = clients.Select((api, i) => Task.Run(async () =>
        {
            for (var n = 0; n < 15; n++)
            {
                try
                {
                    await api.SearchProductsAsync(n % 2 == 0 ? "3 core 2.5 sqmm cable" : "vernier caliper");
                    await api.SearchCustomersAsync("precision");
                    var q = await api.CreateQuotationAsync(Request());
                    numbers.Add(q.Number!);
                    if (n % 5 == 0)
                    {
                        var approved = await api.ApproveQuotationAsync(q.Id, overrideStaleData: true);
                        if (approved.Status != QuotationStatus.Generated && approved.Status != QuotationStatus.Approved)
                            errors.Add($"PC{i}: approve returned {approved.Status}");
                    }
                    await api.QuotationsAsync("sonepar", take: 20);
                }
                catch (ApiException ex) when (ex.Code == ApiErrorCodes.RatesChanged)
                {
                    // Legitimate business response when the sync changed a rate; not a failure.
                }
                catch (Exception ex)
                {
                    errors.Add($"PC{i}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        })).ToList();

        await Task.WhenAll(work);
        var syncRun = await sync;
        output.WriteLine($"{numbers.Count} quotations by 6 PCs + full sync in {sw.Elapsed.TotalSeconds:0.0}s; errors: {errors.Count}");
        foreach (var e in errors.Take(10)) output.WriteLine(e);

        Assert.Empty(errors);
        Assert.Equal(SyncStatus.Succeeded, syncRun!.Status);
        Assert.Equal(90, numbers.Count);
        Assert.Equal(90, numbers.Distinct().Count());
        var fy = FinancialYear.For(DateOnly.FromDateTime(DateTime.Today));
        var seqs = numbers.Select(n => QuotationNumberFormatter.TryParseSequence("TSQ{FY}-{SEQ}", fy, n)!.Value).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(1, 90), seqs);
    }
}
