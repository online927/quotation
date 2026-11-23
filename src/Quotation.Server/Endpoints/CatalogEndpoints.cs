using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quotation.Contracts;
using Quotation.Core.Gst;
using Quotation.Data;
using Quotation.Data.Entities;
using Quotation.Server.Infrastructure;
using Quotation.Server.Services;
using Quotation.Tally;

namespace Quotation.Server.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var p = app.MapGroup("/api/products").RequireAuthorization();

        // Items marked "Do Not Use" (DNU) are left out unless includeHidden (the Products page's "Show DNU items").
        // starredFirst: the user's starred items come first (only the lists people pick from by hand ask for it; the
        // automatic matching — paste a list, Formater, the phone's message — keeps the best match on top).
        p.MapGet("/search", async (string? q, int? limit, bool? includeHidden, bool? starredFirst, CatalogSearchService search, QuotationDbContext db,
            StarsService stars, HttpContext ctx, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            var withHidden = includeHidden == true;
            var starred = stars.Products(ctx.User.UserName());
            var first = starredFirst == true && starred.Count > 0;
            var wide = first ? Math.Min(100, take * 2) : take; // a starred one just below the top is not missed
            if (string.IsNullOrWhiteSpace(q))
            {
                var mine = !first ? [] : await db.Products.AsNoTracking().Where(x => !x.IsDeleted && starred.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
                var byName = await db.Products.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).Take(take * 3).ToListAsync(ct);
                return mine.Concat(byName.Where(x => !mine.Any(m => m.Id == x.Id))).Select(x => (Product: x, Hidden: search.IsHidden(x.Description)))
                    .Where(x => withHidden || !x.Hidden).Take(take)
                    .Select(x => new ProductSearchHitDto(ToSummary(x.Product, x.Hidden), 0, new SearchMatchDto(true, false, false, false, []))
                    {
                        IsStarred = starred.Contains(x.Product.Id),
                    }).ToList();
            }
            var hits = (await search.ProductsAsync(ct)).Search(q, wide).ToList();
            if (withHidden) hits = hits.Concat((await search.HiddenProductsAsync(ct)).Search(q, wide)).OrderByDescending(h => h.Score).Take(wide).ToList();
            var result = new List<ProductSearchHitDto>();
            foreach (var h in hits)
                result.Add(new ProductSearchHitDto(await search.WithRememberedAsync(CatalogSearchService.ToSummary(h.Item), ct), Math.Round(h.Score, 2),
                    CatalogSearchService.ToMatch(h.Evidence)) { IsStarred = starred.Contains(h.Item.Id) });
            return (first ? StarsService.StarredFirst(result, r => r.Product.Id, starred) : result).Take(take).ToList();
        });

        p.MapGet("/{id:int}", async (int id, QuotationDbContext db, CatalogSearchService search, CancellationToken ct) =>
        {
            var x = await db.Products.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            return x is null ? Results.NotFound() : Results.Ok(ToDetail(x, search.IsHidden(x.Description)));
        });

        // Round 36: an item's PDF datasheet (administrators upload, replace or remove it; everyone may see it).
        p.MapGet("/{id:int}/datasheet", async (int id, QuotationDbContext db, CancellationToken ct) =>
            await db.Datasheets.AsNoTracking().FirstOrDefaultAsync(d => d.ProductId == id, ct) is { } d
                ? Results.Ok(DatasheetService.ToDto(d))
                : Results.NotFound(new ApiError("No datasheet.")));
        p.MapGet("/{id:int}/datasheet/file", (int id, DatasheetService sheets) =>
            File.Exists(sheets.PathOf(id)) ? Results.File(File.ReadAllBytes(sheets.PathOf(id)), "application/pdf") : Results.NotFound(new ApiError("No datasheet.")));
        p.MapPut("/{id:int}/datasheet", async (int id, string? name, HttpContext ctx, QuotationDbContext db, DatasheetService sheets,
            AuditService audit, TimeProvider clock, CancellationToken ct) =>
        {
            using var memory = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(memory, ct);
            try
            {
                var dto = await sheets.SaveAsync(db, id, name ?? "datasheet.pdf", memory.ToArray(), ctx.User.UserName(), clock.GetUtcNow().UtcDateTime, ct);
                await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "DatasheetAdded", "Product", id.ToString(System.Globalization.CultureInfo.InvariantCulture), dto.FileName, ct);
                return Results.Ok(dto);
            }
            catch (QuotationException ex)
            {
                return Results.Json(new ApiError(ex.Message, null, ex.Code), statusCode: ex.Status);
            }
        }).RequireAuthorization(SystemEndpoints.AdminPolicy);
        p.MapPut("/{id:int}/datasheet/add-to-pdf", async (int id, bool on, QuotationDbContext db, CancellationToken ct) =>
        {
            var d = await db.Datasheets.FirstOrDefaultAsync(x => x.ProductId == id, ct);
            if (d is null) return Results.NotFound(new ApiError("No datasheet."));
            d.AddToPdf = on;
            await db.SaveChangesAsync(ct);
            return Results.Ok(DatasheetService.ToDto(d));
        }).RequireAuthorization(SystemEndpoints.AdminPolicy);
        p.MapDelete("/{id:int}/datasheet", async (int id, HttpContext ctx, QuotationDbContext db, DatasheetService sheets, AuditService audit, CancellationToken ct) =>
        {
            await sheets.RemoveAsync(db, id, ct);
            await audit.WriteAsync(ctx.User.UserName(), ctx.Machine(), "DatasheetRemoved", "Product", id.ToString(System.Globalization.CultureInfo.InvariantCulture), ct: ct);
            return Results.NoContent();
        }).RequireAuthorization(SystemEndpoints.AdminPolicy);

        var c = app.MapGroup("/api/customers").RequireAuthorization();

        c.MapGet("/search", async (string? q, int? limit, bool? starredFirst, CatalogSearchService search, QuotationDbContext db, StarsService stars,
            HttpContext ctx, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 20, 1, 100);
            var starred = stars.Customers(ctx.User.UserName());
            var first = starredFirst == true && starred.Count > 0;
            if (string.IsNullOrWhiteSpace(q))
            {
                var mine = !first ? [] : await db.Customers.AsNoTracking().Where(x => !x.IsDeleted && starred.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct);
                var byName = await db.Customers.AsNoTracking().Where(x => !x.IsDeleted).OrderBy(x => x.Name).Take(take).ToListAsync(ct);
                return mine.Concat(byName.Where(x => !mine.Any(m => m.Id == x.Id))).Take(take)
                    .Select(x => new CustomerSearchHitDto(ToSummary(x), 0, new SearchMatchDto(true, false, false, false, [])) { IsStarred = starred.Contains(x.Id) })
                    .ToList();
            }
            var index = await search.CustomersAsync(ct);
            var hits = index.Search(q, first ? Math.Min(100, take * 2) : take).Select(h =>
                new CustomerSearchHitDto(CatalogSearchService.ToSummary(h.Item), Math.Round(h.Score, 2), CatalogSearchService.ToMatch(h.Evidence))
                {
                    IsStarred = starred.Contains(h.Item.Id),
                });
            return (first ? StarsService.StarredFirst(hits, h => h.Customer.Id, starred) : hits.ToList()).Take(take).ToList();
        });

        c.MapGet("/{id:int}", async (int id, QuotationDbContext db, CancellationToken ct) =>
        {
            var x = await db.Customers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            return x is null ? Results.NotFound() : Results.Ok(ToDetail(x));
        });
    }

    public static ProductSummaryDto ToSummary(Product x, bool hidden = false) =>
        new(x.Id, x.Name, x.PartNumber, x.Brand, x.StockGroup, x.Unit, x.Hsn, x.GstRate, x.Rate ?? x.LastRate,
            x.LastDescription.Length > 0 ? x.LastDescription : x.Description,
            x.Rate is null && x.LastRate is not null ? x.LastRateNote : "", hidden);

    public static ProductDetailDto ToDetail(Product x, bool hidden = false)
    {
        var rate = LastRateService.Effective(x);
        return new(x.Id, x.Name, SplitLines(x.Aliases), x.PartNumber, x.Brand, x.Manufacturer,
            x.Category, x.StockGroup, x.Unit, x.Description, x.Hsn, x.GstRate, x.GstSource, rate.Rate, rate.Date, rate.Source, x.IsDeleted,
            x.LastSyncedUtc, hidden);
    }

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
