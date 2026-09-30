using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

public class CatalogApiTests : IAsyncLifetime
{
    private readonly TestServer _server = new(products: 500, customers: 200, today: new DateOnly(2026, 9, 30));
    private Quotation.ApiClient.QuotationApiClient _api = null!;

    public async Task InitializeAsync()
    {
        _api = await _server.LoginAsAdminAsync();
        await _api.StartSyncAsync(SyncKind.Full, wait: true);
    }

    public Task DisposeAsync()
    {
        _server.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Product_search_and_detail()
    {
        var hits = await _api.SearchProductsAsync("universal bevel");
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", hits[0].Product.Name);
        Assert.Equal(15600m, hits[0].Product.Rate);
        Assert.True(hits[0].Match.AllTermsMatched);

        var detail = await _api.ProductAsync(hits[0].Product.Id);
        Assert.Equal("90172020", detail.Hsn);
        Assert.Equal(18m, detail.GstRate);
        Assert.Equal("Standard selling price", detail.RateSource);
        Assert.Equal(new DateOnly(2025, 4, 1), detail.RateDate);
    }

    [Fact]
    public async Task Part_number_search()
    {
        var hits = await _api.SearchProductsAsync("18790110");
        Assert.Equal("187-901-10-UNIVERSAL BEVEL PROTRACTOR", hits[0].Product.Name);
        Assert.True(hits[0].Match.ExactIdentifier);
    }

    [Fact]
    public async Task Customer_search_by_name_and_gstin()
    {
        var hits = await _api.SearchCustomersAsync("SONEPAR");
        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Contains("SONEPAR", h.Customer.Name));

        var byGstin = await _api.SearchCustomersAsync("27AAACS1234F1Z3");
        Assert.Equal("SONEPAR INDIA PRIVATE LIMITED", byGstin[0].Customer.Name);

        var detail = await _api.CustomerAsync(byGstin[0].Customer.Id);
        Assert.Equal("27", detail.StateCode);
        Assert.Equal(["Plot No. 12, MIDC Industrial Area", "Chakan, Pune"], detail.AddressLines);
        Assert.Single(detail.ShipToAddresses);
        Assert.Equal("29", detail.ShipToAddresses[0].StateCode);
    }

    [Fact]
    public async Task Customer_search_by_typo_and_phone()
    {
        Assert.Contains(await _api.SearchCustomersAsync("sonepr"), h => h.Customer.Name.StartsWith("SONEPAR"));
        Assert.Equal("BHARAT PRECISION ENGINEERING", (await _api.SearchCustomersAsync("9845098450"))[0].Customer.Name);
    }

    [Fact]
    public async Task Empty_query_lists_alphabetically()
    {
        var hits = await _api.SearchProductsAsync("", 5);
        Assert.Equal(5, hits.Count);
        Assert.Equal(hits.Select(h => h.Product.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase), hits.Select(h => h.Product.Name));
    }

    [Fact]
    public async Task Index_refreshes_after_sync()
    {
        _server.Tally.AddStockItem("ZEBRA SPECIAL GAUGE 42MM", "Measuring Instruments", "NOS", "90173029", 18, 4200m);
        await _api.StartSyncAsync(SyncKind.Incremental, wait: true);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        List<Quotation.Contracts.ProductSearchHitDto> hits;
        do
        {
            hits = await _api.SearchProductsAsync("zebra gauge");
            if (hits.Count > 0) break;
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);
        Assert.Equal("ZEBRA SPECIAL GAUGE 42MM", hits[0].Product.Name);
    }

    [Fact]
    public async Task Deleted_products_are_not_found()
    {
        _server.Tally.DeleteStockItem("530-104-VERNIER CALIPER 0-150MM");
        await _api.StartSyncAsync(SyncKind.Full, wait: true);
        await Task.Delay(500);
        var hits = await _api.SearchProductsAsync("530-104");
        Assert.DoesNotContain(hits, h => h.Product.Name == "530-104-VERNIER CALIPER 0-150MM");
    }

    [Fact]
    public async Task Search_works_while_tally_is_offline()
    {
        _server.Tally.Online = false;
        var hits = await _api.SearchProductsAsync("universal bevel");
        Assert.NotEmpty(hits);
    }
}
