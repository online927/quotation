using Quotation.ApiClient;
using Quotation.Contracts;
using Quotation.Core.Domain;

namespace Quotation.Server.Tests;

/// <summary>Server with a completed full sync and helpers to build quotation requests.</summary>
public abstract class QuotationTestBase : IAsyncLifetime
{
    protected readonly TestServer Server = new(products: 300, customers: 100, today: DateOnly.FromDateTime(DateTime.Today));
    protected QuotationApiClient Api = null!;
    protected static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public virtual async Task InitializeAsync()
    {
        Api = await Server.LoginAsAdminAsync();
        await Api.StartSyncAsync(SyncKind.Full, wait: true);
    }

    public Task DisposeAsync()
    {
        Server.Dispose();
        return Task.CompletedTask;
    }

    protected async Task<ProductSummaryDto> Product(string name) =>
        (await Api.SearchProductsAsync(name, 5)).First(h => h.Product.Name == name).Product;

    protected async Task<CustomerSummaryDto> Customer(string name) =>
        (await Api.SearchCustomersAsync(name, 5)).First(h => h.Customer.Name == name).Customer;

    protected async Task<SaveQuotationRequest> BevelRequest(decimal qty = 2)
    {
        var customer = await Customer("SONEPAR INDIA PRIVATE LIMITED");
        var product = await Product("187-901-10-UNIVERSAL BEVEL PROTRACTOR");
        return new SaveQuotationRequest
        {
            Date = Today,
            CustomerId = customer.Id,
            Lines =
            [
                new QuotationLineDto
                {
                    ProductId = product.Id, Quantity = qty, Rate = product.Rate ?? 0, TallyRate = product.Rate,
                    Description = "Brand: Mitutoyo\nMOQ: 1",
                },
            ],
        };
    }
}
