using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Gst;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Server.Services;
using Quotation.Tally;

namespace Quotation.Server.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var p = app.MapGroup("/api/products").RequireAuthorization();

        p.MapGet("/search", async (string? q, int? limit, CatalogSearchService search, QuotationDbContext db, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            if (string.IsNullOrWhiteSpace(q))
            {
                var first = await db.Products.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).Take(take).ToListAsync(ct);
                return first.Select(x => new ProductSearchHitDto(ToSummary(x), 0, new SearchMatchDto(true, false, false, false, []))).ToList();
            }
            var index = await search.ProductsAsync(ct);
            return index.Search(q, take).Select(h =>
                new ProductSearchHitDto(CatalogSearchService.ToSummary(h.Item), Math.Round(h.Score, 2), CatalogSearchService.ToMatch(h.Evidence))).ToList();
        });

        p.MapGet("/{id:int}", async (int id, QuotationDbContext db, CancellationToken ct) =>
        {
            var x = await db.Products.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            return x is null ? Results.NotFound() : Results.Ok(ToDetail(x));
        });

        var c = app.MapGroup("/api/customers").RequireAuthorization();

        c.MapGet("/search", async (string? q, int? limit, CatalogSearchService search, QuotationDbContext db, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            if (string.IsNullOrWhiteSpace(q))
            {
                var first = await db.Customers.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).Take(take).ToListAsync(ct);
                return first.Select(x => new CustomerSearchHitDto(ToSummary(x), 0, new SearchMatchDto(true, false, false, false, []))).ToList();
            }
            var index = await search.CustomersAsync(ct);
            return index.Search(q, take).Select(h =>
                new CustomerSearchHitDto(CatalogSearchService.ToSummary(h.Item), Math.Round(h.Score, 2), CatalogSearchService.ToMatch(h.Evidence))).ToList();
        });

        c.MapGet("/{id:int}", async (int id, QuotationDbContext db, CancellationToken ct) =>
        {
            var x = await db.Customers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            return x is null ? Results.NotFound() : Results.Ok(ToDetail(x));
        });
    }

    public static ProductSummaryDto ToSummary(Product x) =>
        new(x.Id, x.Name, x.PartNumber, x.Brand, x.StockGroup, x.Unit, x.Hsn, x.GstRate, x.Rate, x.Description);

    public static ProductDetailDto ToDetail(Product x) => new(x.Id, x.Name, SplitLines(x.Aliases), x.PartNumber, x.Brand, x.Manufacturer,
        x.Category, x.StockGroup, x.Unit, x.Description, x.Hsn, x.GstRate, x.GstSource, x.Rate, x.RateDate, x.RateSource, x.IsDeleted,
        x.LastSyncedUtc);

    public static CustomerSummaryDto ToSummary(Customer x) => new(x.Id, x.Name, x.MailingName, x.Address, x.StateName, x.StateCode,
        x.Gstin, x.ContactPerson, x.Mobile.Length > 0 ? x.Mobile : x.Phone, x.Email);

    public static CustomerDetailDto ToDetail(Customer x)
    {
        var shipTo = new List<AddressDto>();
        if (x.ShipToJson.Length > 0)
        {
            foreach (var a in JsonSerializer.Deserialize<List<TallyAddress>>(x.ShipToJson, TallySyncService.Json) ?? [])
            {
                shipTo.Add(new AddressDto(a.Name, a.Lines, a.State, IndianStates.CodeForName(a.State) ?? "", a.Pincode));
            }
        }
        return new CustomerDetailDto(x.Id, x.Name, SplitLines(x.Aliases), x.MailingName, x.LedgerGroup, SplitLines(x.Address),
            x.StateName, x.StateCode, x.Pincode, x.Country, x.Gstin, x.Gstin.Length == 0 || Gstin.IsValid(x.Gstin),
            x.GstRegistrationType, x.Pan, x.ContactPerson, x.Phone, x.Mobile, x.Email, shipTo, x.IsDeleted, x.LastSyncedUtc);
    }

    private static List<string> SplitLines(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
