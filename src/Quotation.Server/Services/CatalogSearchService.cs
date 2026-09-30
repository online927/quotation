using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Search;
using Quotation.Data;
using Quotation.Data.Entities;

namespace Quotation.Server.Services;

/// <summary>Lightweight, immutable copies of catalogue rows held by the search indexes.</summary>
public sealed record ProductDoc(int Id, string Name, string Aliases, string PartNumber, string Brand, string Manufacturer,
    string Category, string StockGroup, string Unit, string Hsn, decimal? GstRate, decimal? Rate, string Description);

public sealed record CustomerDoc(int Id, string Name, string Aliases, string MailingName, string Address, string StateName,
    string StateCode, string Pincode, string Gstin, string ContactPerson, string Phone, string Mobile, string Email);

/// <summary>
/// Holds the in-memory product and customer indexes. Built lazily on first use and rebuilt in the
/// background after every sync that changed data; searches never wait for a rebuild once built.
/// </summary>
public sealed class CatalogSearchService
{
    public static readonly SearchField<ProductDoc>[] ProductFields =
    [
        new("Name", p => p.Name, 1.0, FieldKind.Identifier, IsPrimaryName: true),
        new("Alias", p => p.Aliases, 0.95, FieldKind.Identifier),
        new("PartNumber", p => p.PartNumber, 1.2, FieldKind.Identifier),
        new("Brand", p => p.Brand, 0.6),
        new("Manufacturer", p => p.Manufacturer, 0.6),
        new("Category", p => p.Category, 0.5),
        new("Group", p => p.StockGroup, 0.3),
        new("Description", p => p.Description, 0.3),
        new("HSN", p => p.Hsn, 0.4, FieldKind.Identifier),
    ];

    public static readonly SearchField<CustomerDoc>[] CustomerFields =
    [
        new("Name", c => c.Name, 1.0, IsPrimaryName: true),
        new("Alias", c => c.Aliases, 0.95),
        new("MailingName", c => c.MailingName, 0.9),
        new("GSTIN", c => c.Gstin, 1.2, FieldKind.Identifier),
        new("Phone", c => c.Phone + "\n" + c.Mobile, 0.8, FieldKind.Identifier),
        new("Email", c => c.Email, 0.6),
        new("Contact", c => c.ContactPerson, 0.5),
        new("Address", c => c.Address + " " + c.Pincode, 0.3),
        new("State", c => c.StateName, 0.2),
    ];

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CatalogSearchService> _log;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private volatile SearchIndex<ProductDoc>? _products;
    private volatile SearchIndex<CustomerDoc>? _customers;
    private volatile bool _stale = true;

    public CatalogSearchService(IServiceScopeFactory scopes, TallySyncService sync, ILogger<CatalogSearchService> log)
    {
        _scopes = scopes;
        _log = log;
        sync.DataChanged += () =>
        {
            _stale = true;
            _ = Task.Run(() => RebuildAsync(CancellationToken.None));
        };
    }

    public async Task<SearchIndex<ProductDoc>> ProductsAsync(CancellationToken ct)
    {
        if (_products is null || (_stale && _products.Count == 0)) await RebuildAsync(ct);
        return _products!;
    }

    public async Task<SearchIndex<CustomerDoc>> CustomersAsync(CancellationToken ct)
    {
        if (_customers is null || (_stale && _customers.Count == 0)) await RebuildAsync(ct);
        return _customers!;
    }

    public async Task RebuildAsync(CancellationToken ct)
    {
        await _buildLock.WaitAsync(ct);
        try
        {
            if (!_stale && _products is not null) return;
            _stale = false;
            var sw = Stopwatch.StartNew();
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotationDbContext>();
            var products = await db.Products.AsNoTracking().Where(p => !p.IsDeleted)
                .Select(p => new ProductDoc(p.Id, p.Name, p.Aliases, p.PartNumber, p.Brand, p.Manufacturer, p.Category, p.StockGroup,
                    p.Unit, p.Hsn, p.GstRate, p.Rate, p.Description))
                .ToListAsync(ct);
            var customers = await db.Customers.AsNoTracking().Where(c => !c.IsDeleted)
                .Select(c => new CustomerDoc(c.Id, c.Name, c.Aliases, c.MailingName, c.Address, c.StateName, c.StateCode, c.Pincode,
                    c.Gstin, c.ContactPerson, c.Phone, c.Mobile, c.Email))
                .ToListAsync(ct);
            _products = new SearchIndex<ProductDoc>(products.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase), ProductFields);
            _customers = new SearchIndex<CustomerDoc>(customers.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase), CustomerFields);
            _log.LogInformation("Search indexes built: {Products} products, {Customers} customers in {Ms} ms",
                products.Count, customers.Count, sw.ElapsedMilliseconds);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    public static ProductSummaryDto ToSummary(ProductDoc p) =>
        new(p.Id, p.Name, p.PartNumber, p.Brand, p.StockGroup, p.Unit, p.Hsn, p.GstRate, p.Rate, p.Description);

    public static CustomerSummaryDto ToSummary(CustomerDoc c) =>
        new(c.Id, c.Name, c.MailingName, c.Address, c.StateName, c.StateCode, c.Gstin, c.ContactPerson,
            c.Mobile.Length > 0 ? c.Mobile : c.Phone, c.Email);

    public static SearchMatchDto ToMatch(MatchEvidence e) =>
        new(e.AllTokensMatched, e.ExactName, e.ExactIdentifier, e.UsedFuzzy, e.MatchedFields);
}
